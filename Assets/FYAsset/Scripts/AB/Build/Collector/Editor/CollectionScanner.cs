using System;
using System.Collections.Generic;
using UnityEditor;

/// <summary>
/// 采集扫描引擎 —— 将 AssetCollectionSetting 转化为扁平的资源列表。
/// 纯 Editor 静态工具类，无实例状态。
/// </summary>
/// <remarks>
/// 层级只有 Setting -> Group -> Collector：显式 Collector 收集到的资源都是公共资源，
/// 资产归属其 Collector 所在 Group；依赖分析发现的资源由构建阶段内部化，不经过本扫描器。
/// </remarks>
public static class CollectionScanner
{
    /// <summary>扫描 AssetCollectionSetting 中配置的所有 Group/Collector，返回采集结果。</summary>
    public static ScanResult Scan(AssetCollectionSetting setting)
    {
        ScanResult result = new ScanResult();

        if (setting == null)
        {
            result.Messages.Add(BuildMessage.SettingNull(string.Empty));
            return result;
        }

        if (setting.Groups == null || setting.Groups.Count == 0)
        {
            result.Messages.Add(BuildMessage.NoGroups(string.Empty));
            return result;
        }

        List<CollectorContext> contexts = FlattenCollectors(setting);
        if (contexts.Count == 0)
            return result;

        // 归属规则：更深路径的 Collector 优先，因此按路径深度降序排序
        contexts.Sort((a, b) =>
            CollectorPathUtility.PathDepth(b.Collector.CollectPath)
                .CompareTo(CollectorPathUtility.PathDepth(a.Collector.CollectPath)));

        if (!CheckSameDepthConflicts(contexts, result))
            return result;

        // 每个浅路径 Collector 需排除被其包含的更深路径，避免重复归属
        List<string> currentPaths = new List<string>();
        for (int i = 0; i < contexts.Count; i++)
            currentPaths.Add(CollectorPathUtility.NormalizePath(contexts[i].Collector.CollectPath));

        for (int i = 0; i < contexts.Count; i++)
        {
            List<string> excluded = new List<string>();
            for (int j = 0; j < i; j++)
            {
                // 排序后 j 深于 i；浅路径 i 包含更深路径 j 时，j 从 i 的扫描范围中排除
                if (CollectorPathUtility.IsPathContained(currentPaths[i], currentPaths[j]))
                    excluded.Add(currentPaths[j]);
            }

            contexts[i].ExcludedPaths = excluded;
        }

        List<CollectedAssetInfo> collectedAssets = new List<CollectedAssetInfo>();
        List<string> effectiveIgnorePatterns = setting.GetEffectiveIgnorePatterns();

        for (int ci = 0; ci < contexts.Count; ci++)
        {
            CollectorContext ctx = contexts[ci];
            ctx.Setting = setting;
            ctx.IgnorePatterns = effectiveIgnorePatterns;
            if (!ScanCollector(ctx, result, collectedAssets))
                break;
        }

        result.Assets.AddRange(collectedAssets);
        return result;
    }

    private static bool ScanCollector(
        CollectorContext ctx,
        ScanResult result,
        List<CollectedAssetInfo> collectedAssets)
    {
        Collector collector = ctx.Collector;
        string collectPath = collector.CollectPath;
        string source = ctx.SourceLabel;

        if (string.IsNullOrEmpty(collectPath))
        {
            result.Messages.Add(BuildMessage.EmptyCollectPath(source));
            return false;
        }

        if (!CollectPathExists(collector))
        {
            // 仅 Warning —— 其他 Collector 可能仍有效
            result.Messages.Add(BuildMessage.PathNotFound(collectPath, source));
            return true;
        }

        List<string> assetPaths = CollectAssetPaths(collector, collectPath);
        if (assetPaths.Count == 0)
        {
            // 非错误 —— 仅表示空结果
            result.Messages.Add(BuildMessage.EmptyCollector(collectPath, source));
            return true;
        }

        for (int gi = 0; gi < assetPaths.Count; gi++)
        {
            if (!TryCollectAsset(assetPaths[gi], ctx, result, collectedAssets))
                return false;
        }

        return true;
    }

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

