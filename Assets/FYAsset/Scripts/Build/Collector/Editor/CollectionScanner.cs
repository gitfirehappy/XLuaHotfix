using System;
using System.Collections.Generic;
using UnityEditor;

/// <summary>
/// 采集扫描引擎 —— 将 AssetCollectionSetting SO 转化为扁平的资源列表。
/// 纯 Editor 静态工具类，无实例状态。
/// </summary>
public static class CollectionScanner
{
    #region Public Methods

    /// <summary>
    /// 扫描 AssetCollectionSetting 中配置的所有 Package/Group/Collector，返回采集结果。
    /// </summary>
    public static ScanResult Scan(AssetCollectionSetting setting)
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
            AssetCollectionPackage package = setting.Packages[pkgIdx];
            if (package == null)
                continue;

            if (string.IsNullOrEmpty(package.PackageName))
            {
                result.Messages.Add(BuildMessage.EmptyPackageName(string.Concat("Package[", pkgIdx, "]")));
                continue;
            }

            if (package.Groups == null || package.Groups.Count == 0)
            {
                result.Messages.Add(BuildMessage.EmptyPackage(package.PackageName, string.Empty));
                continue;
            }

            if (!ScanPackage(setting, package, result))
                continue;
        }

        return result;
    }

    #endregion

    #region Private — 跨 Package 路径重叠检测

    private static bool CheckCrossPackageOverlaps(AssetCollectionSetting setting, ScanResult result)
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

    private static bool ScanPackage(AssetCollectionSetting setting, AssetCollectionPackage package, ScanResult result)
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
        Dictionary<string, AssetCollectionGroup> groupLookup = new Dictionary<string, AssetCollectionGroup>(
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
            ctx.Setting = setting;
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
        Dictionary<string, AssetCollectionGroup> groupLookup,
        ScanResult result,
        List<CollectedAssetInfo> packageAssets)
    {
        Collector collector = ctx.Collector;
        string collectPath = collector.CollectPath;

        if (collector.CollectorType == ECollectorType.Implicit)
        {
            result.Messages.Add(BuildMessage.InvalidCollectorType(collector.CollectorType, collectPath));
            return false;
        }

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

        if (filterRule == null || groupRule == null)
            return false; // 错误已由 ResolveRuleSafe 添加

        List<string> assetPaths = CollectAssetPaths(collector, collectPath);
        if (assetPaths.Count == 0)
        {
            result.Messages.Add(BuildMessage.EmptyCollector(collectPath, collectPath));
            return true; // 非错误 —— 仅表示空结果
        }

        for (int gi = 0; gi < assetPaths.Count; gi++)
        {
            if (!TryCollectAsset(
                assetPaths[gi],
                ctx,
                packageName,
                groupLookup,
                result,
                packageAssets,
                filterRule,
                groupRule))
            {
                return false;
            }
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

    private static List<CollectorContext> FlattenCollectors(AssetCollectionPackage package)
    {
        List<CollectorContext> result = new List<CollectorContext>();

        for (int gi = 0; gi < package.Groups.Count; gi++)
        {
            AssetCollectionGroup group = package.Groups[gi];
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
                    ParentGroupName = group.GroupName ?? string.Empty,
                    ParentGroup = group
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
            result.Messages.Add(BuildMessage.EmptyRuleName(ruleType, collectPath));
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

    private static bool TryCollectAsset(
        string assetPath,
        CollectorContext ctx,
        string packageName,
        Dictionary<string, AssetCollectionGroup> groupLookup,
        ScanResult result,
        List<CollectedAssetInfo> packageAssets,
        IFilterRule filterRule,
        IGroupRule groupRule)
    {
        Collector collector = ctx.Collector;
        string collectPath = collector.CollectPath;

        if (string.IsNullOrEmpty(assetPath))
            return true;

        string guid = AssetDatabase.AssetPathToGUID(assetPath);
        if (string.IsNullOrEmpty(guid))
            return true;

        if (IsExcludedByOwnership(assetPath, ctx.ExcludedPaths))
            return true;

        string extension = System.IO.Path.GetExtension(assetPath);
        var filterCtx = new FilterRuleContext
        {
            AssetPath = assetPath,
            Extension = extension,
            CollectPath = collectPath
        };

        bool collectable;
        if (!TryExecuteRule(
                "FilterRule",
                collector.FilterRuleName,
                collectPath,
                assetPath,
                result,
                () => filterRule.IsCollectable(filterCtx),
                out collectable))
        {
            return false;
        }

        if (!collectable)
            return true;

        if (CollectorPathUtility.MatchesIgnorePattern(assetPath, collectPath, collector.IgnorePatterns))
            return true;

        if (!TryExecuteRule(
                "AssetClassifier",
                collector.CollectorType.ToString(),
                collectPath,
                assetPath,
                result,
                () => AssetClassifier.Classify(assetPath, collector.CollectorType, collector.ForcePayloadKind),
                out AssetClassification classification))
        {
            return false;
        }

        string primaryType = GetPrimaryTypeName(assetPath);

        var groupRuleCtx = new GroupRuleContext
        {
            AssetPath = assetPath,
            Classification = classification,
            CollectPath = collectPath,
            PackageName = packageName,
            ParentGroupName = ctx.ParentGroupName
        };

        if (!TryExecuteRule(
                "GroupRule",
                collector.GroupRuleName,
                collectPath,
                assetPath,
                result,
                () => groupRule.GetTargetGroup(groupRuleCtx),
                out string targetGroupName))
        {
            return false;
        }

        if (string.IsNullOrEmpty(targetGroupName))
            targetGroupName = ctx.ParentGroupName;

        AssetCollectionGroup targetGroup = ResolveGroup(groupLookup, targetGroupName, ctx.ParentGroup);
        string generatedAddress = AssetAddressGenerator.GenerateShortAddress(assetPath, primaryType, true);
        AssetEntry entry = ctx.Setting.GetOrCreateAssetEntry(guid, generatedAddress, classification);

        string address = entry.AutoAddress || string.IsNullOrEmpty(entry.Address)
            ? generatedAddress
            : entry.Address;

        AssetClassification resolvedClassification = new AssetClassification
        {
            Role = entry.AutoRole ? classification.Role : entry.Role,
            PayloadKind = entry.AutoPayload ? classification.PayloadKind : entry.PayloadKind
        };

        List<string> groupLabels = CopyLabels(targetGroup?.Labels);
        List<string> assetLabels = CopyLabels(entry.Labels);
        List<string> labels = MergeLabels(groupLabels, assetLabels);
        BundlePackingMode packingMode = ResolvePackingMode(targetGroup, resolvedClassification);
        string bundleKey = BundleNameBuilder.GetBundleKey(packingMode, address, guid, labels);

        string segErr = BundleNameBuilder.ValidateSegment(packageName)
                     ?? BundleNameBuilder.ValidateSegment(targetGroupName)
                     ?? BundleNameBuilder.ValidateBundleKey(bundleKey);
        if (segErr != null)
        {
            result.Messages.Add(BuildMessage.InvalidBundleNameSegment(segErr, assetPath));
            return false;
        }

        if (HasInvalidLabels(labels, assetPath, result))
            return false;

        string bundleName = BundleNameBuilder.Build(packageName, targetGroupName, packingMode, address, guid, labels);

        var collected = new CollectedAssetInfo
        {
            AssetPath = assetPath,
            AssetGUID = guid,
            Address = address,
            PrimaryType = primaryType,
            Labels = labels,
            GroupLabels = groupLabels,
            AssetLabels = assetLabels,
            GroupName = targetGroupName,
            PackageName = packageName,
            BundleName = bundleName,
            BundlePackingMode = packingMode,
            Classification = resolvedClassification,
            CollectorType = collector.CollectorType
        };

        packageAssets.Add(collected);
        return true;
    }

    private static bool TryExecuteRule<T>(
        string ruleType,
        string ruleClassName,
        string collectPath,
        string assetPath,
        ScanResult result,
        Func<T> action,
        out T value)
    {
        try
        {
            value = action();
            return true;
        }
        catch (Exception ex)
        {
            value = default;
            string source = string.Concat(collectPath, " -> ", assetPath);
            result.Messages.Add(BuildMessage.RuleExecutionFailed(
                ruleType,
                ruleClassName,
                assetPath,
                ex.Message,
                source));
            return false;
        }
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
                result.Messages.Add(BuildMessage.InvalidLabel(string.Concat("标签 ", le), assetPath));
                return true;
            }
        }

        return false;
    }

    private static AssetCollectionGroup ResolveGroup(
        Dictionary<string, AssetCollectionGroup> groupLookup,
        string targetGroupName,
        AssetCollectionGroup fallback)
    {
        if (!string.IsNullOrEmpty(targetGroupName) &&
            groupLookup.TryGetValue(targetGroupName, out AssetCollectionGroup targetGroup))
        {
            return targetGroup;
        }

        return fallback;
    }

    private static List<string> CopyLabels(List<string> source)
    {
        List<string> result = new List<string>();
        if (source == null)
            return result;

        for (int i = 0; i < source.Count; i++)
        {
            if (!string.IsNullOrEmpty(source[i]))
                result.Add(source[i]);
        }

        return result;
    }

    private static List<string> MergeLabels(List<string> groupLabels, List<string> assetLabels)
    {
        HashSet<string> dedup = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (groupLabels != null)
        {
            for (int i = 0; i < groupLabels.Count; i++)
            {
                if (!string.IsNullOrEmpty(groupLabels[i]))
                    dedup.Add(groupLabels[i]);
            }
        }

        if (assetLabels != null)
        {
            for (int i = 0; i < assetLabels.Count; i++)
            {
                if (!string.IsNullOrEmpty(assetLabels[i]))
                    dedup.Add(assetLabels[i]);
            }
        }

        return new List<string>(dedup);
    }

    private static BundlePackingMode ResolvePackingMode(AssetCollectionGroup targetGroup, AssetClassification classification)
    {
        if (classification.PayloadKind == EPayloadKind.Scene)
            return BundlePackingMode.PackSeparately;

        return targetGroup != null ? targetGroup.BundlePackingMode : BundlePackingMode.PackTogetherByLabel;
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
        public AssetCollectionSetting Setting;
        public Collector Collector;
        public string ParentGroupName;
        public AssetCollectionGroup ParentGroup;
        public List<string> ExcludedPaths = new();
    }

    #endregion
}
