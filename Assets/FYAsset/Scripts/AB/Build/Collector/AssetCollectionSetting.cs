using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 资产 Address 的生成样式。未显式编辑的资产默认使用完整 Assets 路径并保留扩展名。
/// </summary>
public enum AssetAddressStyle
{
    ShortName = 0,
    LongAssetPath = 1
}

/// <summary>
/// 资产采集配置资产。层级结构：Setting -> Group -> Collector；
/// 资产级只保存用户明确编辑的 Address 与 Labels，按 GUID 关联。
/// </summary>
public class AssetCollectionSetting : ScriptableObject
{
    /// <summary>全部采集 Group；Group 只控制显式资源的打包方式。</summary>
    public List<AssetCollectionGroup> Groups = new();

    /// <summary>按 GUID 保存用户明确编辑的资产 Address 与 Labels。</summary>
    public List<AssetAddressEntry> AssetAddressEntries = new();

    /// <summary>全局忽略规则，用于 Project Scan 和构建扫描阶段过滤项目资产。</summary>
    public List<string> IgnorePatterns = CreateDefaultIgnorePatterns();

    /// <summary>项目级 RawFile 规则，使用与 Ignore 相同的 gitignore 基础语义。</summary>
    public RawFileRules RawFileRules = new();

    /// <summary>依赖共享策略：强制共享与禁止共享的路径匹配规则。</summary>
    public SharePolicyConfig SharePolicy = new();

    /// <summary>按 GUID 查找用户保存的资源信息；未配置时返回 null。</summary>
    public AssetAddressEntry FindAssetAddressEntry(string assetGuid)
    {
        if (string.IsNullOrEmpty(assetGuid) || AssetAddressEntries == null)
            return null;

        for (int i = 0; i < AssetAddressEntries.Count; i++)
        {
            AssetAddressEntry entry = AssetAddressEntries[i];
            if (entry != null && string.Equals(entry.AssetGUID, assetGuid, StringComparison.Ordinal))
                return entry;
        }

        return null;
    }

    /// <summary>为编辑器交互获取或创建资源信息；扫描阶段不得调用。</summary>
    public AssetAddressEntry GetOrCreateAssetAddressEntry(string assetGuid)
    {
        AssetAddressEntry existing = FindAssetAddressEntry(assetGuid);
        if (existing != null)
            return existing;

        AssetAddressEntries ??= new List<AssetAddressEntry>();
        var created = new AssetAddressEntry { AssetGUID = assetGuid };
        AssetAddressEntries.Add(created);
        return created;
    }

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
}

/// <summary>
/// 资源级 Address/Labels 信息。Address 为空表示使用默认完整长路径。
/// </summary>
[Serializable]
public class AssetAddressEntry
{
    public string AssetGUID;
    public string Address;
    public List<string> Labels = new();
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
