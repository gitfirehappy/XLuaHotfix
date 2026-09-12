using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// FYAsset 运行期与全局设置。
/// Runtime 程序集，打包时包含在 build 中。
/// </summary>
public class FYAssetSettings : ScriptableObject
{
    // ═══ 可配置字段（SO 实例数据） ═══

    [Header("Project")]
    public string ProjectName = "ProjectName";

    [Header("Build")]
    public string BuildOutputRoot = "HotfixOutput";
    public string BuildPackagesFolderName = "Packages";

    [Header("Version")]
    public string BuildIndexJsonPath = "Assets/Build/Bootstrap/BuildIndex.json";

    [Header("Push")]
    public List<PushTargetConfig> PushTargets = new();

    // ═══ 纯编译期常量（static const） ═══

    public const string HOTFIX_GROUP_NAME = "HotfixGroup";
    public const string BUILD_INDEX_FILENAME = "BuildIndex.json";

    /// <summary>累计 Hotfix 输出根目录名（相对 BuildOutputRoot）：Hotfix 累计、Full 重置</summary>

    // --- 输出文件命名 ---
    public const string PACKAGE_INDEX_FILE_NAME = "PackageIndex.json";
    public const string MANIFEST_FILE_NAME = "ABManifest.json";
    public const string MANIFEST_FILE_NAME_BIN = "ABManifest.bin";
    public const string AA_MANIFEST_FILE_NAME = "AAManifest.json";
    public const string AA_MANIFEST_FILE_NAME_BIN = "AAManifest.bin";
    public const string BUNDLES_DIRECTORY_NAME = "bundles";
    public const string STANDALONE_DIRECTORY_NAME = "Standalone";
    public const string ADDRESSABLES_CATALOG_FILE_NAME = "catalog.json";

    // ═══ Singleton ═══

    private static FYAssetSettings _instance;
    public static FYAssetSettings Instance => _instance ??= LoadOrCreate();

    public const string DEFAULT_ASSET_PATH = "Assets/Resources/FYAssetSettings.asset";
    public const string RESOURCE_LOAD_PATH = "FYAssetSettings";

    private static FYAssetSettings LoadOrCreate()
    {
        return FYAssetSettingsLoader.LoadOrCreate<FYAssetSettings>(DEFAULT_ASSET_PATH, RESOURCE_LOAD_PATH);
    }

}

public enum ManifestOutputFormat
{
    JsonOnly = 0,
    JsonAndBinary = 1,
    BinaryOnly = 2
}
