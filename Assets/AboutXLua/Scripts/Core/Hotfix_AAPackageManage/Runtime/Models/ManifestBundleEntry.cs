using System;
using System.Collections.Generic;

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
    /// 分类标签（预留，用于按标签选择性下载，当前默认空）。
    /// 与 Asset Labels 不同：Bundle Tags 用于包级别的下载筛选策略。
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
    /// </summary>
    [NonSerialized]
    public List<ManifestAssetEntry> IncludeAssets = new();

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
