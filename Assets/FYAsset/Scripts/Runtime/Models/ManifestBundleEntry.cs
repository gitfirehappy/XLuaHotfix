using System;
using System.Collections.Generic;

/// <summary>
/// 清单 Bundle 条目 — 描述一个 AssetBundle 文件的完整元数据。
/// 包含完整性校验信息（Hash/CRC/Size）、依赖关系（int 索引）、分类标签。
/// </summary>
[Serializable]
[BinarySerializable]
public class ManifestBundleEntry
{
    #region 标识与校验

    /// <summary>Bundle 文件名（唯一标识，如 "characters_assets_all_abc123.bundle"）</summary>
    [BinaryField(0)]
    public string BundleName;

    /// <summary>文件内容哈希（MD5），用于完整性校验和增量更新比较</summary>
    [BinaryField(1)]
    public string FileHash;

    /// <summary>文件 CRC32 校验码，用于快速校验</summary>
    [BinaryField(2)]
    public uint FileCRC;

    /// <summary>文件大小（字节），用于下载进度估算</summary>
    [BinaryField(3)]
    public long FileSize;

    /// <summary>是否加密</summary>
    [BinaryField(4)]
    public bool Encrypted;

    #endregion

    #region 分类与依赖

    /// <summary>
    /// Bundle 内容类型字符串 — 由构建管线通过 >80% 阈值自动推断。
    /// 主导类型占比 >80% → PrimaryType 名称（如 "Texture2D"）；否则 "Mixed"。
    /// V1 不使用枚举，新增资产类型自动支持。
    /// </summary>
    [BinaryField(5)]
    public string BundleType = "";

    /// <summary>
    /// Bundle 级下载策略标签（如 "必装"/"DLC-1"/"语音包"）。
    /// 语义与资产 Labels 完全不同：Labels 描述资产特征，Tags 描述 Bundle 的分包/下载策略。
    /// 不从 asset Labels 自动聚合——Tags 由独立的 Bundle 级配置填入。
    /// </summary>
    [BinaryField(6)]
    public List<string> Tags = new();

    /// <summary>
    /// 依赖 Bundle 索引数组 — 指向 ABManifest.BundleEntries 的下标。
    /// 数据来源：Unity BuildPipeline 输出的直接依赖关系。
    /// 递归展开由运行时 ABBundleLoader 负责。
    /// </summary>
    [BinaryField(7)]
    public int[] DependBundleIndices = new int[0];

    #endregion

    #region 运行时字段（不序列化，由 ABManifest.Initialize() 填充）

    /// <summary>
    /// 该 Bundle 包含的资源条目列表（反向查找）。
    /// 由 Initialize() 第 6 步构建。
    /// </summary>
    [NonSerialized]
    public List<ManifestAssetEntry> IncludeAssets = new();

    /// <summary>
    /// 反向依赖列表 — 依赖本 Bundle 的其他 Bundle 在 ABManifest.BundleEntries 中的索引。
    /// 由 Initialize() 第 7 步从 DependBundleIndices 反转构建。
    /// 用途：卸载安全判断（引用归零才可卸载）、影响分析（改了本包影响哪些包）、
    /// 依赖图可视化。
    /// </summary>
    [NonSerialized]
    public List<int> ReferencedByBundleIndices = new();

    #endregion

    /// <summary>
    /// 判断两个 Bundle 内容是否相同（基于 FileHash 比较）。
    /// 用于增量更新时判断 Bundle 是否需要重新下载。
    /// </summary>
    public bool ContentEquals(ManifestBundleEntry other)
    {
        if (other == null) return false;
        return string.Equals(FileHash, other.FileHash, StringComparison.Ordinal);
    }
}
