using System.Collections.Generic;
using UnityEditor;

/// <summary>
/// Project 窗口右键菜单 —— 批量添加/移除 Collector。
/// </summary>
public static class CollectorContextMenu
{
    [MenuItem("Assets/FYAsset/Add to Collector Group", false, 1000)]
    private static void AddToAssetCollectionGroup()
    {
        string[] assetPaths = GetSelectedAssetPaths(includeFolders: true);
        if (assetPaths.Length == 0)
            return;

        CollectorTargetPickerWindow.Show(assetPaths, () =>
        {
            CollectorMutationUtility.NotifyChanged();
        });
    }

    [MenuItem("Assets/FYAsset/Add to Collector Group", true)]
    private static bool AddToAssetCollectionGroupValidate()
    {
        return GetSelectedAssetPaths(includeFolders: true).Length > 0;
    }

    [MenuItem("Assets/FYAsset/Remove from Collector", false, 1001)]
    private static void RemoveFromCollector()
    {
        string[] assetPaths = GetSelectedAssetPaths(includeFolders: true);
        if (assetPaths.Length == 0)
            return;

        bool changed = false;

        for (int i = 0; i < assetPaths.Length; i++)
            changed |= CollectorMutationUtility.RemoveOrExclude(assetPaths[i]);

        if (changed)
            CollectorMutationUtility.NotifyChanged();
    }

    [MenuItem("Assets/FYAsset/Remove from Collector", true)]
    private static bool RemoveFromCollectorValidate()
    {
        string[] assetPaths = GetSelectedAssetPaths(includeFolders: true);
        if (assetPaths.Length == 0)
            return false;

        for (int i = 0; i < assetPaths.Length; i++)
        {
            CollectorMutationUtility.MembershipInfo membership = CollectorMutationUtility.GetMembership(assetPaths[i]);
            if (membership.State == CollectorMutationUtility.CollectionState.DirectCollector)
                continue;
            if (!membership.IsFolder && membership.State == CollectorMutationUtility.CollectionState.CoveredByFolderCollector)
                continue;
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

}
