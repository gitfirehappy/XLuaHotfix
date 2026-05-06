using System.IO;
using UnityEditor;

/// <summary>
/// Collector 数据目录与旧路径迁移工具。
/// </summary>
public static class CollectorDataMigrator
{
    private const string LegacyCollectorSettingAssetPath = "Assets/Build/CollectorSetting.asset";

    /// <summary>确保 CollectorData 目录存在。</summary>
    public static void EnsureDataFolder()
    {
        EnsureFolder(FYAssetConstants.COLLECTOR_DATA_FOLDER);
    }

    /// <summary>
    /// 将旧路径的 CollectorSetting 迁移到新路径。
    /// 新路径已存在时不覆盖；仅当旧路径存在且新路径不存在时迁移。
    /// </summary>
    public static void MigrateFromLegacyPath()
    {
        EnsureDataFolder();

        if (AssetDatabase.LoadAssetAtPath<CollectorSetting>(FYAssetConstants.COLLECTOR_SETTING_ASSET_PATH) != null)
            return;

        CollectorSetting legacyAsset = AssetDatabase.LoadAssetAtPath<CollectorSetting>(LegacyCollectorSettingAssetPath);
        if (legacyAsset == null)
            return;

        string error = AssetDatabase.MoveAsset(LegacyCollectorSettingAssetPath, FYAssetConstants.COLLECTOR_SETTING_ASSET_PATH);
        if (!string.IsNullOrEmpty(error))
        {
            AssetDatabase.CopyAsset(LegacyCollectorSettingAssetPath, FYAssetConstants.COLLECTOR_SETTING_ASSET_PATH);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        CollectorReverseIndex.Instance.MarkDirty();
    }

    private static void EnsureFolder(string assetFolderPath)
    {
        string normalizedPath = assetFolderPath.Replace('\\', '/').TrimEnd('/');
        if (string.IsNullOrEmpty(normalizedPath) || AssetDatabase.IsValidFolder(normalizedPath))
            return;

        string parentPath = Path.GetDirectoryName(normalizedPath)?.Replace('\\', '/');
        string folderName = Path.GetFileName(normalizedPath);

        if (string.IsNullOrEmpty(parentPath) || string.IsNullOrEmpty(folderName))
            return;

        EnsureFolder(parentPath);
        if (!AssetDatabase.IsValidFolder(normalizedPath))
            AssetDatabase.CreateFolder(parentPath, folderName);
    }
}
