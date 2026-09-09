using System;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// FYAsset Settings 资产的共享加载器。Editor 下按 assetPath 加载或创建并保存，Runtime 下从 Resources 加载。
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
