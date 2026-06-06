using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// AB backend runtime and build settings.
/// </summary>
public sealed class FYAssetABSettings : ScriptableObject
{
    [Header("Hotfix")]
    public string HotfixUrl = "https://firehappy-cfy.com/";
    public int HotfixMaxRetryCount = 3;
    public float HotfixRetryBaseDelaySeconds = 1f;

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
    private const string RESOURCE_LOAD_PATH = "FYAssetABSettings";

    private static FYAssetABSettings LoadOrCreate()
    {
#if UNITY_EDITOR
        if (!AssetDatabase.IsValidFolder("Assets/Resources"))
            AssetDatabase.CreateFolder("Assets", "Resources");

        FYAssetABSettings settings = AssetDatabase.LoadAssetAtPath<FYAssetABSettings>(DEFAULT_ASSET_PATH);
        if (settings != null)
            return settings;

        settings = CreateInstance<FYAssetABSettings>();
        AssetDatabase.CreateAsset(settings, DEFAULT_ASSET_PATH);
        AssetDatabase.SaveAssets();
        return settings;
#else
        return Resources.Load<FYAssetABSettings>(RESOURCE_LOAD_PATH) ?? CreateInstance<FYAssetABSettings>();
#endif
    }
}
