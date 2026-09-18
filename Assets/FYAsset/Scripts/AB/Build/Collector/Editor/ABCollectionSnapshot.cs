using System;
using System.Collections.Generic;

/// <summary>Collect 阶段冻结的 AB 构建输入；后续 Task 不得回读配置资产或全局设置。</summary>
public sealed class ABCollectionSnapshot
{
    private readonly List<CollectedAssetInfo> _collectedAssets;
    private readonly SharePolicyConfig _sharePolicy;
    private readonly RawFileRules _rawFileRules;
    private readonly List<string> _ignorePatterns;
    private readonly List<string> _dependencyFilterExtensions;

    public ABCollectionSnapshot(
        IReadOnlyList<CollectedAssetInfo> collectedAssets,
        SharePolicyConfig sharePolicy,
        RawFileRules rawFileRules,
        IReadOnlyList<string> ignorePatterns,
        IReadOnlyList<string> dependencyFilterExtensions)
    {
        _collectedAssets = CloneAssets(collectedAssets);
        _sharePolicy = CloneSharePolicy(sharePolicy);
        _rawFileRules = CloneRawFileRules(rawFileRules);
        _ignorePatterns = CloneStrings(ignorePatterns);
        _dependencyFilterExtensions = CloneStrings(dependencyFilterExtensions);
    }

    public IReadOnlyList<CollectedAssetInfo> CollectedAssets => _collectedAssets;
    public SharePolicyConfig SharePolicy => CloneSharePolicy(_sharePolicy);
    public RawFileRules RawFileRules => CloneRawFileRules(_rawFileRules);
    public IReadOnlyList<string> IgnorePatterns => _ignorePatterns.AsReadOnly();
    public IReadOnlyList<string> DependencyFilterExtensions => _dependencyFilterExtensions.AsReadOnly();

    private static List<CollectedAssetInfo> CloneAssets(IReadOnlyList<CollectedAssetInfo> source)
    {
        var result = new List<CollectedAssetInfo>();
        if (source == null)
            return result;

        for (int i = 0; i < source.Count; i++)
        {
            CollectedAssetInfo item = source[i];
            if (item == null)
                continue;
            result.Add(new CollectedAssetInfo
            {
                AssetPath = item.AssetPath,
                AssetGUID = item.AssetGUID,
                Address = item.Address,
                AssetType = item.AssetType,
                Labels = CloneStrings(item.Labels),
                GroupName = item.GroupName,
                SourceGroupName = item.SourceGroupName,
                SourceCollectorPath = item.SourceCollectorPath,
                ContentName = item.ContentName,
                BundlePackingMode = item.BundlePackingMode,
                ContentType = item.ContentType,
                DependencyOrigin = item.DependencyOrigin,
                IsPublic = item.IsPublic,
                HasError = item.HasError,
                HasWarning = item.HasWarning
            });
        }
        return result;
    }

    private static SharePolicyConfig CloneSharePolicy(SharePolicyConfig source)
    {
        return new SharePolicyConfig
        {
            ForceSharePatterns = CloneStrings(source?.ForceSharePatterns),
            NoSharePatterns = CloneStrings(source?.NoSharePatterns)
        };
    }

    private static RawFileRules CloneRawFileRules(RawFileRules source)
    {
        return new RawFileRules { Patterns = CloneStrings(source?.Patterns) };
    }

    private static List<string> CloneStrings(IReadOnlyList<string> source)
    {
        var result = new List<string>();
        if (source != null)
        {
            for (int i = 0; i < source.Count; i++)
                result.Add(source[i]);
        }
        return result;
    }
}
