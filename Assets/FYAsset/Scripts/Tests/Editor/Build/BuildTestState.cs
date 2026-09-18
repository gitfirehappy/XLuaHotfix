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
    private sealed class SettingsSnapshot
    {
        public BackendMode Backend;
        public string AAHotfixUrl;
        public string ABCurrentTargetId;
    }

    public static FYAssetBackendSettings GetBackendSettings()
    {
        FYAssetBackendSettings settings = AssetDatabase.LoadAssetAtPath<FYAssetBackendSettings>(
            FYAssetBackendSettings.DEFAULT_ASSET_PATH);
        if (settings == null)
            throw new InvalidOperationException(
                $"FYAssetBackendSettings not found: {FYAssetBackendSettings.DEFAULT_ASSET_PATH}");
        return settings;
    }

    /// <summary>本地 PackageIndex 路径：构建不再产出该文件，快照只为恢复历史残留。</summary>
    private static string LocalPackageIndexPath =>
        FYAssetPathUtility.JoinFilePath(BuildPathManager.OutputRoot, FYAssetSettings.PACKAGE_INDEX_FILE_NAME);

    /// <summary>
    /// restore 范围内的分发决策，是唯一允许不读取备份内容就判定行为的方法。
    /// 规则：快照不完整 => 一律拒绝任何破坏性动作；声明完整但备份不可用 => 明确失败；
    /// 快照前不存在 => 恢复 = 删除运行期新建内容；已恢复 => 跳过。
    /// </summary>
    public static BuildTestScopeRestoreAction ClassifyScopeRestore(string snapshotState, string restoreState, bool backupContentExists)
    {
        if (string.Equals(restoreState, BuildTestRecoveryScopeStates.Restored, StringComparison.Ordinal))
            return BuildTestScopeRestoreAction.SkipAlreadyRestored;
        switch (snapshotState)
        {
            case BuildTestRecoveryScopeStates.AbsentBefore:
                return BuildTestScopeRestoreAction.DeleteForAbsent;
            case BuildTestRecoveryScopeStates.SnapshotComplete:
                return backupContentExists
                    ? BuildTestScopeRestoreAction.RestoreFromBackup
                    : BuildTestScopeRestoreAction.FailBackupMissing;
            default:
                return BuildTestScopeRestoreAction.FailRefuseDestructive;
        }
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
        record.Restored = restored;
        // Completed 只在所有范围终态后成立；恢复失败保留未完成记录，保证下次启动继续尝试。
        record.Completed = restored && AllScopesTerminal(record);
        PersistRecovery(runRoot, record);
    }

    private static bool AllScopesTerminal(BuildTestRecoveryRecord record)
    {
        if (record.Scopes == null || record.Scopes.Count == 0)
            return record.Restored;
        for (int i = 0; i < record.Scopes.Count; i++)
        {
            if (!string.Equals(record.Scopes[i].RestoreState, BuildTestRecoveryScopeStates.Restored, StringComparison.Ordinal))
                return false;
        }
        return true;
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
            if (record.Scopes == null || record.Scopes.Count == 0)
            {
                // 旧格式记录：没有范围状态可参考，拒绝自动恢复但标记已处理，
                // 避免每次启动都会无限重试同一个需要人工检查的损坏快照。
                record.Completed = true;
                record.Restored = false;
                restored = false;
                if (string.IsNullOrEmpty(failure))
                    failure = "旧格式 recovery 记录缺少范围状态，拒绝自动恢复，请人工检查后清理该 runRoot。";
            }
            else
            {
                // 恢复失败的记录保持未完成，下次启动会重试尚未 Restored 的范围。
                record.Restored = restored;
                record.Completed = restored && AllScopesTerminal(record);
            }
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

    private static string ScopeProjectId(string name) => "project/" + name;
    private static string ScopeTargetId(string targetId) => "target/" + targetId;

    private static bool PathExistsAny(string sourcePath)
    {
        string abs = ResolveMaybeAsset(sourcePath);
        return !string.IsNullOrEmpty(abs)
               && (FileHelper.Exists(abs) || FileHelper.DirectoryExists(abs));
    }

    /// <summary>
    /// 按范围快照：每个范围先用 durable 记录声明意图，完成后再标记；异常使状态停留在 Pending，
    /// 恢复时该状态拒绝任何删除动作。
    /// </summary>
    private static void SnapshotScopeEntry(string runRoot, string scopeId, Func<bool> existsBefore, Action snapshot)
    {
        if (!existsBefore())
        {
            MarkScopeSnapshot(runRoot, scopeId, BuildTestRecoveryScopeStates.AbsentBefore);
            return;
        }

        MarkScopeSnapshot(runRoot, scopeId, BuildTestRecoveryScopeStates.Pending);
        snapshot();
        MarkScopeSnapshot(runRoot, scopeId, BuildTestRecoveryScopeStates.SnapshotComplete);
    }

    public static void SnapshotProject(string runRoot, BuildTestBackend backend)
    {
        string backup = BuildTestPaths.ProjectBackupRoot(runRoot);
        FileHelper.EnsureDirectory(backup);

        SnapshotScopeEntry(runRoot, ScopeProjectId("settings.json"), () => true, () => SnapshotSettings(backup));
        SnapshotScopeEntry(runRoot, ScopeProjectId("bootstrap_buildindex.json"), () => PathExistsAny(FYAssetSettings.Instance.BuildIndexJsonPath),
            () => SnapshotPath(FYAssetSettings.Instance.BuildIndexJsonPath, backup, "bootstrap_buildindex.json"));
        SnapshotScopeEntry(runRoot, ScopeProjectId("package_index.json"), () => PathExistsAny(LocalPackageIndexPath),
            () => SnapshotPath(LocalPackageIndexPath, backup, "package_index.json"));
        SnapshotScopeEntry(runRoot, ScopeProjectId("packages"), () => FileHelper.DirectoryExists(BuildPathManager.PackagesDir),
            () => SnapshotDirectory(BuildPathManager.PackagesDir, backup, "packages"));
        SnapshotScopeEntry(runRoot, ScopeProjectId("builddata"),
            () => FileHelper.DirectoryExists(FYAssetPathUtility.JoinFilePath(BuildPathManager.ProjectRoot, "BuildData")),
            () => SnapshotDirectory(
                FYAssetPathUtility.JoinFilePath(BuildPathManager.ProjectRoot, "BuildData"),
                backup,
                "builddata"));
        SnapshotScopeEntry(runRoot, ScopeProjectId("streamingassets"), () => FileHelper.DirectoryExists(Application.streamingAssetsPath),
            () => SnapshotDirectory(Application.streamingAssetsPath, backup, "streamingassets"));
        SnapshotScopeEntry(runRoot, ScopeProjectId("fixture_sync.txt"), () => PathExistsAny(BuildTestConstants.SyncAssetPath),
            () => SnapshotPath(BuildTestConstants.SyncAssetPath, backup, "fixture_sync.txt"));
        SnapshotScopeEntry(runRoot, ScopeProjectId("fixture_raw.fyraw"), () => PathExistsAny(BuildTestConstants.RawAssetPath),
            () => SnapshotPath(BuildTestConstants.RawAssetPath, backup, "fixture_raw.fyraw"));
        SnapshotScopeEntry(runRoot, ScopeProjectId("aa_hotfix_group_undo.json"), () => PathExistsAny("Assets/FYAsset/Editor/Generated/HotfixGroupUndoLog.json"),
            () => SnapshotPath("Assets/FYAsset/Editor/Generated/HotfixGroupUndoLog.json", backup, "aa_hotfix_group_undo.json"));
    }

    public static void RestoreProject(string runRoot, BuildTestBackend backend)
    {
        var errors = new List<string>();
        RestoreProject(runRoot, backend, errors);
        ThrowIfRestoreErrors("project", errors);
    }

    private static void RestoreProject(string runRoot, BuildTestBackend backend, List<string> errors)
    {
        string backup = BuildTestPaths.ProjectBackupRoot(runRoot);

        ExecuteScopeRestore(runRoot, ScopeProjectId("settings.json"), BackupEntryExists(backup, "settings.json"),
            () => RestoreSettings(backup), () => { }, errors);
        ExecuteScopeRestore(runRoot, ScopeProjectId("bootstrap_buildindex.json"), BackupEntryExists(backup, "bootstrap_buildindex.json"),
            () => RestorePath(FYAssetSettings.Instance.BuildIndexJsonPath, backup, "bootstrap_buildindex.json"),
            () => DeleteExistingPath(FYAssetSettings.Instance.BuildIndexJsonPath), errors);
        ExecuteScopeRestore(runRoot, ScopeProjectId("package_index.json"), BackupEntryExists(backup, "package_index.json"),
            () => RestorePath(LocalPackageIndexPath, backup, "package_index.json"),
            () => DeleteExistingPath(LocalPackageIndexPath), errors);
        ExecuteScopeRestore(runRoot, ScopeProjectId("packages"), BackupEntryExists(backup, "packages"),
            () => RestoreDirectory(BuildPathManager.PackagesDir, backup, "packages"),
            () => DeleteExistingPath(BuildPathManager.PackagesDir), errors);
        string buildDataDir = FYAssetPathUtility.JoinFilePath(BuildPathManager.ProjectRoot, "BuildData");
        ExecuteScopeRestore(runRoot, ScopeProjectId("builddata"), BackupEntryExists(backup, "builddata"),
            () => RestoreDirectory(buildDataDir, backup, "builddata"),
            () => DeleteExistingPath(buildDataDir), errors);
        ExecuteScopeRestore(runRoot, ScopeProjectId("streamingassets"), BackupEntryExists(backup, "streamingassets"),
            () => RestoreDirectory(Application.streamingAssetsPath, backup, "streamingassets"),
            () => DeleteExistingPath(Application.streamingAssetsPath), errors);
        ExecuteScopeRestore(runRoot, ScopeProjectId("fixture_sync.txt"), BackupEntryExists(backup, "fixture_sync.txt"),
            () => RestorePath(BuildTestConstants.SyncAssetPath, backup, "fixture_sync.txt"),
            () => DeleteExistingPath(BuildTestConstants.SyncAssetPath), errors);
        ExecuteScopeRestore(runRoot, ScopeProjectId("fixture_raw.fyraw"), BackupEntryExists(backup, "fixture_raw.fyraw"),
            () => RestorePath(BuildTestConstants.RawAssetPath, backup, "fixture_raw.fyraw"),
            () => DeleteExistingPath(BuildTestConstants.RawAssetPath), errors);
        ExecuteScopeRestore(runRoot, ScopeProjectId("aa_hotfix_group_undo.json"), BackupEntryExists(backup, "aa_hotfix_group_undo.json"),
            () => RestorePath("Assets/FYAsset/Editor/Generated/HotfixGroupUndoLog.json", backup, "aa_hotfix_group_undo.json"),
            () => DeleteExistingPath("Assets/FYAsset/Editor/Generated/HotfixGroupUndoLog.json"), errors);

        // fixture_async.asset 刻意不纳入恢复快照：它属于静态夹具本体，运行期不应被修改。
        AssetDatabase.Refresh();
    }

    private static void ThrowIfRestoreErrors(string sourceLabel, List<string> errors)
    {
        if (errors != null && errors.Count > 0)
            throw new InvalidOperationException(sourceLabel + " restore failed: " + string.Join(" | ", errors));
    }

    public static void PrepareIsolatedFullProject(BuildTestBackend backend)
    {
        // 隔离 Full 项目意味着清空构建事实：Summary 与 Index 都从零开始。
        if (!BuildSummaryStore.CreateDefault().TryResetAll(out string resetError))
            throw new InvalidOperationException($"构建事实重置失败: {resetError}");

        // 清理残留 AA Hotfix 分组移动，不弹 UI 对话框。
        if (backend == BuildTestBackend.AA)
            ClearAAHotfixGroupMovesForTest();
    }

    public static void ClearAAHotfixGroupMovesForTest()
    {
        try
        {
            HotfixGroupRestoreResult restore = AAHotfixGroupMover.Restore();
            if (restore != null && !string.IsNullOrEmpty(restore.Message))
                Debug.Log("[BuildTestState] AA Hotfix group 恢复 / restore: " + restore.Message);
            HotfixGroupRestoreResult discard = AAHotfixGroupMover.DiscardUnrestorableRecords();
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

            if (!FYAssetSettings.Instance.TryResolvePublishTarget(id, out PublishTargetConfig config, out string targetError))
                throw new InvalidOperationException(targetError);
            if (string.IsNullOrWhiteSpace(config.Path))
                throw new InvalidOperationException("Target Path is empty: " + id);
            if (!config.TryNormalizePublicBaseUrl(out _, out string urlError))
                throw new InvalidOperationException("Target PublicBaseUrl invalid: " + id + " - " + urlError);
            bool external = false;

            string serviceRoot = config.ResolveServiceRoot();
            if (!serviceRoots.Add(FYAssetPathUtility.NormalizePath(serviceRoot)))
                throw new InvalidOperationException("Service-root collision for target: " + id);

            string runtimeUrl = config.GetHotfixUrl( BackendModeNames.FromBackendMode(ToBackendMode(backend)));
            snapshots.Add(new BuildTestTargetSnapshot
            {
                TargetId = config.TargetId,
                IsExternal = false,
                ServiceRoot = serviceRoot,
                BackendPublishRoot = config.ResolveBackendRoot(backendName),
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
            string scopeId = ScopeTargetId(target.TargetId);
            SnapshotScopeEntry(
                runRoot,
                scopeId,
                () => FileHelper.DirectoryExists(target.ServiceRoot),
                () =>
                {
                    FileHelper.EnsureDirectory(targetBackup);
                    SnapshotDirectory(target.ServiceRoot, targetBackup, "service");
                });

            var meta = new
            {
                target.TargetId,
                target.IsExternal,
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
        var errors = new List<string>();
        RestoreTargetScope(runRoot, target, errors);
        ThrowIfRestoreErrors(target.TargetId, errors);
    }

    private static void RestoreTargetScope(string runRoot, BuildTestTargetSnapshot target, List<string> errors)
    {
        string targetBackupRoot = FYAssetPathUtility.JoinFilePath(
            BuildTestPaths.TargetsBackupRoot(runRoot),
            Sanitize(target.TargetId));
        string serviceBackup = FYAssetPathUtility.JoinFilePath(targetBackupRoot, "service");
        ExecuteScopeRestore(
            runRoot,
            ScopeTargetId(target.TargetId),
            FileHelper.DirectoryExists(serviceBackup),
            () =>
            {
                RestoreDirectory(target.ServiceRoot, targetBackupRoot, "service");
            },
            () => DeleteExistingPath(target.ServiceRoot),
            errors);
    }

    /// <summary>
    /// 断言目标根 PackageIndex 指向预期的后端/包名/版本，并返回该索引。
    /// </summary>
    /// <remarks>requirePackagePresent 为 true 时要求服务器上存在该包目录。</remarks>
    public static PackageIndex ProbeTargetIdentity(
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
        string version = index.LatestVersion.GetReleaseVersionString();
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

        return index;
    }

    /// <summary>本次交付的发布源目录：Full 与 Hotfix 都是 Packages 下的独立包目录。</summary>
    public static string ResolveDeliverySourceDir(BuildTestBackend backend, bool isHotfix, string packageName = null)
    {
        _ = backend;
        _ = isHotfix;
        if (string.IsNullOrEmpty(packageName))
            throw new InvalidOperationException("发布源目录需要显式包名。");
        return BuildPathManager.GetPackageDir(packageName);
    }

    /// <summary>把交付目录发布到目标，并把发布结果写入 publishJsonPath；发布失败抛出。</summary>
    public static PublishResult PublishDeliveryToTarget(
        BuildTestBackend backend,
        BuildTestTargetSnapshot target,
        string sourcePackageDir,
        string publishJsonPath)
    {
        PublishTargetConfig config = null;
        for (int i = 0; FYAssetSettings.Instance.PublishTargets != null && i < FYAssetSettings.Instance.PublishTargets.Count; i++)
        {
            PublishTargetConfig candidate = FYAssetSettings.Instance.PublishTargets[i];
            if (candidate != null && string.Equals(candidate.TargetId, target.TargetId, StringComparison.OrdinalIgnoreCase))
            {
                config = candidate;
                break;
            }
        }
        if (config == null)
            throw new InvalidOperationException("Unknown target id: " + target.TargetId);

        // 发布身份只来自正式 Summary：包目录不承载构建事实，测试也不得从目录名推断身份。
        string packageName = Path.GetFileName(
            sourcePackageDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        PackageBuildIdentity identity = BuildTestAcceptance.RequireDeliveryIdentity(backend, packageName);
        BuildSummaryStore.CreateDefault().TryReadSummaryDocument(
            BuildTestPaths.BackendSegment(backend),
            packageName,
            out CompleteBuildSummary.SummaryDocument document,
            out _);

        var request = new PublishRequest
        {
            BackendKey = BuildTestPaths.BackendSegment(backend),
            SourcePackageDir = sourcePackageDir,
            ManifestReader = BuildTestAcceptance.ResolveManifestReader(backend),
            PackagesFolderName = FYAssetSettings.Instance.BuildPackagesFolderName,
            Identity = identity,
            BaseFullSummaryId = document?.BaseFullSummaryId
        };

        PublishResult result = PackagePublisher.Publish(request, config);
        FileHelper.WriteAllTextAtomic(
            publishJsonPath,
            SerializationUtility.SerializeToJson(result, true));
        if (result == null || !result.Success)
            throw new InvalidOperationException(
                $"Publish failed for {target.TargetId}: {result?.Error}");
        return result;
    }

    public static PackageIndex ReadPackageIndex(BuildTestTargetSnapshot target)
    {
        string path = FYAssetPathUtility.JoinFilePath(
            target.BackendPublishRoot,
            FYAssetSettings.PACKAGE_INDEX_FILE_NAME);
        if (FileHelper.Exists(path))
            return SerializationUtility.DeserializeJson<PackageIndex>(File.ReadAllText(path, Encoding.UTF8));

        // Cloudflare 部署后可能只暴露公共 URL；尝试 HTTP 读取。
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

    private static BuildTestRecoveryRecord TryLoadRecovery(string runRoot)
    {
        string path = BuildTestPaths.RecoveryJson(runRoot);
        if (!FileHelper.Exists(path))
            return null;
        return SerializationUtility.DeserializeJson<BuildTestRecoveryRecord>(File.ReadAllText(path, Encoding.UTF8));
    }

    private static void MarkScopeSnapshot(string runRoot, string scopeId, string snapshotState)
    {
        BuildTestRecoveryRecord record = TryLoadRecovery(runRoot);
        if (record == null)
            throw new InvalidOperationException("recovery.json missing: " + runRoot);
        BuildTestScopeRecoveryState scope = FindOrCreateScope(record, scopeId);
        switch (snapshotState)
        {
            case BuildTestRecoveryScopeStates.Pending:
                scope.SnapshotState = BuildTestRecoveryScopeStates.Pending;
                scope.RestoreState = BuildTestRecoveryScopeStates.None;
                scope.Error = null;
                break;
            case BuildTestRecoveryScopeStates.SnapshotComplete:
                scope.SnapshotState = BuildTestRecoveryScopeStates.SnapshotComplete;
                break;
            case BuildTestRecoveryScopeStates.AbsentBefore:
                scope.SnapshotState = BuildTestRecoveryScopeStates.AbsentBefore;
                scope.RestoreState = BuildTestRecoveryScopeStates.None;
                scope.Error = null;
                break;
        }
        PersistRecovery(runRoot, record);
    }

    private static void MarkScopeRestore(string runRoot, string scopeId, bool success, string error)
    {
        BuildTestRecoveryRecord record = TryLoadRecovery(runRoot);
        if (record == null)
            throw new InvalidOperationException("recovery.json missing: " + runRoot);
        BuildTestScopeRecoveryState scope = FindOrCreateScope(record, scopeId);
        scope.RestoreState = success
            ? BuildTestRecoveryScopeStates.Restored
            : BuildTestRecoveryScopeStates.RestoreFailed;
        scope.Error = error;
        PersistRecovery(runRoot, record);
    }

    private static BuildTestScopeRecoveryState FindOrCreateScope(BuildTestRecoveryRecord record, string scopeId)
    {
        if (record.Scopes == null)
            record.Scopes = new List<BuildTestScopeRecoveryState>();
        for (int i = 0; i < record.Scopes.Count; i++)
        {
            if (string.Equals(record.Scopes[i].ScopeId, scopeId, StringComparison.Ordinal))
                return record.Scopes[i];
        }
        var scope = new BuildTestScopeRecoveryState { ScopeId = scopeId };
        record.Scopes.Add(scope);
        return scope;
    }

    private static bool RestoreFromRecord(BuildTestRecoveryRecord record, out string failure)
    {
        failure = string.Empty;
        if (record.Scopes == null || record.Scopes.Count == 0)
        {
            failure = "旧格式 recovery 记录缺少范围状态，拒绝自动恢复，请人工检查后清理该 runRoot。";
            return false;
        }

        try
        {
            if (!Enum.TryParse(record.Backend, true, out BuildTestBackend backend))
                backend = BuildTestBackend.AA;

            string runRoot = Path.GetFullPath(Path.Combine(record.ProjectBackupRoot, "..", ".."));
            var errors = new List<string>();
            RestoreProject(runRoot, backend, errors);
            if (record.Targets != null)
            {
                for (int i = 0; i < record.Targets.Count; i++)
                    RestoreTargetScope(runRoot, record.Targets[i], errors);
            }

            if (errors.Count > 0)
            {
                failure = string.Join(" | ", errors);
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            failure = ex.Message;
            return false;
        }
    }

    /// <summary>为正常运行与中断 run 恢复（TryRecoverStaleRun）共享的范围恢复执行器：结果同时写入 durable 状态与错误表。</summary>
    private static void ExecuteScopeRestore(
        string runRoot,
        string scopeId,
        bool backupContentExists,
        Action restoreFromBackup,
        Action deleteForAbsent,
        List<string> errors)
    {
        BuildTestRecoveryRecord record = TryLoadRecovery(runRoot);
        BuildTestScopeRecoveryState scope = record?.Scopes?.Find(
            s => string.Equals(s.ScopeId, scopeId, StringComparison.Ordinal));
        string snapshotState = scope?.SnapshotState ?? BuildTestRecoveryScopeStates.Pending;
        var action = ClassifyScopeRestore(snapshotState, scope?.RestoreState, backupContentExists);
        try
        {
            switch (action)
            {
                case BuildTestScopeRestoreAction.SkipAlreadyRestored:
                    return;
                case BuildTestScopeRestoreAction.FailRefuseDestructive:
                    throw new InvalidOperationException(
                        $"快照范围 '{scopeId}' 未完成（状态 {snapshotState ?? "未知"}），它不是“原状不存在”的证据，拒绝任何破坏性恢复。");
                case BuildTestScopeRestoreAction.FailBackupMissing:
                    throw new InvalidOperationException(
                        $"快照范围 '{scopeId}' 声明完整但备份内容不存在/不可用。");
                case BuildTestScopeRestoreAction.RestoreFromBackup:
                    restoreFromBackup();
                    break;
                case BuildTestScopeRestoreAction.DeleteForAbsent:
                    deleteForAbsent();
                    break;
            }

            TryMarkScopeRestore(runRoot, scopeId, true, null);
        }
        catch (Exception ex)
        {
            TryMarkScopeRestore(runRoot, scopeId, false, ex.Message);
            errors.Add(scopeId + ": " + ex.Message);
        }
    }

    private static bool BackupEntryExists(string backupRoot, string name)
    {
        string backup = FYAssetPathUtility.JoinFilePath(backupRoot, name);
        return FileHelper.Exists(backup) || FileHelper.DirectoryExists(backup);
    }

    private static void DeleteExistingPath(string sourcePath)
    {
        string abs = ResolveMaybeAsset(sourcePath);
        if (string.IsNullOrEmpty(abs))
            return;
        if (FileHelper.DirectoryExists(abs))
            FileHelper.TryDeleteDirectory(abs, true);
        else if (FileHelper.Exists(abs))
            FileHelper.TryDelete(abs);
    }

    private static void TryMarkScopeRestore(string runRoot, string scopeId, bool success, string error)
    {
        try
        {
            MarkScopeRestore(runRoot, scopeId, success, error);
        }
        catch (Exception persistEx)
        {
            Debug.LogWarning($"[BuildTestState] 范围恢复状态写失败 {scopeId}: {persistEx.Message}");
        }
    }

    private static void SnapshotSettings(string backup)
    {
        var snap = new SettingsSnapshot
        {
            Backend = GetBackendSettings().Backend,
            AAHotfixUrl = FYAssetAASettings.Instance.HotfixUrl,
            ABCurrentTargetId = FYAssetSettings.Instance.CurrentABTargetId
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
        FYAssetBackendSettings backendSettings = GetBackendSettings();
        backendSettings.Backend = snap.Backend;
        FYAssetAASettings.Instance.HotfixUrl = snap.AAHotfixUrl;
        FYAssetSettings.Instance.CurrentABTargetId = snap.ABCurrentTargetId;
        EditorUtility.SetDirty(backendSettings);
        EditorUtility.SetDirty(FYAssetAASettings.Instance);
        EditorUtility.SetDirty(FYAssetSettings.Instance);
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

        // 备份缺失不再允许假定“原状不存在”；删除语义只能由范围状态 AbsentBefore 显式驱动。
        throw new FileNotFoundException($"快照备份缺失，拒绝恢复: source={abs}, backup={backup}");
    }

    private static void SnapshotDirectory(string sourceDir, string backupRoot, string name)
    {
        string dest = FYAssetPathUtility.JoinFilePath(backupRoot, name);
        // 调用方（SnapshotScopeEntry）保证源目录必存在才进入该函数；
        // 空目录也是合法状态，复制结果同样是一个空 dest。
        if (!FileHelper.DirectoryExists(sourceDir))
            throw new DirectoryNotFoundException($"快照源目录不存在: {sourceDir}");
        CopyDir(sourceDir, dest);
    }

    private static void RestoreDirectory(string sourceDir, string backupRoot, string name)
    {
        string backup = FYAssetPathUtility.JoinFilePath(backupRoot, name);
        if (!FileHelper.DirectoryExists(backup))
            throw new DirectoryNotFoundException($"快照备份缺失，拒绝目录恢复: {backup}");
        if (FileHelper.DirectoryExists(sourceDir))
            FileHelper.TryDeleteDirectory(sourceDir, true);
        CopyDir(backup, sourceDir);
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
            // 仅当本地也为空时才允许公共内容为空。
        }

        if (!string.Equals(NormalizeJson(localJson), NormalizeJson(remoteJson), StringComparison.Ordinal))
            throw new InvalidOperationException(
                "Cloudflare local mirror and public content disagree for " + target.TargetId);
    }

    private static void RedeployCloudflareServiceRoot(BuildTestTargetSnapshot target)
    {
        throw new NotSupportedException("目录发布目标不支持 Cloudflare 部署。");
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
