#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Editor-only access point for FYAsset build configuration assets.
/// </summary>
public static class FYAssetBuildSettingsProvider
{
    private static FYAssetSettings _global;
    private static FYAssetAASettings _aa;
    private static FYAssetABSettings _ab;

    public static FYAssetSettings Global => _global ??= FYAssetSettingsLoader.LoadOrCreate(
        FYAssetSettings.DEFAULT_ASSET_PATH,
        FYAssetSettings.RESOURCE_LOAD_PATH,
        CreateDefaultGlobalSettings);
    public static FYAssetAASettings AA => _aa ??= FYAssetSettingsLoader.LoadOrCreate(
        FYAssetAASettings.DEFAULT_ASSET_PATH,
        FYAssetAASettings.RESOURCE_LOAD_PATH,
        CreateDefaultAASettings);
    public static FYAssetABSettings AB => _ab ??= FYAssetSettingsLoader.LoadOrCreate(
        FYAssetABSettings.DEFAULT_ASSET_PATH,
        FYAssetABSettings.RESOURCE_LOAD_PATH,
        CreateDefaultABSettings);

    public static ScriptableObject CurrentBackend => FYAssetSettings.Instance.UseABBackend ? AB : AA;

    public static string GetPipelineConfigPath(BackendMode mode)
    {
        return mode == BackendMode.ABManifest
            ? AB.BuildPipelineConfigPath
            : AA.BuildPipelineConfigPath;
    }

    public static ManifestOutputFormat GetManifestOutputFormat(BackendMode mode)
    {
        return mode == BackendMode.ABManifest
            ? AB.ManifestOutputFormat
            : AA.ManifestOutputFormat;
    }

    public static long GetMaxHotfixSizeBytes(BackendMode mode)
    {
        return mode == BackendMode.ABManifest
            ? AB.MaxHotfixSizeBytes
            : AA.MaxHotfixSizeBytes;
    }

    public static void Reload()
    {
        _global = null;
        _aa = null;
        _ab = null;
    }

    private static FYAssetSettings CreateDefaultGlobalSettings()
    {
        FYAssetSettings settings = ScriptableObject.CreateInstance<FYAssetSettings>();
        settings.PushTargets.Add(new PushTargetConfig
        {
            Id = "local",
            Type = PushTargetType.LocalDirectory,
            Path = "HotfixPublish/Local",
            PublicBaseUrl = "http://127.0.0.1:18080/"
        });
        settings.PushTargets.Add(new PushTargetConfig
        {
            Id = "cloudflare",
            Type = PushTargetType.CloudflarePages,
            Path = "HotfixPublish/Cloudflare",
            PublicBaseUrl = "https://firehappy-cfy.com/"
        });
        return settings;
    }

    private static FYAssetAASettings CreateDefaultAASettings()
    {
        return ScriptableObject.CreateInstance<FYAssetAASettings>();
    }

    private static FYAssetABSettings CreateDefaultABSettings()
    {
        return ScriptableObject.CreateInstance<FYAssetABSettings>();
    }
}
#endif
