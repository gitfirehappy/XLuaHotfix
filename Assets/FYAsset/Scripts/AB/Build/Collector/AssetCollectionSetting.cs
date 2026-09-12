using System;
using System.Collections.Generic;
#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;

/// <summary>
/// 项目级自动 Address 生成样式。
/// </summary>
public enum AssetAddressStyle
{
    ShortName = 0,
    LongAssetPathWithoutExtension = 1,
    NameType = 2
}

/// <summary>
/// 资产采集配置资产。层级结构：Setting -> Group -> Collector；
/// 资产级只保留 Address 与 Labels 的人工覆盖，按 GUID 独立存储。
/// </summary>
/// <remarks>
/// 显式 Collector 收集到的资源都是公共资源；依赖分析自动发现的资源由构建阶段内部化，
/// 因此配置层不再需要 Package、Role 或载荷类型的用户声明。
/// </remarks>
public class AssetCollectionSetting : ScriptableObject
{
    /// <summary>自动 Address 的项目级默认生成样式。</summary>
    public AssetAddressStyle AddressStyle = AssetAddressStyle.ShortName;

    /// <summary>全部采集 Group；Group 只控制显式资源的打包方式。</summary>
    public List<AssetCollectionGroup> Groups = new();

    /// <summary>资产级 Address / Labels 人工覆盖，按 Unity GUID 作为权威键。</summary>
    public List<AssetOverride> AssetOverrides = new();

    /// <summary>全局忽略规则，用于 Project Scan 和构建扫描阶段过滤项目资产。</summary>
    public List<string> IgnorePatterns = CreateDefaultIgnorePatterns();

    /// <summary>被 Folder Collector 覆盖但显式排除的资产列表，按 GUID 判断，路径只作为可读缓存。</summary>
    public List<AssetExclusion> ExcludedAssets = new();

    /// <summary>项目级 RawFile 白名单：命中的文件即使 Unity 可识别也按原始文件构建与加载。</summary>
    public RawFileRules RawFileRules = new();

    /// <summary>依赖共享策略：强制共享与禁止共享的路径匹配规则。</summary>
    public SharePolicyConfig SharePolicy = new();

    /// <summary>按 GUID 查找人工覆盖；未配置覆盖时返回 null。</summary>
    public AssetOverride FindAssetOverride(string assetGuid)
    {
        if (string.IsNullOrEmpty(assetGuid) || AssetOverrides == null)
            return null;

        for (int i = 0; i < AssetOverrides.Count; i++)
        {
            AssetOverride entry = AssetOverrides[i];
            if (entry != null && string.Equals(entry.AssetGUID, assetGuid, StringComparison.Ordinal))
                return entry;
        }

        return null;
    }

    /// <summary>为编辑器交互获取或创建覆盖条目；扫描阶段不得调用，避免把自动结果写回配置。</summary>
    public AssetOverride GetOrCreateAssetOverride(string assetGuid)
    {
        AssetOverride existing = FindAssetOverride(assetGuid);
        if (existing != null)
            return existing;

        AssetOverrides ??= new List<AssetOverride>();
        var created = new AssetOverride { AssetGUID = assetGuid };
        AssetOverrides.Add(created);
        return created;
    }

    public AssetExclusion FindExcludedAsset(string assetGuid)
    {
        if (string.IsNullOrEmpty(assetGuid) || ExcludedAssets == null)
            return null;

        for (int i = 0; i < ExcludedAssets.Count; i++)
        {
            AssetExclusion exclusion = ExcludedAssets[i];
            if (exclusion != null && string.Equals(exclusion.AssetGUID, assetGuid, StringComparison.Ordinal))
                return exclusion;
        }

        return null;
    }

    public bool IsExcludedAssetGuid(string assetGuid)
    {
        return FindExcludedAsset(assetGuid) != null;
    }

    public bool AddExcludedAsset(string assetGuid, string assetPath)
    {
        if (string.IsNullOrEmpty(assetGuid))
            return false;

        ExcludedAssets ??= new List<AssetExclusion>();
        AssetExclusion existing = FindExcludedAsset(assetGuid);
        if (existing != null)
        {
            string normalizedPath = NormalizeAssetPath(assetPath);
            if (string.Equals(existing.AssetPath, normalizedPath, StringComparison.Ordinal))
                return false;

            existing.AssetPath = normalizedPath;
            return true;
        }

        ExcludedAssets.Add(new AssetExclusion
        {
            AssetGUID = assetGuid,
            AssetPath = NormalizeAssetPath(assetPath)
        });
        return true;
    }

