#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 构建编排入口。
/// 统一管理版本号更新、后端路由和构建仓库提交。
/// </summary>
public static class BuildProjectManager
{
    public static bool LastBuildSuccess { get; private set; } = true;

    private static string versionDataBasePath => FYAssetSettings.Instance.VersionDataBasePath;
    
    /// <summary>
    /// 构建完整包，用于大版本更新
    /// </summary>
    public static void BuildFullPackage(BuildExecutionOptions options = null)
    {
        LastBuildSuccess = true;
        VersionDataBase versionData = LoadVersionDataBase();
        if (versionData == null)
        {
            LastBuildSuccess = false;
            return;
        }
        
        // 大版本更新，先暂存版本号，构建和 Repository commit 成功后才写回 VersionDataBase。
        VersionNumber nextVersion = versionData.BuildNextVersion(true);

        LastBuildSuccess = RunBuild(nextVersion, BuildType.Full, options);
        if (LastBuildSuccess)
        {
            versionData.ApplyVersion(nextVersion);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        if (LastBuildSuccess && !Application.isBatchMode)
        {
            EditorApplication.ExecuteMenuItem("File/Build Settings...");
            Debug.Log("[BuildProjectManager] 请在弹出的Build Settings中选择目标平台和场景，点Build按钮后自动导出包体！");
        }
    }
    
    /// <summary>
    /// 构建热更包，用于小版本更新
    /// </summary>
    public static void BuildHotfix(BuildExecutionOptions options = null)
    {
        LastBuildSuccess = true;
        VersionDataBase versionData = LoadVersionDataBase();
        if (versionData == null)
        {
            LastBuildSuccess = false;
            return;
        }
        
        // 小版本更新，先暂存版本号，构建和 Repository commit 成功后才写回 VersionDataBase。
        VersionNumber nextVersion = versionData.BuildNextVersion();

        LastBuildSuccess = RunBuild(nextVersion, BuildType.Hotfix, options);
        if (LastBuildSuccess)
        {
            versionData.ApplyVersion(nextVersion);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }
    }
    
    /// <summary>
    /// 重置分组 (Manual Trigger)
    /// 将位于 Hotfix 组的资源还原回它们原始的分组 (通常在打整包前，或者放弃本次热更时使用)
    /// </summary>
    public static void ResetGroupsToOriginal()
    {
        if (FYAssetSettings.Instance.UseABBackend)
        {
            Debug.LogWarning("[BuildProjectManager] ResetGroupsToOriginal 仅适用于 AA 构建链路，AB backend 下已跳过。");
            return;
        }

        bool confirm = EditorUtility.DisplayDialog("重置分组", 
            "确定要将所有热更组 (Remote_Hotfix_Group) 中的资源还原回原始分组吗？\n\n注意：这通常在构建新的整包前执行。", 
            "确定重置", "取消");

        if (confirm)
        {
            TaskMoveAddressableHotfixGroups.Restore();
        }
    }

    private static bool RunBuild(VersionNumber version, BuildType buildType, BuildExecutionOptions options)
    {
        Debug.Log($"[{nameof(BuildProjectManager)}] 开始 {buildType} build。Version={version.GetReleaseVersionString()}, Build={version.Build}");

        BuildPackageRequest request = null;
        RepositoryCommit repositoryCommit = null;
        bool repositoryCommitted = false;

        try
        {
            // LuaScriptsIndex 仍由编排层统一导出；AA/AB package 产物由各自 DAG 负责。
            LuaScriptsIndexExporter.ExportData();
            AssetDatabase.Refresh();

            BackendMode backendMode = FYAssetSettings.Instance.UseABBackend ? BackendMode.ABManifest : BackendMode.AA;
            request = BuildPackageRequest.Create(version, buildType, backendMode);
            Debug.Log($"[{nameof(BuildProjectManager)}] 已创建 BuildPackageRequest: Package={request.PackageName}, Backend={backendMode}, Output={request.OutputDir}");

            IBuildBackend backend = CreateBackend();
            var buildResult = backend.BuildAsync(request, options).GetAwaiter().GetResult();
            if (!buildResult.Success)
            {
                var err = buildResult.Error;
                string reason = err != null ? $"[{err.Code}] {err.Message}" : "未知错误";
                Debug.LogError($"[{nameof(BuildProjectManager)}] 后端 build 失败: {reason}");
                HandleFailedPackage(request, reason);
                return false;
            }

            FileHelper.EnsureDirectory(BuildPathManager.PackagesDir);

            // Repository commit 目前仍在 DAG 外执行；DAG 只负责生成本次 package 和 RepositoryArtifacts。
            repositoryCommit = CommitBuildRepository(request, backendMode, buildResult.Artifacts);
            repositoryCommitted = true;
            PublishBuildArtifacts(request);

            Debug.Log($"[{nameof(BuildProjectManager)}] Package build 完成，已提交 Repository HEAD 并发布包指针: {request.OutputDir}");
            if (!Application.isBatchMode)
                TryRevealPackage(request.OutputDir);

            return true;
        }
        catch (Exception ex)
        {
            if (repositoryCommitted)
                TryRollbackRepositoryHead(repositoryCommit);
            HandleFailedPackage(request, ex.Message);
            Debug.LogError($"[{nameof(BuildProjectManager)}] Build 流程异常，Version={version.GetReleaseVersionString()}, Type={buildType}: {ex}");
            return false;
        }
    }

    private static IBuildBackend CreateBackend()
    {
        return FYAssetSettings.Instance.UseABBackend
            ? new ABBuildBackend()
            : new AABuildBackend();
    }
    
    private static VersionDataBase LoadVersionDataBase()
    {
        VersionDataBase versionData = AssetDatabase.LoadAssetAtPath<VersionDataBase>(versionDataBasePath);
        if (versionData == null)
        {
            Debug.LogError($"[{nameof(BuildProjectManager)}] 未找到 VersionDataBase: {versionDataBasePath}");
            return null;
        }
        return versionData;
    }
    
    private static RepositoryCommit CommitBuildRepository(BuildPackageRequest request, BackendMode backendMode, System.Collections.Generic.IReadOnlyList<ArtifactDigest> artifacts)
    {
        try
        {
            Debug.Log($"[{nameof(BuildProjectManager)}] 提交 Build Repository: Channel={BuildRepositoryFacade.GetChannelKey(request)}, Artifacts={(artifacts != null ? artifacts.Count : 0)}");
            return BuildRepositoryFacade.Commit(request, artifacts, backendMode == BackendMode.ABManifest ? "AB" : null);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Build Repository commit failed. Package={request?.PackageName}, Backend={backendMode}, Reason={ex.Message}", ex);
        }
    }

    private static void PublishBuildArtifacts(BuildPackageRequest request)
    {
        TaskExportLocalBuildData.Publish(request);
        TaskWritePackageIndex.Publish(request);
    }

    private static void TryRollbackRepositoryHead(RepositoryCommit commit)
    {
        if (commit == null)
            return;

        if (BuildRepositoryFacade.TryRollbackHead(commit, out string reason))
        {
            Debug.LogWarning($"[{nameof(BuildProjectManager)}] 发布失败，已回滚 Repository HEAD: {reason}");
            return;
        }

        Debug.LogWarning($"[{nameof(BuildProjectManager)}] 发布失败，但 Repository HEAD 回滚未完成: {reason}");
    }

    private static void HandleFailedPackage(BuildPackageRequest request, string reason)
    {
        if (request == null)
            return;
        if (!FileHelper.DirectoryExists(request.OutputDir))
            return;
        if (!IsSafePackageOutputDir(request, out string safetyReason))
        {
            Debug.LogWarning($"[{nameof(BuildProjectManager)}] 跳过失败包清理: {safetyReason}");
            TryWriteFailedPackageMarker(request, reason);
            return;
        }

        if (FileHelper.TryDeleteDirectory(request.OutputDir, true))
        {
            Debug.LogWarning($"[{nameof(BuildProjectManager)}] 已删除失败包目录: {request.OutputDir}");
            return;
        }

        TryWriteFailedPackageMarker(request, reason);
    }

    private static bool IsSafePackageOutputDir(BuildPackageRequest request, out string reason)
    {
        reason = string.Empty;
        string packagesDir = FYAssetPathUtility.NormalizePath(BuildPathManager.PackagesDir);
        string outputDir = FYAssetPathUtility.NormalizePath(request.OutputDir);
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
            Debug.LogWarning($"[{nameof(BuildProjectManager)}] 失败包删除失败，已写入标记: {markerPath}");
        }
        catch (Exception markerEx)
        {
            Debug.LogWarning($"[{nameof(BuildProjectManager)}] 写入失败包标记失败: {markerEx.Message}");
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
            Debug.LogWarning($"[{nameof(BuildProjectManager)}] 打开包目录失败: {ex.Message}");
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
