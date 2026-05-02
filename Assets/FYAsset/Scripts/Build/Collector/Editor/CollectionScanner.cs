using System;
using System.Collections.Generic;
using UnityEditor;

/// <summary>
/// 采集扫描引擎 —— 将 CollectorSetting SO 转化为扁平的资源列表。
/// 纯 Editor 静态工具类，无实例状态。
/// </summary>
public static class CollectionScanner
{
    #region Public Methods

    /// <summary>
    /// 扫描 CollectorSetting 中配置的所有 Package/Group/Collector，返回采集结果。
    /// </summary>
    public static ScanResult Scan(CollectorSetting setting)
    {
        ScanResult result = new ScanResult();

        if (setting == null)
        {
            result.Messages.Add(Error(BuildErrorCodes.SettingNull, "CollectorSetting is null.", string.Empty));
            return result;
        }

        if (setting.Packages == null || setting.Packages.Count == 0)
        {
            result.Messages.Add(Warning(BuildErrorCodes.NoPackages, "CollectorSetting has no Packages configured.", string.Empty));
            return result;
        }

        // Step 0: Cross-Package overlap detection
        if (!CheckCrossPackageOverlaps(setting, result))
            return result;

        // Per-Package scan
        for (int pkgIdx = 0; pkgIdx < setting.Packages.Count; pkgIdx++)
        {
            CollectorPackage package = setting.Packages[pkgIdx];
            if (package == null || string.IsNullOrEmpty(package.PackageName))
                continue;

            if (package.Groups == null || package.Groups.Count == 0)
            {
                result.Messages.Add(Warning(BuildErrorCodes.EmptyPackage,
                    string.Concat("Package '", package.PackageName, "' has no Groups."), string.Empty));
                continue;
            }

            if (!ScanPackage(package, result))
                continue;
        }

        return result;
    }

    #endregion

    #region Private — Step 0: Cross-Package Overlap

    private static bool CheckCrossPackageOverlaps(CollectorSetting setting, ScanResult result)
    {
        List<(string path, string pkgName)> allCollectors = new List<(string, string)>();

        for (int pi = 0; pi < setting.Packages.Count; pi++)
        {
            var pkg = setting.Packages[pi];
            if (pkg == null || pkg.Groups == null)
                continue;

            for (int gi = 0; gi < pkg.Groups.Count; gi++)
            {
                var grp = pkg.Groups[gi];
                if (grp == null || grp.Collectors == null)
                    continue;

                for (int ci = 0; ci < grp.Collectors.Count; ci++)
                {
                    var col = grp.Collectors[ci];
                    if (col == null || string.IsNullOrEmpty(col.CollectPath))
                        continue;

                    string normalized = NormalizePath(col.CollectPath);
                    allCollectors.Add((normalized, pkg.PackageName));
                }
            }
        }

        for (int i = 0; i < allCollectors.Count; i++)
        {
            for (int j = i + 1; j < allCollectors.Count; j++)
            {
                var (pathI, pkgI) = allCollectors[i];
                var (pathJ, pkgJ) = allCollectors[j];

                if (string.Equals(pkgI, pkgJ, StringComparison.Ordinal))
                    continue;

                // Same path across different Packages
                if (string.Equals(pathI, pathJ, StringComparison.OrdinalIgnoreCase))
                {
                    result.Messages.Add(Error(BuildErrorCodes.CrossPackageOverlap,
                        string.Concat("Cross-Package path conflict: ", pathI, " in Package '", pkgI, "' and '", pkgJ, "'."),
                        pathI));
                    return false;
                }

                // Path containment across Packages
                if (IsPathContained(pathI, pathJ))
                {
                    result.Messages.Add(Error(BuildErrorCodes.CrossPackageOverlap,
                        string.Concat("Cross-Package path overlap: ", pathI, " (", pkgI, ") contains ", pathJ, " (", pkgJ, ")."),
                        pathI));
                    return false;
                }

                if (IsPathContained(pathJ, pathI))
                {
                    result.Messages.Add(Error(BuildErrorCodes.CrossPackageOverlap,
                        string.Concat("Cross-Package path overlap: ", pathJ, " (", pkgJ, ") contains ", pathI, " (", pkgI, ")."),
                        pathJ));
                    return false;
                }
            }
        }

        return true;
    }

