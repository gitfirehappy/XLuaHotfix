using System.Collections.Generic;
using System.Threading.Tasks;

/// <summary>
/// 热更后端差异接口，由 HotfixFlowBase 编排调用。
/// 包检查不跨目录回退，元数据持久化与激活分开执行。
/// </summary>
public interface IHotfixPipeline
{
    /// <summary>
    /// 后端初始化。
    /// AA: catalog 初始化；AB: 无操作。
    /// </summary>
    Task<HotfixStepResult> InitializeBackendAsync();

    /// <summary>
    /// 精确检查指定包根目录，不回退到其他目录。
    /// </summary>
    Task<HotfixPackageInspection> InspectPackageAsync(
        string packageRoot,
        PackageIndex expectedIndex,
        bool requirePackageDirectoryMatch = true);

    /// <summary>
    /// 下载并解析远端版本信息。
    /// 后端需缓存原始数据以供 PersistRemoteMetadataAsync 使用。
    /// </summary>
    Task<HotfixVersionInfo> FetchRemoteVersionAsync(
        string remoteUrlRoot,
        int timeoutSeconds,
        int maxRetryCount,
        float retryBaseDelaySeconds);

    /// <summary>
    /// 从统一版本视图中提取待下载 Bundle 列表。
    /// </summary>
    IReadOnlyList<BundleDownloadItem> GetBundleDownloadList(HotfixVersionInfo remoteInfo);

    /// <summary>
    /// 持久化缓存的远端 manifest。后端自行决定其额外元数据是否需要下载。
    /// </summary>
    Task<HotfixStepResult> PersistRemoteMetadataAsync(
        HotfixContext ctx,
        int timeoutSeconds,
        int maxRetryCount,
        float retryBaseDelaySeconds);

    /// <summary>
    /// 从本地文件激活已验证的包。
    /// </summary>
    Task<HotfixStepResult> ActivatePackageAsync(string packageRoot);
}