    public bool RemoveExcludedAsset(string assetGuid)
    {
        if (string.IsNullOrEmpty(assetGuid) || ExcludedAssets == null)
            return false;

        for (int i = ExcludedAssets.Count - 1; i >= 0; i--)
        {
            AssetExclusion exclusion = ExcludedAssets[i];
            if (exclusion == null || !string.Equals(exclusion.AssetGUID, assetGuid, StringComparison.Ordinal))
                continue;

            ExcludedAssets.RemoveAt(i);
            return true;
        }

        return false;
    }

#if UNITY_EDITOR
    /// <summary>刷新排除条目的路径缓存；GUID 已失效的条目直接移除。</summary>
    public bool RefreshExcludedAssetPaths()
    {
        if (ExcludedAssets == null)
            return false;

        bool changed = false;
        for (int i = ExcludedAssets.Count - 1; i >= 0; i--)
        {
            AssetExclusion exclusion = ExcludedAssets[i];
            if (exclusion == null || string.IsNullOrEmpty(exclusion.AssetGUID))
            {
                ExcludedAssets.RemoveAt(i);
                changed = true;
                continue;
            }

            string assetPath = NormalizeAssetPath(AssetDatabase.GUIDToAssetPath(exclusion.AssetGUID));
            if (string.Equals(exclusion.AssetPath, assetPath, StringComparison.Ordinal))
                continue;

            exclusion.AssetPath = assetPath;
            changed = true;
        }

        return changed;
    }
#endif

    public static List<string> CreateDefaultIgnorePatterns()
    {
        var patterns = new List<string>
        {
            "Assets/FYAsset/**",
            "Assets/Build/**",
            "Assets/StreamingAssets/**"
        };

        AddDefaultBundleEntryIgnorePatterns(patterns);
        return patterns;
    }

    public List<string> GetEffectiveIgnorePatterns()
    {
        var patterns = IgnorePatterns != null
            ? new List<string>(IgnorePatterns)
            : CreateDefaultIgnorePatterns();

        AddDefaultBundleEntryIgnorePatterns(patterns);
        return patterns;
    }

    private static void AddDefaultBundleEntryIgnorePatterns(List<string> patterns)
    {
        AddPatternIfMissing(patterns, "Assets/AddressableAssetsData/**");
        AddPatternIfMissing(patterns, "*.cginc");
        AddPatternIfMissing(patterns, "*.hlsl");
        AddPatternIfMissing(patterns, "*.hlslinc");
        AddPatternIfMissing(patterns, "*.pdf");
    }

    private static void AddPatternIfMissing(List<string> patterns, string pattern)
    {
        if (patterns == null || string.IsNullOrEmpty(pattern))
            return;

        for (int i = 0; i < patterns.Count; i++)
        {
            if (string.Equals(patterns[i], pattern, StringComparison.OrdinalIgnoreCase))
                return;
        }

        patterns.Add(pattern);
    }

    private static string NormalizeAssetPath(string assetPath)
    {
        return string.IsNullOrEmpty(assetPath) ? string.Empty : assetPath.Replace('\\', '/').TrimEnd('/');
    }
}

/// <summary>
/// 资产级排除条目。GUID 是权威键，AssetPath 是面向编辑器显示和迁移审计的缓存。
/// </summary>
[Serializable]
public class AssetExclusion
{
    public string AssetGUID;
    public string AssetPath;
}

/// <summary>
/// Group 级别配置，对应一组共享打包策略的采集器。
/// </summary>
[Serializable]
public class AssetCollectionGroup
{
    /// <summary>组名，用于构建内容逻辑名的分组段</summary>
    public string GroupName;

    /// <summary>是否启用该 Group。为 false 时 CollectionScanner 跳过整个 Group</summary>
    public bool Enabled = true;

    /// <summary>Addressables 风格的 Group 打包模式</summary>
    public BundlePackingMode BundlePackingMode = BundlePackingMode.PackTogetherByLabel;

    /// <summary>该组下的所有采集器配置</summary>
    public List<Collector> Collectors = new();
}

/// <summary>
/// 最底层的采集规则绑定单元：指定一个目录或文件路径。
/// 显式采集到的资源都是公共资源，路径所在 Group 决定打包方式。
/// </summary>
[Serializable]
public class Collector
{
    /// <summary>采集根路径（相对于 Assets/，可指向目录或单个文件）</summary>
    public string CollectPath;

    /// <summary>采集路径类型；默认 Folder 表示目录采集器</summary>
    public ECollectPathType CollectPathType = ECollectPathType.Folder;
}

/// <summary>
/// 资产级人工覆盖。Address 为空表示沿用自动生成地址；Labels 与自动结果无关，全部来自此处。
/// </summary>
[Serializable]
public class AssetOverride
{
    public string AssetGUID;

    /// <summary>公开 Address 覆盖；为空时由 AssetAddressGenerator 生成。</summary>
    public string Address;

    public List<string> Labels = new();
}
