using System;
using System.Collections.Generic;

/// <summary>
/// CollectorSetting 保存时校验器 —— 9 条规则，返回 BuildMessage 列表。
/// SO 修改后自动调用（ApplyModifiedProperties 返回 true 时）。
/// </summary>
public static class CollectorSettingValidator
{
    #region Public API

    /// <summary>校验 CollectorSetting，返回所有发现的问题。空列表表示无问题。</summary>
    public static List<BuildMessage> Validate(CollectorSetting setting)
    {
        var messages = new List<BuildMessage>();

        if (setting == null || setting.Packages == null || setting.Packages.Count == 0)
        {
            messages.Add(BuildMessage.NoPackages("Setting"));
            return messages;
        }

        // 1. Duplicate PackageName
        HashSet<string> seenPkg = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int pi = 0; pi < setting.Packages.Count; pi++)
        {
            var pkg = setting.Packages[pi];
            if (pkg == null) continue;

            string pkgSrc = string.Concat("Package[", pi, "]");

            // 1a. Empty PackageName
            if (string.IsNullOrEmpty(pkg.PackageName))
            {
                messages.Add(BuildMessage.EmptyPackageName(pkgSrc));
            }
            else if (!seenPkg.Add(pkg.PackageName))
            {
                // 8. Duplicate PackageName
                messages.Add(BuildMessage.DuplicatePackageName(pkg.PackageName, pkgSrc));
            }

            ValidatePackage(pkg, pi, messages);
        }

        // 5. 跨 Package 路径重叠检测
        CheckCrossPackageOverlaps(setting, messages);

        return messages;
    }

    #endregion

    #region Private — Per-Package Validation

    private static void ValidatePackage(CollectorPackage pkg, int pkgIdx, List<BuildMessage> messages)
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
            if (grp == null) continue;

            string grpSrc = string.Concat("Package[", pkgIdx, "].Group[", gi, "]");

            // 2. Empty GroupName
            if (string.IsNullOrEmpty(grp.GroupName))
            {
                messages.Add(BuildMessage.EmptyGroupName(grpSrc));
            }
            else if (!seenGrp.Add(grp.GroupName))
            {
                // 9. Duplicate GroupName
                messages.Add(BuildMessage.DuplicateGroupName(grp.GroupName, pkg.PackageName, grpSrc));
            }

            if (grp.Collectors == null)
                continue;

            for (int ci = 0; ci < grp.Collectors.Count; ci++)
            {
                var col = grp.Collectors[ci];
                if (col == null) continue;

                string colSrc = string.Concat("Package[", pkgIdx, "].Group[", gi, "].Collector[", ci, "]");

                // 3. Empty CollectPath
                if (string.IsNullOrEmpty(col.CollectPath))
                {
                    messages.Add(BuildMessage.EmptyCollectPath(colSrc));
                }
                else
                {
                    string normalized = NormalizePath(col.CollectPath);
                    allCollectPaths.Add((normalized, colSrc));

                    // 4. Path not found
                    if (!CollectPathExists(col))
                    {
                        messages.Add(BuildMessage.PathNotFound(col.CollectPath, colSrc));
                    }
                }

                // 7. Rule class name cannot be resolved
                ValidateRule(col.AddressRuleName, "AddressRule", colSrc, messages);
                ValidateRule(col.PackRuleName, "PackRule", colSrc, messages);
                ValidateRule(col.FilterRuleName, "FilterRule", colSrc, messages);
                ValidateRule(col.GroupRuleName, "GroupRule", colSrc, messages);
            }
        }

        // 6. Same-depth same-path within Package
        CheckSameDepthConflicts(allCollectPaths, pkg.PackageName ?? "(unnamed)", messages);
    }

    #endregion

    #region Private — Overlap Checks

    private static void CheckCrossPackageOverlaps(CollectorSetting setting, List<BuildMessage> messages)
    {
        List<(string path, string pkgName, string src)> all = new List<(string, string, string)>();

        for (int pi = 0; pi < setting.Packages.Count; pi++)
        {
            var pkg = setting.Packages[pi];
            if (pkg?.Groups == null) continue;

            for (int gi = 0; gi < pkg.Groups.Count; gi++)
            {
                var grp = pkg.Groups[gi];
                if (grp?.Collectors == null) continue;

                for (int ci = 0; ci < grp.Collectors.Count; ci++)
                {
                    var col = grp.Collectors[ci];
                    if (col == null || string.IsNullOrEmpty(col.CollectPath)) continue;

                    string src = string.Concat("Package[", pi, "].Group[", gi, "].Collector[", ci, "]");
                    all.Add((NormalizePath(col.CollectPath), pkg.PackageName, src));
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
                {
                    messages.Add(BuildMessage.CrossPackageOverlap(pathI, pkgI, pkgJ, srcI));
                }

                if (IsPathContained(pathI, pathJ))
                {
                    messages.Add(BuildMessage.CrossPackageContainment(pathI, pkgI, pathJ, pkgJ, srcI));
                }
                if (IsPathContained(pathJ, pathI))
                {
                    messages.Add(BuildMessage.CrossPackageContainment(pathJ, pkgJ, pathI, pkgI, srcJ));
                }
            }
        }
    }

    private static void CheckSameDepthConflicts(
        List<(string path, string src)> paths, string pkgName, List<BuildMessage> messages)
    {
        for (int i = 0; i < paths.Count; i++)
        {
            for (int j = i + 1; j < paths.Count; j++)
            {
                if (string.Equals(paths[i].path, paths[j].path, StringComparison.OrdinalIgnoreCase))
                {
                    messages.Add(BuildMessage.SamePathConflict(paths[i].path, paths[i].src));
                }
            }
        }
    }

    #endregion

    #region Private — Rule Validation

    private static void ValidateRule(string className, string ruleType, string source, List<BuildMessage> messages)
    {
        if (string.IsNullOrEmpty(className))
        {
            messages.Add(BuildMessage.Error(BuildErrorCodes.RuleNotFound,
                string.Concat("Empty ", ruleType, " class name: ", source), source));
            return;
        }

        try
        {
            if (!Resolver.CanResolve(className, ruleType))
            {
                messages.Add(BuildMessage.RuleNotFound(className, source));
            }
        }
        catch (Exception)
        {
            messages.Add(BuildMessage.RuleNotFound(className, source));
        }
    }

    /// <summary>轻量规则解析检查，避免创建完整实例</summary>
    private static class Resolver
    {
        public static bool CanResolve(string className, string ruleType)
        {
            object rule = ruleType switch
            {
                "AddressRule" => RuleResolver.GetAddressRule(className),
                "PackRule" => RuleResolver.GetPackRule(className),
                "FilterRule" => RuleResolver.GetFilterRule(className),
                "GroupRule" => RuleResolver.GetGroupRule(className),
                _ => null
            };
            return rule != null;
        }
    }

    #endregion

    #region Private — Path Utilities

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrEmpty(path)) return string.Empty;
        return path.Replace('\\', '/').TrimEnd('/');
    }

    private static bool IsPathContained(string parent, string child)
    {
        if (string.Equals(parent, child, StringComparison.OrdinalIgnoreCase)) return true;
        if (child.Length > parent.Length &&
            child[parent.Length] == '/' &&
            child.StartsWith(parent, StringComparison.OrdinalIgnoreCase))
            return true;
        return false;
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
