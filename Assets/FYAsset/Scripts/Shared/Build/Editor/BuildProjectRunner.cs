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
        BackendMode backendMode,
        Func<IBuildBackend> backendFactory,
        BuildExecutionOptions options = null)
    {
        VersionRecord versionData = LoadVersionRecord();
        if (versionData == null)
            return false;

        VersionNumber nextVersion = versionData.BuildNextVersion(true);

        bool success = RunBuild(nextVersion, BuildType.Standalone, backendMode, backendFactory, options);
        if (success)
            success = ApplyBuiltVersion(nextVersion);

        return success;
    }

    /// <summary>
    /// 构建完整包，用于大版本更新
    /// </summary>
    public static bool BuildFullPackage(
        BackendMode backendMode,
        Func<IBuildBackend> backendFactory,
        BuildExecutionOptions options = null)
    {
        VersionRecord versionData = LoadVersionRecord();
        if (versionData == null)
            return false;
        
        // 大版本更新，先暂存版本号，构建和 Repository commit 成功后才写回 VersionRecord。
        VersionNumber nextVersion = versionData.BuildNextVersion(true);

        bool success = RunBuild(nextVersion, BuildType.Full, backendMode, backendFactory, options);
        if (success)
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
        BackendMode backendMode,
        Func<IBuildBackend> backendFactory,
        BuildExecutionOptions options = null)
    {
        VersionRecord versionData = LoadVersionRecord();
        if (versionData == null)
            return false;
        
        // 小版本更新，先暂存版本号，构建和 Repository commit 成功后才写回 VersionRecord。
        VersionNumber nextVersion = versionData.BuildNextVersion();

        bool success = RunBuild(nextVersion, BuildType.Hotfix, backendMode, backendFactory, options);
        if (success)
            success = ApplyBuiltVersion(nextVersion);

        return success;
    }
    

    private static bool RunBuild(
        VersionNumber version,
        BuildType buildType,
        BackendMode backendMode,
        Func<IBuildBackend> backendFactory,
        BuildExecutionOptions options)
    {
        Debug.Log($"[{nameof(BuildProjectRunner)}] 开始 {buildType} build。Backend={backendMode}, Version={version.GetReleaseVersionString()}, Build={version.Build}");

        BuildPackageRequest request = null;

        try
        {
            request = BuildPackageRequest.Create(version, buildType, backendMode);
            Debug.Log($"[{nameof(BuildProjectRunner)}] 已创建 BuildPackageRequest: Package={request.PackageName}, Backend={backendMode}, Output={request.OutputDir}");

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

            // Standalone 不推送 Repository；完整包已直接写入 StreamingAssets/Standalone/，这里只发布 BuildIndex。
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
        string channelKey = BuildBaselineStore.GetChannelKey(request.Version, request.BackendMode);
        var artifacts = buildResult?.Artifacts != null
            ? new System.Collections.Generic.List<ArtifactDigest>(buildResult.Artifacts)
            : new System.Collections.Generic.List<ArtifactDigest>();
        BuildBaselineStore.Save(channelKey, new BuildBaseline
        {
            Version = request.Version,
            BuildType = request.BuildType.ToString(),
            PackageName = request.PackageName,
            BackendMode = request.BackendMode == BackendMode.ABManifest ? BackendModeNames.AB : BackendModeNames.AA,
            PackageRootDir = request.OutputDir,
            CommitDelta = buildResult?.Delta,
            ManifestFileNames = backend?.BaselineHandler?.RequiredManifestFileNames != null
                ? new System.Collections.Generic.List<string>(backend.BaselineHandler.RequiredManifestFileNames)
                : null,
            CreatedAtUtc = DateTime.UtcNow.ToString("o"),
            Artifacts = artifacts
        });
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
                BackendMode = BackendModeNames.FromBackendMode(request.BackendMode),
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
