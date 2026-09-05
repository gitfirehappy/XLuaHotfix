#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 项目/Target 快照、恢复与 durable recovery。
/// </summary>
public static class BuildTestState
{
    [Serializable]
    private sealed class VersionSnapshot
    {
        public int Major;
        public int Minor;
        public int Patch;
        public int Build;
        public string Channel;
        public string LastBuildTime;
        public int DailyBuildCount;
    }

    [Serializable]
    private sealed class SettingsSnapshot
    {
        public bool UseABBackend;
        public string AAHotfixUrl;
        public string ABHotfixUrl;
    }

    public static BuildTestRecoveryRecord WriteRecovery(
        string runRoot,
        BuildTestRequest request,
        List<BuildTestTargetSnapshot> targets)
    {
        var record = new BuildTestRecoveryRecord
        {
            RunId = Path.GetFileName(runRoot),
            Backend = request.Backend.ToString(),
            Mode = request.Mode.ToString(),
            CreatedAtUtc = DateTime.UtcNow.ToString("o"),
            Completed = false,
            Restored = false,
            ProjectBackupRoot = BuildTestPaths.ProjectBackupRoot(runRoot),
            TargetsBackupRoot = BuildTestPaths.TargetsBackupRoot(runRoot),
            Targets = targets ?? new List<BuildTestTargetSnapshot>(),
            FixturePaths = new List<string>
            {
                BuildTestConstants.SyncAssetPath,
                BuildTestConstants.RawAssetPath
            }
        };
        PersistRecovery(runRoot, record);
        return record;
    }

    public static void MarkRecoveryCompleted(string runRoot, BuildTestRecoveryRecord record, bool restored)
    {
        if (record == null)
            return;
        record.Completed = true;
        record.Restored = restored;
        PersistRecovery(runRoot, record);
    }

    public static bool TryRecoverStaleRun(out BuildTestResult result)
    {
        result = null;
        string root = BuildTestPaths.TestRunsRoot;
        if (!FileHelper.DirectoryExists(root))
            return false;

        string[] recoveryFiles = Directory.GetFiles(root, "recovery.json", SearchOption.AllDirectories);
        for (int i = 0; i < recoveryFiles.Length; i++)
        {
            string recoveryPath = recoveryFiles[i];
            if (!BuildTestPaths.IsInsideTestRuns(recoveryPath))
                continue;

            string json = File.ReadAllText(recoveryPath, Encoding.UTF8);
            var record = SerializationUtility.DeserializeJson<BuildTestRecoveryRecord>(json);
            if (record == null || record.Completed)
                continue;

            string runRoot = Path.GetDirectoryName(recoveryPath);
            bool restored = RestoreFromRecord(record, out string failure);
            record.Completed = true;
            record.Restored = restored;
            PersistRecovery(runRoot, record);

            result = new BuildTestResult
            {
                Passed = false,
                ExitCode = restored ? BuildTestExitCodes.PreconditionFailed : BuildTestExitCodes.RestoreFailed,
                Backend = record.Backend,
                Mode = record.Mode,
                RunId = record.RunId,
                RunRoot = runRoot,
                RecoveryOnly = true,
                RestorationSucceeded = restored,
                FirstFailure = restored
                    ? "Stale recovery restored. Inspect evidence before rerun."
                    : "Stale recovery failed: " + failure,
                FailedStage = BuildTestStage.RecoveryOnly.ToString()
            };
            FileHelper.WriteAllTextAtomic(
                BuildTestPaths.ResultJson(runRoot),
                SerializationUtility.SerializeToJson(result, true));
            return true;
        }

        return false;
    }

