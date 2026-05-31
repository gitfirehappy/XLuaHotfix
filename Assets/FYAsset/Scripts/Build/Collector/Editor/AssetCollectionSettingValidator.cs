using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// AssetCollectionSetting save-time validator.
/// </summary>
public static class AssetCollectionSettingValidator
{
    #region Public API

    public static List<BuildMessage> Validate(AssetCollectionSetting setting)
    {
        var messages = new List<BuildMessage>();

        if (setting == null || setting.Packages == null || setting.Packages.Count == 0)
        {
            messages.Add(BuildMessage.NoPackages("Setting"));
            return messages;
        }

        HashSet<string> seenPkg = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int pi = 0; pi < setting.Packages.Count; pi++)
        {
            var pkg = setting.Packages[pi];
            if (pkg == null)
                continue;

            string pkgSrc = string.Concat("Package[", pi, "]");
            if (string.IsNullOrEmpty(pkg.PackageName))
                messages.Add(BuildMessage.EmptyPackageName(pkgSrc));
            else
            {
                string segmentError = BundleNameBuilder.ValidateSegment(pkg.PackageName);
                if (segmentError != null)
                    messages.Add(BuildMessage.InvalidBundleNameSegment(segmentError, pkgSrc));
                if (!seenPkg.Add(pkg.PackageName))
                    messages.Add(BuildMessage.DuplicatePackageName(pkg.PackageName, pkgSrc));
            }

            ValidatePackage(pkg, pi, messages);
        }

