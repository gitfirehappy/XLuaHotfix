using System;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// runtime-safe FYAsset settings asset 的共享 loader。
/// </summary>
public static class FYAssetSettingsLoader
{
    public static T LoadOrCreate<T>(
        string assetPath,
        string resourceLoadPath,
        Func<T> factory = null) where T : ScriptableObject
    {
#if UNITY_EDITOR
        if (!AssetDatabase.IsValidFolder("Assets/Resources"))
            AssetDatabase.CreateFolder("Assets", "Resources");

        T settings = AssetDatabase.LoadAssetAtPath<T>(assetPath);
        if (settings != null)
            return settings;

        settings = factory != null ? factory() : ScriptableObject.CreateInstance<T>();
        AssetDatabase.CreateAsset(settings, assetPath);
        EditorUtility.SetDirty(settings);
        AssetDatabase.SaveAssets();
        return settings;
#else
        return Resources.Load<T>(resourceLoadPath)
               ?? (factory != null ? factory() : ScriptableObject.CreateInstance<T>());
#endif
    }
}