    public static void SnapshotProject(string runRoot, BuildTestBackend backend)
    {
        string backup = BuildTestPaths.ProjectBackupRoot(runRoot);
        FileHelper.EnsureDirectory(backup);

        SnapshotVersion(backup);
        SnapshotSettings(backup);
        SnapshotPath(FYAssetSettings.Instance.VersionRecordPath, backup, "version.asset");
        SnapshotPath(FYAssetSettings.Instance.BuildIndexJsonPath, backup, "bootstrap_buildindex.json");

        string packageIndex = BuildPathManager.PackageIndexPath;
        SnapshotPath(packageIndex, backup, "package_index.json");

        string packagesDir = BuildPathManager.PackagesDir;
        SnapshotDirectory(packagesDir, backup, "packages");

        // Repository lives under project-root BuildData/Baselines (not HotfixOutput).
        SnapshotDirectory(
            FYAssetPathUtility.JoinFilePath(BuildPathManager.ProjectRoot, "BuildData"),
            backup,
            "builddata");

        SnapshotDirectory(Application.streamingAssetsPath, backup, "streamingassets");
        SnapshotPath(BuildTestConstants.SyncAssetPath, backup, "fixture_sync.txt");
        SnapshotPath(BuildTestConstants.RawAssetPath, backup, "fixture_raw.fyraw");
        SnapshotPath(BuildTestConstants.AsyncAssetPath, backup, "fixture_async.asset");
        // AA Hotfix group move undo log is project-owned state that blocks subsequent Hotfix builds.
        SnapshotPath("Assets/FYAsset/Editor/Generated/HotfixGroupUndoLog.json", backup, "aa_hotfix_group_undo.json");
    }

    public static void RestoreProject(string runRoot, BuildTestBackend backend)
    {
        string backup = BuildTestPaths.ProjectBackupRoot(runRoot);
        RestoreVersion(backup);
        RestoreSettings(backup);
        RestorePath(FYAssetSettings.Instance.BuildIndexJsonPath, backup, "bootstrap_buildindex.json");
        RestorePath(BuildPathManager.PackageIndexPath, backup, "package_index.json");
        RestoreDirectory(BuildPathManager.PackagesDir, backup, "packages");
        RestoreDirectory(
            FYAssetPathUtility.JoinFilePath(BuildPathManager.ProjectRoot, "BuildData"),
            backup,
            "builddata");
        RestoreDirectory(Application.streamingAssetsPath, backup, "streamingassets");
        RestorePath(BuildTestConstants.SyncAssetPath, backup, "fixture_sync.txt");
        RestorePath(BuildTestConstants.RawAssetPath, backup, "fixture_raw.fyraw");
        RestorePath("Assets/FYAsset/Editor/Generated/HotfixGroupUndoLog.json", backup, "aa_hotfix_group_undo.json");
        // Keep async SO content from fixture ensure path; versioned marker is fixed.
        AssetDatabase.Refresh();
    }

    public static void PrepareIsolatedFullProject(BuildTestBackend backend)
    {
        VersionRecord version = AssetDatabase.LoadAssetAtPath<VersionRecord>(
            FYAssetSettings.Instance.VersionRecordPath);
        if (version == null)
            throw new InvalidOperationException("VersionRecord missing.");

        version.CurrentVersion = new VersionNumber
        {
            Major = 1,
            Minor = 0,
            Patch = 0,
            Build = 0,
            Channel = string.Empty
        };
        version.DailyBuildCount = 0;
        version.LastBuildTime = string.Empty;
        EditorUtility.SetDirty(version);
        AssetDatabase.SaveAssets();

        string channelKey = BuildBaselineStore.GetChannelKey(string.Empty, ToBackendMode(backend));
        BuildBaselineStore.ClearForTest(channelKey);

        // Clear residual AA Hotfix group moves without UI dialogs.
        if (backend == BuildTestBackend.AA)
            ClearAAHotfixGroupMovesForTest();
    }

    public static void ClearAAHotfixGroupMovesForTest()
    {
        try
        {
            HotfixGroupRestoreResult restore = TaskMoveAAHotfixGroups.Restore();
            if (restore != null && !string.IsNullOrEmpty(restore.Message))
                Debug.Log("[BuildTestState] AA Hotfix group 恢复 / restore: " + restore.Message);
            HotfixGroupRestoreResult discard = TaskMoveAAHotfixGroups.DiscardUnrestorableRecords();
            if (discard != null && !string.IsNullOrEmpty(discard.Message))
                Debug.Log("[BuildTestState] AA Hotfix group 丢弃 / discard: " + discard.Message);
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[BuildTestState] AA Hotfix group 清理失败 / cleanup failed: " + ex.Message);
        }
    }

