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

    [Header("Backend")]
    public bool UseABBackend = false;

    [Header("Hotfix Policies")]
    public HotfixRemoteFailurePolicy RemoteFailurePolicy = HotfixRemoteFailurePolicy.ContinueWithLocal;
    public HotfixMajorVersionMismatchPolicy MajorVersionMismatchPolicy = HotfixMajorVersionMismatchPolicy.ContinueWithLocal;

    [Header("Build")]
    public string BuildOutputRoot = "HotfixOutput";
    public string BuildPackagesFolderName = "Packages";

    [Header("Version")]
    public string VersionDataBasePath = "Assets/Build/VersionDataBase.asset";
    public string BuildIndexJsonPath = "Assets/Build/Bootstrap/BuildIndex.json";

    [Header("Push")]
    public List<PushTargetConfig> PushTargets = new();

    // ═══ 纯编译期常量（static const） ═══

    // --- 旧管线标识符 ---
    public const string LUA_SCRIPTS_INDEX = "LuaScriptsIndex";
    public const string DEFAULT_XLUA_TYPE_CONFIG_LOAD_LABEL = "XLuaConfigs";
    public const string HOTFIX_GROUP_NAME = "HotfixGroup";
    public const string BUILD_INDEX_FILENAME = "BuildIndex.json";

    // --- 新管线文件命名 ---
    public const string PACKAGE_INDEX_FILE_NAME = "PackageIndex.json";
    public const string MANIFEST_FILE_NAME = "ABManifest.json";
    public const string MANIFEST_FILE_NAME_BIN = "ABManifest.bin";
    public const string AA_MANIFEST_FILE_NAME = "AAManifest.json";
    public const string AA_MANIFEST_FILE_NAME_BIN = "AAManifest.bin";
    public const string BUNDLES_DIRECTORY_NAME = "bundles";
    public const string ADDRESSABLES_CATALOG_FILE_NAME = "catalog.json";

    // --- 编辑器路径 ---
    public const string BUILD_PIPELINE_WINDOW_MENU_PATH = "Tools/Build/Build Pipeline";
    public const string BINARY_SERIALIZER_GENERATE_PATH = "Assets/Tools/Scripts/Serialization/Generated";

    // --- Collector 规则名 ---
    public const string RULE_COLLECT_ALL = "CollectAll";
    public const string RULE_GROUP_ALL = "GroupAll";
    public const string RULE_GROUP_BY_TYPE = "GroupByType";
    public const string RULE_GROUP_BY_LABEL = "GroupByLabel";
    public const string RULE_GROUP_BY_DIRECTORY = "GroupByDirectory";

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
