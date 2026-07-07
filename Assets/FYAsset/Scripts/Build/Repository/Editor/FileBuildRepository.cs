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
    private static string SnapshotRoot => FYAssetPathUtility.JoinFilePath(BuildPathManager.ProjectRoot, "BuildData", "Snapshots");

    public RepositoryStatus GetStatus(string channelKey)
    {
        var status = new RepositoryStatus
        {
            ChannelKey = channelKey,
            HasHead = false,
            HasHeadError = false,
            HeadVersion = string.Empty,
            PackageName = string.Empty,
            ArtifactCount = 0
        };

        var head = TryLoadHead(channelKey);
        if (TryGetLastHeadError(channelKey, out string headError))
        {
            status.HasHeadError = true;
            status.HeadErrorReason = headError;
        }
        if (head == null)
            return status;

        status.HasHead = true;
        status.HeadVersion = head.Version != null ? head.Version.GetReleaseVersionString() : string.Empty;
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

    public RepositoryHealthReport GetHealth(string channelKey)
    {
        return BuildHealthReport(channelKey);
    }

    public RepositoryRepairResult Repair(string channelKey, bool dryRun)
    {
        var result = new RepositoryRepairResult
        {
            DryRun = dryRun,
            Before = BuildHealthReport(channelKey)
        };
        result.Actions.AddRange(result.Before.RepairActions);

        if (dryRun)
        {
            result.Success = true;
            result.Message = result.Actions.Count == 0
                ? "Repository repair dry run found no actions."
                : $"Repository repair dry run found {result.Actions.Count} action(s).";
            return result;
        }

        if (result.Actions.Count == 0)
        {
            result.Success = true;
            result.Message = "Repository repair found no actions.";
            result.After = result.Before;
            return result;
        }

        string channelRoot = GetChannelRoot(channelKey);
        string quarantineRoot = FYAssetPathUtility.JoinFilePath(
            channelRoot,
            "repair-quarantine",
            DateTime.UtcNow.ToString("yyyyMMddHHmmss") + "_" + Guid.NewGuid().ToString("N").Substring(0, 8));

        try
        {
            string headPath = FYAssetPathUtility.JoinFilePath(channelRoot, "HEAD.json");
            if (result.Before.HasFatalIssue && FileHelper.Exists(headPath))
                MoveFileToQuarantine(channelRoot, quarantineRoot, headPath);

            List<string> objectFiles = CollectRepairObjectFiles(channelKey);
            for (int i = 0; i < objectFiles.Count; i++)
                MoveFileToQuarantine(channelRoot, quarantineRoot, objectFiles[i]);

            result.After = BuildHealthReport(channelKey);
            result.Success = !result.After.HasFatalIssue;
            result.Message = result.Success
                ? $"Repository repair completed. Quarantine: {quarantineRoot}"
                : $"Repository repair completed but fatal issues remain. Quarantine: {quarantineRoot}";
            WriteRepairLog(channelKey, result);
            return result;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Message = $"Repository repair failed: {ex.Message}";
            return result;
        }
    }

    public RepositoryCommit GetHeadCommit(string channelKey)
    {
        var head = TryLoadHead(channelKey);
        if (head != null)
            return head;
        if (TryGetLastHeadError(channelKey, out string headError))
            throw new RepositoryHeadException(headError);
        throw new RepositoryHeadException($"Repository has no HEAD: {channelKey}");
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
            string versionName = Path.GetFileNameWithoutExtension(files[i]);
            if (!IsReleaseVersionString(versionName))
            {
                Debug.LogWarning($"[FileBuildRepository] 已忽略旧版本快照文件: {files[i]}");
                continue;
            }

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
        string objectsDir = FYAssetPathUtility.JoinFilePath(channelRoot, "objects");
        string objectPath = FYAssetPathUtility.JoinFilePath(objectsDir, GetSnapshotFileName(commit.Version));
        string headPath = FYAssetPathUtility.JoinFilePath(channelRoot, "HEAD.json");

        var parent = TryLoadHead(commit.ChannelKey);
        if (TryGetLastHeadError(commit.ChannelKey, out string headError))
        {
            throw new RepositoryHeadException(headError);
        }

        commit.ParentVersion = parent != null && parent.Version != null ? parent.Version.GetReleaseVersionString() : string.Empty;
        commit.CommitDelta = ArtifactDiffer.Diff(parent != null ? parent.Artifacts : null, commit.Artifacts);

        FileHelper.EnsureDirectory(objectsDir);
        FileHelper.WriteAllTextAtomic(objectPath, SerializationUtility.SerializeToJson(commit, true));

        var head = new RepositoryHeadState
        {
            HeadVersion = commit.Version.GetReleaseVersionString()
        };
        try
        {
            FileHelper.WriteAllTextAtomic(headPath, SerializationUtility.SerializeToJson(head, true));
        }
        catch (Exception ex)
        {
            bool objectDeleted = FileHelper.TryDelete(objectPath);
            string cleanup = objectDeleted ? "commit object deleted" : "commit object cleanup failed";
            throw new IOException($"HEAD write failed; {cleanup}: {objectPath}. Reason: {ex.Message}", ex);
        }
    }

    public bool TryRollbackHead(string channelKey, string expectedHeadVersion, string parentVersion, out string reason)
    {
        reason = string.Empty;
        if (string.IsNullOrEmpty(channelKey))
        {
            reason = "channelKey is empty.";
            return false;
        }
        if (string.IsNullOrEmpty(expectedHeadVersion))
        {
            reason = "expectedHeadVersion is empty.";
            return false;
        }

        string channelRoot = GetChannelRoot(channelKey);
        string headPath = FYAssetPathUtility.JoinFilePath(channelRoot, "HEAD.json");
        if (!FileHelper.Exists(headPath))
        {
            reason = "HEAD already missing.";
            return true;
        }

        RepositoryHeadState currentHead;
        try
        {
            currentHead = SerializationUtility.ReadFromFile<RepositoryHeadState>(headPath);
        }
        catch (Exception ex)
        {
            reason = $"HEAD read failed during rollback: {headPath}, {ex.Message}";
            return false;
        }

        if (currentHead == null || string.IsNullOrEmpty(currentHead.HeadVersion))
        {
            reason = $"HEAD content invalid during rollback: {headPath}";
            return false;
        }

        if (!string.Equals(currentHead.HeadVersion, expectedHeadVersion, StringComparison.Ordinal))
        {
            reason = $"HEAD changed after commit. Expected={expectedHeadVersion}, Actual={currentHead.HeadVersion}";
            return false;
        }

        try
        {
            if (string.IsNullOrEmpty(parentVersion))
            {
                if (!FileHelper.TryDelete(headPath))
                {
                    reason = $"Failed to delete HEAD during rollback: {headPath}";
                    return false;
                }
            }
            else
            {
                if (!IsReleaseVersionString(parentVersion))
                {
                    reason = $"ParentVersion is not a release version: {parentVersion}";
                    return false;
                }

                string parentObjectPath = FYAssetPathUtility.JoinFilePath(GetObjectsDir(channelKey), parentVersion + ".json");
                if (!FileHelper.Exists(parentObjectPath))
                {
                    reason = $"Parent commit object missing during rollback: {parentObjectPath}";
                    return false;
                }

                var restoredHead = new RepositoryHeadState
                {
                    HeadVersion = parentVersion
                };
                FileHelper.WriteAllTextAtomic(headPath, SerializationUtility.SerializeToJson(restoredHead, true));
            }

            string objectPath = FYAssetPathUtility.JoinFilePath(GetObjectsDir(channelKey), expectedHeadVersion + ".json");
            if (FileHelper.Exists(objectPath) && !FileHelper.TryDelete(objectPath))
            {
                reason = $"HEAD rolled back, but failed to delete rolled-back commit object: {objectPath}";
                return false;
            }

            reason = string.IsNullOrEmpty(parentVersion)
                ? "HEAD removed; rolled-back commit object deleted."
                : $"HEAD restored to {parentVersion}; rolled-back commit object deleted.";
            return true;
        }
        catch (Exception ex)
        {
            reason = $"HEAD rollback failed: {ex.Message}";
            return false;
        }
    }

    public PushReceipt PushHead(string channelKey, IPushTarget target)
    {
        if (string.IsNullOrEmpty(channelKey))
            throw new ArgumentException("channelKey 不能为空。", nameof(channelKey));
        if (target == null)
            throw new ArgumentNullException(nameof(target));

        var toCommit = GetHeadCommit(channelKey);
        VersionNumber fromVersion = string.IsNullOrEmpty(toCommit.ParentVersion) ? null : VersionNumber.Parse(toCommit.ParentVersion);
        var fromCommit = fromVersion != null ? TryLoadCommit(channelKey, fromVersion) : null;
        if (fromVersion != null && fromCommit == null)
            return FailPush(target, $"Parent commit missing: {toCommit.ParentVersion}");

        ArtifactDelta delta = toCommit.CommitDelta ?? ArtifactDiffer.Diff(fromCommit != null ? fromCommit.Artifacts : null, toCommit.Artifacts);
        return PushResolved(channelKey, fromCommit, toCommit, target, delta);
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

        var toCommit = TryLoadCommit(channelKey, toVersion);
        if (toCommit == null)
            return FailPush(target, $"Target commit missing: {toVersion.GetReleaseVersionString()}");
        if (string.IsNullOrEmpty(toCommit.PackageRootDir))
            return FailPush(target, "Target commit PackageRootDir is empty.");

        var fromCommit = TryLoadCommit(channelKey, fromVersion);
        if (fromCommit == null)
            return FailPush(target, $"Baseline commit missing: {fromVersion.GetReleaseVersionString()}");

        return PushResolved(channelKey, fromCommit, toCommit, target, ArtifactDiffer.Diff(fromCommit.Artifacts, toCommit.Artifacts));
    }

    public List<PushHistoryEntry> ListPushHistory(string channelKey)
    {
        return TryLoadPushHistory(channelKey);
    }

    private static RepositoryCommit TryLoadHead(string channelKey)
    {
        ClearLastHeadError(channelKey);
        string headPath = FYAssetPathUtility.JoinFilePath(GetChannelRoot(channelKey), "HEAD.json");
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
            SetLastHeadError(channelKey, $"HEAD read failed: {headPath}");
            return null;
        }

        if (headState == null || string.IsNullOrEmpty(headState.HeadVersion))
        {
            Debug.LogError($"[FileBuildRepository] HEAD 内容无效: {headPath}");
            SetLastHeadError(channelKey, $"HEAD content invalid: {headPath}");
            return null;
        }

        if (!IsReleaseVersionString(headState.HeadVersion))
        {
            Debug.LogError($"[FileBuildRepository] HEAD 版本不是 release identity: {headState.HeadVersion}");
            SetLastHeadError(channelKey, $"HEAD version is not a release identity: {headState.HeadVersion}");
            return null;
        }

        string objectPath = FYAssetPathUtility.JoinFilePath(GetObjectsDir(channelKey), headState.HeadVersion + ".json");
        if (!FileHelper.Exists(objectPath))
        {
            Debug.LogError($"[FileBuildRepository] HEAD 指向的 commit 不存在: {objectPath}");
            SetLastHeadError(channelKey, $"HEAD target missing: {objectPath}");
            return null;
        }

        var headCommit = TryReadSnapshot(objectPath);
        if (headCommit == null)
        {
            SetLastHeadError(channelKey, $"HEAD snapshot invalid: {objectPath}");
            return null;
        }
        return headCommit;
    }

    private static readonly Dictionary<string, string> LastHeadErrors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    private static void SetLastHeadError(string channelKey, string reason)
    {
        LastHeadErrors[channelKey ?? string.Empty] = reason;
    }

    private static void ClearLastHeadError(string channelKey)
    {
        LastHeadErrors.Remove(channelKey ?? string.Empty);
    }

    private static bool TryGetLastHeadError(string channelKey, out string reason)
    {
        return LastHeadErrors.TryGetValue(channelKey ?? string.Empty, out reason);
    }

    private static RepositoryCommit TryLoadCommit(string channelKey, VersionNumber version)
    {
        if (version == null)
            return null;
        string objectPath = FYAssetPathUtility.JoinFilePath(GetObjectsDir(channelKey), version.GetReleaseVersionString() + ".json");
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
            return FYAssetPathUtility.JoinFilePath(SnapshotRoot, SanitizePathSegment(parts[0]), SanitizePathSegment(parts[1]));
        return FYAssetPathUtility.JoinFilePath(SnapshotRoot, SanitizePathSegment(channelKey));
    }

    private static string GetObjectsDir(string channelKey)
    {
        return FYAssetPathUtility.JoinFilePath(GetChannelRoot(channelKey), "objects");
    }

    private static string GetSnapshotFileName(VersionNumber version)
    {
        return $"{version.GetReleaseVersionString()}.json";
    }

    private static string GetPushHistoryPath(string channelKey)
    {
        return FYAssetPathUtility.JoinFilePath(GetChannelRoot(channelKey), "PushHistory.json");
    }

    private static bool IsReleaseVersionString(string value)
    {
        return VersionNumber.TryParse(value, out _);
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

    private static PushReceipt PushResolved(string channelKey, RepositoryCommit fromCommit, RepositoryCommit toCommit, IPushTarget target, ArtifactDelta delta)
    {
        RepositoryHealthReport health = BuildHealthReport(channelKey);
        if (health.HasFatalIssue)
            return FailPush(target, health.Summary);

        if (toCommit == null)
            return FailPush(target, "Target commit is null.");
        if (string.IsNullOrEmpty(toCommit.PackageRootDir))
            return FailPush(target, "Target commit PackageRootDir is empty.");

        var payload = new PushPayload
        {
            FromCommit = fromCommit,
            ToCommit = toCommit,
            ChangedArtifactCount = CountDelta(delta)
        };

        var receipt = target.Push(payload);
        if (receipt == null || !receipt.Success)
            return receipt ?? FailPush(target, "Push target returned null receipt.");

        var history = TryLoadPushHistory(channelKey);
        history.Add(new PushHistoryEntry
        {
            FromVersion = fromCommit != null && fromCommit.Version != null ? fromCommit.Version.GetReleaseVersionString() : string.Empty,
            ToVersion = toCommit.Version != null ? toCommit.Version.GetReleaseVersionString() : string.Empty,
            TargetId = receipt.TargetId,
            TargetLocation = receipt.TargetLocation,
            PushedAtUtc = receipt.PushedAtUtc,
            DeltaFileCount = payload.ChangedArtifactCount
        });
        WritePushHistory(channelKey, history);
        return receipt;
    }

    private static int CountDelta(ArtifactDelta delta)
    {
        if (delta == null)
            return 0;
        int added = delta.Added != null ? delta.Added.Count : 0;
        int modified = delta.Modified != null ? delta.Modified.Count : 0;
        int removed = delta.Removed != null ? delta.Removed.Count : 0;
        return added + modified + removed;
    }

    private static RepositoryHealthReport BuildHealthReport(string channelKey)
    {
        var report = new RepositoryHealthReport
        {
            ChannelKey = channelKey ?? string.Empty
        };

        var reachable = new HashSet<string>(StringComparer.Ordinal);
        LoadHeadForHealth(channelKey, report, reachable);
        CollectObjectHealth(channelKey, report, reachable);

        string repairLogPath = GetRepairLogPath(channelKey);
        if (FileHelper.Exists(repairLogPath))
        {
            try
            {
                report.LastRepairAtUtc = File.GetLastWriteTimeUtc(repairLogPath).ToString("o");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[FileBuildRepository] 读取 repair log 时间失败: {repairLogPath}, 原因: {ex.Message}");
                report.LastRepairAtUtc = string.Empty;
            }
        }

        FinalizeHealthReport(report);
        return report;
    }

    private static void LoadHeadForHealth(string channelKey, RepositoryHealthReport report, HashSet<string> reachable)
    {
        string headPath = FYAssetPathUtility.JoinFilePath(GetChannelRoot(channelKey), "HEAD.json");
        if (!FileHelper.Exists(headPath))
            return;

        RepositoryHeadState headState;
        try
        {
            headState = SerializationUtility.ReadFromFile<RepositoryHeadState>(headPath);
        }
        catch (Exception ex)
        {
            AddFatal(report, $"HEAD read failed: {headPath}, {ex.Message}");
            AddRepairAction(report, $"Quarantine HEAD: {headPath}");
            return;
        }

        if (headState == null || string.IsNullOrEmpty(headState.HeadVersion))
        {
            AddFatal(report, $"HEAD content invalid: {headPath}");
            AddRepairAction(report, $"Quarantine HEAD: {headPath}");
            return;
        }

        if (!IsReleaseVersionString(headState.HeadVersion))
        {
            AddFatal(report, $"HEAD version is not a release identity: {headState.HeadVersion}");
            AddRepairAction(report, $"Quarantine HEAD: {headPath}");
            return;
        }

        WalkReachableCommits(channelKey, headState.HeadVersion, report, reachable);
        if (report.HasFatalIssue)
            AddRepairAction(report, $"Quarantine HEAD: {headPath}");
    }

    private static void WalkReachableCommits(string channelKey, string headVersion, RepositoryHealthReport report, HashSet<string> reachable)
    {
        string version = headVersion;
        while (!string.IsNullOrEmpty(version))
        {
            if (!IsReleaseVersionString(version))
            {
                AddFatal(report, $"Commit parent version is not a release identity: {version}");
                return;
            }

            if (!reachable.Add(version))
            {
                AddFatal(report, $"Repository parent chain has a cycle at {version}.");
                return;
            }

            string objectPath = FYAssetPathUtility.JoinFilePath(GetObjectsDir(channelKey), version + ".json");
            if (!FileHelper.Exists(objectPath))
            {
                AddFatal(report, $"HEAD chain commit object missing: {objectPath}");
                return;
            }

            if (!TryReadSnapshotForHealth(objectPath, out RepositoryCommit commit, out string reason))
            {
                AddFatal(report, $"HEAD chain commit object invalid: {objectPath}, {reason}");
                return;
            }

            version = commit != null ? commit.ParentVersion : string.Empty;
        }
    }

    private static void CollectObjectHealth(string channelKey, RepositoryHealthReport report, HashSet<string> reachable)
    {
        string objectsDir = GetObjectsDir(channelKey);
        if (!FileHelper.DirectoryExists(objectsDir))
            return;

        string[] files = FileHelper.GetFiles(objectsDir, "*.json", SearchOption.TopDirectoryOnly);
        Array.Sort(files, StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < files.Length; i++)
        {
            string versionName = Path.GetFileNameWithoutExtension(files[i]);
            if (!IsReleaseVersionString(versionName))
            {
                report.LegacyObjectCount++;
                AddWarning(report, $"Legacy object ignored: {files[i]}");
                AddRepairAction(report, $"Quarantine legacy object: {files[i]}");
                continue;
            }

            if (!TryReadSnapshotForHealth(files[i], out _, out string reason))
            {
                report.InvalidObjectCount++;
                AddWarning(report, $"Invalid object ignored: {files[i]}, {reason}");
                AddRepairAction(report, $"Quarantine invalid object: {files[i]}");
                continue;
            }

            if (!reachable.Contains(versionName))
            {
                report.LooseObjectCount++;
                AddWarning(report, $"Loose object ignored: {files[i]}");
                AddRepairAction(report, $"Quarantine loose object: {files[i]}");
            }
        }
    }

    private static List<string> CollectRepairObjectFiles(string channelKey)
    {
        var report = new RepositoryHealthReport { ChannelKey = channelKey ?? string.Empty };
        var reachable = new HashSet<string>(StringComparer.Ordinal);
        LoadHeadForHealth(channelKey, report, reachable);

        var filesToMove = new List<string>();
        string objectsDir = GetObjectsDir(channelKey);
        if (!FileHelper.DirectoryExists(objectsDir))
            return filesToMove;

        string[] files = FileHelper.GetFiles(objectsDir, "*.json", SearchOption.TopDirectoryOnly);
        Array.Sort(files, StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < files.Length; i++)
        {
            string versionName = Path.GetFileNameWithoutExtension(files[i]);
            if (!IsReleaseVersionString(versionName) ||
                !TryReadSnapshotForHealth(files[i], out _, out _) ||
                !reachable.Contains(versionName))
            {
                filesToMove.Add(files[i]);
            }
        }

        return filesToMove;
    }

    private static bool TryReadSnapshotForHealth(string path, out RepositoryCommit commit, out string reason)
    {
        commit = null;
        reason = string.Empty;
        try
        {
            commit = SerializationUtility.ReadFromFile<RepositoryCommit>(path);
            if (commit == null)
            {
                reason = "deserialized commit is null";
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            reason = ex.Message;
            return false;
        }
    }

    private static void MoveFileToQuarantine(string channelRoot, string quarantineRoot, string sourcePath)
    {
        if (!FileHelper.Exists(sourcePath))
            return;

        string relativePath = FYAssetPathUtility.GetRelativeFilePath(channelRoot, sourcePath);
        string targetPath = FYAssetPathUtility.JoinFilePath(quarantineRoot, relativePath);
        FileHelper.EnsureDirectoryForFile(targetPath);
        if (FileHelper.Exists(targetPath))
            FileHelper.TryDelete(targetPath);
        File.Move(sourcePath, targetPath);
    }

    private static void WriteRepairLog(string channelKey, RepositoryRepairResult result)
    {
        string path = GetRepairLogPath(channelKey);
        FileHelper.WriteAllTextAtomic(path, SerializationUtility.SerializeToJson(result, true));
    }

    private static string GetRepairLogPath(string channelKey)
    {
        return FYAssetPathUtility.JoinFilePath(GetChannelRoot(channelKey), "RepositoryRepairLog.json");
    }

    private static void AddFatal(RepositoryHealthReport report, string message)
    {
        report.HasFatalIssue = true;
        report.FatalCount++;
        report.FatalIssues.Add(message);
    }

    private static void AddWarning(RepositoryHealthReport report, string message)
    {
        report.WarningCount++;
        report.Warnings.Add(message);
    }

    private static void AddRepairAction(RepositoryHealthReport report, string action)
    {
        if (string.IsNullOrEmpty(action))
            return;
        for (int i = 0; i < report.RepairActions.Count; i++)
        {
            if (string.Equals(report.RepairActions[i], action, StringComparison.Ordinal))
                return;
        }
        report.RepairActions.Add(action);
    }

    private static void FinalizeHealthReport(RepositoryHealthReport report)
    {
        if (report.HasFatalIssue)
        {
            report.Summary = $"Repository health failed. Fatal={report.FatalCount}, Warnings={report.WarningCount}. Run explicit Repair before build or Push.";
            return;
        }

        if (report.WarningCount > 0)
        {
            report.Summary = $"Repository health OK with cleanup warnings. Warnings={report.WarningCount}, Loose={report.LooseObjectCount}, Legacy={report.LegacyObjectCount}, Invalid={report.InvalidObjectCount}.";
            return;
        }

        report.Summary = "Repository health OK.";
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
