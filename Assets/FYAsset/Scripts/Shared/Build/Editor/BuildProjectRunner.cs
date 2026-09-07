#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 共享构建编排 runner。
/// 具体的 AA/AB build manager 提供 backend mode 和 backend factory。
/// </summary>
public static class BuildProjectRunner
{
    private static string versionDataBasePath => FYAssetSettings.Instance.VersionRecordPath;
    
    /// <summary>
    /// 构建单机离线包，产物直接写入 StreamingAssets/Standalone/，不推送 Repository。
    /// </summary>
    public static bool BuildStandalone(
        string backendKey,
        Func<IBuildBackend> backendFactory,
        BuildExecutionOptions options = null,
        bool attemptDelivery = false)
    {
        VersionRecord versionData = LoadVersionRecord();
        if (versionData == null)
            return false;

        VersionNumber nextVersion = versionData.BuildNextVersion(true);

        bool success = RunBuild(nextVersion, BuildType.Standalone, backendKey, backendFactory, options, attemptDelivery);
        // attempt 交付在 Runner 事务内部提交版本；非 attempt 路径保持原版外部提交。
        if (success && !attemptDelivery)
            success = ApplyBuiltVersion(nextVersion);

        return success;
    }

    /// <summary>
    /// 构建完整包，用于大版本更新
    /// </summary>
    public static bool BuildFullPackage(
        string backendKey,
        Func<IBuildBackend> backendFactory,
        BuildExecutionOptions options = null,
        bool attemptDelivery = false)
    {
        VersionRecord versionData = LoadVersionRecord();
        if (versionData == null)
            return false;
        
        // 大版本更新，先暂存版本号，构建和 Repository commit 成功后才写回 VersionRecord。
        VersionNumber nextVersion = versionData.BuildNextVersion(true);

        bool success = RunBuild(nextVersion, BuildType.Full, backendKey, backendFactory, options, attemptDelivery);
        if (success && !attemptDelivery)
            success = ApplyBuiltVersion(nextVersion);

        if (success && !Application.isBatchMode)
        {
            EditorApplication.ExecuteMenuItem("File/Build Settings...");
            Debug.Log("[BuildProjectRunner] 请在弹出的Build Settings中选择目标平台和场景，点Build按钮后自动导出包体！");
        }

        return success;
    }
    
    /// <summary>
    /// 构建热更包，用于小版本更新
    /// </summary>
    public static bool BuildHotfix(
        string backendKey,
        Func<IBuildBackend> backendFactory,
        BuildExecutionOptions options = null,
        bool attemptDelivery = false)
    {
        VersionRecord versionData = LoadVersionRecord();
        if (versionData == null)
            return false;
        
        // 小版本更新，先暂存版本号，构建和 Repository commit 成功后才写回 VersionRecord。
        VersionNumber nextVersion = versionData.BuildNextVersion();

        bool success = RunBuild(nextVersion, BuildType.Hotfix, backendKey, backendFactory, options, attemptDelivery);
        if (success && !attemptDelivery)
            success = ApplyBuiltVersion(nextVersion);

        return success;
    }
    

    private static bool RunBuild(
        VersionNumber version,
        BuildType buildType,
        string backendKey,
        Func<IBuildBackend> backendFactory,
        BuildExecutionOptions options,
        bool attemptDelivery = false)
    {
        Debug.Log($"[{nameof(BuildProjectRunner)}] 开始 {buildType} build。Backend={backendKey}, Version={version.GetReleaseVersionString()}, Build={version.Build}");

        BuildPackageRequest request = null;

        try
        {
            request = BuildPackageRequest.Create(version, buildType, backendKey, attemptDelivery);
            Debug.Log($"[{nameof(BuildProjectRunner)}] 已创建 BuildPackageRequest: Package={request.PackageName}, Backend={backendKey}, Output={request.OutputDir}");

            IBuildBackend backend = backendFactory != null
                ? backendFactory()
                : throw new InvalidOperationException("Build backend factory 为 null。");
            var buildResult = backend.BuildAsync(request, options).GetAwaiter().GetResult();
            if (!buildResult.Success)
            {
                var err = buildResult.Error;
                string reason = err != null ? $"[{err.Code}] {err.Message}" : "未知错误";
                Debug.LogError($"[{nameof(BuildProjectRunner)}] 后端 build 失败: {reason}");
                HandleFailedPackage(request, reason);
                return false;
            }

            if (request.IsAttemptLayout)
            {
                // attempt 交付：Runner 独占提交点，包/本地数据/PackageIndex/baseline/VersionRecord 要么全部完成、要么全部回滚。
                if (!DeliverAttemptBuild(request, backend, buildResult))
                    return false;

                Debug.Log($"[{nameof(BuildProjectRunner)}] Package build 完成，已交付: {request.DeliveryOutputDir}");
                if (!Application.isBatchMode)
                    TryRevealPackage(request.DeliveryOutputDir);
                return true;
            }

            // 非 attempt（AA 现存行为，待 AA 对齐轮收统）
            if (buildType == BuildType.Standalone)
            {
                PublishBuildArtifacts(request, backend);
                Debug.Log($"[{nameof(BuildProjectRunner)}] Standalone build 完成: {request.OutputDir}");
                if (!Application.isBatchMode)
                    TryRevealPackage(request.OutputDir);
                return true;
            }

            PublishBuildArtifacts(request, backend);
            // baseline 只在构建+发布全部成功后写入，自然免除回滚。
            RecordDeliveredBaseline(request, buildResult, backend);

            Debug.Log($"[{nameof(BuildProjectRunner)}] Package build 完成，已写入交付基线并发布本地启动数据: {request.OutputDir}");
            if (!Application.isBatchMode)
                TryRevealPackage(request.OutputDir);

            return true;
        }
        catch (Exception ex)
        {
            HandleFailedPackage(request, ex.Message);
            Debug.LogError($"[{nameof(BuildProjectRunner)}] Build 流程异常，Version={version.GetReleaseVersionString()}, Type={buildType}: {ex}");
            return false;
        }
    }
    
