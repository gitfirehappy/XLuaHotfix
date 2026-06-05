using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Shared editor mutations for Collector membership and AB asset exclusions.
/// </summary>
public static class CollectorMutationUtility
{
    public enum CollectionState
    {
        Uncollected,
        DirectCollector,
        CoveredByFolderCollector,
        Excluded
    }

    public sealed class MembershipInfo
    {
        public string AssetPath;
        public string AssetGuid;
        public bool IsFolder;
        public CollectionState State;
        public CollectorReverseIndex.CollectorRef CollectorRef;
        public Collector Collector;
        public AssetCollectionPackage Package;
        public AssetCollectionGroup Group;
    }

    public static event Action Changed;

    public static AssetCollectionSetting LoadSetting()
    {
        return AssetDatabase.LoadAssetAtPath<AssetCollectionSetting>(FYAssetBuildSettingsProvider.AB.AssetCollectionSettingPath);
    }

    public static MembershipInfo GetMembership(string assetPath)
    {
        string normalized = CollectorPathUtility.NormalizePath(assetPath);
        string guid = AssetDatabase.AssetPathToGUID(normalized);
        bool isFolder = AssetDatabase.IsValidFolder(normalized);

        var info = new MembershipInfo
        {
            AssetPath = normalized,
            AssetGuid = guid,
            IsFolder = isFolder,
            State = IsExcludedGuid(guid) ? CollectionState.Excluded : CollectionState.Uncollected
        };

        AssetCollectionSetting setting = LoadSetting();
        if (setting == null || !CollectorReverseIndex.Instance.TryGetCollector(normalized, out CollectorReverseIndex.CollectorRef collectorRef))
            return info;

        info.CollectorRef = collectorRef;
        info.Package = GetPackage(setting, collectorRef);
        info.Group = GetGroup(setting, collectorRef);
        info.Collector = GetCollector(setting, collectorRef);
        if (info.Collector == null)
            return info;

        bool directMatch = IsDirectMatch(info.Collector, normalized, isFolder);
        info.State = directMatch ? CollectionState.DirectCollector : CollectionState.CoveredByFolderCollector;
        if (!isFolder && IsExcludedGuid(guid))
            info.State = CollectionState.Excluded;

        return info;
    }

    public static bool AddToGroup(AssetCollectionSetting setting, AssetCollectionGroup group, string assetPath, ECollectorType collectorType, EForcePayloadKind forcePayloadKind)
    {
        if (setting == null || group == null)
            return false;

        string normalized = CollectorPathUtility.NormalizePath(assetPath);
        if (string.IsNullOrEmpty(normalized) || string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(normalized)))
            return false;

        string guid = AssetDatabase.AssetPathToGUID(normalized);
        if (RemoveExcludedGuid(guid))
        {
            SaveABSettings();
            NotifyChanged();
            return true;
        }

        if (CollectorReverseIndex.Instance.IsAssetCollected(normalized))
            return false;

        bool isFolder = AssetDatabase.IsValidFolder(normalized);
        group.Collectors ??= new List<Collector>();
        Undo.RecordObject(setting, "Add Asset To Collector Group");
        group.Collectors.Add(new Collector
        {
            CollectPath = normalized,
            CollectPathType = isFolder ? ECollectPathType.Folder : ECollectPathType.File,
            CollectorType = collectorType,
            ForcePayloadKind = isFolder ? forcePayloadKind : ResolveFilePayloadKind(normalized, forcePayloadKind),
            FilterRuleName = FYAssetSettings.RULE_COLLECT_ALL,
            GroupRuleName = FYAssetSettings.RULE_GROUP_ALL
        });

