#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 构建编排入口。
/// 统一管理版本号更新、后端路由（AB / AA）、包体产物组织和 PackageIndex 更新。
/// 通过 legacy MenuItem 保留 Full Package / Hotfix Package / Reset Groups 四个旧工具入口。
/// TODO： 后续删除独立按钮，全部由构建面板管理
/// </summary>
public static class BuildProjectManager
{
    public static bool LastBuildSuccess { get; private set; } = true;

    // 热更包体大小限制
    private static string versionDataBasePath => FYAssetSettings.Instance.VersionDataBasePath;
    
    /// <summary>
    /// 构建完整包，用于大版本更新
    /// </summary>
    [MenuItem("Tools/Build/[Legacy] Build Full Package",false, 1)]
    public static void BuildFullPackage()
    {
        BuildFullPackage(null);
    }

    public static void BuildFullPackage(BuildExecutionOptions options)
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
    [MenuItem("Tools/Build/[Legacy] Build Hotfix Package",false, 2)]
    public static void BuildHotfix()
    {
        BuildHotfix(null);
    }

    public static void BuildHotfix(BuildExecutionOptions options)
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
    [MenuItem("Tools/Build/[Legacy] Reset Remote Groups to Original",false, 0)]
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
            DifferentialProcessor.RestoreOriginalGroups();
        }
    }

    private static bool RunBuild(VersionNumber version, BuildType buildType, BuildExecutionOptions options)
    {
        Debug.Log($"[BuildProjectManager] 开始构建 {buildType} 包 Version: {version.GetFullVersionString()}");

        try
        {
            LuaScriptsIndexExporter.ExportData();
            AssetDatabase.Refresh();

            if (buildType == BuildType.Hotfix && !FYAssetSettings.Instance.UseABBackend)
            {
                bool hasChanges = DifferentialProcessor.PrepareHotfix(version);
                if (!hasChanges)
                    Debug.LogWarning("[BuildProjectManager] 无资源变更，继续执行热更构建。");
            }

            BackendMode backendMode = FYAssetSettings.Instance.UseABBackend ? BackendMode.ABManifest : BackendMode.AA;
            BuildPackageRequest request = BuildPackageRequest.Create(version, buildType, backendMode);

            IBuildBackend backend = CreateBackend();
            var buildResult = backend.BuildAsync(request, options).GetAwaiter().GetResult();
            if (!buildResult.Success)
            {
                var err = buildResult.Error;
                Debug.LogError($"[BuildProjectManager] 后端构建失败: {(err != null ? $"[{err.Code}] {err.Message}" : "未知错误")}");
                return false;
            }

            FileHelper.EnsureDirectory(BuildPathManager.PackagesDir);

            UpdatePackageIndexFile(request);

            CommitBuildRepository(request, backendMode);

            Debug.Log($"[BuildProjectManager] 包体构建完毕: {request.OutputDir}");
            if (!Application.isBatchMode)
                EditorUtility.RevealInFinder(request.OutputDir);

            return true;
        }
        catch (Exception ex)
        {
            Debug.LogError($"[BuildProjectManager] 构建过程中出现异常: {ex}");
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
            Debug.LogError($"[BuildProjectManager] 未找到版本数据库: {versionDataBasePath}");
            return null;
        }
        return versionData;
    }
    
    /// <summary>
    /// 更新 PackageIndex 文件，包含最新包体名和版本
    /// </summary>
    private static void UpdatePackageIndexFile(BuildPackageRequest request)
    {
        var data = new PackageIndex
        {
            LatestPackage = request.PackageName,
            LatestVersion = request.Version
        };
        
        // 生成 PackageIndex 内容（包含最新包体名）
        SerializationUtility.WriteToFile(request.PackageIndexPath, data);
        Debug.Log($"[BuildProjectManager] 更新 PackageIndex 包体名: {request.PackageName}，版本: {request.Version.GetFullVersionString()}");
    }

    private static void CommitBuildRepository(BuildPackageRequest request, BackendMode backendMode)
    {
        try
        {
            if (backendMode == BackendMode.AA)
            {
                var settings = UnityEditor.AddressableAssets.AddressableAssetSettingsDefaultObject.Settings;
                var scanner = new AddressableSourceArtifactScanner(settings);
                BuildRepositoryFacade.Commit(request, scanner);
            }
            else
            {
                var manifestPath = System.IO.Path.Combine(request.OutputDir, FYAssetSettings.MANIFEST_FILE_NAME);
                if (!FileHelper.Exists(manifestPath))
                    throw new InvalidOperationException($"AB manifest not found: {manifestPath}");

                var manifest = SerializationUtility.ReadFromFile<ABManifest>(manifestPath);
                var scanner = new AbBundleOutputArtifactScanner(manifest != null ? manifest.BundleEntries : null);
                BuildRepositoryFacade.Commit(request, scanner, "AB");
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Build repository commit failed: {ex.Message}", ex);
        }
    }
}
#endif
