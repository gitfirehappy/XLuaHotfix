using System.Collections.Generic;

/// <summary>
/// 采集扫描管线产出的中间数据记录（仅 Editor 使用，不序列化）。
/// 由 CollectionScanner 生成，经依赖分析、打包后最终转换为 ManifestAssetEntry + ManifestContentEntry。
/// </summary>
public class CollectedAssetInfo
{
    /// <summary>资产在项目中的相对路径（如 Assets/Textures/icon.png）</summary>
    public string AssetPath;

    /// <summary>资产的 Unity GUID，对应 RuntimeAssetEntry.EntryId</summary>
    public string AssetGUID;

    /// <summary>运行时寻址地址：人工覆盖优先，否则由 AssetAddressGenerator 生成</summary>
    public string Address;

    /// <summary>资产主类型名称（如 Texture2D / GameObject），来自 AssetDatabase</summary>
    public string PrimaryType;

    /// <summary>最终标签列表；只来自资产级人工覆盖</summary>
    public List<string> Labels = new();

    /// <summary>所属 Group 名称；隐式提取的共享内容使用 SystemIdentifiers.SharedGroupName</summary>
    public string GroupName;

    /// <summary>命中资产时所属的原始 Group 名称，供编辑器预览树回溯 Collector 来源</summary>
    public string SourceGroupName;

    /// <summary>命中资产时所属的原始 Collector 路径，供编辑器预览树回溯 Collector 来源</summary>
    public string SourceCollectorPath;

    /// <summary>
    /// 逻辑内容名称，由 BundleNameBuilder 组装；同一名称的资产物理上打入同一个内容。
    /// 单引用隐式依赖不生成独立条目，随引用方内容一起构建。
    /// </summary>
    public string ContentName;

    /// <summary>Group 配置的打包模式；Scene / RawFile 在扫描时强制为 PackSeparately</summary>
    public BundlePackingMode BundlePackingMode;

    /// <summary>分类结果：资产的内容类型，决定 SerializedObject / Scene / RawFile 构建路线</summary>
    public AssetContentType ContentType;

    /// <summary>构建来源：显式采集还是依赖分析自动发现；只用于构建诊断</summary>
    public AssetDependencyOrigin DependencyOrigin;

    /// <summary>是否为公共资源：显式采集的资源进入公共 Address 索引，隐式依赖一律内部化</summary>
    public bool IsPublic;

    public bool HasError;

    public bool HasWarning;
}
