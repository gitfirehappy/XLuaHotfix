#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Editor-only access point for FYAsset build configuration assets.
/// </summary>
public static class FYAssetBuildSettingsProvider
{
    private static SharedBuildSettings _shared;
    private static AABuildSettings _aa;
    private static ABBuildSettings _ab;

    public static SharedBuildSettings Shared => _shared ??= LoadOrCreate(SharedBuildSettings.DEFAULT_ASSET_PATH, CreateDefaultSharedSettings);
    public static AABuildSettings AA => _aa ??= LoadOrCreate(AABuildSettings.DEFAULT_ASSET_PATH, CreateDefaultAASettings);
    public static ABBuildSettings AB => _ab ??= LoadOrCreate(ABBuildSettings.DEFAULT_ASSET_PATH, CreateDefaultABSettings);

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
        _shared = null;
        _aa = null;
        _ab = null;
    }

    private static T LoadOrCreate<T>(string assetPath, System.Func<T> factory) where T : ScriptableObject
    {
        var asset = AssetDatabase.LoadAssetAtPath<T>(assetPath);
        if (asset != null)
            return asset;

        EnsureAssetParentFolder(assetPath);
        asset = factory();
        AssetDatabase.CreateAsset(asset, assetPath);
        EditorUtility.SetDirty(asset);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        return asset;
    }

    private static SharedBuildSettings CreateDefaultSharedSettings()
    {
        var settings = ScriptableObject.CreateInstance<SharedBuildSettings>();
        settings.PushTargets.Add(new PushTargetConfig
        {
            Id = "local",
            Type = PushTargetType.LocalDirectory,
            Path = Path.Combine(settings.BuildOutputRoot, "PushTargets", "local")
        });
        return settings;
    }

    private static AABuildSettings CreateDefaultAASettings()
    {
        return ScriptableObject.CreateInstance<AABuildSettings>();
    }

    private static ABBuildSettings CreateDefaultABSettings()
    {
        return ScriptableObject.CreateInstance<ABBuildSettings>();
    }

    private static void EnsureAssetParentFolder(string assetPath)
    {
        string folder = Path.GetDirectoryName(assetPath)?.Replace('\\', '/');
        if (string.IsNullOrEmpty(folder) || AssetDatabase.IsValidFolder(folder))
            return;

        string[] parts = folder.Split('/');
        string current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }
    }
}
#endif