    #endregion

    #region Private — Per-Package Scan

    private static bool ScanPackage(CollectorPackage package, ScanResult result)
    {
        string packageName = package.PackageName;

        // Flatten all Collectors with their parent Group context
        List<CollectorContext> contexts = FlattenCollectors(package);
        if (contexts.Count == 0)
            return true;

        // Step 1: Build Ownership Map — deepest path first
        contexts.Sort((a, b) => PathDepth(b.Collector.CollectPath).CompareTo(PathDepth(a.Collector.CollectPath)));

        // Check same-depth same-path conflicts
        if (!CheckSameDepthConflicts(contexts, packageName, result))
            return false;

        // Build excluded paths per Collector (deeper Collectors claim ownership)
        List<string> currentPaths = new List<string>();
        for (int i = 0; i < contexts.Count; i++)
            currentPaths.Add(NormalizePath(contexts[i].Collector.CollectPath));

        for (int i = 0; i < contexts.Count; i++)
        {
            List<string> excluded = new List<string>();
            for (int j = 0; j < i; j++)
            {
                // i 是更浅的路径（后排序），j 是更深的路径（先排序）
                // 检查更浅路径 i 是否包含更深路径 j —— 若是，j 应从 i 的扫描范围中排除
                if (IsPathContained(currentPaths[i], currentPaths[j]))
                    excluded.Add(currentPaths[j]);
            }

            contexts[i].ExcludedPaths = excluded;
        }

        // Build Group name → Group lookup for Tags merge
        Dictionary<string, CollectorGroup> groupLookup = new Dictionary<string, CollectorGroup>(
            StringComparer.OrdinalIgnoreCase);
        for (int gi = 0; gi < package.Groups.Count; gi++)
        {
            var grp = package.Groups[gi];
            if (grp != null && !string.IsNullOrEmpty(grp.GroupName))
                groupLookup[grp.GroupName] = grp;
        }

        // Step 2: Per-Collector scan
        List<CollectedAssetInfo> packageAssets = new List<CollectedAssetInfo>();

        for (int ci = 0; ci < contexts.Count; ci++)
        {
            var ctx = contexts[ci];
            if (!ScanCollector(ctx, packageName, groupLookup, result, packageAssets))
                return false;
        }

        // Step 3: GUID uniqueness validation
        if (!CheckGuidUniqueness(packageAssets, result))
            return false;

        result.Assets.AddRange(packageAssets);
        return true;
    }