    private static VersionRecord LoadVersionRecord()
    {
        VersionRecord versionData = AssetDatabase.LoadAssetAtPath<VersionRecord>(versionDataBasePath);
        if (versionData == null)
        {
            Debug.LogError($"[{nameof(BuildProjectRunner)}] 未找到 VersionRecord: {versionDataBasePath}");
            return null;
        }
        return versionData;
    }

    private static bool ApplyBuiltVersion(VersionNumber version)
    {
        VersionRecord versionData = LoadVersionRecord();
        if (versionData == null)
            return false;

        versionData.ApplyVersion(version);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        return true;
    }
    
    /// <summary>
    /// 交付成功（构建+发布）后记录双槽 baseline，作为后续 hotfix diff 的历史基准。
    /// </summary>
    private static void RecordDeliveredBaseline(BuildPackageRequest request, BuildBackendResult buildResult, IBuildBackend backend)
    {
        string channelKey = BuildBaselineStore.GetChannelKey(request.Version, request.BackendKey);
        var artifacts = buildResult?.Artifacts != null
            ? new System.Collections.Generic.List<ArtifactDigest>(buildResult.Artifacts)
            : new System.Collections.Generic.List<ArtifactDigest>();
        BuildBaselineStore.Save(channelKey, new BuildBaseline
        {
            Version = request.Version,
            BuildType = request.BuildType.ToString(),
            PackageName = request.PackageName,
            BackendMode = request.BackendKey,
            PackageRootDir = request.OutputDir,
            CommitDelta = buildResult?.Delta,
            ManifestFileNames = backend?.BaselineHandler?.RequiredManifestFileNames != null
                ? new System.Collections.Generic.List<string>(backend.BaselineHandler.RequiredManifestFileNames)
                : null,
            CreatedAtUtc = DateTime.UtcNow.ToString("o"),
            Artifacts = artifacts
        });
    }

    /// <summary>
    /// attempt 交付事务：validate → promote → 本地数据 → PackageIndex → baseline → VersionRecord。
    /// 任一步骤失败按逆序补偿，live 状态回到事务开始前的值；补偿本身失败则记录错误但尽力继续其余补偿。
    /// </summary>
    private static bool DeliverAttemptBuild(BuildPackageRequest request, IBuildBackend backend, BuildBackendResult buildResult)
    {
        var compensation = new BuildDeliveryCompensation();
        try
        {
            ValidateAttemptPackage(request);

            compensation.PromoteToken = BuildDeliveryPromoter.Promote(request.OutputDir, request.DeliveryOutputDir);
            BuildPackageRequest delivered = request.WithPromotedOutput();

            compensation.LocalData = TaskExportLocalBuildData.BeginDelivery(delivered, backend?.BaselineHandler);

            if (delivered.BuildType != BuildType.Standalone)
            {
                compensation.CapturePackageIndex(delivered.PackageIndexPath);
                TaskWritePackageIndex.Publish(delivered);

                string channelKey = BuildBaselineStore.GetChannelKey(delivered.Version, delivered.BackendKey);
                compensation.CaptureBaseline(channelKey);
                RecordDeliveredBaseline(delivered, buildResult, backend);
            }

            compensation.CaptureVersionRecord();
            if (!ApplyBuiltVersion(delivered.Version))
                throw new InvalidOperationException($"VersionRecord 应用失败: {versionDataBasePath}");

            compensation.Commit();
            return true;
        }
        catch (Exception ex)
        {
            compensation.Rollback();
            Debug.LogError($"[{nameof(BuildProjectRunner)}] 交付提交失败，已回滚 live 状态: Package={request.PackageName}: {ex}");
            return false;
        }
    }

