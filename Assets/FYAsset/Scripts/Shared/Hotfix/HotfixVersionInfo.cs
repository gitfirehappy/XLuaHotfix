using System.Collections.Generic;

/// <summary>
/// 热更流程共享的远端版本和下载内容视图。
/// </summary>
public sealed class HotfixVersionInfo
{
    /// <summary>远端 Manifest 的内容哈希。</summary>
    public string ManifestHash;

    /// <summary>远端版本号。</summary>
    public VersionNumber Version;

    /// <summary>Manifest 中声明的 Bundle 数量。</summary>
    public int BundleCount;

    /// <summary>待下载内容的总字节数。</summary>
    public long TotalSize;

    /// <summary>待下载的 Bundle。</summary>
    public IReadOnlyList<BundleDownloadItem> Bundles;
}
