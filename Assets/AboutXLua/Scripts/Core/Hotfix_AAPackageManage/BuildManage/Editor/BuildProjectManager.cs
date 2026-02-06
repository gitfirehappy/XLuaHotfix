#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel.Design.Serialization;
using System.IO;
using Codice.Client.Common.EventTracking;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Build;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine;

public static class BuildProjectManager
{
    // 热更包输出根目录
    private static string OutputRoot => Path.Combine(Directory.GetParent(Application.dataPath).FullName, "HotfixOutput");
    
    // 热更包体大小限制
    private static long MaxHotfixSizeBytes = 1L * 1024 * 1024 * 1024;
    
    private static string versionDataBasePath => "Assets/Build/VersionDataBase.asset";

    private enum BuildType
    {
        Full,
        Hotfix
    }
    
    /// <summary>
    /// 构建完整包，用于大版本更新
    /// </summary>
    [MenuItem("Tools/Build/Build Full Package",false, 1)]
    public static void BuildFullPackage()
    {
        VersionDataBase versionData = LoadVersionDataBase();
        if (versionData == null) return;
        
        // 大版本更新，增加Major版本
        versionData.IncrementVersion(true);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        
        ExecuteBuildFlow(versionData.CurrentVersion, BuildType.Full);
        
        EditorApplication.ExecuteMenuItem("File/Build Settings...");
        Debug.Log("[BuildProjectManager] 请在弹出的Build Settings中选择目标平台和场景，点Build按钮后自动导出包体！");
    }
    
    /// <summary>
    /// 构建热更包，用于小版本更新
    /// </summary>
    [MenuItem("Tools/Build/Build Hotfix Package",false, 2)]
    public static void BuildHotfix()
    {
        VersionDataBase versionData = LoadVersionDataBase();
        if (versionData == null) return;
        
        // 小版本更新，增加Patch版本
        versionData.IncrementVersion();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        
        ExecuteBuildFlow(versionData.CurrentVersion, BuildType.Hotfix);
    }
    
    /// <summary>
    /// 确认发布上线 (Manual Trigger)
    /// 将 Staged 快照转正为 Head，通常在热更包上传 CDN 后点击
    /// </summary>
    [MenuItem("Tools/Build/Confirm Release Hotfix",false, 3)]
    public static void ConfirmReleaseHotfix()
    {
        DifferentialProcessor.ConfirmRelease();
    }

    /// <summary>
    /// 重置分组 (Manual Trigger)
    /// 将位于 Hotfix 组的资源还原回它们原始的分组 (通常在打整包前，或者放弃本次热更时使用)
    /// </summary>
    [MenuItem("Tools/Build/Reset Remote Groups to Original",false, 0)]
    public static void ResetGroupsToOriginal()
    {
        bool confirm = EditorUtility.DisplayDialog("重置分组", 
            "确定要将所有热更组 (Remote_Hotfix_Group) 中的资源还原回原始分组吗？\n\n注意：这通常在构建新的整包前执行。", 
            "确定重置", "取消");

        if (confirm)
        {
            DifferentialProcessor.RestoreOriginalGroups();
        }
    }
    