    public static List<BuildTestTargetSnapshot> FreezeTargets(
        BuildTestBackend backend,
        IList<string> targetIds,
        IList<string> externalConfirms)
    {
        if (targetIds == null || targetIds.Count == 0)
            throw new InvalidOperationException("At least one --target is required.");

        var snapshots = new List<BuildTestTargetSnapshot>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var confirmSet = new HashSet<string>(
            externalConfirms ?? Array.Empty<string>(),
            StringComparer.OrdinalIgnoreCase);
        var serviceRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string backendName = BuildTestPaths.BackendSegment(backend);

        for (int i = 0; i < targetIds.Count; i++)
        {
            string id = targetIds[i];
            if (string.IsNullOrWhiteSpace(id))
                throw new InvalidOperationException("Empty target id.");
            if (!seen.Add(id))
                throw new InvalidOperationException("Duplicate target id: " + id);

            PushTargetConfig config = PushTargetUtility.FindConfig(id);
            if (config == null)
                throw new InvalidOperationException("Unknown target id: " + id);
            if (string.IsNullOrWhiteSpace(config.Path))
                throw new InvalidOperationException("Target Path is empty: " + id);
            if (!FYAssetPathUtility.IsHttpUrl(config.PublicBaseUrl))
                throw new InvalidOperationException("Target PublicBaseUrl invalid: " + id);
            if (config.Type != PushTargetType.LocalDirectory && config.Type != PushTargetType.CloudflarePages)
                throw new InvalidOperationException("Unsupported target type: " + id);

            bool external = config.Type != PushTargetType.LocalDirectory;
            if (external && !confirmSet.Contains(id))
                throw new InvalidOperationException(
                    "External target requires --confirm-external-publish " + id);

            string serviceRoot = PushTargetUtility.ResolveServiceRoot(config);
            if (!serviceRoots.Add(FYAssetPathUtility.NormalizePath(serviceRoot)))
                throw new InvalidOperationException("Service-root collision for target: " + id);

            string runtimeUrl = PushTargetUtility.GetBackendHotfixUrl(config, ToBackendMode(backend));
            snapshots.Add(new BuildTestTargetSnapshot
            {
                TargetId = config.Id,
                TargetType = config.Type,
                ServiceRoot = serviceRoot,
                BackendPublishRoot = PushTargetUtility.ResolveBackendRoot(config, backendName),
                PublicBaseUrl = config.PublicBaseUrl,
                RuntimeUrl = runtimeUrl,
                PackageIndexUrl = FYAssetPathUtility.JoinUrl(runtimeUrl, FYAssetSettings.PACKAGE_INDEX_FILE_NAME),
                RequiresExternalConfirm = external
            });
        }

        return snapshots;
    }

    public static void SnapshotTargets(string runRoot, List<BuildTestTargetSnapshot> targets)
    {
        string backupRoot = BuildTestPaths.TargetsBackupRoot(runRoot);
        for (int i = 0; i < targets.Count; i++)
        {
            BuildTestTargetSnapshot target = targets[i];
            string targetBackup = FYAssetPathUtility.JoinFilePath(backupRoot, Sanitize(target.TargetId));
            FileHelper.EnsureDirectory(targetBackup);
            SnapshotDirectory(target.ServiceRoot, targetBackup, "service");

            if (target.TargetType == PushTargetType.CloudflarePages)
                AssertCloudflareMirrorConsistent(target);

            var meta = new
            {
                target.TargetId,
                target.TargetType,
                target.ServiceRoot,
                target.PackageIndexUrl,
                SnapshotAtUtc = DateTime.UtcNow.ToString("o")
            };
            FileHelper.WriteAllTextAtomic(
                FYAssetPathUtility.JoinFilePath(BuildTestPaths.TargetDir(runRoot, target.TargetId), "snapshot-meta.json"),
                SerializationUtility.SerializeToJson(meta, true));
        }
    }

    public static void RestoreTarget(string runRoot, BuildTestTargetSnapshot target)
    {
        string targetBackup = FYAssetPathUtility.JoinFilePath(
            BuildTestPaths.TargetsBackupRoot(runRoot),
            Sanitize(target.TargetId),
            "service");
        RestoreDirectory(target.ServiceRoot, Path.GetDirectoryName(targetBackup), "service");

        if (target.TargetType == PushTargetType.CloudflarePages)
        {
            PushTargetConfig config = PushTargetUtility.FindConfig(target.TargetId);
            IPushTarget pushTarget = CompatPushTargetFactory.CreateFull(config);
            // Redeploy restored mirror via Cloudflare target by pushing empty? Use wrangler deploy of service root.
            RedeployCloudflareServiceRoot(target);
        }
    }

