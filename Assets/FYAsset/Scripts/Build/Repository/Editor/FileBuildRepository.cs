#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 文件系统版 Build Repository。
/// JSON 是唯一持久化格式，所有写入都必须走 FileHelper 原子写。
/// </summary>
public sealed class FileBuildRepository : IBuildRepository
{
    private static string SnapshotRoot => Path.Combine(BuildPathManager.ProjectRoot, "BuildData", "Snapshots");

    public RepositoryStatus GetStatus(string channelKey)
    {
        var status = new RepositoryStatus
        {
            ChannelKey = channelKey,
            HasHead = false,
            HeadVersion = string.Empty,
            PackageName = string.Empty,
            ArtifactCount = 0
        };

        var head = TryLoadHead(channelKey);
        if (head == null)
            return status;

        status.HasHead = true;
        status.HeadVersion = head.Version != null ? head.Version.GetFullVersionString() : string.Empty;
        status.PackageName = head.PackageName ?? string.Empty;
        status.ArtifactCount = head.Artifacts != null ? head.Artifacts.Count : 0;
        var pushHistory = TryLoadPushHistory(channelKey);
        if (pushHistory.Count > 0)
        {
            var last = pushHistory[pushHistory.Count - 1];
            status.LastPushTargetId = last.TargetId;
            status.LastPushAtUtc = last.PushedAtUtc;
        }
        return status;
    }

    public RepositoryCommit GetHeadCommit(string channelKey)
    {
        return TryLoadHead(channelKey);
    }

    public List<RepositoryCommit> ListCommits(string channelKey)
    {
        var result = new List<RepositoryCommit>();
        string objectsDir = GetObjectsDir(channelKey);
        if (!FileHelper.DirectoryExists(objectsDir))
            return result;

        string[] files = FileHelper.GetFiles(objectsDir, "*.json", SearchOption.TopDirectoryOnly);
        Array.Sort(files, StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < files.Length; i++)
        {
            var snapshot = TryReadSnapshot(files[i]);
            if (snapshot != null)
                result.Add(snapshot);
        }

        return result;
    }

    public void Commit(RepositoryCommit commit)
    {
        if (commit == null)
            throw new ArgumentNullException(nameof(commit));
        if (commit.Version == null)
            throw new ArgumentException("commit.Version 不能为空。", nameof(commit));
        if (string.IsNullOrEmpty(commit.ChannelKey))
            throw new ArgumentException("commit.ChannelKey 不能为空。", nameof(commit));
        if (!ChannelKeyMatchesBackend(commit.ChannelKey, commit.BackendMode))
            throw new ArgumentException($"commit.BackendMode 与 channelKey 不匹配: {commit.ChannelKey} / {commit.BackendMode}", nameof(commit));

        string channelRoot = GetChannelRoot(commit.ChannelKey);
        string objectsDir = Path.Combine(channelRoot, "objects");
        string objectPath = Path.Combine(objectsDir, GetSnapshotFileName(commit.Version));
        string headPath = Path.Combine(channelRoot, "HEAD.json");

        FileHelper.EnsureDirectory(objectsDir);
        FileHelper.WriteAllTextAtomic(objectPath, SerializationUtility.SerializeToJson(commit, true));

        var head = new RepositoryHeadState
        {
            HeadVersion = commit.Version.GetFullVersionString()
        };
        try
        {
            FileHelper.WriteAllTextAtomic(headPath, SerializationUtility.SerializeToJson(head, true));
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[FileBuildRepository] HEAD swap failed; object remains as orphan: {objectPath}, reason: {ex.Message}");
            throw;
        }
    }

    public PushReceipt Push(string channelKey, VersionNumber fromVersion, VersionNumber toVersion, IPushTarget target)
    {
        if (string.IsNullOrEmpty(channelKey))
            throw new ArgumentException("channelKey 不能为空。", nameof(channelKey));
        if (fromVersion == null)
            return FailPush(target, "fromVersion is required.");
        if (toVersion == null)
            return FailPush(target, "toVersion is null.");
        if (target == null)
            throw new ArgumentNullException(nameof(target));
        if (channelKey.IndexOf("/AA", StringComparison.OrdinalIgnoreCase) >= 0)
            return FailPush(target, "AA Push not supported in this version.");

        var toCommit = TryLoadCommit(channelKey, toVersion);
        if (toCommit == null)
            return FailPush(target, $"Target commit missing: {toVersion.GetFullVersionString()}");
        if (string.IsNullOrEmpty(toCommit.PackageRootDir))
            return FailPush(target, "Target commit PackageRootDir is empty.");

        var fromCommit = TryLoadCommit(channelKey, fromVersion);
        if (fromCommit == null)
            return FailPush(target, $"Baseline commit missing: {fromVersion.GetFullVersionString()}");

        var delta = ArtifactDiffer.Diff(fromCommit.Artifacts, toCommit.Artifacts);
        var payload = new PushPayload
        {
            FromCommit = fromCommit,
            ToCommit = toCommit,
            AbManifestPath = Path.Combine(toCommit.PackageRootDir, FYAssetSettings.MANIFEST_FILE_NAME),
            PackageIndexPath = BuildPathManager.PackageIndexPath
        };

        for (int i = 0; i < delta.Added.Count; i++)
        {
            string filePath = Path.Combine(toCommit.PackageRootDir, "bundles", delta.Added[i].Name);
            if (!FileHelper.Exists(filePath))
                return FailPush(target, $"Missing delta file: {filePath}");
            payload.DeltaBundleFiles.Add(filePath);
        }
        for (int i = 0; i < delta.Modified.Count; i++)
        {
            string filePath = Path.Combine(toCommit.PackageRootDir, "bundles", delta.Modified[i].Name);
            if (!FileHelper.Exists(filePath))
                return FailPush(target, $"Missing delta file: {filePath}");
            payload.DeltaBundleFiles.Add(filePath);
        }

        var receipt = target.Push(payload);
        if (receipt == null || !receipt.Success)
            return receipt ?? FailPush(target, "Push target returned null receipt.");

        var history = TryLoadPushHistory(channelKey);
        history.Add(new PushHistoryEntry
        {
            FromVersion = fromVersion.GetFullVersionString(),
            ToVersion = toVersion.GetFullVersionString(),
            TargetId = receipt.TargetId,
            TargetLocation = receipt.TargetLocation,
            PushedAtUtc = receipt.PushedAtUtc,
            DeltaFileCount = payload.DeltaBundleFiles.Count
        });
        WritePushHistory(channelKey, history);
        return receipt;
    }

