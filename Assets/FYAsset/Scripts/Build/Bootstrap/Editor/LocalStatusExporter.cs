#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 本地Build数据导出，只有整包构建时使用，小版本热更不需要
/// 将 BuildIndex 和 ABManifest 导出为 JSON/Binary 文件到 StreamingAssets，绕过 Addressables 缓存问题
/// </summary>
public static class LocalStatusExporter
{
    private const string BUILD_INDEX_FILENAME = FYAssetSettings.BUILD_INDEX_FILENAME;
    private const string MANIFEST_FILENAME_JSON = FYAssetSettings.MANIFEST_FILE_NAME;
    private const string MANIFEST_FILENAME_BIN = FYAssetSettings.MANIFEST_FILE_NAME_BIN;

    private static bool ExportBinaryManifest = true;

    public static string BuildIndexStreamingPath => Path.Combine(Application.streamingAssetsPath, BUILD_INDEX_FILENAME);
    public static string ManifestJsonStreamingPath => Path.Combine(Application.streamingAssetsPath, MANIFEST_FILENAME_JSON);
    public static string ManifestBinStreamingPath => Path.Combine(Application.streamingAssetsPath, MANIFEST_FILENAME_BIN);

    /// <summary>
    /// 总导出入口 - 负责导出启动期所需的本地构建数据（Bootstrap）。
    /// </summary>
    public static void ExportData(VersionNumber version)
    {
        Debug.Log("[LocalStatusExporter] 开始导出所有本地构建数据到 StreamingAssets...");
        
        if (!Directory.Exists(Application.streamingAssetsPath))
        {
            Directory.CreateDirectory(Application.streamingAssetsPath);
        }

        ExportBuildIndex(version);
        ExportABManifest(version);

        AssetDatabase.Refresh();
        Debug.Log("[LocalStatusExporter] 本地数据导出完成。");
    }

    private static void ExportBuildIndex(VersionNumber version)
    {
        Debug.Log("[LocalStatusExporter] 正在生成 BuildIndex...");

        var buildIndexData = new BuildIndexData
        {
            BuildGUID = System.DateTime.UtcNow.ToString("yyyyMMddHHmmss"),
            BuildTime = System.DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
            IsDebug = EditorUserBuildSettings.development,
            Platform = EditorUserBuildSettings.activeBuildTarget.ToString(),
            Version = version
        };

        SerializationUtility.WriteToFile(BuildIndexStreamingPath, buildIndexData);
        
        string projectPath = FYAssetSettings.Instance.BuildIndexJsonPath;
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
    /// 导出 ABManifest 到 StreamingAssets（同时导出 JSON 和 Binary 格式）
    /// </summary>
    private static void ExportABManifest(VersionNumber version)
    {
        Debug.Log("[LocalStatusExporter] 正在生成 ABManifest...");

        var manifest = CreateEmptyManifest(version);
        if (manifest == null)
        {
            Debug.LogWarning("[LocalStatusExporter] ABManifest 生成跳过 - 当前构建管线尚未实现完整的数据填充");
            return;
        }

        SerializationUtility.WriteToFile(ManifestJsonStreamingPath, manifest, "json", true);
        Debug.Log($"[LocalStatusExporter] ABManifest.json 已写入: {ManifestJsonStreamingPath}");

        if (ExportBinaryManifest)
        {
            SerializationUtility.WriteToFile(ManifestBinStreamingPath, manifest, "binary", false);
            Debug.Log($"[LocalStatusExporter] ABManifest.bin 已写入: {ManifestBinStreamingPath}");
        }

        Debug.Log($"[LocalStatusExporter] ABManifest Info - Package: {manifest.PackageName}, Ver: {manifest.PackageVersion.GetVersionString()}");
    }

    /// <summary>
    /// 创建空的 ABManifest（占位实现，完整数据填充由构建管线 Task 负责）
    /// </summary>
    private static ABManifest CreateEmptyManifest(VersionNumber version)
    {
        return new ABManifest
        {
            PackageName = "MainPackage",
            PackageVersion = version,
            BuildTimestamp = System.DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            AssetEntries = new System.Collections.Generic.List<ManifestAssetEntry>(),
            BundleEntries = new System.Collections.Generic.List<ManifestBundleEntry>()
        };
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

    /// <summary>
    /// 清理旧的 ABManifest 文件（如果存在）
    /// </summary>
    public static void CleanABManifest()
    {
        bool cleaned = false;
        if (File.Exists(ManifestJsonStreamingPath))
        {
            File.Delete(ManifestJsonStreamingPath);
            cleaned = true;
        }
        if (File.Exists(ManifestBinStreamingPath))
        {
            File.Delete(ManifestBinStreamingPath);
            cleaned = true;
        }
        if (cleaned)
        {
            AssetDatabase.Refresh();
            Debug.Log("[LocalStatusExporter] 已清理旧的 ABManifest 文件");
        }
    }
}
#endif
