using UnityEngine;

/// <summary>
/// AB backend build-time configuration.
/// </summary>
public sealed class ABBuildSettings : ScriptableObject
{
    public const string DEFAULT_ASSET_PATH = "Assets/Build/FYAssetABBuildSettings.asset";

    public string BuildPipelineConfigPath = "Assets/Build/BuildPipelineConfig.asset";
    public ManifestOutputFormat ManifestOutputFormat = ManifestOutputFormat.JsonAndBinary;
    public long MaxHotfixSizeBytes = 1L * 1024 * 1024 * 1024;
}
