using System.Collections.Generic;

/// <summary>
/// 热更流程使用的统一版本视图。
/// 屏蔽 Legacy/AB 后端的数据模型差异，提供一致的版本比较接口。
/// </summary>
public class HotfixVersionInfo
{
    /// <summary>版本号</summary>
    public VersionNumber Version;

    /// <summary>Bundle 总数</summary>
    public int BundleCount;

    /// <summary>总下载大小（字节）</summary>
    public long TotalSize;

    /// <summary>待下载的 Bundle 列表</summary>
    public IReadOnlyList<BundleDownloadItem> Bundles;
}