    private static bool CheckSameDepthConflicts(List<CollectorContext> contexts, ScanResult result)
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
                    result.Messages.Add(BuildMessage.SamePathConflict(pathI, string.Empty));
                    return false;
                }
            }
        }

        return true;
    }

    private static List<CollectorContext> FlattenCollectors(AssetCollectionSetting setting)
    {
        List<CollectorContext> result = new List<CollectorContext>();

        for (int gi = 0; gi < setting.Groups.Count; gi++)
        {
            AssetCollectionGroup group = setting.Groups[gi];
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
                    ParentGroup = group,
                    SourceLabel = string.Concat("Group[", gi.ToString(), "]/Collector[", ci.ToString(), "]")
                });
            }
        }

        return result;
    }

    private static bool TryCollectAsset(
        string assetPath,
        CollectorContext ctx,
        ScanResult result,
        List<CollectedAssetInfo> collectedAssets)
    {
        Collector collector = ctx.Collector;
        string collectPath = collector.CollectPath;
        string source = ctx.SourceLabel;

        if (string.IsNullOrEmpty(assetPath))
            return true;

        string guid = AssetDatabase.AssetPathToGUID(assetPath);
        if (string.IsNullOrEmpty(guid))
            return true;

        if (IsExcludedByOwnership(assetPath, ctx.ExcludedPaths))
            return true;

        string extension = System.IO.Path.GetExtension(assetPath);

        // 默认排除（脚本 / 程序集定义 / 元文件 / Editor 目录）必须最先生效：
        // 这些内容不是运行时资源，且 .cs 会与同名 ScriptableObject 争抢自动 Address。
        if (AssetClassifier.IsExcludedByDefault(assetPath))
            return true;

        // 目录型资产（如 xlua.bundle、*.framework）Unity 会识别为 DefaultAsset 文件夹：
        // 既不能作为 SerializedObject 构建，也不能当作单个物理文件拷贝，因此不参与采集。
        if (System.IO.Directory.Exists(assetPath))
            return true;

        // 场景必须由 File Collector 显式声明：目录采集会连同场景内部对象一起纳入，语义不明确。
        if (collector.CollectPathType == ECollectPathType.Folder &&
            string.Equals(extension, ".unity", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (AssetClassifier.IsUnsupportedAssetBundleEntry(assetPath, out string unsupportedReason))
        {
            result.Messages.Add(BuildMessage.UnsupportedBundleEntryAsset(assetPath, unsupportedReason, assetPath));
            return true;
        }

        // 分类顺序由 AssetClassifier 统一保证：白名单 → Scene → 可序列化 → RawFile。
        // Unity 无法识别的文件按白名单兜底为 RawFile，不再作为 Bundle 入口被跳过。
        AssetContentType contentType = AssetClassifier.ClassifyContentType(assetPath, ctx.Setting.RawFileRules);

        string primaryType = GetPrimaryTypeName(assetPath);
        string generatedAddress = AssetAddressGenerator.GenerateAddress(assetPath, primaryType, AssetAddressStyle.LongAssetPath);
        AssetAddressEntry addressEntry = ctx.Setting.FindAssetAddressEntry(guid);

        string address = addressEntry != null && !string.IsNullOrEmpty(addressEntry.Address)
            ? addressEntry.Address
            : generatedAddress;

        List<string> labels = CopyLabels(addressEntry?.Labels);

        string targetGroupName = ctx.ParentGroupName;
        BundlePackingMode packingMode = ResolvePackingMode(ctx.ParentGroup, contentType);
        string bundleKey = BundleNameBuilder.GetBundleKey(packingMode, address, guid, labels);

        string segErr = BundleNameBuilder.ValidateSegment(targetGroupName)
                     ?? BundleNameBuilder.ValidateBundleKey(bundleKey);
        if (segErr != null)
        {
            result.Messages.Add(BuildMessage.InvalidBundleNameSegment(segErr, assetPath));
            return false;
        }

        if (HasInvalidLabels(labels, assetPath, result))
            return false;

        string contentName = BundleNameBuilder.Build(
            targetGroupName,
            packingMode,
            address,
            guid,
            labels,
            contentType,
            primaryType);

        collectedAssets.Add(new CollectedAssetInfo
        {
            AssetPath = assetPath,
            AssetGUID = guid,
            Address = address,
            PrimaryType = primaryType,
            Labels = labels,
            GroupName = targetGroupName,
            SourceGroupName = ctx.ParentGroupName,
            SourceCollectorPath = collectPath,
            ContentName = contentName,
            BundlePackingMode = packingMode,
            ContentType = contentType,
            // 显式 Collector 收集的资源都是公共资源；隐式条目只由依赖分析产生。
            DependencyOrigin = AssetDependencyOrigin.Explicit,
            IsPublic = true
        });

        return true;
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
            if (!string.IsNullOrEmpty(assetPath) && !AssetDatabase.IsValidFolder(assetPath))
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

    private static BundlePackingMode ResolvePackingMode(AssetCollectionGroup targetGroup, AssetContentType contentType)
    {
        if (contentType == AssetContentType.Scene || contentType == AssetContentType.RawFile)
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

    private class CollectorContext
    {
        public AssetCollectionSetting Setting;
        public Collector Collector;
        public string ParentGroupName;
        public AssetCollectionGroup ParentGroup;
        public string SourceLabel;
        public List<string> IgnorePatterns;
        public List<string> ExcludedPaths = new();
    }
}