    private static void ExecuteBuildFlow(VersionNumber version, BuildType buildType)
    { 
        Debug.Log($"[BuildProjectManager] 开始构建热更包 Version: {version}");
        
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        
        // 1. 生成HelperBuildData并进行基础设置
        HelperBuildDataExporter.ExportData();
        ConfigureBasicSettings(settings);
        AssetDatabase.Refresh();

        try
        {
            List<string> deleteList = new List<string>();
            HashSet<string> unchangedBundles = new HashSet<string>();
            if (buildType == BuildType.Hotfix)
            {
                // 将变动资源移入 Remote_Hotfix_Group
                bool hasChanges = DifferentialProcessor.PrepareHotfix(version, deleteList, out unchangedBundles);
                if (!hasChanges)
                {
                    Debug.LogWarning("无资源变更，终止构建。");
                }
            }    
            
            // 3. 构建前清理ServerData
            BuildPathCustomizer.CleanServerData();

            // 4. 构建Remote包
            Debug.Log("[BuildProjectManager] 开始执行 Addressables BuildPlayerContent...");
            AddressableAssetSettings.BuildPlayerContent(out AddressablesPlayerBuildResult result);

            if (!string.IsNullOrEmpty(result.Error))
            {
                Debug.LogError($"[BuildProjectManager] 构建失败: {result.Error}");
                return;
            }

            // 5. BuildPathCustomizer 整理Remote包目录, 删除不必要的文件
            // 获取 Addressables 默认的 RemoteBuildPath (通常在 ServerData/[Platform])
            string serverDataPath = Path.Combine(
                Directory.GetParent(Application.dataPath).FullName,
                "ServerData",
                EditorUserBuildSettings.activeBuildTarget.ToString()
            );

            string currentPackageName = $"Build_{DateTime.Now:yyyyMMdd}_{version.GetVersionString()}";

            string packagesDir = Path.Combine(OutputRoot, "Packages");
            Directory.CreateDirectory(packagesDir);
            string hotfixOutputDir = Path.Combine(packagesDir, currentPackageName);
            
            BuildPathCustomizer.OrganizeBuildOutput(serverDataPath, hotfixOutputDir, unchangedBundles); 

            // 6. 生成 version_state.json 到指定目录
            GenerateVersionStateFile(hotfixOutputDir, version, deleteList);

            // 7. 更新 Manifest 文件
            UpdateManifestFile(currentPackageName, version);

            // 8. 如果是整包构建，需要导出 LocalStatus
            if (buildType == BuildType.Full)
            {
                LocalStatusExporter.ExportData(version);
                LocalStatusExporter.EnsureExportDataInGroup();
                
                DifferentialProcessor.ReBuildSnapShots(version);
            }

            Debug.Log($"[BuildProjectManager] 包体构建完毕: {hotfixOutputDir}");
            EditorUtility.RevealInFinder(hotfixOutputDir);
        }
        catch(Exception ex)
        {
            Debug.LogError($"[BuildProjectManager] 构建过程中出现异常: {ex}");
        }
    }

    /// <summary>
    /// 强制配置 Addressable Settings (PackTogetherByLabel, RemotePath 等)
    /// </summary>
    private static void ConfigureBasicSettings(AddressableAssetSettings settings)
    {
        // 设置 Build Remote Catalog
        settings.BuildRemoteCatalog = true;
        settings.OverridePlayerVersion = "addressables_content_state"; // 保持 Content State 一致，防止 Hash 剧烈变化

        // 遍历 Group 强制设置 BundleMode
        foreach (var group in settings.groups)
        {
            // 跳过部分 Group
            // HelperBuildData 统一设置为 PackTogetherByLabel
            if (group == null) continue;
            
            if (group.Name == "Built In Data" || group.HasSchema<PlayerDataGroupSchema>())
            {
                if (group.HasSchema<BundledAssetGroupSchema>())
                {
                    Debug.LogWarning($"[BuildProjectManager] 修复冲突：移除 {group.Name} 中错误的 BundledAssetGroupSchema");
                    group.RemoveSchema<BundledAssetGroupSchema>();
                    EditorUtility.SetDirty(group);
                }
                continue; 
            }

            var schema = group.GetSchema<BundledAssetGroupSchema>();
            if (schema == null)
            {
                schema = group.AddSchema<BundledAssetGroupSchema>();
            }

            // 统一采用 PackTogetherByLabel （所有包按标签打包）
            if (schema.BundleMode != BundledAssetGroupSchema.BundlePackingMode.PackTogetherByLabel)
            {
                schema.BundleMode = BundledAssetGroupSchema.BundlePackingMode.PackTogetherByLabel;
                EditorUtility.SetDirty(group);
            }
            
            // HelperBuildData (必要的辅助数据)，必须强制为 Remote，否则无法热更配置
            if (group.Name == Constants.HELPER_BUILD_DATA_GROUP_NAME)
            {
                SetSchemaPathToRemote(settings, schema);
            }
            // 剩余组会在DifferentialProcessor 中处理
        }
        AssetDatabase.SaveAssets();
    }
    
