using System;
using System.Collections.Generic;

/// <summary>
/// 序列化的 Bundle 文件元数据与依赖下标。
/// </summary>
[Serializable]
[BinarySerializable]
public class ManifestBundleEntry
{

    /// <summary>用于查找和下载的 Bundle 文件名。</summary>
    [BinaryField(0)]
    public string BundleName;

    /// <summary>内容 Hash，热更时用来复用已有文件。</summary>
    [BinaryField(1)]
    public string FileHash;

    /// <summary>校验下载和复用文件的 CRC。</summary>
    [BinaryField(2)]
    public uint FileCRC;

    /// <summary>Expected file length in bytes.</summary>
    [BinaryField(3)]
    public long FileSize;

    /// <summary>加密标记；ABBundleLoader 不解密文件。</summary>
    [BinaryField(4)]
    public bool Encrypted;

    /// <summary>
    /// Build-time content classification.
    /// </summary>
    [BinaryField(5)]
    public string BundleType = "";

    /// <summary>
    /// Bundle 策略 Tags，与资源 Labels 分开；热更后端返回全部 Bundle。
    /// </summary>
    [BinaryField(6)]
    public List<string> Tags = new();

    /// <summary>
    /// ABManifest.BundleEntries 中的直接依赖下标，由 ABBundleLoader 遍历。
    /// </summary>
    [BinaryField(7)]
    public int[] DependBundleIndices = new int[0];

    /// <summary>
    /// Manifest 初始化时填充的反向资源列表，不序列化。
    /// </summary>
    [NonSerialized]
    public List<ManifestAssetEntry> IncludeAssets = new();

    /// <summary>
    /// 反向依赖下标，不序列化；不是 loader 引用计数。
    /// </summary>
    [NonSerialized]
    public List<int> ReferencedByBundleIndices = new();

}
