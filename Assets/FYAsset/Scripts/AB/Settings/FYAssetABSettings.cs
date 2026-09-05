using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// AB backend 的 runtime 与构建设置。
/// </summary>
public sealed class FYAssetABSettings : ScriptableObject
{
    [Header("Hotfix")]
    public string HotfixUrl = "https://firehappy-cfy.com/";
    public int HotfixMaxRetryCount = 3;
    public float HotfixRetryBaseDelaySeconds = 1f;
    public int HotfixMetadataTimeoutSeconds = 15;
    public int HotfixBundleTimeoutSeconds = 300;

    [Header("Build")]
    public string BuildPipelineConfigPath = "Assets/Build/BuildPipelineConfig.asset";
    public ManifestOutputFormat ManifestOutputFormat = ManifestOutputFormat.JsonAndBinary;
    public long MaxHotfixSizeBytes = 1L * 1024 * 1024 * 1024;

    [Header("Collector")]
    public string AssetCollectionDataFolder = "Assets/FYAsset/CollectorData";
    public string AssetCollectionSettingPath = "Assets/FYAsset/CollectorData/CollectorSetting.asset";

    public List<string> DependencyFilterExtensions = new();

    private static FYAssetABSettings _instance;
    public static FYAssetABSettings Instance => _instance ??= LoadOrCreate();

    public const string DEFAULT_ASSET_PATH = "Assets/Resources/FYAssetABSettings.asset";
    public const string RESOURCE_LOAD_PATH = "FYAssetABSettings";

    private static FYAssetABSettings LoadOrCreate()
    {
        return FYAssetSettingsLoader.LoadOrCreate<FYAssetABSettings>(DEFAULT_ASSET_PATH, RESOURCE_LOAD_PATH);
    }
}