    /// <summary>
    /// 辅助方法：将 Schema 设置为 Remote 路径
    /// </summary>
    private static void SetSchemaPathToRemote(AddressableAssetSettings settings, BundledAssetGroupSchema schema)
    {
        bool changed = false;
        
        // 检查并设置 BuildPath -> RemoteBuildPath
        if (schema.BuildPath.GetName(settings) != AddressableAssetSettings.kRemoteBuildPath)
        {
            schema.BuildPath.SetVariableByName(settings, AddressableAssetSettings.kRemoteBuildPath);
            changed = true;
        }

        // 检查并设置 LoadPath -> RemoteLoadPath
        if (schema.LoadPath.GetName(settings) != AddressableAssetSettings.kRemoteLoadPath)
        {
            schema.LoadPath.SetVariableByName(settings, AddressableAssetSettings.kRemoteLoadPath);
            changed = true;
        }

        if (changed)
        {
            Debug.Log($"[BuildProjectManager] 已将 Schema 路径修正为 Remote: {schema.Group.Name}");
        }
    }

    /// <summary>
    /// 生成 version_state.json
    /// </summary>
    private static void GenerateVersionStateFile(string outputDir, VersionNumber version, List<string> deleteList)
    {
        Debug.Log("[BuildProjectManager] 正在生成 version_state.json...");
        
        var versionState = new VersionState
        {
            version = version,
            bundles = new List<BundleInfo>(),
            deleteList = new List<string>()
        };
        
        // 扫描 bundles 目录下的所有文件
        string bundlesDir = Path.Combine(outputDir, "bundles");
        if (Directory.Exists(bundlesDir))
        {
            var files = Directory.GetFiles(bundlesDir, "*", SearchOption.TopDirectoryOnly);
            foreach (var file in files)
            {
                if(!file.EndsWith(".bundle")) continue; 
                
                var fileInfo = new FileInfo(file);
                
                var bundleInfo = new BundleInfo
                {
                    bundleName = Path.GetFileName(file),
                    hash = HashGenerator.GenerateFileHash(file),
                    size = fileInfo.Length
                };
                
                versionState.bundles.Add(bundleInfo);
                versionState.totalSize += bundleInfo.size;
            }
        }
        
        // 导入删除列表
        versionState.deleteList.AddRange(deleteList);
        
        // 包体大小预警
        if (versionState.totalSize >= MaxHotfixSizeBytes)
        {
            Debug.LogError($"[BuildProjectManager] 热更包大小过大，需缩减大小: {versionState.totalSize} >= {MaxHotfixSizeBytes}");
            EditorUtility.DisplayDialog("热更包过大", $"热更包大小 ({versionState.totalSize / (1024 * 1024)} MB) 已超过阈值 ({MaxHotfixSizeBytes / (1024 * 1024)} MB)。请缩减资源大小。", "OK");
            return;
        }

        // 计算整个包的 Hash
        // GeneratePackageHash 会遍历目录下所有文件（除了 version_state.json）
        versionState.hash = HashGenerator.GeneratePackageHash(outputDir);

        // 序列化并写入
        string json = JsonUtility.ToJson(versionState, true);
        string savePath = Path.Combine(outputDir, "version_state.json");
        File.WriteAllText(savePath, json);
        
        Debug.Log($"[BuildProjectManager] version_state.json 生成完毕。Hash: {versionState.hash} BundleSize: {versionState.totalSize}");
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
    /// 更新 manifest.json
    /// </summary>
    private static void UpdateManifestFile(string packageName, VersionNumber version)
    {
        string manifestPath = Path.Combine(OutputRoot, "manifest.json");

        var data = new Manifest
        {
            latestPackage = packageName,
            latestversion = version
        };
        
        // 生成 manifest内容（包含最新包体名）
        string manifestJson = JsonUtility.ToJson(data, true);
    
        File.WriteAllText(manifestPath, manifestJson);
        Debug.Log($"[BuildProjectManager] 更新 manifest.json 包体名: {packageName}，版本: {version.GetVersionString()}");
    }
}
#endif
