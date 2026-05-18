#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 构建编排入口。
/// 统一管理版本号更新、后端路由（AB / Legacy Addressables）、包体产物组织和 PackageIndex 更新。
/// 通过 MenuItem 提供 Full Package / Hotfix Package / Confirm Release / Reset Groups 四个工具入口。
/// </summary>
public static class BuildProjectManager
{
    public static bool LastBuildSuccess { get; private set; } = true;

    // 热更包输出根目录
    private static string OutputRoot => Path.Combine(Directory.GetParent(Application.dataPath).FullName, "HotfixOutput");
    
    // 热更包体大小限制
    private static string versionDataBasePath => FYAssetSettings.Instance.VersionDataBasePath;
    
    /// <summary>
    /// 构建完整包，用于大版本更新
    /// </summary>
    [MenuItem("Tools/Build/Build Full Package",false, 1)]
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
    [MenuItem("Tools/Build/Build Hotfix Package",false, 2)]
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
    /// 确认发布上线 (Manual Trigger)
    /// 将 Staged 快照转正为 Head，通常在热更包上传 CDN 后点击
    /// </summary>
    [MenuItem("Tools/Build/Confirm Release Hotfix",false, 3)]
    public static void ConfirmReleaseHotfix()
    {
        if (FYAssetSettings.Instance.UseABBackend)
        {
            Debug.LogWarning("[BuildProjectManager] ConfirmReleaseHotfix 仅适用于 Legacy Addressables 构建链路，AB backend 下已跳过。");
            return;
        }

        DifferentialProcessor.ConfirmRelease();
    }

    /// <summary>
    /// 重置分组 (Manual Trigger)
    /// 将位于 Hotfix 组的资源还原回它们原始的分组 (通常在打整包前，或者放弃本次热更时使用)
    /// </summary>
    [MenuItem("Tools/Build/Reset Remote Groups to Original",false, 0)]
    public static void ResetGroupsToOriginal()
    {
        if (FYAssetSettings.Instance.UseABBackend)
        {
            Debug.LogWarning("[BuildProjectManager] ResetGroupsToOriginal 仅适用于 Legacy Addressables 构建链路，AB backend 下已跳过。");
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

            IBuildBackend backend = CreateBackend();
            var buildResult = backend.BuildAsync(version, buildType, options).GetAwaiter().GetResult();
            if (!buildResult.Success)
            {
                var err = buildResult.Error;
                Debug.LogError($"[BuildProjectManager] 后端构建失败: {(err != null ? $"[{err.Code}] {err.Message}" : "未知错误")}");
                return false;
            }

            string currentPackageName = $"Build_{DateTime.Now:yyyyMMdd}_{version.GetFullVersionString()}";
            string packagesDir = Path.Combine(OutputRoot, "Packages");
            Directory.CreateDirectory(packagesDir);
            string outputDir = Path.Combine(packagesDir, currentPackageName);

            backend.OrganizeOutput(outputDir, version);
            backend.GeneratePackageManifest(outputDir, version);
            UpdateManifestFile(currentPackageName, version);

            if (buildType == BuildType.Full)
            {
                LocalStatusExporter.ExportData(version);
                DifferentialProcessor.ReBuildSnapShots(version);
            }

            Debug.Log($"[BuildProjectManager] 包体构建完毕: {outputDir}");
            if (!Application.isBatchMode)
                EditorUtility.RevealInFinder(outputDir);

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
            : new LegacyAddressableBuildBackend();
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
    /// 更新 manifest.json（PackageIndex）
    /// </summary>
    private static void UpdateManifestFile(string packageName, VersionNumber version)
    {
        string manifestPath = Path.Combine(OutputRoot, "manifest.json");

        var data = new PackageIndex
        {
            LatestPackage = packageName,
            LatestVersion = version
        };
        
        // 生成 PackageIndex 内容（包含最新包体名）
        SerializationUtility.WriteToFile(manifestPath, data);
        Debug.Log($"[BuildProjectManager] 更新 manifest.json 包体名: {packageName}，版本: {version.GetFullVersionString()}");
    }
}
#endif
