using System;

/// <summary>
/// 序列化的内容条目 —— 描述一次构建产出的一个可下载物理文件。
/// 内容既可能是 AssetBundle（SerializedObject / Scene），也可能是普通物理文件（RawFile）。
/// </summary>
/// <remarks>
/// 条目只记录物理文件事实与内容级依赖下标，不记录它属于哪个 Bundle，也不记录反向资源列表；
/// 多个 ManifestAssetEntry 可以通过 ContentIndex 指向同一个条目，形成资产到内容的一对多映射。
/// </remarks>
[Serializable]
[BinarySerializable]
public class ManifestContentEntry
{

    /// <summary>下载、查重与本地复用使用的实际输出文件名（不含目录）。</summary>
    [BinaryField(0)]
    public string FileName;

    /// <summary>文件内容 Hash；本地已有同名同 Hash 的文件即可跳过下载。</summary>
    [BinaryField(1)]
    public string FileHash;

    /// <summary>文件 CRC32，用于下载与复用时的完整性校验。</summary>
    [BinaryField(2)]
    public uint FileCRC;

    /// <summary>文件字节数。</summary>
    [BinaryField(3)]
    public long FileSize;

    /// <summary>内容类型：决定运行时走 Bundle 加载还是物理文件读取。</summary>
    [BinaryField(4)]
    public AssetContentType ContentType = AssetContentType.SerializedObject;

    /// <summary>
    /// 直接依赖的内容下标，指向同一 ABManifest.ContentEntries。
    /// 事实来源是 Unity 构建后的 AssetBundleManifest；运行时只消费这份下标，不再自行推导依赖。
    /// </summary>
    [BinaryField(5)]
    public int[] DependencyIndices = new int[0];
}