        EditorUtility.SetDirty(setting);
        AssetDatabase.SaveAssets();
        NotifyChanged();
        return true;
    }

    public static bool RemoveOrExclude(string assetPath)
    {
        string normalized = CollectorPathUtility.NormalizePath(assetPath);
        MembershipInfo info = GetMembership(normalized);
        if (info.State == CollectionState.Excluded)
            return false;

        AssetCollectionSetting setting = LoadSetting();
        if (setting == null)
            return false;

        if (info.State == CollectionState.DirectCollector)
        {
            Undo.RecordObject(setting, "Remove Asset From Collector");
            if (!RemoveCollector(setting, info.CollectorRef))
                return false;

            EditorUtility.SetDirty(setting);
            AssetDatabase.SaveAssets();
            NotifyChanged();
            return true;
        }

        if (!info.IsFolder && info.State == CollectionState.CoveredByFolderCollector)
        {
            string guid = AssetDatabase.AssetPathToGUID(normalized);
            if (!AddExcludedGuid(guid))
                return false;

            SaveABSettings();
            NotifyChanged();
            return true;
        }

        return false;
    }

    public static bool RestoreExcluded(string assetPath)
    {
        string guid = AssetDatabase.AssetPathToGUID(CollectorPathUtility.NormalizePath(assetPath));
        if (!RemoveExcludedGuid(guid))
            return false;

        SaveABSettings();
        NotifyChanged();
        return true;
    }

    public static bool ExcludeAsset(string assetPath)
    {
        string normalized = CollectorPathUtility.NormalizePath(assetPath);
        if (AssetDatabase.IsValidFolder(normalized))
            return false;

        string guid = AssetDatabase.AssetPathToGUID(normalized);
        if (!AddExcludedGuid(guid))
            return false;

        SaveABSettings();
        NotifyChanged();
        return true;
    }

    public static bool IsExcludedGuid(string guid)
    {
        if (string.IsNullOrEmpty(guid))
            return false;

        List<string> excluded = FYAssetABSettings.Instance.ExcludedAssetGUIDs;
        if (excluded == null)
            return false;

        for (int i = 0; i < excluded.Count; i++)
        {
            if (string.Equals(excluded[i], guid, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    public static void NotifyChanged()
    {
        CollectorReverseIndex.Instance.MarkDirty();
        Changed?.Invoke();
        UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
    }

    private static AssetCollectionPackage GetPackage(AssetCollectionSetting setting, CollectorReverseIndex.CollectorRef collectorRef)
    {
        if (setting?.Packages == null || collectorRef.PackageIndex < 0 || collectorRef.PackageIndex >= setting.Packages.Count)
            return null;

        return setting.Packages[collectorRef.PackageIndex];
    }

    private static AssetCollectionGroup GetGroup(AssetCollectionSetting setting, CollectorReverseIndex.CollectorRef collectorRef)
    {
        AssetCollectionPackage package = GetPackage(setting, collectorRef);
        if (package?.Groups == null || collectorRef.GroupIndex < 0 || collectorRef.GroupIndex >= package.Groups.Count)
            return null;

        return package.Groups[collectorRef.GroupIndex];
    }

    private static Collector GetCollector(AssetCollectionSetting setting, CollectorReverseIndex.CollectorRef collectorRef)
    {
        AssetCollectionGroup group = GetGroup(setting, collectorRef);
        if (group?.Collectors == null || collectorRef.CollectorIndex < 0 || collectorRef.CollectorIndex >= group.Collectors.Count)
            return null;

        return group.Collectors[collectorRef.CollectorIndex];
    }

    private static bool RemoveCollector(AssetCollectionSetting setting, CollectorReverseIndex.CollectorRef collectorRef)
    {
        AssetCollectionGroup group = GetGroup(setting, collectorRef);
        if (group?.Collectors == null || collectorRef.CollectorIndex < 0 || collectorRef.CollectorIndex >= group.Collectors.Count)
            return false;

        group.Collectors.RemoveAt(collectorRef.CollectorIndex);
        return true;
    }

    private static bool IsDirectMatch(Collector collector, string assetPath, bool isFolder)
    {
        if (collector == null)
            return false;

        bool typeMatches = isFolder
            ? collector.CollectPathType == ECollectPathType.Folder
            : collector.CollectPathType == ECollectPathType.File;
        return typeMatches &&
               string.Equals(CollectorPathUtility.NormalizePath(collector.CollectPath), assetPath, StringComparison.OrdinalIgnoreCase);
    }

    private static EForcePayloadKind ResolveFilePayloadKind(string assetPath, EForcePayloadKind requested)
    {
        if (string.Equals(System.IO.Path.GetExtension(assetPath), ".unity", StringComparison.OrdinalIgnoreCase))
            return EForcePayloadKind.Scene;

        return requested;
    }

    private static bool AddExcludedGuid(string guid)
    {
        if (string.IsNullOrEmpty(guid))
            return false;

        FYAssetABSettings settings = FYAssetABSettings.Instance;
        settings.ExcludedAssetGUIDs ??= new List<string>();
        if (IsExcludedGuid(guid))
            return false;

        Undo.RecordObject(settings, "Exclude Asset From Collector");
        settings.ExcludedAssetGUIDs.Add(guid);
        return true;
    }

    private static bool RemoveExcludedGuid(string guid)
    {
        if (string.IsNullOrEmpty(guid))
            return false;

        FYAssetABSettings settings = FYAssetABSettings.Instance;
        if (settings.ExcludedAssetGUIDs == null)
            return false;

        for (int i = settings.ExcludedAssetGUIDs.Count - 1; i >= 0; i--)
        {
            if (!string.Equals(settings.ExcludedAssetGUIDs[i], guid, StringComparison.Ordinal))
                continue;

            Undo.RecordObject(settings, "Restore Asset To Collector");
            settings.ExcludedAssetGUIDs.RemoveAt(i);
            return true;
        }

        return false;
    }

    private static void SaveABSettings()
    {
        EditorUtility.SetDirty(FYAssetABSettings.Instance);
        AssetDatabase.SaveAssets();
    }
}
