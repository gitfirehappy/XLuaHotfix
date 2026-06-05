#if UNITY_EDITOR
using System;
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
        
        // 大版本更新，增加Major版本
        versionData.IncrementVersion(true);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        LastBuildSuccess = RunBuild(versionData.CurrentVersion, BuildType.Full, options);

        if (!Application.isBatchMode)
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
        
        // 小版本更新，增加Patch版本
        versionData.IncrementVersion();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        LastBuildSuccess = RunBuild(versionData.CurrentVersion, BuildType.Hotfix, options);
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
        Debug.Log($"[{nameof(BuildProjectManager)}] 开始 {buildType} build。Version={version.GetFullVersionString()}");

        try
        {
            // LuaScriptsIndex 仍由编排层统一导出；AA/AB package 产物由各自 DAG 负责。
            LuaScriptsIndexExporter.ExportData();
            AssetDatabase.Refresh();

            BackendMode backendMode = FYAssetSettings.Instance.UseABBackend ? BackendMode.ABManifest : BackendMode.AA;
            BuildPackageRequest request = BuildPackageRequest.Create(version, buildType, backendMode);
            Debug.Log($"[{nameof(BuildProjectManager)}] 已创建 BuildPackageRequest: Package={request.PackageName}, Backend={backendMode}, Output={request.OutputDir}");

            IBuildBackend backend = CreateBackend();
            var buildResult = backend.BuildAsync(request, options).GetAwaiter().GetResult();
            if (!buildResult.Success)
            {
                var err = buildResult.Error;
                Debug.LogError($"[{nameof(BuildProjectManager)}] 后端 build 失败: {(err != null ? $"[{err.Code}] {err.Message}" : "未知错误")}");
                return false;
            }

            FileHelper.EnsureDirectory(BuildPathManager.PackagesDir);

            // Repository commit 目前仍在 DAG 外执行；DAG 只负责生成本次 package 和 RepositoryArtifacts。
            CommitBuildRepository(request, backendMode, buildResult.Artifacts);

            Debug.Log($"[{nameof(BuildProjectManager)}] Package build 完成，已提交 Repository HEAD: {request.OutputDir}");
            if (!Application.isBatchMode)
                EditorUtility.RevealInFinder(request.OutputDir);

            return true;
        }
        catch (Exception ex)
        {
            Debug.LogError($"[{nameof(BuildProjectManager)}] Build 流程异常，Version={version.GetFullVersionString()}, Type={buildType}: {ex}");
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
    
    private static void CommitBuildRepository(BuildPackageRequest request, BackendMode backendMode, System.Collections.Generic.IReadOnlyList<ArtifactDigest> artifacts)
    {
        try
        {
            Debug.Log($"[{nameof(BuildProjectManager)}] 提交 Build Repository: Channel={BuildRepositoryFacade.GetChannelKey(request)}, Artifacts={(artifacts != null ? artifacts.Count : 0)}");
            BuildRepositoryFacade.Commit(request, artifacts, backendMode == BackendMode.ABManifest ? "AB" : null);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Build Repository commit failed. Package={request?.PackageName}, Backend={backendMode}, Reason={ex.Message}", ex);
        }
    }
}
#endif
