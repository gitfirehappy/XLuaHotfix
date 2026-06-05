#if UNITY_EDITOR
using System.IO;
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

    public static FYAssetSettings Global => _global ??= LoadOrCreate(FYAssetSettings.DEFAULT_ASSET_PATH, CreateDefaultGlobalSettings);
    public static FYAssetAASettings AA => _aa ??= LoadOrCreate(FYAssetAASettings.DEFAULT_ASSET_PATH, CreateDefaultAASettings);
    public static FYAssetABSettings AB => _ab ??= LoadOrCreate(FYAssetABSettings.DEFAULT_ASSET_PATH, CreateDefaultABSettings);

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

    private static FYAssetSettings CreateDefaultGlobalSettings()
    {
        FYAssetSettings settings = ScriptableObject.CreateInstance<FYAssetSettings>();

        SharedBuildSettings oldShared = AssetDatabase.LoadAssetAtPath<SharedBuildSettings>(SharedBuildSettings.DEFAULT_ASSET_PATH);
        if (oldShared != null)
        {
            settings.BuildOutputRoot = oldShared.BuildOutputRoot;
            settings.VersionDataBasePath = oldShared.VersionDataBasePath;
            settings.BuildIndexJsonPath = oldShared.BuildIndexJsonPath;
        }

        BuildRepositorySettings oldRepository = AssetDatabase.LoadAssetAtPath<BuildRepositorySettings>(BuildRepositorySettings.DEFAULT_ASSET_PATH);
        if (oldRepository?.PushTargets != null && oldRepository.PushTargets.Count > 0)
            settings.PushTargets = new System.Collections.Generic.List<PushTargetConfig>(oldRepository.PushTargets);
        else
            settings.PushTargets.Add(new PushTargetConfig
            {
                Id = "local",
                Type = PushTargetType.LocalDirectory,
                Path = string.Empty
            });

        return settings;
    }

    private static FYAssetAASettings CreateDefaultAASettings()
    {
        FYAssetAASettings settings = ScriptableObject.CreateInstance<FYAssetAASettings>();

        AABuildSettings oldAA = AssetDatabase.LoadAssetAtPath<AABuildSettings>(AABuildSettings.DEFAULT_ASSET_PATH);
        if (oldAA != null)
        {
            settings.BuildPipelineConfigPath = oldAA.BuildPipelineConfigPath;
            settings.ManifestOutputFormat = oldAA.ManifestOutputFormat;
            settings.MaxHotfixSizeBytes = oldAA.MaxHotfixSizeBytes;
        }

        SharedBuildSettings oldShared = AssetDatabase.LoadAssetAtPath<SharedBuildSettings>(SharedBuildSettings.DEFAULT_ASSET_PATH);
        if (oldShared != null)
            settings.LuaScriptsIndexPath = oldShared.LuaScriptsIndexPath;

        return settings;
    }

    private static FYAssetABSettings CreateDefaultABSettings()
    {
        FYAssetABSettings settings = ScriptableObject.CreateInstance<FYAssetABSettings>();

        ABBuildSettings oldAB = AssetDatabase.LoadAssetAtPath<ABBuildSettings>(ABBuildSettings.DEFAULT_ASSET_PATH);
        if (oldAB != null)
        {
            settings.BuildPipelineConfigPath = oldAB.BuildPipelineConfigPath;
            settings.ManifestOutputFormat = oldAB.ManifestOutputFormat;
            settings.MaxHotfixSizeBytes = oldAB.MaxHotfixSizeBytes;
            settings.AssetCollectionDataFolder = oldAB.AssetCollectionDataFolder;
            settings.AssetCollectionSettingPath = oldAB.AssetCollectionSettingPath;
            settings.DependencyFilterExtensions = oldAB.DependencyFilterExtensions != null
                ? new System.Collections.Generic.List<string>(oldAB.DependencyFilterExtensions)
                : new System.Collections.Generic.List<string>();
        }

        return settings;
    }

    private static void EnsureAssetParentFolder(string assetPath)
    {
        string folder = FYAssetPathUtility.NormalizeAssetPath(Path.GetDirectoryName(assetPath));
        if (string.IsNullOrEmpty(folder) || AssetDatabase.IsValidFolder(folder))
            return;

        string[] parts = folder.Split('/');
        string current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = FYAssetPathUtility.JoinAssetPath(current, parts[i]);
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }
    }
}
#endif
