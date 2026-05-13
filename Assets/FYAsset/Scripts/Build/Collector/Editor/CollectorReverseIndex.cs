using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Collector 反向索引：将资产路径映射回 Package / Group / Collector。
/// </summary>
public sealed class CollectorReverseIndex
{
    public struct CollectorRef : IEquatable<CollectorRef>
    {
        public int PackageIndex;
        public int GroupIndex;
        public int CollectorIndex;

        public CollectorRef(int packageIndex, int groupIndex, int collectorIndex)
        {
            PackageIndex = packageIndex;
            GroupIndex = groupIndex;
            CollectorIndex = collectorIndex;
        }

        public bool Equals(CollectorRef other) =>
            PackageIndex == other.PackageIndex &&
            GroupIndex == other.GroupIndex &&
            CollectorIndex == other.CollectorIndex;

        public override bool Equals(object obj) =>
            obj is CollectorRef other && Equals(other);

        public override int GetHashCode() =>
            HashCode.Combine(PackageIndex, GroupIndex, CollectorIndex);

        public override string ToString() =>
            $"[{PackageIndex}][{GroupIndex}][{CollectorIndex}]";
    }

    private struct CollectorBuildEntry
    {
        public Collector Collector;
        public CollectorRef Reference;
    }

    private static readonly CollectorReverseIndex _instance = new CollectorReverseIndex();

    private readonly Dictionary<string, CollectorRef> _map = new Dictionary<string, CollectorRef>(StringComparer.OrdinalIgnoreCase);
    private bool _dirty = true;

    public static CollectorReverseIndex Instance => _instance;

    private CollectorReverseIndex()
    {
        Undo.undoRedoPerformed += MarkDirty;
    }

    public void MarkDirty()
    {
        _dirty = true;
    }

    public void RebuildIfDirty(CollectorSetting setting)
    {
        if (!_dirty)
            return;

        _map.Clear();

        CollectorSetting actualSetting = setting;
        if (actualSetting == null)
        {
            CollectorDataMigrator.EnsureDataFolder();
            CollectorDataMigrator.MigrateFromLegacyPath();
            actualSetting = AssetDatabase.LoadAssetAtPath<CollectorSetting>(FYAssetSettings.Instance.CollectorSettingPath);
        }

        if (actualSetting != null)
        {
            List<CollectorBuildEntry> entries = BuildEntries(actualSetting);
            for (int i = 0; i < entries.Count; i++)
                IndexCollector(entries[i]);
        }

        _dirty = false;
    }

    public bool TryGetCollector(string assetPath, out CollectorRef result)
    {
        RebuildIfDirty(null);
        return _map.TryGetValue(CollectorPathUtility.NormalizePath(assetPath), out result);
    }

    public bool IsAssetCollected(string assetPath)
    {
        return TryGetCollector(assetPath, out _);
    }

    private List<CollectorBuildEntry> BuildEntries(CollectorSetting setting)
    {
        List<CollectorBuildEntry> entries = new List<CollectorBuildEntry>();
        if (setting.Packages == null)
            return entries;

        for (int pi = 0; pi < setting.Packages.Count; pi++)
        {
            CollectorPackage package = setting.Packages[pi];
            if (package == null || package.Groups == null)
                continue;

            for (int gi = 0; gi < package.Groups.Count; gi++)
            {
                CollectorGroup group = package.Groups[gi];
                if (group == null || !group.Enabled || group.Collectors == null)
                    continue;

                for (int ci = 0; ci < group.Collectors.Count; ci++)
                {
                    Collector collector = group.Collectors[ci];
                    if (collector == null || string.IsNullOrEmpty(collector.CollectPath))
                        continue;

                    entries.Add(new CollectorBuildEntry
                    {
                        Collector = collector,
                        Reference = new CollectorRef(pi, gi, ci)
                    });
                }
            }
        }

        entries.Sort(CompareEntries);
        return entries;
    }

    private void IndexCollector(CollectorBuildEntry entry)
    {
        Collector collector = entry.Collector;
        if (collector.CollectPathType == ECollectPathType.File)
        {
            IndexFileCollector(entry.Reference, collector);
            return;
        }

        IndexFolderCollector(entry.Reference, collector);
    }

    private void IndexFileCollector(CollectorRef collectorRef, Collector collector)
    {
        string collectPath = CollectorPathUtility.NormalizePath(collector.CollectPath);
        if (!IsValidFileCollectPath(collectPath))
            return;

        if (ShouldSkipAsset(collector, collectPath))
            return;

        AddIfMissing(collectPath, collectorRef);
    }

    private void IndexFolderCollector(CollectorRef collectorRef, Collector collector)
    {
        string collectPath = CollectorPathUtility.NormalizePath(collector.CollectPath);
        if (!AssetDatabase.IsValidFolder(collectPath))
            return;

        AddIfMissing(collectPath, collectorRef);

        string[] guids = AssetDatabase.FindAssets(string.Empty, new[] { collectPath });
        if (guids == null || guids.Length == 0)
            return;

        for (int i = 0; i < guids.Length; i++)
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(guids[i]);
            if (string.IsNullOrEmpty(assetPath) || AssetDatabase.IsValidFolder(assetPath))
                continue;

            assetPath = CollectorPathUtility.NormalizePath(assetPath);
            if (ShouldSkipAsset(collector, assetPath))
                continue;

            AddIfMissing(assetPath, collectorRef);
        }
    }

    private bool ShouldSkipAsset(Collector collector, string assetPath)
    {
        string extension = System.IO.Path.GetExtension(assetPath);
        IFilterRule filterRule;
        try
        {
            filterRule = RuleResolver.GetFilterRule(collector.FilterRuleName);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[CollectorReverseIndex] 过滤规则解析失败，跳过资源: {assetPath}, Rule={collector.FilterRuleName}, Error={ex.Message}");
            return true;
        }

        if (filterRule == null)
            return true;

        FilterRuleContext context = new FilterRuleContext
        {
            AssetPath = assetPath,
            Extension = extension,
            CollectPath = collector.CollectPath
        };

        if (!filterRule.IsCollectable(context))
            return true;

        return CollectorPathUtility.MatchesIgnorePattern(assetPath, collector.CollectPath, collector.IgnorePatterns);
    }

    private void AddIfMissing(string assetPath, CollectorRef collectorRef)
    {
        if (!_map.ContainsKey(assetPath))
            _map.Add(assetPath, collectorRef);
    }

    private static int CompareEntries(CollectorBuildEntry a, CollectorBuildEntry b)
    {
        int depthCompare = CollectorPathUtility.PathDepth(b.Collector.CollectPath).CompareTo(CollectorPathUtility.PathDepth(a.Collector.CollectPath));
        if (depthCompare != 0)
            return depthCompare;

        if (a.Collector.CollectPathType != b.Collector.CollectPathType)
            return a.Collector.CollectPathType == ECollectPathType.File ? -1 : 1;

        return string.Compare(a.Collector.CollectPath, b.Collector.CollectPath, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsValidFileCollectPath(string collectPath)
    {
        if (string.IsNullOrEmpty(collectPath) || AssetDatabase.IsValidFolder(collectPath))
            return false;

        return !string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(collectPath));
    }
}