    public static void ProbeTargetIdentity(
        BuildTestTargetSnapshot target,
        string expectedBackend,
        string expectedPackage,
        string expectedVersion,
        bool requirePackagePresent,
        string probePath)
    {
        PackageIndex index = ReadPackageIndex(target);
        if (index == null)
            throw new InvalidOperationException("PackageIndex missing on target: " + target.TargetId);
        if (!string.Equals(index.BackendMode, expectedBackend, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"Target backend mismatch {target.TargetId}: {index.BackendMode} != {expectedBackend}");
        if (!string.Equals(index.LatestPackage, expectedPackage, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"Target package mismatch {target.TargetId}: {index.LatestPackage} != {expectedPackage}");
        string version = index.LatestVersion != null ? index.LatestVersion.GetReleaseVersionString() : string.Empty;
        if (!string.Equals(version, expectedVersion, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"Target version mismatch {target.TargetId}: {version} != {expectedVersion}");

        if (requirePackagePresent)
        {
            string packageDir = FYAssetPathUtility.JoinFilePath(
                target.BackendPublishRoot,
                FYAssetSettings.Instance.BuildPackagesFolderName,
                expectedPackage);
            if (!FileHelper.DirectoryExists(packageDir))
                throw new InvalidOperationException("Target package dir missing: " + packageDir);
        }

        if (!string.IsNullOrEmpty(probePath))
        {
            FileHelper.WriteAllTextAtomic(
                probePath,
                SerializationUtility.SerializeToJson(new
                {
                    target.TargetId,
                    index.BackendMode,
                    index.LatestPackage,
                    Version = version,
                    ProbedAtUtc = DateTime.UtcNow.ToString("o")
                }, true));
        }
    }

    public static void PublishHeadToTarget(BuildTestBackend backend, BuildTestTargetSnapshot target, string publishJsonPath)
    {
        string channelKey = BuildBaselineStore.GetChannelKey(string.Empty, ToBackendMode(backend));
        PushTargetConfig config = PushTargetUtility.FindConfig(target.TargetId);
        IPushTarget pushTarget = CompatPushTargetFactory.CreateFull(config);
        PushReceipt receipt = BuildPublisher.PushLatest(channelKey, pushTarget);
        FileHelper.WriteAllTextAtomic(
            publishJsonPath,
            SerializationUtility.SerializeToJson(receipt, true));
        if (receipt == null || !receipt.Success)
            throw new InvalidOperationException(
                $"Publish failed for {target.TargetId}: {receipt?.FailureReason}");
    }

    public static PackageIndex ReadPackageIndex(BuildTestTargetSnapshot target)
    {
        string path = FYAssetPathUtility.JoinFilePath(
            target.BackendPublishRoot,
            FYAssetSettings.PACKAGE_INDEX_FILE_NAME);
        if (FileHelper.Exists(path))
            return SerializationUtility.DeserializeJson<PackageIndex>(File.ReadAllText(path, Encoding.UTF8));

        // Cloudflare may only expose public URL after deploy; try HTTP.
        try
        {
            using var client = new WebClient();
            string json = client.DownloadString(target.PackageIndexUrl);
            return SerializationUtility.DeserializeJson<PackageIndex>(json);
        }
        catch
        {
            return null;
        }
    }

    public static BackendMode ToBackendMode(BuildTestBackend backend)
    {
        return backend == BuildTestBackend.AB ? BackendMode.ABManifest : BackendMode.AA;
    }

    private static void PersistRecovery(string runRoot, BuildTestRecoveryRecord record)
    {
        FileHelper.WriteAllTextAtomic(
            BuildTestPaths.RecoveryJson(runRoot),
            SerializationUtility.SerializeToJson(record, true));
    }

    private static bool RestoreFromRecord(BuildTestRecoveryRecord record, out string failure)
    {
        failure = string.Empty;
        try
        {
            if (!Enum.TryParse(record.Backend, true, out BuildTestBackend backend))
                backend = BuildTestBackend.AA;

            string runRoot = Path.GetDirectoryName(
                FYAssetPathUtility.JoinFilePath(record.ProjectBackupRoot, "..", ".."));
            // recovery path layout: runRoot/backup/project
            runRoot = Path.GetFullPath(Path.Combine(record.ProjectBackupRoot, "..", ".."));
            RestoreProject(runRoot, backend);
            if (record.Targets != null)
            {
                for (int i = 0; i < record.Targets.Count; i++)
                    RestoreTarget(runRoot, record.Targets[i]);
            }
            return true;
        }
        catch (Exception ex)
        {
            failure = ex.Message;
            return false;
        }
    }

    private static void SnapshotVersion(string backup)
    {
        VersionRecord version = AssetDatabase.LoadAssetAtPath<VersionRecord>(
            FYAssetSettings.Instance.VersionRecordPath);
        if (version == null)
            return;
        var snap = new VersionSnapshot
        {
            Major = version.CurrentVersion != null ? version.CurrentVersion.Major : 1,
            Minor = version.CurrentVersion != null ? version.CurrentVersion.Minor : 0,
            Patch = version.CurrentVersion != null ? version.CurrentVersion.Patch : 0,
            Build = version.CurrentVersion != null ? version.CurrentVersion.Build : 0,
            Channel = version.CurrentVersion != null ? version.CurrentVersion.Channel : string.Empty,
            LastBuildTime = version.LastBuildTime,
            DailyBuildCount = version.DailyBuildCount
        };
        FileHelper.WriteAllTextAtomic(
            FYAssetPathUtility.JoinFilePath(backup, "version.json"),
            SerializationUtility.SerializeToJson(snap, true));
    }

    private static void RestoreVersion(string backup)
    {
        string path = FYAssetPathUtility.JoinFilePath(backup, "version.json");
        if (!FileHelper.Exists(path))
            return;
        var snap = SerializationUtility.DeserializeJson<VersionSnapshot>(File.ReadAllText(path, Encoding.UTF8));
        VersionRecord version = AssetDatabase.LoadAssetAtPath<VersionRecord>(
            FYAssetSettings.Instance.VersionRecordPath);
        if (version == null || snap == null)
            return;
        version.CurrentVersion = new VersionNumber
        {
            Major = snap.Major,
            Minor = snap.Minor,
            Patch = snap.Patch,
            Build = snap.Build,
            Channel = snap.Channel ?? string.Empty
        };
        version.LastBuildTime = snap.LastBuildTime;
        version.DailyBuildCount = snap.DailyBuildCount;
        EditorUtility.SetDirty(version);
        AssetDatabase.SaveAssets();
    }

    private static void SnapshotSettings(string backup)
    {
        var snap = new SettingsSnapshot
        {
            UseABBackend = FYAssetSettings.Instance.UseABBackend,
            AAHotfixUrl = FYAssetAASettings.Instance.HotfixUrl,
            ABHotfixUrl = FYAssetABSettings.Instance.HotfixUrl
        };
        FileHelper.WriteAllTextAtomic(
            FYAssetPathUtility.JoinFilePath(backup, "settings.json"),
            SerializationUtility.SerializeToJson(snap, true));
    }

    private static void RestoreSettings(string backup)
    {
        string path = FYAssetPathUtility.JoinFilePath(backup, "settings.json");
        if (!FileHelper.Exists(path))
            return;
        var snap = SerializationUtility.DeserializeJson<SettingsSnapshot>(File.ReadAllText(path, Encoding.UTF8));
        if (snap == null)
            return;
        FYAssetSettings.Instance.UseABBackend = snap.UseABBackend;
        FYAssetAASettings.Instance.HotfixUrl = snap.AAHotfixUrl;
        FYAssetABSettings.Instance.HotfixUrl = snap.ABHotfixUrl;
        EditorUtility.SetDirty(FYAssetSettings.Instance);
        EditorUtility.SetDirty(FYAssetAASettings.Instance);
        EditorUtility.SetDirty(FYAssetABSettings.Instance);
        AssetDatabase.SaveAssets();
    }

    private static void SnapshotPath(string sourcePath, string backupRoot, string name)
    {
        string abs = ResolveMaybeAsset(sourcePath);
        string dest = FYAssetPathUtility.JoinFilePath(backupRoot, name);
        if (FileHelper.Exists(abs))
            FileHelper.CopyFile(abs, dest, true);
        else if (FileHelper.DirectoryExists(abs))
            CopyDir(abs, dest);
    }

    private static void RestorePath(string sourcePath, string backupRoot, string name)
    {
        string abs = ResolveMaybeAsset(sourcePath);
        string backup = FYAssetPathUtility.JoinFilePath(backupRoot, name);
        if (FileHelper.Exists(backup))
        {
            FileHelper.EnsureDirectory(Path.GetDirectoryName(abs));
            FileHelper.CopyFile(backup, abs, true);
            return;
        }
        if (FileHelper.DirectoryExists(backup))
        {
            if (FileHelper.DirectoryExists(abs))
                FileHelper.TryDeleteDirectory(abs, true);
            CopyDir(backup, abs);
            return;
        }

        // No backup means original was absent.
        if (FileHelper.Exists(abs))
            FileHelper.TryDelete(abs);
        else if (FileHelper.DirectoryExists(abs))
            FileHelper.TryDeleteDirectory(abs, true);
    }

    private static void SnapshotDirectory(string sourceDir, string backupRoot, string name)
    {
        string dest = FYAssetPathUtility.JoinFilePath(backupRoot, name);
        if (FileHelper.DirectoryExists(sourceDir))
            CopyDir(sourceDir, dest);
        else
            FileHelper.EnsureDirectory(dest);
    }

    private static void RestoreDirectory(string sourceDir, string backupRoot, string name)
    {
        string backup = FYAssetPathUtility.JoinFilePath(backupRoot, name);
        if (FileHelper.DirectoryExists(sourceDir))
            FileHelper.TryDeleteDirectory(sourceDir, true);
        if (FileHelper.DirectoryExists(backup) && Directory.GetFileSystemEntries(backup).Length > 0)
            CopyDir(backup, sourceDir);
        else
            FileHelper.EnsureDirectory(sourceDir);
    }

    private static void CopyDir(string source, string dest)
    {
        FileHelper.EnsureDirectory(dest);
        string[] files = FileHelper.GetFiles(source, "*", SearchOption.AllDirectories);
        for (int i = 0; i < files.Length; i++)
        {
            string rel = FYAssetPathUtility.GetRelativeFilePath(source, files[i]);
            string target = FYAssetPathUtility.JoinFilePath(dest, rel);
            FileHelper.CopyFile(files[i], target, true);
        }
    }

    private static string ResolveMaybeAsset(string path)
    {
        if (string.IsNullOrEmpty(path))
            return path;
        if (Path.IsPathRooted(path))
            return FYAssetPathUtility.NormalizePath(path);
        return FYAssetPathUtility.ResolveFilePath(BuildPathManager.ProjectRoot, path);
    }

    private static void AssertCloudflareMirrorConsistent(BuildTestTargetSnapshot target)
    {
        string localIndex = FYAssetPathUtility.JoinFilePath(
            target.BackendPublishRoot,
            FYAssetSettings.PACKAGE_INDEX_FILE_NAME);
        string localJson = FileHelper.Exists(localIndex) ? File.ReadAllText(localIndex, Encoding.UTF8) : string.Empty;
        string remoteJson = string.Empty;
        try
        {
            using var client = new WebClient();
            remoteJson = client.DownloadString(target.PackageIndexUrl);
        }
        catch
        {
            // Empty public content is allowed only when local is also empty.
        }

        if (!string.Equals(NormalizeJson(localJson), NormalizeJson(remoteJson), StringComparison.Ordinal))
            throw new InvalidOperationException(
                "Cloudflare local mirror and public content disagree for " + target.TargetId);
    }

    private static void RedeployCloudflareServiceRoot(BuildTestTargetSnapshot target)
    {
        PushTargetConfig config = PushTargetUtility.FindConfig(target.TargetId);
        if (config == null)
            throw new InvalidOperationException("Cloudflare target missing: " + target.TargetId);

        string wrangler = PushTargetUtility.FindExecutableOnPath("wrangler");
        if (string.IsNullOrEmpty(wrangler))
            throw new InvalidOperationException("wrangler not found for Cloudflare restore.");

        string args = PushTargetUtility.BuildWranglerDeployArguments(
            target.ServiceRoot,
            FYAssetSettings.Instance.ProjectName);
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = wrangler,
            Arguments = args,
            WorkingDirectory = BuildPathManager.ProjectRoot,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        using var process = System.Diagnostics.Process.Start(psi);
        if (process == null)
            throw new InvalidOperationException("Failed to start wrangler for restore.");
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            string err = process.StandardError.ReadToEnd();
            throw new InvalidOperationException("Cloudflare restore deploy failed: " + err);
        }
    }

    private static string NormalizeJson(string json)
    {
        return string.IsNullOrWhiteSpace(json) ? string.Empty : json.Trim();
    }

    private static string Sanitize(string value)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        char[] chars = (value ?? "target").ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            if (Array.IndexOf(invalid, chars[i]) >= 0)
                chars[i] = '_';
        }
        return new string(chars);
    }
}
#endif