    private static void ValidateAttemptPackage(BuildPackageRequest request)
    {
        if (!FileHelper.DirectoryExists(request.OutputDir))
            throw new DirectoryNotFoundException($"attempt 包目录不存在: {request.OutputDir}");
        if (!FileHelper.DirectoryExists(request.BundlesDir)
            || FileHelper.GetFiles(request.BundlesDir, "*", SearchOption.AllDirectories).Length == 0)
            throw new InvalidOperationException($"attempt 包 dirs 为空(bundle 缺失): {request.BundlesDir}");
    }

    /// <summary>
    /// 交付补偿集合。Capture 在进入对应步骤之前调用；Rollback 逆序执行已登记的补偿；Commit 释放扩展备份。
    /// </summary>
    private sealed class BuildDeliveryCompensation
    {
        public BuildDeliveryPromoteToken PromoteToken;
        public TaskExportLocalBuildData.LocalBuildDataDelivery LocalData;
        private string _packageIndexPath;
        private byte[] _packageIndexBytes;
        private string _baselineChannelKey;
        private byte[] _baselineBytes;
        private byte[] _versionRecordBytes;
        private bool _versionRecordExisted;

        public void CapturePackageIndex(string packageIndexPath)
        {
            _packageIndexPath = packageIndexPath;
            _packageIndexBytes = FileHelper.Exists(packageIndexPath) ? FileHelper.ReadAllBytes(packageIndexPath) : null;
        }

        public void CaptureBaseline(string channelKey)
        {
            _baselineChannelKey = channelKey;
            _baselineBytes = BuildBaselineStore.CaptureRawForRollback(channelKey);
        }

        public void CaptureVersionRecord()
        {
            _versionRecordExisted = FileHelper.Exists(versionDataBasePath);
            _versionRecordBytes = _versionRecordExisted ? FileHelper.ReadAllBytes(versionDataBasePath) : null;
        }

        public void Commit()
        {
            LocalData?.Commit();
            PromoteToken?.Commit();
        }

        public void Rollback()
        {
            RollbackSafely(RestoreVersionRecord);
            RollbackSafely(RestoreBaseline);
            RollbackSafely(RestorePackageIndex);
            RollbackSafely(() => LocalData?.Rollback());
            RollbackSafely(() => PromoteToken?.Rollback());
        }

        private void RestoreVersionRecord()
        {
            if (!_versionRecordExisted && _versionRecordBytes == null)
                return;
            if (_versionRecordBytes != null)
                File.WriteAllBytes(versionDataBasePath, _versionRecordBytes);
            else
                FileHelper.TryDelete(versionDataBasePath);
            AssetDatabase.ImportAsset(versionDataBasePath, ImportAssetOptions.ForceUpdate);
            AssetDatabase.Refresh();
        }

        private void RestoreBaseline()
        {
            if (_baselineChannelKey == null)
                return;
            BuildBaselineStore.RestoreRawForRollback(_baselineChannelKey, _baselineBytes);
        }

        private void RestorePackageIndex()
        {
            if (_packageIndexPath == null)
                return;
            if (_packageIndexBytes != null)
                File.WriteAllBytes(_packageIndexPath, _packageIndexBytes);
            else
                FileHelper.TryDelete(_packageIndexPath);
        }

