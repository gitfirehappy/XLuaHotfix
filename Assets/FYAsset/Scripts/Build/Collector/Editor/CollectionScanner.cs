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
            result.Messages.Add(BuildMessage.SettingNull(string.Empty));
            return result;
        }

        if (setting.Packages == null || setting.Packages.Count == 0)
        {
            result.Messages.Add(BuildMessage.NoPackages(string.Empty));
            return result;
        }

        // 跨 Package 路径重叠检测
        if (!CheckCrossPackageOverlaps(setting, result))
            return result;

        // 逐 Package 扫描
        for (int pkgIdx = 0; pkgIdx < setting.Packages.Count; pkgIdx++)
        {
            CollectorPackage package = setting.Packages[pkgIdx];
            if (package == null || string.IsNullOrEmpty(package.PackageName))
                continue;

            if (package.Groups == null || package.Groups.Count == 0)
            {
                result.Messages.Add(BuildMessage.EmptyPackage(package.PackageName, string.Empty));
                continue;
            }

            if (!ScanPackage(package, result))
                continue;
        }

        return result;
    }

    #endregion

    #region Private — 跨 Package 路径重叠检测

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

                    string normalized = CollectorPathUtility.NormalizePath(col.CollectPath);
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

                // 跨 Package 相同路径
                if (string.Equals(pathI, pathJ, StringComparison.OrdinalIgnoreCase))
                {
                    result.Messages.Add(BuildMessage.CrossPackageOverlap(pathI, pkgI, pkgJ, pathI));
                    return false;
                }

                // 跨 Package 路径包含
                if (CollectorPathUtility.IsPathContained(pathI, pathJ))
                {
                    result.Messages.Add(BuildMessage.CrossPackageContainment(pathI, pkgI, pathJ, pkgJ, pathI));
                    return false;
                }

                if (CollectorPathUtility.IsPathContained(pathJ, pathI))
                {
                    result.Messages.Add(BuildMessage.CrossPackageContainment(pathJ, pkgJ, pathI, pkgI, pathJ));
                    return false;
                }
            }
        }

        return true;
    }

    #endregion

    #region Private — 逐 Package 扫描

    private static bool ScanPackage(CollectorPackage package, ScanResult result)
    {
        string packageName = package.PackageName;

        // 扁平化所有 Collectors，构建归属映射
        List<CollectorContext> contexts = FlattenCollectors(package);
        if (contexts.Count == 0)
            return true;

        // 第一步：构建归属映射 —— 最深路径优先
        contexts.Sort((a, b) => CollectorPathUtility.PathDepth(b.Collector.CollectPath).CompareTo(CollectorPathUtility.PathDepth(a.Collector.CollectPath)));

        // 检查同深度同路径冲突
        if (!CheckSameDepthConflicts(contexts, packageName, result))
            return false;

        // 第二步：构建排除路径
        // 每个 Collector 都需要排除更浅的路径（更浅的路径被更深的路径包含）
        List<string> currentPaths = new List<string>();
        for (int i = 0; i < contexts.Count; i++)
            currentPaths.Add(CollectorPathUtility.NormalizePath(contexts[i].Collector.CollectPath));

        for (int i = 0; i < contexts.Count; i++)
        {
            List<string> excluded = new List<string>();
            for (int j = 0; j < i; j++)
            {
                // i 是更浅的路径（后排序），j 是更深的路径（先排序）
                // 检查更浅路径 i 是否包含更深路径 j —— 若是，j 应从 i 的扫描范围中排除
                if (CollectorPathUtility.IsPathContained(currentPaths[i], currentPaths[j]))
                    excluded.Add(currentPaths[j]);
            }

            contexts[i].ExcludedPaths = excluded;
        }

        // 第三步：构建 Group 名称 → Group 映射，用于Tags合并
        Dictionary<string, CollectorGroup> groupLookup = new Dictionary<string, CollectorGroup>(
            StringComparer.OrdinalIgnoreCase);
        for (int gi = 0; gi < package.Groups.Count; gi++)
        {
            var grp = package.Groups[gi];
            if (grp != null && !string.IsNullOrEmpty(grp.GroupName))
                groupLookup[grp.GroupName] = grp;
        }

        // 逐 Collector 扫描
        List<CollectedAssetInfo> packageAssets = new List<CollectedAssetInfo>();

        for (int ci = 0; ci < contexts.Count; ci++)
        {
            var ctx = contexts[ci];
            if (!ScanCollector(ctx, packageName, groupLookup, result, packageAssets))
                return false;
        }

        // GUID 唯一性校验
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

        // 校验采集路径
        if (string.IsNullOrEmpty(collectPath))
        {
            result.Messages.Add(BuildMessage.EmptyCollectPath(string.Empty));
            return false;
        }

        if (!CollectPathExists(collector))
        {
            result.Messages.Add(BuildMessage.PathNotFound(collectPath, collectPath));
            return true; // 仅 Warning —— Package 内其他 Collector 可能仍有效
        }

        // 解析规则
        IFilterRule filterRule = ResolveRuleSafe<IFilterRule>(collector.FilterRuleName, "FilterRule", collectPath, result);
        IGroupRule groupRule = ResolveRuleSafe<IGroupRule>(collector.GroupRuleName, "GroupRule", collectPath, result);
        IAddressRule addressRule = ResolveRuleSafe<IAddressRule>(collector.AddressRuleName, "AddressRule", collectPath, result);
        IPackRule packRule = ResolveRuleSafe<IPackRule>(collector.PackRuleName, "PackRule", collectPath, result);

        if (filterRule == null || groupRule == null || addressRule == null || packRule == null)
            return false; // 错误已由 ResolveRuleSafe 添加

        List<string> assetPaths = CollectAssetPaths(collector, collectPath);
        if (assetPaths.Count == 0)
        {
            result.Messages.Add(BuildMessage.EmptyCollector(collectPath, collectPath));
            return true; // 非错误 —— 仅表示空结果
        }

        for (int gi = 0; gi < assetPaths.Count; gi++)
        {
            TryCollectAsset(
                assetPaths[gi],
                ctx,
                packageName,
                groupLookup,
                result,
                packageAssets,
                filterRule,
                groupRule,
                addressRule,
                packRule);
        }

        return true;
    }

    #endregion

    #region Private — Ownership & Dedup

    private static bool IsExcludedByOwnership(string assetPath, List<string> excludedPaths)
    {
        string normalized = CollectorPathUtility.NormalizePath(assetPath);
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
            string pathI = CollectorPathUtility.NormalizePath(contexts[i].Collector.CollectPath);
            int depthI = CollectorPathUtility.PathDepth(pathI);

            for (int j = i + 1; j < contexts.Count; j++)
            {
                string pathJ = CollectorPathUtility.NormalizePath(contexts[j].Collector.CollectPath);
                int depthJ = CollectorPathUtility.PathDepth(pathJ);

                if (depthI == depthJ && string.Equals(pathI, pathJ, StringComparison.OrdinalIgnoreCase))
                {
                    result.Messages.Add(BuildMessage.SamePathConflict(pathI, pathI));
                    return false;
                }
            }
        }

        return true;
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
            result.Messages.Add(BuildMessage.Error(BuildErrorCodes.RuleNotFound,
                string.Concat("Empty ", ruleType, " class name for Collector: ", collectPath), collectPath));
            return null;
        }

        try
        {
            return RuleResolver.GetRule<T>(className);
        }
        catch (Exception)
        {
            result.Messages.Add(BuildMessage.RuleNotFound(className, collectPath));
            return null;
        }
    }

    private static void TryCollectAsset(
        string assetPath,
        CollectorContext ctx,
        string packageName,
        Dictionary<string, CollectorGroup> groupLookup,
        ScanResult result,
        List<CollectedAssetInfo> packageAssets,
        IFilterRule filterRule,
        IGroupRule groupRule,
        IAddressRule addressRule,
        IPackRule packRule)
    {
        Collector collector = ctx.Collector;
        string collectPath = collector.CollectPath;

        if (string.IsNullOrEmpty(assetPath))
            return;

        string guid = AssetDatabase.AssetPathToGUID(assetPath);
        if (string.IsNullOrEmpty(guid))
            return;

        if (IsExcludedByOwnership(assetPath, ctx.ExcludedPaths))
            return;

        string extension = System.IO.Path.GetExtension(assetPath);
        var filterCtx = new FilterRuleContext
        {
            AssetPath = assetPath,
            Extension = extension,
            CollectPath = collectPath
        };

        if (!filterRule.IsCollectable(filterCtx))
            return;

        if (CollectorPathUtility.MatchesIgnorePattern(assetPath, collectPath, collector.IgnorePatterns))
            return;

        AssetClassification classification = AssetClassifier.Classify(
            assetPath, collector.CollectorType, collector.ForcePayloadKind);

        string primaryType = GetPrimaryTypeName(assetPath);

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

        var addressCtx = new AddressRuleContext
        {
            AssetPath = assetPath,
            GroupName = targetGroupName,
            CollectPath = collectPath,
            PrimaryType = primaryType
        };

        string address = addressRule.GetAddress(addressCtx);
        List<string> labels = MergeLabels(groupLookup, targetGroupName, collector.Labels);

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
        string segErr = BundleNameBuilder.ValidateSegment(packageName)
                     ?? BundleNameBuilder.ValidateSegment(targetGroupName)
                     ?? BundleNameBuilder.ValidatePackKey(packKey);
        if (segErr != null)
        {
            result.Messages.Add(BuildMessage.Error(
                BuildErrorCodes.RuleNotFound, segErr, assetPath));
            return;
        }

        if (HasInvalidLabels(labels, assetPath, result))
            return;

        string bundleName = BundleNameBuilder.Build(packageName, targetGroupName, packKey);

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

    private static List<string> CollectAssetPaths(Collector collector, string collectPath)
    {
        List<string> assetPaths = new List<string>();

        if (collector.CollectPathType == ECollectPathType.File)
        {
            if (IsValidFileCollectPath(collectPath))
                assetPaths.Add(collectPath);

            return assetPaths;
        }

        string[] guids = AssetDatabase.FindAssets(string.Empty, new[] { collectPath });
        if (guids == null || guids.Length == 0)
            return assetPaths;

        for (int i = 0; i < guids.Length; i++)
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(guids[i]);
            if (!string.IsNullOrEmpty(assetPath))
                assetPaths.Add(assetPath);
        }

        return assetPaths;
    }

    private static bool CollectPathExists(Collector collector)
    {
        if (collector == null || string.IsNullOrEmpty(collector.CollectPath))
            return false;

        return collector.CollectPathType == ECollectPathType.File
            ? IsValidFileCollectPath(collector.CollectPath)
            : AssetDatabase.IsValidFolder(collector.CollectPath);
    }

    private static bool IsValidFileCollectPath(string collectPath)
    {
        if (string.IsNullOrEmpty(collectPath) || AssetDatabase.IsValidFolder(collectPath))
            return false;

        return !string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(collectPath));
    }

    private static bool HasInvalidLabels(List<string> labels, string assetPath, ScanResult result)
    {
        if (labels == null)
            return false;

        for (int i = 0; i < labels.Count; i++)
        {
            string le = BundleNameBuilder.ValidateSegment(labels[i]);
            if (le != null)
            {
                result.Messages.Add(BuildMessage.Error(
                    BuildErrorCodes.RuleNotFound,
                    string.Concat("Label ", le), assetPath));
                return true;
            }
        }

        return false;
    }

    private static List<string> MergeLabels(
        Dictionary<string, CollectorGroup> groupLookup,
        string targetGroupName,
        List<string> collectorLabels)
    {
        HashSet<string> dedup = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 加入目标 Group 的 Labels
        if (groupLookup.TryGetValue(targetGroupName, out CollectorGroup targetGroup) &&
            targetGroup.Labels != null)
        {
            for (int i = 0; i < targetGroup.Labels.Count; i++)
            {
                if (!string.IsNullOrEmpty(targetGroup.Labels[i]))
                    dedup.Add(targetGroup.Labels[i]);
            }
        }

        // 加入 Collector 的 Labels（并集）
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
                result.Messages.Add(BuildMessage.DuplicateGuid(guid, assets[i].AssetPath));
                return false;
            }
        }

        return true;
    }

    #endregion

    #region Private — 嵌套类型 — Collector 上下文

    private class CollectorContext
    {
        public Collector Collector;
        public string ParentGroupName;
        public List<string> ExcludedPaths = new();
    }

    #endregion
}
