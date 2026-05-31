using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Asset collection configuration asset.
/// Hierarchy: Setting -> Package -> Group -> Collector, with asset metadata stored separately by GUID.
/// </summary>
public class AssetCollectionSetting : ScriptableObject
{
    #region 字段

    /// <summary>所有资产包配置列表</summary>
    public List<AssetCollectionPackage> Packages = new();

    /// <summary>Project Scan 阶段的全局忽略规则，用于生成候选 Collector 前过滤项目资产。</summary>
    public List<string> IgnorePatterns = CreateDefaultIgnorePatterns();

    /// <summary>资产级元数据，按 Unity GUID 作为权威键</summary>
    public List<AssetEntry> AssetEntries = new();

    #endregion

    #region Public Methods

    public AssetEntry FindAssetEntry(string assetGuid)
    {
        if (string.IsNullOrEmpty(assetGuid) || AssetEntries == null)
            return null;

        for (int i = 0; i < AssetEntries.Count; i++)
        {
            AssetEntry entry = AssetEntries[i];
            if (entry != null && string.Equals(entry.AssetGUID, assetGuid, StringComparison.Ordinal))
                return entry;
        }

        return null;
    }

    public AssetEntry GetOrCreateAssetEntry(string assetGuid, string generatedAddress, AssetClassification generatedClassification)
    {
        AssetEntries ??= new List<AssetEntry>();

        AssetEntry existing = FindAssetEntry(assetGuid);
        if (existing != null)
            return existing;

        AssetEntry entry = new AssetEntry
        {
            AssetGUID = assetGuid,
            AutoAddress = true,
            Address = generatedAddress,
            AutoRole = true,
            Role = generatedClassification.Role,
            AutoPayload = true,
            PayloadKind = generatedClassification.PayloadKind,
            Labels = new List<string>()
        };
        AssetEntries.Add(entry);
        return entry;
    }

    public static List<string> CreateDefaultIgnorePatterns()
    {
        return new List<string>
        {
            "Assets/FYAsset/**",
            "Assets/Build/**",
            "Assets/StreamingAssets/**"
        };
    }

    #endregion
}

/// <summary>
/// Package 级别配置，对应一个独立的资产包（如主包、DLC 包等）。
/// </summary>
[Serializable]
public class AssetCollectionPackage
{
    #region 字段

    /// <summary>包名，用于构建 Bundle 逻辑名的第一段前缀</summary>
    public string PackageName;
    
    /// <summary>该包下的所有 Group 配置</summary>
    public List<AssetCollectionGroup> Groups = new();
    
    /// <summary>Per-Package 共享提取策略，由依赖分析 Task 读取</summary>
    public SharePolicyConfig SharePolicy = new();

    #endregion
}

/// <summary>
/// Group 级别配置，对应一组具有相同标签和打包策略的采集器。
/// </summary>
[Serializable]
public class AssetCollectionGroup
{
    #region 字段

    /// <summary>组名，用于构建 Bundle 逻辑名的第二段</summary>
    public string GroupName;

    /// <summary>是否启用该 Group。为 false 时 CollectionScanner 跳过整个 Group</summary>
    public bool Enabled = true;

    /// <summary>组级别标签，会强制继承到该 Group 下所有资产</summary>
    public List<string> Labels = new();

    /// <summary>Addressables 风格的 Group 打包模式</summary>
    public BundlePackingMode BundlePackingMode = BundlePackingMode.PackTogetherByLabel;

    /// <summary>该组下的所有采集器配置</summary>
    public List<Collector> Collectors = new();

    #endregion
}

/// <summary>
/// 最底层的采集规则绑定单元，指定一个目录或文件路径及其对应的规则组合。
/// </summary>
[Serializable]
public class Collector
{
    #region 字段

    /// <summary>采集根路径（相对于 Assets/，可指向目录或单个文件）</summary>
    public string CollectPath;

    /// <summary>采集路径类型；默认 Folder 以兼容旧序列化数据</summary>
    public ECollectPathType CollectPathType = ECollectPathType.Folder;

    /// <summary>采集器类型，决定资产的语义角色</summary>
    public ECollectorType CollectorType;

    /// <summary>强制指定载荷类型；Auto 表示由 Classifier 自动推断</summary>
    public EForcePayloadKind ForcePayloadKind;

    /// <summary>过滤规则类名，由 RuleResolver 反射解析为 IFilterRule 实例</summary>
    public string FilterRuleName;

    /// <summary>分组规则类名，由 RuleResolver 反射解析为 IGroupRule 实例</summary>
    public string GroupRuleName;

    /// <summary>忽略规则模式列表（类 gitignore 子集：*.ext / dirname/ / *keyword*）</summary>
    public List<string> IgnorePatterns = new();

    #endregion
}

/// <summary>
/// Asset-level authoritative metadata keyed by Unity GUID.
/// </summary>
[Serializable]
public class AssetEntry
{
    public string AssetGUID;
    public bool AutoAddress = true;
    public string Address;
    public List<string> Labels = new();
    public bool AutoRole = true;
    public EAssetRole Role = EAssetRole.Main;
    public bool AutoPayload = true;
    public EPayloadKind PayloadKind = EPayloadKind.Serialized;
}
