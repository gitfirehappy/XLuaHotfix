using System;

/// <summary>
/// 构建快照中用于 Diff 的条目；AA 的 Name 为 Asset GUID，AB 为 BundleName。
/// </summary>
[Serializable]
public class BuildDiffEntry
{
    /// <summary>产物身份。调用方必须保证同一次 Diff 两侧处于同一命名域。</summary>
    public string Name;

    /// <summary>内容 Hash；ArtifactDiffer 只比较该字段判断 Modified。</summary>
    public string Hash;

    /// <summary>字节数；不参与 Diff 比较。</summary>
    public long Size;

    /// <summary>CRC32 元数据；不参与 Diff 比较。</summary>
    public uint CRC;
}