        ValidateAssetEntries(setting, messages);
        CheckCrossPackageOverlaps(setting, messages);
        return messages;
    }

    #endregion

    #region Private Methods

    private static void ValidatePackage(AssetCollectionPackage pkg, int pkgIdx, List<BuildMessage> messages)
    {
        if (pkg.Groups == null || pkg.Groups.Count == 0)
        {
            string src = string.Concat("Package[", pkgIdx, "]");
            messages.Add(BuildMessage.EmptyPackage(pkg.PackageName ?? "(unnamed)", src));
            return;
        }

        HashSet<string> seenGrp = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        List<(string path, string src)> allCollectPaths = new List<(string, string)>();

        for (int gi = 0; gi < pkg.Groups.Count; gi++)
        {
            var grp = pkg.Groups[gi];
            if (grp == null)
                continue;

            string grpSrc = string.Concat("Package[", pkgIdx, "].Group[", gi, "]");
            if (string.IsNullOrEmpty(grp.GroupName))
                messages.Add(BuildMessage.EmptyGroupName(grpSrc));
            else
            {
                string segmentError = BundleNameBuilder.ValidateSegment(grp.GroupName);
                if (segmentError != null)
                    messages.Add(BuildMessage.InvalidBundleNameSegment(segmentError, grpSrc));
                if (!seenGrp.Add(grp.GroupName))
                    messages.Add(BuildMessage.DuplicateGroupName(grp.GroupName, pkg.PackageName, grpSrc));
            }

            ValidateLabels(grp.Labels, grpSrc, messages);

            if (grp.Collectors == null)
                continue;

            for (int ci = 0; ci < grp.Collectors.Count; ci++)
            {
                var col = grp.Collectors[ci];
                if (col == null)
                    continue;

                string colSrc = string.Concat("Package[", pkgIdx, "].Group[", gi, "].Collector[", ci, "]");

                if (col.CollectorType == ECollectorType.Implicit)
                    messages.Add(BuildMessage.InvalidCollectorType(col.CollectorType, colSrc));

                if (string.IsNullOrEmpty(col.CollectPath))
                {
                    messages.Add(BuildMessage.EmptyCollectPath(colSrc));
                }
                else
                {
                    string normalized = CollectorPathUtility.NormalizePath(col.CollectPath);
                    allCollectPaths.Add((normalized, colSrc));
                    if (!CollectPathExists(col))
                        messages.Add(BuildMessage.PathNotFound(col.CollectPath, colSrc));
                }

                ValidateRule(col.FilterRuleName, "FilterRule", colSrc, messages);
                ValidateRule(col.GroupRuleName, "GroupRule", colSrc, messages);
            }
        }

        CheckSameDepthConflicts(allCollectPaths, messages);
    }

    private static void ValidateAssetEntries(AssetCollectionSetting setting, List<BuildMessage> messages)
    {
        if (setting.AssetEntries == null)
            return;

        HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < setting.AssetEntries.Count; i++)
        {
            AssetEntry entry = setting.AssetEntries[i];
            if (entry == null)
                continue;

            string src = string.Concat("AssetEntries[", i, "]");
            if (string.IsNullOrEmpty(entry.AssetGUID))
                continue;

            if (!seen.Add(entry.AssetGUID))
                messages.Add(BuildMessage.DuplicateGuid(entry.AssetGUID, src));

            ValidateLabels(entry.Labels, src, messages);
        }
    }

    private static void ValidateLabels(List<string> labels, string source, List<BuildMessage> messages)
    {
        if (labels == null)
            return;

        for (int i = 0; i < labels.Count; i++)
        {
            string label = labels[i];
            string error = BundleNameBuilder.ValidateSegment(label);
            if (error != null)
                messages.Add(BuildMessage.InvalidLabel(error, source));
        }
    }

    private static void CheckCrossPackageOverlaps(AssetCollectionSetting setting, List<BuildMessage> messages)
    {
        List<(string path, string pkgName, string src)> all = new List<(string, string, string)>();

        for (int pi = 0; pi < setting.Packages.Count; pi++)
        {
            var pkg = setting.Packages[pi];
            if (pkg?.Groups == null)
                continue;

            for (int gi = 0; gi < pkg.Groups.Count; gi++)
            {
                var grp = pkg.Groups[gi];
                if (grp?.Collectors == null)
                    continue;

                for (int ci = 0; ci < grp.Collectors.Count; ci++)
                {
                    var col = grp.Collectors[ci];
                    if (col == null || string.IsNullOrEmpty(col.CollectPath))
                        continue;

                    string src = string.Concat("Package[", pi, "].Group[", gi, "].Collector[", ci, "]");
                    all.Add((CollectorPathUtility.NormalizePath(col.CollectPath), pkg.PackageName, src));
                }
            }
        }

        for (int i = 0; i < all.Count; i++)
        {
            for (int j = i + 1; j < all.Count; j++)
            {
                var (pathI, pkgI, srcI) = all[i];
                var (pathJ, pkgJ, srcJ) = all[j];
                if (string.Equals(pkgI, pkgJ, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (string.Equals(pathI, pathJ, StringComparison.OrdinalIgnoreCase))
                    messages.Add(BuildMessage.CrossPackageOverlap(pathI, pkgI, pkgJ, srcI));

                if (CollectorPathUtility.IsPathContained(pathI, pathJ))
                    messages.Add(BuildMessage.CrossPackageContainment(pathI, pkgI, pathJ, pkgJ, srcI));

                if (CollectorPathUtility.IsPathContained(pathJ, pathI))
                    messages.Add(BuildMessage.CrossPackageContainment(pathJ, pkgJ, pathI, pkgI, srcJ));
            }
        }
    }

    private static void CheckSameDepthConflicts(List<(string path, string src)> paths, List<BuildMessage> messages)
    {
        for (int i = 0; i < paths.Count; i++)
        {
            for (int j = i + 1; j < paths.Count; j++)
            {
                if (string.Equals(paths[i].path, paths[j].path, StringComparison.OrdinalIgnoreCase))
                    messages.Add(BuildMessage.SamePathConflict(paths[i].path, paths[i].src));
            }
        }
    }

    private static void ValidateRule(string className, string ruleType, string source, List<BuildMessage> messages)
    {
        if (string.IsNullOrEmpty(className))
        {
            messages.Add(BuildMessage.EmptyRuleName(ruleType, source));
            return;
        }

        try
        {
            object rule = ruleType switch
            {
                "FilterRule" => RuleResolver.GetFilterRule(className),
                "GroupRule" => RuleResolver.GetGroupRule(className),
                _ => null
            };

            if (rule == null)
                messages.Add(BuildMessage.RuleNotFound(className, source));
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[AssetCollectionSettingValidator] 规则解析失败: {ruleType}={className}, Source={source}, Error={ex.Message}");
            messages.Add(BuildMessage.RuleNotFound(className, source));
        }
    }

    private static bool CollectPathExists(Collector collector)
    {
        if (collector == null || string.IsNullOrEmpty(collector.CollectPath))
            return false;

        if (collector.CollectPathType == ECollectPathType.File)
        {
            return !UnityEditor.AssetDatabase.IsValidFolder(collector.CollectPath) &&
                   !string.IsNullOrEmpty(UnityEditor.AssetDatabase.AssetPathToGUID(collector.CollectPath));
        }

        return UnityEditor.AssetDatabase.IsValidFolder(collector.CollectPath);
    }

    #endregion
}