    private static bool ScanCollector(
        CollectorContext ctx,
        string packageName,
        Dictionary<string, CollectorGroup> groupLookup,
        ScanResult result,
        List<CollectedAssetInfo> packageAssets)
    {
        Collector collector = ctx.Collector;
        string collectPath = collector.CollectPath;

        // Validate collect path
        if (string.IsNullOrEmpty(collectPath))
        {
            result.Messages.Add(Error(BuildErrorCodes.EmptyCollectPath,
                string.Concat("Collector in Package '", packageName, "' Group '", ctx.ParentGroupName, "' has empty CollectPath."),
                string.Empty));
            return false;
        }

        if (!AssetDatabase.IsValidFolder(collectPath))
        {
            result.Messages.Add(Warning(BuildErrorCodes.PathNotFound,
                string.Concat("CollectPath not found: ", collectPath, " (Package '", packageName, "', Group '", ctx.ParentGroupName, "')."),
                collectPath));
            return true; // Warning only — other Collectors in this Package may still be valid
        }

        // Resolve rules
        IFilterRule filterRule = ResolveRuleSafe<IFilterRule>(collector.FilterRuleName, "FilterRule", collectPath, result);
        IGroupRule groupRule = ResolveRuleSafe<IGroupRule>(collector.GroupRuleName, "GroupRule", collectPath, result);
        IAddressRule addressRule = ResolveRuleSafe<IAddressRule>(collector.AddressRuleName, "AddressRule", collectPath, result);
        IPackRule packRule = ResolveRuleSafe<IPackRule>(collector.PackRuleName, "PackRule", collectPath, result);

        if (filterRule == null || groupRule == null || addressRule == null || packRule == null)
            return false; // Error already added by ResolveRuleSafe

        // Find assets in directory
        string[] guids = AssetDatabase.FindAssets(string.Empty, new[] { collectPath });
        if (guids == null || guids.Length == 0)
        {
            result.Messages.Add(Warning(BuildErrorCodes.EmptyCollector,
                string.Concat("No assets found for Collector: ", collectPath), collectPath));
            return true; // Not an error — just empty
        }

        for (int gi = 0; gi < guids.Length; gi++)
        {
            string guid = guids[gi];
            string assetPath = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(assetPath))
                continue;

            // Exclude sub-paths claimed by deeper Collectors
            if (IsExcludedByOwnership(assetPath, ctx.ExcludedPaths))
                continue;

            // FilterRule
            string extension = System.IO.Path.GetExtension(assetPath);
            var filterCtx = new FilterRuleContext
            {
                AssetPath = assetPath,
                Extension = extension,
                CollectPath = collectPath
            };

            if (!filterRule.IsCollectable(filterCtx))
                continue;

            // IgnorePatterns
            if (MatchesIgnorePattern(assetPath, collectPath, collector.IgnorePatterns))
                continue;

            // Classify
            AssetClassification classification = AssetClassifier.Classify(
                assetPath, collector.CollectorType, collector.ForcePayloadKind);

            // PrimaryType
            string primaryType = GetPrimaryTypeName(assetPath);

            // GroupRule — determine target group
            var groupRuleCtx = new GroupRuleContext
            {
                AssetPath = assetPath,
                Classification = classification,
                CollectPath = collectPath,
                PackageName = packageName,
                ParentGroupName = ctx.ParentGroupName
            };

            string targetGroupName = groupRule.GetTargetGroup(groupRuleCtx);
            if (string.IsNullOrEmpty(targetGroupName))
                targetGroupName = ctx.ParentGroupName;

            // AddressRule
            var addressCtx = new AddressRuleContext
            {
                AssetPath = assetPath,
                GroupName = targetGroupName,
                CollectPath = collectPath,
                PrimaryType = primaryType
            };

            string address = addressRule.GetAddress(addressCtx);

            // Labels merge: targetGroup.Labels ∪ Collector.Labels
            List<string> labels = MergeLabels(groupLookup, targetGroupName, collector.Labels);

            // PackRule
            var packCtx = new PackRuleContext
            {
                AssetPath = assetPath,
                GroupName = targetGroupName,
                CollectPath = collectPath,
                PackageName = packageName,
                Classification = classification,
                Labels = labels
            };

            string packKey = packRule.GetPackKey(packCtx);
            string bundleName = BundleNameBuilder.Build(packageName, targetGroupName, packKey);

            // Assemble CollectedAssetInfo
            var collected = new CollectedAssetInfo
            {
                AssetPath = assetPath,
                AssetGUID = guid,
                Address = address,
                PrimaryType = primaryType,
                Labels = labels,
                GroupName = targetGroupName,
                PackageName = packageName,
                BundleName = bundleName,
                Classification = classification,
                CollectorType = collector.CollectorType
            };

            packageAssets.Add(collected);
        }

