using UnityEngine;

/// <summary>
/// AA backend build-time configuration.
/// </summary>
public sealed class AABuildSettings : ScriptableObject
{
    public const string DEFAULT_ASSET_PATH = "Assets/Build/FYAssetAABuildSettings.asset";

    public string BuildPipelineConfigPath = "Assets/Build/AABuildPipelineConfig.asset";
    public ManifestOutputFormat ManifestOutputFormat = ManifestOutputFormat.JsonAndBinary;
    public long MaxHotfixSizeBytes = 1L * 1024 * 1024 * 1024;
}
