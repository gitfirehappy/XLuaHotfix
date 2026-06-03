using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// AB backend build-time configuration.
/// </summary>
public sealed class ABBuildSettings : ScriptableObject
{
    public const string DEFAULT_ASSET_PATH = "Assets/Build/FYAssetABBuildSettings.asset";

    public string BuildPipelineConfigPath = "Assets/Build/BuildPipelineConfig.asset";
    public ManifestOutputFormat ManifestOutputFormat = ManifestOutputFormat.JsonAndBinary;
    public long MaxHotfixSizeBytes = 1L * 1024 * 1024 * 1024;

    [Header("Collector")]
    [FormerlySerializedAs("CollectorDataFolder")]
    public string AssetCollectionDataFolder = "Assets/FYAsset/CollectorData";

    [FormerlySerializedAs("CollectorSettingPath")]
    public string AssetCollectionSettingPath = "Assets/FYAsset/CollectorData/CollectorSetting.asset";

    public List<string> DependencyFilterExtensions = new();
}
