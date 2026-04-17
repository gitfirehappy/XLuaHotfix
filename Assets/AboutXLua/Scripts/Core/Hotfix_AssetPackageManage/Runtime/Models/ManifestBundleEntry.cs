using System;
using System.Collections.Generic;

/// <summary>
/// Bundle 内容类型枚举 — 由构建管线根据包内资源类型自动推断。
/// 规则：如果 >80% 的资源属于同一类型，标为该类型；否则标为 Mixed。
/// 当前为预留字段，Phase 6 E5/E6 构建管线实现时赋值。
/// TODO: 纯枚举是否方便？
/// </summary>
public enum EBundleType
{
    Unknown = 0,
    Script = 1,
    Scene = 2,
    Prefab = 3,
    Texture = 4,
    Shader = 5,
    Audio = 6,
    Config = 7,
    Mixed = 8,
}

/// <summary>
/// 清单 Bundle 条目 — 描述一个 AssetBundle 文件的完整元数据。
/// 包含完整性校验信息（Hash/CRC/Size）、依赖关系（int 索引）、分类标签。
/// </summary>
[Serializable]
public class ManifestBundleEntry
{
    #region 标识与校验

    /// <summary>Bundle 文件名（唯一标识，如 "characters_assets_all_abc123.bundle"）</summary>
    public string BundleName;

    /// <summary>文件内容哈希（MD5），用于完整性校验和增量更新比较</summary>
    public string FileHash;

    /// <summary>文件 CRC32 校验码，用于快速校验</summary>
    public uint FileCRC;

    /// <summary>文件大小（字节），用于下载进度估算</summary>
    public long FileSize;

    /// <summary>是否加密</summary>
    public bool Encrypted;

    #endregion

    #region 分类与依赖

    /// <summary>
    /// Bundle 内容类型（预留，由 Phase 6 构建管线自动推断赋值，默认 Unknown）。
    /// 用途：差异化加载策略、压缩算法选择、下载优先级排序、可视化分组。
    /// TODO：是否需要纯枚举？int 还是 EBundleType？
    /// </summary>
    public int BundleType;

    /// <summary>
    /// 分类标签（预留，用于按标签选择性下载，当前默认空）。
    /// 语义：Bundle 级下载策略标签（如 "必装", "DLC-1"），不是 Asset Labels 的聚合。
    /// 赋值规则在 B9（增量下载适配）/ E6（构建导出）时定义。
    /// </summary>
    public List<string> Tags = new();

    /// <summary>
    /// 依赖 Bundle 索引数组 — 指向 ABManifest.BundleEntries 的下标。
    /// 数据来源：Unity BuildPipeline 输出的直接依赖关系。
    /// 递归展开由运行时 ABBundleLoader 负责。
    /// </summary>
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
    /// 依赖图可视化（Phase 8 G2）。
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
