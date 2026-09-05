/// <summary>
/// 统一的 Bundle 下载项。
/// 包含下载和校验所需的最小信息集。
/// </summary>
public struct BundleDownloadItem
{
    /// <summary>Bundle 文件名（含扩展名）</summary>
    public string BundleName;

    /// <summary>文件哈希值，用于增量更新校验</summary>
    public string FileHash;

    /// <summary>文件 CRC32 校验码。运行时拒绝 0 值。</summary>
    public uint FileCRC;

    /// <summary>文件大小（字节）</summary>
    public long FileSize;
}
