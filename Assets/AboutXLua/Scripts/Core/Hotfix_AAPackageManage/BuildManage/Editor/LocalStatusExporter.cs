#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

/// <summary>
/// 本地Build数据导出，只有整包构建时使用，小版本热更不需要
/// </summary>
public class LocalStatusExporter
{
    private const string _buildIndexAssetPath = Constants.BUILD_INDEX_ASSETPATH;

    private const string _groupName = Constants.LOCAL_STATUS_GROUP_NAME;

    /// <summary>
    /// 总导出入口
    /// </summary>
    public static void ExportData(VersionNumber version)
    {
        Debug.Log("[LocalBuildData] 开始导出所有本地构建数据...");
    
        ExportBuildIndex(version);
    
        AssetDatabase.SaveAssets();
        Debug.Log("[LocalBuildData] 导出完成。");
    }
    
    /// <summary>
    /// 确保所有本地配置进入 AddressableGroup
    /// </summary>
    public static void EnsureExportDataInGroup()
    {
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null) return;

        var group = settings.FindGroup(_groupName);
        if (group == null)
        {
            group = settings.CreateGroup(_groupName, false, false, true, null);
        }

        var schema = group.GetSchema<UnityEditor.AddressableAssets.Settings.GroupSchemas.BundledAssetGroupSchema>();
        if (schema == null) schema = group.AddSchema<UnityEditor.AddressableAssets.Settings.GroupSchemas.BundledAssetGroupSchema>();
        
        // 强制禁用缓存，确保每次都读取包内最新的 BuildIndex
        if (schema.UseAssetBundleCache)
        {
            schema.UseAssetBundleCache = false; 
            EditorUtility.SetDirty(group);
        }
        
        // BuildIndex (附带 BuildIndex 标签)
        EnsureAssetInGroup(settings, group, _buildIndexAssetPath, Constants.BUILD_INDEX, Constants.BUILD_INDEX);
    
        Debug.Log("[LocalBuildData] 已确保本地数据进入 Group。");
    }
    
    /// <summary>
    /// 辅助方法：确保资源进入指定组，并设置地址和标签
    /// </summary>
    private static void EnsureAssetInGroup(AddressableAssetSettings settings, AddressableAssetGroup group, string path, string address, string label = null)
    {
        var guid = AssetDatabase.AssetPathToGUID(path);
        if (string.IsNullOrEmpty(guid)) return;

        var entry = settings.CreateOrMoveEntry(guid, group);
        entry.address = address;
    
        if (!string.IsNullOrEmpty(label))
        {
            if (!entry.labels.Contains(label))
            {
                entry.labels.Add(label);
            }
        }
    }
    
    #region BuildIndex
    
    /// <summary>
    /// 导出BuildIndex
    /// </summary>
    private static void ExportBuildIndex(VersionNumber version)
    { 
        // 创建BuildIndex
        BuildIndex buildIndex = ScriptableObject.CreateInstance<BuildIndex>();
        
        buildIndex.BuildGUID = System.DateTime.UtcNow.ToString("yyyyMMddHHmmss");
        
        buildIndex.BuildTime = System.DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        
        buildIndex.IsDebug = EditorUserBuildSettings.development;
        
        buildIndex.Platform = EditorUserBuildSettings.activeBuildTarget.ToString();
        
        buildIndex.Version = version;
        
        // 保存到Asset
        string directoryPath = Path.GetDirectoryName(_buildIndexAssetPath);
        if (!Directory.Exists(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }
        
        AssetDatabase.CreateAsset(buildIndex, _buildIndexAssetPath);
        
        Debug.Log($"[BuildIndexExporter] BuildIndex已生成: {buildIndex.BuildGUID}, Platform: {buildIndex.Platform}");
    }
    
    #endregion
}
#endif
