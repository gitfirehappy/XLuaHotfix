using System;
using System.Collections.Generic;

/// <summary>
/// AB Manifest 中的公共运行时资源映射。
/// Address 是业务查询身份，AssetPath 只用于从所属 Content 中提取对象。
/// 隐式依赖不生成此条目；所有物理 Content 事实由 ManifestContentEntry 表达。
/// </summary>
[Serializable]
[BinarySerializable]
public sealed class ManifestAssetEntry
{
    /// <summary>公共查询键；同一 Manifest 内大小写不敏感唯一。</summary>
    [BinaryField(0)]
    public string Address;

    /// <summary>精确资产类型查询键，格式为 assembly-simple-name:type-full-name。</summary>
    [BinaryField(1)]
    public string AssetType;

    /// <summary>非空、trim、大小写不敏感唯一的业务分类集合。</summary>
    [BinaryField(2)]
    public List<string> Labels = new();

    /// <summary>Bundle 内对象路径，或 Editor 直读的工程路径；不是业务身份。</summary>
    [BinaryField(3)]
    public string AssetPath;

    /// <summary>同一 Manifest 内定位物理 Content 的局部下标。</summary>
    [BinaryField(4)]
    public int ContentIndex;
}
