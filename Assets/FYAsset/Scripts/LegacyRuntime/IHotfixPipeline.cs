using System.Collections.Generic;
using System.Threading.Tasks;

/// <summary>
/// 热更后端接口。HotfixManager 负责公共编排；后端只实现自身差异步骤。
/// </summary>
public interface IHotfixPipeline
{
    /// <summary>
    /// 后端初始化。
    /// Legacy: Addressables.InitializeAsync；AB: 无操作。
    /// </summary>
    Task<bool> InitializeBackendAsync();

    /// <summary>
    /// 从当前生效目录读取本地版本信息。
    /// 无本地版本时返回 null。
    /// </summary>
    Task<HotfixVersionInfo> LoadLocalVersionAsync(string currentGUIDRoot);

    /// <summary>
    /// 下载并解析远端版本信息。
    /// </summary>
    Task<HotfixVersionInfo> FetchRemoteVersionAsync(string remoteUrlRoot);

    /// <summary>
    /// 从统一版本视图中提取待下载 Bundle 列表。
    /// </summary>
    IReadOnlyList<BundleDownloadItem> GetBundleDownloadList(HotfixVersionInfo remoteInfo);

    /// <summary>
    /// 下载完成后的后处理。
    /// </summary>
    Task<bool> PostDownloadAsync(HotfixContext ctx);
}

/// <summary>
/// 热更流程使用的统一版本视图。
/// </summary>
public class HotfixVersionInfo
{
    public VersionNumber Version;
    public int BundleCount;
    public long TotalSize;
    public IReadOnlyList<BundleDownloadItem> Bundles;
}

/// <summary>
/// 跨后端统一的 Bundle 下载项。
/// </summary>
public struct BundleDownloadItem
{
    public string BundleName;
    public string FileHash;
    public long FileSize;
}

/// <summary>
/// 热更后处理所需的共享上下文。
/// </summary>
public class HotfixContext
{
    public BuildIndexData BuildIndex;
    public string TargetPackageName;
    public string RemoteUrlRoot;
    public string TargetGUIDRoot;
}
