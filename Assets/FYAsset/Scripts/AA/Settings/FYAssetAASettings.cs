using UnityEngine;

/// <summary>
/// AA backend 的 runtime 与构建设置。
/// </summary>
public sealed class FYAssetAASettings : ScriptableObject
{
    [Header("Hotfix")]
    public string HotfixUrl = "https://firehappy-cfy.com/";
    public int HotfixMaxRetryCount = 3;
    public float HotfixRetryBaseDelaySeconds = 1f;
    public int HotfixMetadataTimeoutSeconds = 15;
    public int HotfixBundleTimeoutSeconds = 300;

    [Header("Build")]
    public string BuildPipelineConfigPath = "Assets/Build/AABuildPipelineConfig.asset";
    public ManifestOutputFormat ManifestOutputFormat = ManifestOutputFormat.JsonAndBinary;
    public long MaxHotfixSizeBytes = 1L * 1024 * 1024 * 1024;

    private static FYAssetAASettings _instance;
    public static FYAssetAASettings Instance => _instance ??= LoadOrCreate();

    public const string DEFAULT_ASSET_PATH = "Assets/Resources/FYAssetAASettings.asset";
    public const string RESOURCE_LOAD_PATH = "FYAssetAASettings";

    private static FYAssetAASettings LoadOrCreate()
    {
        return FYAssetSettingsLoader.LoadOrCreate<FYAssetAASettings>(DEFAULT_ASSET_PATH, RESOURCE_LOAD_PATH);
    }
}