    public List<PushHistoryEntry> ListPushHistory(string channelKey)
    {
        return TryLoadPushHistory(channelKey);
    }

    private static RepositoryCommit TryLoadHead(string channelKey)
    {
        string headPath = Path.Combine(GetChannelRoot(channelKey), "HEAD.json");
        if (!FileHelper.Exists(headPath))
            return null;

        RepositoryHeadState headState;
        try
        {
            headState = SerializationUtility.ReadFromFile<RepositoryHeadState>(headPath);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[FileBuildRepository] 读取 HEAD 失败: {headPath}, 原因: {ex.Message}");
            return null;
        }

        if (headState == null || string.IsNullOrEmpty(headState.HeadVersion))
        {
            Debug.LogError($"[FileBuildRepository] HEAD 内容无效: {headPath}");
            return null;
        }

        string objectPath = Path.Combine(GetObjectsDir(channelKey), headState.HeadVersion + ".json");
        if (!FileHelper.Exists(objectPath))
        {
            Debug.LogError($"[FileBuildRepository] HEAD 指向的 commit 不存在: {objectPath}");
            return null;
        }

        return TryReadSnapshot(objectPath);
    }

    private static RepositoryCommit TryLoadCommit(string channelKey, VersionNumber version)
    {
        if (version == null)
            return null;
        string objectPath = Path.Combine(GetObjectsDir(channelKey), version.GetFullVersionString() + ".json");
        return FileHelper.Exists(objectPath) ? TryReadSnapshot(objectPath) : null;
    }

    private static RepositoryCommit TryReadSnapshot(string path)
    {
        try
        {
            return SerializationUtility.ReadFromFile<RepositoryCommit>(path);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[FileBuildRepository] 读取快照失败: {path}, 原因: {ex.Message}");
            return null;
        }
    }

    private static string GetChannelRoot(string channelKey)
    {
        string[] parts = (channelKey ?? string.Empty).Split('/');
        if (parts.Length == 2)
            return Path.Combine(SnapshotRoot, SanitizePathSegment(parts[0]), SanitizePathSegment(parts[1]));
        return Path.Combine(SnapshotRoot, SanitizePathSegment(channelKey));
    }

    private static string GetObjectsDir(string channelKey)
    {
        return Path.Combine(GetChannelRoot(channelKey), "objects");
    }

    private static string GetSnapshotFileName(VersionNumber version)
    {
        return $"{version.GetFullVersionString()}.json";
    }

    private static string GetPushHistoryPath(string channelKey)
    {
        return Path.Combine(GetChannelRoot(channelKey), "PushHistory.json");
    }

    private static List<PushHistoryEntry> TryLoadPushHistory(string channelKey)
    {
        string path = GetPushHistoryPath(channelKey);
        if (!FileHelper.Exists(path))
            return new List<PushHistoryEntry>();
        try
        {
            string json = FileHelper.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(json))
                return new List<PushHistoryEntry>();

            string wrapped = "{\"items\":" + json + "}";
            var document = SerializationUtility.DeserializeJson<PushHistoryArrayDocument>(wrapped);
            return document != null && document.items != null ? new List<PushHistoryEntry>(document.items) : new List<PushHistoryEntry>();
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[FileBuildRepository] 读取 PushHistory 失败: {path}, 原因: {ex.Message}");
            return new List<PushHistoryEntry>();
        }
    }

    private static void WritePushHistory(string channelKey, List<PushHistoryEntry> history)
    {
        string path = GetPushHistoryPath(channelKey);
        var builder = new System.Text.StringBuilder();
        builder.AppendLine("[");
        for (int i = 0; i < history.Count; i++)
        {
            if (i > 0)
                builder.AppendLine(",");
            string itemJson = SerializationUtility.SerializeToJson(history[i], true);
            builder.Append(itemJson);
        }
        builder.AppendLine();
        builder.Append("]");
        FileHelper.WriteAllTextAtomic(path, builder.ToString());
    }

    private static PushReceipt FailPush(IPushTarget target, string reason)
    {
        return new PushReceipt
        {
            Success = false,
            TargetId = target != null ? target.Id : string.Empty,
            TargetLocation = string.Empty,
            PushedAtUtc = DateTime.UtcNow.ToString("o"),
            FailureReason = reason
        };
    }

    private static string SanitizePathSegment(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "default";

        foreach (char c in Path.GetInvalidFileNameChars())
            value = value.Replace(c, '_');
        return value.Replace('/', '_').Replace('\\', '_');
    }

    private static bool ChannelKeyMatchesBackend(string channelKey, string backendMode)
    {
        if (string.IsNullOrEmpty(channelKey) || string.IsNullOrEmpty(backendMode))
            return false;
        string suffix = "/" + backendMode;
        return channelKey.EndsWith(suffix, StringComparison.Ordinal);
    }

    [Serializable]
    private sealed class PushHistoryArrayDocument
    {
        public PushHistoryEntry[] items;
    }
}
#endif
