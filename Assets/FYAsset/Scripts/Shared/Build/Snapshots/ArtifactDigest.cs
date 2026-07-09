using System;

/// <summary>
/// 构建产物的最小内容指纹。AA 使用 Asset GUID 作为 Name，AB 使用 BundleName 作为 Name。
/// </summary>
 [Serializable]
public class ArtifactDigest
{
    /// <summary>产物身份。调用方必须保证同一次 Diff 两侧处于同一命名域。</summary>
    public string Name;

    /// <summary>内容 Hash，当前使用 MD5 字符串。</summary>
    public string Hash;

    /// <summary>产物大小，单位为 byte。</summary>
    public long Size;

    /// <summary>CRC32 快速校验值。</summary>
    public uint CRC;
}
