using System;
using System.Collections.Generic;
using UnityEditor;

/// <summary>
/// Collector 归属关系和 Ignore 规则的共享 Editor 修改入口。
/// </summary>
public static class CollectorMutationUtility
{
    public enum CollectionState
    {
        Uncollected,
        DirectCollector,
        CoveredByFolderCollector,
        Ignored
    }

    public sealed class MembershipInfo
    {
        public string AssetPath;
        public string AssetGuid;
        public bool IsFolder;
        public CollectionState State;
        public CollectorReverseIndex.CollectorRef CollectorRef;
        public Collector Collector;
        public AssetCollectionGroup Group;
    }

    public static event Action Changed;

    public static AssetCollectionSetting LoadSetting()
    {
        return AssetDatabase.LoadAssetAtPath<AssetCollectionSetting>(FYAssetABSettings.Instance.AssetCollectionSettingPath);
    }

    public static MembershipInfo GetMembership(string assetPath)
    {
        string normalized = CollectorPathUtility.NormalizePath(assetPath);
        string guid = AssetDatabase.AssetPathToGUID(normalized);
        bool isFolder = AssetDatabase.IsValidFolder(normalized);
        AssetCollectionSetting setting = LoadSetting();

        var info = new MembershipInfo
        {
            AssetPath = normalized,
            AssetGuid = guid,
            IsFolder = isFolder,
            State = IsIgnoredPath(setting, normalized) ? CollectionState.Ignored : CollectionState.Uncollected
        };

        if (setting == null || !CollectorReverseIndex.Instance.TryGetCollector(normalized, out CollectorReverseIndex.CollectorRef collectorRef))
            return info;

        info.CollectorRef = collectorRef;
        info.Group = GetGroup(setting, collectorRef);
        info.Collector = GetCollector(setting, collectorRef);
        if (info.Collector == null)
            return info;

        bool directMatch = IsDirectMatch(info.Collector, normalized, isFolder);
        info.State = directMatch ? CollectionState.DirectCollector : CollectionState.CoveredByFolderCollector;
        if (!isFolder && IsIgnoredPath(setting, normalized))
            info.State = CollectionState.Ignored;

        return info;
    }

    public static bool AddToGroup(AssetCollectionSetting setting, AssetCollectionGroup group, string assetPath)
    {
        if (setting == null || group == null)
            return false;

        string normalized = CollectorPathUtility.NormalizePath(assetPath);
        if (string.IsNullOrEmpty(normalized) || string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(normalized)))
            return false;

        if (RemoveIgnorePattern(setting, normalized))
        {
            SaveSetting(setting);
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
            CollectPathType = isFolder ? ECollectPathType.Folder : ECollectPathType.File
        });

        SaveSetting(setting);
        NotifyChanged();
        return true;
    }

    public static bool RemoveOrIgnore(string assetPath)
    {
        string normalized = CollectorPathUtility.NormalizePath(assetPath);
        MembershipInfo info = GetMembership(normalized);
        if (info.State == CollectionState.Ignored)
            return false;

        AssetCollectionSetting setting = LoadSetting();
        if (setting == null)
            return false;

        if (info.State == CollectionState.DirectCollector)
        {
            Undo.RecordObject(setting, "Remove Asset From Collector");
            if (!RemoveCollector(setting, info.CollectorRef))
                return false;

            SaveSetting(setting);
            NotifyChanged();
            return true;
        }

        if (!info.IsFolder && info.State == CollectionState.CoveredByFolderCollector)
        {
            if (!AddIgnorePattern(setting, normalized))
                return false;

            SaveSetting(setting);
            NotifyChanged();
            return true;
        }

        return false;
    }

    public static bool RestoreIgnored(string assetPath)
    {
        string normalized = CollectorPathUtility.NormalizePath(assetPath);
        AssetCollectionSetting setting = LoadSetting();
        if (!RemoveIgnorePattern(setting, normalized))
            return false;

        SaveSetting(setting);
        NotifyChanged();
        return true;
    }

    public static bool IsIgnoredGuid(string guid)
    {
        if (string.IsNullOrEmpty(guid))
            return false;

        string assetPath = CollectorPathUtility.NormalizePath(AssetDatabase.GUIDToAssetPath(guid));
        return IsIgnoredPath(LoadSetting(), assetPath);
    }

    public static void NotifyChanged()
    {
        CollectorReverseIndex.Instance.MarkDirty();
        Changed?.Invoke();
        UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
    }

    private static bool IsIgnoredPath(AssetCollectionSetting setting, string assetPath)
    {
        return setting != null && GitIgnoreMatcher.Evaluate(assetPath, setting.GetEffectiveIgnorePatterns());
    }

    private static bool AddIgnorePattern(AssetCollectionSetting setting, string assetPath)
    {
        if (setting == null || string.IsNullOrEmpty(assetPath))
            return false;

        setting.IgnorePatterns ??= new List<string>();
        for (int i = 0; i < setting.IgnorePatterns.Count; i++)
        {
            if (string.Equals(setting.IgnorePatterns[i], assetPath, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        Undo.RecordObject(setting, "Ignore Asset");
        setting.IgnorePatterns.Add(assetPath);
        return true;
    }

    private static bool RemoveIgnorePattern(AssetCollectionSetting setting, string assetPath)
    {
        if (setting?.IgnorePatterns == null || string.IsNullOrEmpty(assetPath))
            return false;

        bool removed = false;
        for (int i = setting.IgnorePatterns.Count - 1; i >= 0; i--)
        {
            if (!string.Equals(setting.IgnorePatterns[i], assetPath, StringComparison.OrdinalIgnoreCase))
                continue;

            setting.IgnorePatterns.RemoveAt(i);
            removed = true;
        }

        return removed;
    }

    private static AssetCollectionGroup GetGroup(AssetCollectionSetting setting, CollectorReverseIndex.CollectorRef collectorRef)
    {
        if (setting?.Groups == null || collectorRef.GroupIndex < 0 || collectorRef.GroupIndex >= setting.Groups.Count)
            return null;

        return setting.Groups[collectorRef.GroupIndex];
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

    private static void SaveSetting(AssetCollectionSetting setting)
    {
        EditorUtility.SetDirty(setting);
        AssetDatabase.SaveAssets();
    }
}
