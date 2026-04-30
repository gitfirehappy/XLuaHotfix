using System.Collections.Generic;

/// <summary>
/// 采集扫描管线产出的中间数据记录（仅 Editor 使用，不序列化）
/// 由 CollectionScanner 生成，经依赖分析、打包后
/// 最终转换为 ManifestAssetEntry + ManifestBundleEntry
/// </summary>
public class CollectedAssetInfo
{
    #region 字段

    /// <summary>资产在项目中的相对路径（如 Assets/Textures/icon.png）</summary>
    public string AssetPath;

    /// <summary>资产的 Unity GUID，对应 RuntimeAssetEntry.EntryId</summary>
    public string AssetGUID;

    /// <summary>运行时寻址地址，由 IAddressRule 生成</summary>
    public string Address;

    /// <summary>资产主类型名称（如 Texture2D / GameObject），来自 AssetDatabase</summary>
    public string PrimaryType;

    /// <summary>合并后的标签列表（Group.Labels ∪ Collector.Labels，去重）</summary>
    public List<string> Labels = new();

    /// <summary>所属 Group 名称</summary>
    public string GroupName;

    /// <summary>所属 Package 名称</summary>
    public string PackageName;

    /// <summary>逻辑 Bundle 名称，由 BundleNameBuilder 组装</summary>
    public string BundleName;

    /// <summary>分类结果：资产角色 + 载荷类型</summary>
    public AssetClassification Classification;

    /// <summary>采集器类型，透传自 Collector.CollectorType</summary>
    public ECollectorType CollectorType;

    /// <summary>依赖分析决策：该资产被打入共享 Bundle（GroupName = "$shared"）</summary>
    public bool IsInSharedBundle;

    /// <summary>依赖分析决策：该隐式依赖被复制到多个引用 Bundle 中</summary>
    public bool IsDuplicated;

    #endregion
}
