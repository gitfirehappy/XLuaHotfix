using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// AA backend runtime and build settings.
/// </summary>
public sealed class FYAssetAASettings : ScriptableObject
{
    [Header("Hotfix")]
    public string HotfixUrl = "https://firehappy-cfy.com/";
    public int HotfixMaxRetryCount = 3;
    public float HotfixRetryBaseDelaySeconds = 1f;

    [Header("Build")]
    public string BuildPipelineConfigPath = "Assets/Build/AABuildPipelineConfig.asset";
    public ManifestOutputFormat ManifestOutputFormat = ManifestOutputFormat.JsonAndBinary;
    public long MaxHotfixSizeBytes = 1L * 1024 * 1024 * 1024;
    public string LuaScriptsIndexPath = "Assets/Build/LuaScriptsIndex.asset";

    private static FYAssetAASettings _instance;
    public static FYAssetAASettings Instance => _instance ??= LoadOrCreate();

    public const string DEFAULT_ASSET_PATH = "Assets/Resources/FYAssetAASettings.asset";
    private const string RESOURCE_LOAD_PATH = "FYAssetAASettings";

    private static FYAssetAASettings LoadOrCreate()
    {
#if UNITY_EDITOR
        if (!AssetDatabase.IsValidFolder("Assets/Resources"))
            AssetDatabase.CreateFolder("Assets", "Resources");

        FYAssetAASettings settings = AssetDatabase.LoadAssetAtPath<FYAssetAASettings>(DEFAULT_ASSET_PATH);
        if (settings != null)
            return settings;

        settings = CreateInstance<FYAssetAASettings>();
        AssetDatabase.CreateAsset(settings, DEFAULT_ASSET_PATH);
        AssetDatabase.SaveAssets();
        return settings;
#else
        return Resources.Load<FYAssetAASettings>(RESOURCE_LOAD_PATH) ?? CreateInstance<FYAssetAASettings>();
#endif
    }
}
