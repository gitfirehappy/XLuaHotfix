#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 本地Build数据导出，只有整包构建时使用，小版本热更不需要
/// 将 BuildIndex 导出为 JSON 文件到 StreamingAssets，绕过 Addressables 缓存问题
/// </summary>
public static class LocalStatusExporter
{
    /// <summary>
    /// StreamingAssets 中 BuildIndex.json 的相对路径
    /// </summary>
    private const string BUILD_INDEX_FILENAME = Constants.BUILD_INDEX_FILENAME;
    
    /// <summary>
    /// 获取 BuildIndex.json 在 StreamingAssets 中的完整路径
    /// </summary>
    public static string BuildIndexStreamingPath => Path.Combine(Application.streamingAssetsPath, BUILD_INDEX_FILENAME);

    /// <summary>
    /// 总导出入口 - 负责调用所有本地静态数据（LocalStaticData）的导出逻辑
    /// </summary>
    public static void ExportData(VersionNumber version)
    {
        Debug.Log("[LocalStatusExporter] 开始导出所有本地构建数据到 StreamingAssets...");
        
        // 确保 StreamingAssets 目录存在
        if (!Directory.Exists(Application.streamingAssetsPath))
        {
            Directory.CreateDirectory(Application.streamingAssetsPath);
        }

        // 1. 导出 BuildIndex
        ExportBuildIndex(version);
        
        // 2. 预留其他本地数据的导出位置
        // ExportOtherLocalData(version);

        AssetDatabase.Refresh();
        Debug.Log("[LocalStatusExporter] 本地数据导出完成。");
    }

    /// <summary>
    /// 导出 BuildIndex 到 StreamingAssets
    /// </summary>
    private static void ExportBuildIndex(VersionNumber version)
    {
        Debug.Log("[LocalStatusExporter] 正在生成 BuildIndex...");

        // 创建数据对象
        var buildIndexData = new BuildIndexData
        {
            BuildGUID = System.DateTime.UtcNow.ToString("yyyyMMddHHmmss"),
            BuildTime = System.DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
            IsDebug = EditorUserBuildSettings.development,
            Platform = EditorUserBuildSettings.activeBuildTarget.ToString(),
            Version = version
        };

        // 写入 StreamingAssets
        SerializationUtility.WriteToFile(BuildIndexStreamingPath, buildIndexData);
        
        // 额外写入一份到编辑器 LocalStaticData 目录（便于查看，不做运行时读取）
        string projectPath = Constants.BUILD_INDEX_JSON_PROJECT_PATH;
        string projectDir = Path.GetDirectoryName(projectPath);
        if (!Directory.Exists(projectDir))
        {
            Directory.CreateDirectory(projectDir);
        }
        SerializationUtility.WriteToFile(projectPath, buildIndexData);
        
        Debug.Log($"[LocalStatusExporter] BuildIndex 已写入: {BuildIndexStreamingPath}");
        Debug.Log($"[LocalStatusExporter] BuildIndex 副本已写入: {projectPath}");
        Debug.Log($"[LocalStatusExporter] Info - GUID: {buildIndexData.BuildGUID}, Ver: {version.GetVersionString()}");
    }

    /// <summary>
    /// 清理旧的 BuildIndex（如果存在）
    /// </summary>
    public static void CleanBuildIndex()
    {
        if (File.Exists(BuildIndexStreamingPath))
        {
            File.Delete(BuildIndexStreamingPath);
            AssetDatabase.Refresh();
            Debug.Log("[LocalStatusExporter] 已清理旧的 BuildIndex.json");
        }
    }
}
#endif
