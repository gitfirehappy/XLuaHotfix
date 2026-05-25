using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Project 窗口右键菜单 —— 批量添加/移除 Collector。
/// </summary>
public static class CollectorContextMenu
{
    [MenuItem("Assets/FYAsset/Add to Collector Group", false, 1000)]
    private static void AddToCollectorGroup()
    {
        string[] assetPaths = GetSelectedAssetPaths(includeFolders: true);
        if (assetPaths.Length == 0)
            return;

        CollectorTargetPickerWindow.Show(assetPaths, () =>
        {
            CollectorReverseIndex.Instance.MarkDirty();
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        });
    }

    [MenuItem("Assets/FYAsset/Add to Collector Group", true)]
    private static bool AddToCollectorGroupValidate()
    {
        return GetSelectedAssetPaths(includeFolders: true).Length > 0;
    }

    [MenuItem("Assets/FYAsset/Remove from Collector", false, 1001)]
    private static void RemoveFromCollector()
    {
        string[] assetPaths = GetSelectedAssetPaths(includeFolders: true);
        if (assetPaths.Length == 0)
            return;

        CollectorSetting setting = LoadSetting();
        if (setting == null)
            return;

        Undo.RecordObject(setting, "Remove Assets From Collector");
        bool changed = false;

        for (int i = 0; i < assetPaths.Length; i++)
        {
            if (!CollectorReverseIndex.Instance.TryGetCollector(assetPaths[i], out CollectorReverseIndex.CollectorRef collectorRef))
                continue;

            Collector collector = GetCollector(setting, collectorRef);
            if (collector == null)
                continue;

            bool isFolder = AssetDatabase.IsValidFolder(assetPaths[i]);
            bool isDirectMatch = isFolder
                ? collector.CollectPathType == ECollectPathType.Folder
                : collector.CollectPathType == ECollectPathType.File;
            if (!isDirectMatch)
                continue;
            if (!string.Equals(CollectorPathUtility.NormalizePath(collector.CollectPath), assetPaths[i], StringComparison.OrdinalIgnoreCase))
                continue;

            CollectorGroup group = GetGroup(setting, collectorRef);
            if (group?.Collectors == null || collectorRef.CollectorIndex < 0 || collectorRef.CollectorIndex >= group.Collectors.Count)
                continue;

            group.Collectors.RemoveAt(collectorRef.CollectorIndex);
            changed = true;
            CollectorReverseIndex.Instance.MarkDirty();
        }

        if (changed)
        {
            EditorUtility.SetDirty(setting);
            AssetDatabase.SaveAssets();
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        }
    }

    [MenuItem("Assets/FYAsset/Remove from Collector", true)]
    private static bool RemoveFromCollectorValidate()
    {
        string[] assetPaths = GetSelectedAssetPaths(includeFolders: true);
        if (assetPaths.Length == 0)
            return false;

        CollectorSetting setting = LoadSetting();
        for (int i = 0; i < assetPaths.Length; i++)
        {
            if (!CollectorReverseIndex.Instance.TryGetCollector(assetPaths[i], out CollectorReverseIndex.CollectorRef collectorRef))
                return false;

            Collector collector = GetCollector(setting, collectorRef);
            if (collector == null)
                return false;

            bool isFolder = AssetDatabase.IsValidFolder(assetPaths[i]);
            bool isDirectMatch = isFolder
                ? collector.CollectPathType == ECollectPathType.Folder
                : collector.CollectPathType == ECollectPathType.File;
            if (!isDirectMatch)
                return false;
            if (!string.Equals(CollectorPathUtility.NormalizePath(collector.CollectPath), assetPaths[i], StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }

    private static string[] GetSelectedAssetPaths(bool includeFolders)
    {
        List<string> result = new List<string>();
        string[] guids = Selection.assetGUIDs;
        for (int i = 0; i < guids.Length; i++)
        {
            string assetPath = CollectorPathUtility.NormalizePath(AssetDatabase.GUIDToAssetPath(guids[i]));
            if (string.IsNullOrEmpty(assetPath))
                continue;
            if (!includeFolders && AssetDatabase.IsValidFolder(assetPath))
                continue;
            result.Add(assetPath);
        }

        return result.ToArray();
    }

    private static CollectorSetting LoadSetting()
    {
        return AssetDatabase.LoadAssetAtPath<CollectorSetting>(FYAssetBuildSettingsProvider.Shared.CollectorSettingPath);
    }

    private static Collector GetCollector(CollectorSetting setting, CollectorReverseIndex.CollectorRef collectorRef)
    {
        CollectorGroup group = GetGroup(setting, collectorRef);
        if (group?.Collectors == null || collectorRef.CollectorIndex < 0 || collectorRef.CollectorIndex >= group.Collectors.Count)
            return null;

        return group.Collectors[collectorRef.CollectorIndex];
    }

    private static CollectorGroup GetGroup(CollectorSetting setting, CollectorReverseIndex.CollectorRef collectorRef)
    {
        if (setting?.Packages == null || collectorRef.PackageIndex < 0 || collectorRef.PackageIndex >= setting.Packages.Count)
            return null;

        CollectorPackage package = setting.Packages[collectorRef.PackageIndex];
        if (package?.Groups == null || collectorRef.GroupIndex < 0 || collectorRef.GroupIndex >= package.Groups.Count)
            return null;

        return package.Groups[collectorRef.GroupIndex];
    }

}