        return true;
    }

    #endregion

    #region Private — Ownership & Dedup

    private static bool IsExcludedByOwnership(string assetPath, List<string> excludedPaths)
    {
        string normalized = NormalizePath(assetPath);
        for (int i = 0; i < excludedPaths.Count; i++)
        {
            if (IsPathOwnedBy(normalized, excludedPaths[i]))
                return true;
        }

        return false;
    }

    private static bool IsPathOwnedBy(string assetPath, string ownerPath)
    {
        if (string.Equals(assetPath, ownerPath, StringComparison.OrdinalIgnoreCase))
            return true;

        if (assetPath.Length > ownerPath.Length &&
            assetPath[ownerPath.Length] == '/' &&
            assetPath.StartsWith(ownerPath, StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    private static bool CheckSameDepthConflicts(
        List<CollectorContext> contexts, string packageName, ScanResult result)
    {
        for (int i = 0; i < contexts.Count; i++)
        {
            string pathI = NormalizePath(contexts[i].Collector.CollectPath);
            int depthI = PathDepth(pathI);

            for (int j = i + 1; j < contexts.Count; j++)
            {
                string pathJ = NormalizePath(contexts[j].Collector.CollectPath);
                int depthJ = PathDepth(pathJ);

                if (depthI == depthJ && string.Equals(pathI, pathJ, StringComparison.OrdinalIgnoreCase))
                {
                    result.Messages.Add(Error(BuildErrorCodes.SamePathConflict,
                        string.Concat("Same-depth same-path conflict in Package '", packageName, "': ", pathI),
                        pathI));
                    return false;
                }
            }
        }

        return true;
    }

    #endregion

    #region Private — IgnorePatterns

    private static bool MatchesIgnorePattern(string assetPath, string collectPath, List<string> patterns)
    {
        if (patterns == null || patterns.Count == 0)
            return false;

        // Relative path = assetPath minus collectPath prefix
        string normalizedAsset = NormalizePath(assetPath);
        string normalizedCollect = NormalizePath(collectPath);
        string relativePath;

        if (normalizedAsset.Length > normalizedCollect.Length + 1 &&
            normalizedAsset.StartsWith(normalizedCollect, StringComparison.OrdinalIgnoreCase) &&
            normalizedAsset[normalizedCollect.Length] == '/')
        {
            relativePath = normalizedAsset.Substring(normalizedCollect.Length + 1);
        }
        else if (string.Equals(normalizedAsset, normalizedCollect, StringComparison.OrdinalIgnoreCase))
        {
            // Asset is the collect path itself (unlikely, but handle it)
            return false;
        }
        else
        {
            // Asset is outside collectPath — shouldn't happen, but skip
            return false;
        }

        for (int i = 0; i < patterns.Count; i++)
        {
            string pattern = patterns[i];
            if (string.IsNullOrEmpty(pattern))
                continue;

            if (pattern.EndsWith("/"))
            {
                // Directory match: any path segment equals the directory name
                string dirName = pattern.Substring(0, pattern.Length - 1);
                if (ContainsPathSegment(relativePath, dirName))
                    return true;
            }
            else
            {
                // Glob match for *.ext and *keyword* patterns
                if (GlobMatcher.IsMatch(relativePath, pattern))
                    return true;
            }
        }

        return false;
    }

    private static bool ContainsPathSegment(string path, string segment)
    {
        int start = 0;
        int len = path.Length;
        int segLen = segment.Length;

        while (start <= len)
        {
            int slash = path.IndexOf('/', start);
            int end = slash < 0 ? len : slash;
            int currentLen = end - start;

            if (currentLen == segLen &&
                string.Compare(path, start, segment, 0, segLen, StringComparison.OrdinalIgnoreCase) == 0)
                return true;

            start = end + 1;
            if (slash < 0)
                break;
        }

        return false;
    }

    #endregion

    #region Private — Helpers

    private static List<CollectorContext> FlattenCollectors(CollectorPackage package)
    {
        List<CollectorContext> result = new List<CollectorContext>();

        for (int gi = 0; gi < package.Groups.Count; gi++)
        {
            CollectorGroup group = package.Groups[gi];
            if (group == null || group.Collectors == null || !group.Enabled)
                continue;

            for (int ci = 0; ci < group.Collectors.Count; ci++)
            {
                Collector collector = group.Collectors[ci];
                if (collector == null)
                    continue;

                result.Add(new CollectorContext
                {
                    Collector = collector,
                    ParentGroupName = group.GroupName ?? string.Empty
                });
            }
        }

        return result;
    }

    private static T ResolveRuleSafe<T>(string className, string ruleType, string collectPath, ScanResult result)
        where T : class
    {
        if (string.IsNullOrEmpty(className))
        {
            result.Messages.Add(Error(BuildErrorCodes.RuleNotFound,
                string.Concat("Empty ", ruleType, " class name for Collector: ", collectPath), collectPath));
            return null;
        }

        try
        {
            if (typeof(T) == typeof(IAddressRule))
                return RuleResolver.GetAddressRule(className) as T;
            if (typeof(T) == typeof(IPackRule))
                return RuleResolver.GetPackRule(className) as T;
            if (typeof(T) == typeof(IFilterRule))
                return RuleResolver.GetFilterRule(className) as T;
            if (typeof(T) == typeof(IGroupRule))
                return RuleResolver.GetGroupRule(className) as T;

            return null;
        }
        catch (Exception ex)
        {
            result.Messages.Add(Error(BuildErrorCodes.RuleNotFound,
                string.Concat("Failed to resolve ", ruleType, " '", className, "' at ", collectPath, ": ", ex.Message),
                collectPath));
            return null;
        }
    }

    private static List<string> MergeLabels(
        Dictionary<string, CollectorGroup> groupLookup,
        string targetGroupName,
        List<string> collectorLabels)
    {
        HashSet<string> dedup = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Add target Group's Labels
        if (groupLookup.TryGetValue(targetGroupName, out CollectorGroup targetGroup) &&
            targetGroup.Labels != null)
        {
            for (int i = 0; i < targetGroup.Labels.Count; i++)
            {
                if (!string.IsNullOrEmpty(targetGroup.Labels[i]))
                    dedup.Add(targetGroup.Labels[i]);
            }
        }

        // Add Collector's Labels (union)
        if (collectorLabels != null)
        {
            for (int i = 0; i < collectorLabels.Count; i++)
            {
                if (!string.IsNullOrEmpty(collectorLabels[i]))
                    dedup.Add(collectorLabels[i]);
            }
        }

        return new List<string>(dedup);
    }

    private static string GetPrimaryTypeName(string assetPath)
    {
        Type type = AssetDatabase.GetMainAssetTypeAtPath(assetPath);
        return type != null ? type.Name : "Unknown";
    }

    private static bool CheckGuidUniqueness(List<CollectedAssetInfo> assets, ScanResult result)
    {
        HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < assets.Count; i++)
        {
            string guid = assets[i].AssetGUID;
            if (string.IsNullOrEmpty(guid))
                continue;

            if (!seen.Add(guid))
            {
                result.Messages.Add(Error(BuildErrorCodes.DuplicateGuid,
                    string.Concat("Duplicate GUID in result: ", guid, " (", assets[i].AssetPath, ")."),
                    assets[i].AssetPath));
                return false;
            }
        }

        return true;
    }

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrEmpty(path))
            return string.Empty;

        return path.Replace('\\', '/').TrimEnd('/');
    }

    private static int PathDepth(string path)
    {
        if (string.IsNullOrEmpty(path))
            return 0;

        int count = 0;
        string normalized = NormalizePath(path);
        for (int i = 0; i < normalized.Length; i++)
        {
            if (normalized[i] == '/')
                count++;
        }

        return count;
    }

    private static bool IsPathContained(string parent, string child)
    {
        if (string.Equals(parent, child, StringComparison.OrdinalIgnoreCase))
            return true;

        if (child.Length > parent.Length &&
            child[parent.Length] == '/' &&
            child.StartsWith(parent, StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    #endregion

    #region Private — Helpers: Messages

    /// <summary>
    /// 私有 Error 包装器 —— 避免修改 20+ 处调用点的方法签名。
    /// 等价于直接调用 BuildMessage.Error(code, message, source)。
    /// </summary>
    private static BuildMessage Error(string code, string message, string source)
    {
        return BuildMessage.Error(code, message, source);
    }

    /// <summary>
    /// 私有 Warning 包装器 —— 同上。
    /// </summary>
    private static BuildMessage Warning(string code, string message, string source)
    {
        return BuildMessage.Warning(code, message, source);
    }

    #endregion

    #region Private — Nested Types

    private class CollectorContext
    {
        public Collector Collector;
        public string ParentGroupName;
        public List<string> ExcludedPaths = new();
    }

    #endregion
}