        private static void RollbackSafely(Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[{nameof(BuildProjectRunner)}] 交付补偿步骤失败（已尽力继续其余补偿）: {ex}");
            }
        }
    }

    private static void PublishBuildArtifacts(BuildPackageRequest request, IBuildBackend backend)
    {
        TaskExportLocalBuildData.Publish(request, backend?.BaselineHandler);
        if (request.BuildType != BuildType.Standalone)
            TaskWritePackageIndex.Publish(request);
    }

    private static void HandleFailedPackage(BuildPackageRequest request, string reason)
    {
        if (request == null)
            return;
        if (!FileHelper.DirectoryExists(request.OutputDir))
            return;
        if (!IsSafePackageOutputDir(request, out string safetyReason))
        {
            Debug.LogWarning($"[{nameof(BuildProjectRunner)}] 跳过失败包清理: {safetyReason}");
            TryWriteFailedPackageMarker(request, reason);
            return;
        }

        if (FileHelper.TryDeleteDirectory(request.OutputDir, true))
        {
            Debug.LogWarning($"[{nameof(BuildProjectRunner)}] 已删除失败包目录: {request.OutputDir}");
            return;
        }

        TryWriteFailedPackageMarker(request, reason);
    }

    private static bool IsSafePackageOutputDir(BuildPackageRequest request, out string reason)
    {
        reason = string.Empty;
        string outputDir = FYAssetPathUtility.NormalizePath(request.OutputDir);

        // attempt 布局：失败清理只允许删除 attempt 根之下的目录，任何 live 出口一律拒绝。
        if (request.IsAttemptLayout)
        {
            string attemptRoot = FYAssetPathUtility.NormalizePath(BuildPathManager.AttemptPackagesRoot);
            string attemptRootWithSeparator = attemptRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            StringComparison attemptComparison = Path.DirectorySeparatorChar == '\\'
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            if (string.IsNullOrEmpty(outputDir) || !outputDir.StartsWith(attemptRootWithSeparator, attemptComparison))
            {
                reason = $"attempt 布局下只允许清理 attempt 根之下的目录。Root={attemptRoot}, Actual={outputDir}";
                return false;
            }

            return true;
        }

        if (request.BuildType == BuildType.Standalone)
        {
            string standaloneDir = FYAssetPathUtility.NormalizePath(BuildPathManager.StandalonePackageDir);
            if (FYAssetPathUtility.AreSamePath(standaloneDir, outputDir))
                return true;

            reason = $"Standalone OutputDir must equal StandalonePackageDir. Expected={standaloneDir}, Actual={outputDir}";
            return false;
        }

        string packagesDir = FYAssetPathUtility.NormalizePath(BuildPathManager.PackagesDir);
        if (string.IsNullOrEmpty(packagesDir) || string.IsNullOrEmpty(outputDir))
        {
            reason = "PackagesDir or OutputDir is empty.";
            return false;
        }
        if (FYAssetPathUtility.AreSamePath(packagesDir, outputDir))
        {
            reason = $"OutputDir equals PackagesDir: {outputDir}";
            return false;
        }

        string rootWithSeparator = packagesDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        StringComparison comparison = Path.DirectorySeparatorChar == '\\'
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!outputDir.StartsWith(rootWithSeparator, comparison))
        {
            reason = $"OutputDir is outside PackagesDir. PackagesDir={packagesDir}, OutputDir={outputDir}";
            return false;
        }
        if (!string.Equals(Path.GetFileName(outputDir), request.PackageName, comparison))
        {
            reason = $"OutputDir name does not match PackageName. OutputDir={outputDir}, PackageName={request.PackageName}";
            return false;
        }

        return true;
    }

    private static void TryWriteFailedPackageMarker(BuildPackageRequest request, string reason)
    {
        try
        {
            FileHelper.EnsureDirectory(request.OutputDir);
            var marker = new FailedBuildMarker
            {
                PackageName = request.PackageName,
                Version = request.Version != null ? request.Version.GetReleaseVersionString() : string.Empty,
                Build = request.Version != null ? request.Version.Build : 0,
                BuildType = request.BuildType.ToString(),
                BackendMode = request.BackendKey,
                FailedAtUtc = DateTime.UtcNow.ToString("o"),
                Reason = reason ?? string.Empty,
                OutputDir = request.OutputDir
            };

            string markerPath = FYAssetPathUtility.JoinFilePath(request.OutputDir, "FAILED_BUILD.json");
            FileHelper.WriteAllTextAtomic(markerPath, SerializationUtility.SerializeToJson(marker, true));
            Debug.LogWarning($"[{nameof(BuildProjectRunner)}] 失败包删除失败，已写入标记: {markerPath}");
        }
        catch (Exception markerEx)
        {
            Debug.LogWarning($"[{nameof(BuildProjectRunner)}] 写入失败包标记失败: {markerEx.Message}");
        }
    }

    private static void TryRevealPackage(string outputDir)
    {
        try
        {
            EditorUtility.RevealInFinder(outputDir);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[{nameof(BuildProjectRunner)}] 打开包目录失败: {ex.Message}");
        }
    }

    [Serializable]
    private sealed class FailedBuildMarker
    {
        public string PackageName;
        public string Version;
        public int Build;
        public string BuildType;
        public string BackendMode;
        public string FailedAtUtc;
        public string Reason;
        public string OutputDir;
    }
}
#endif
