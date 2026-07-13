using System.Collections.Generic;
using System.Threading.Tasks;

/// <summary>
/// 热更后端接口。HotfixManager 负责公共编排；后端只实现自身差异步骤。
///
/// 设计说明：
/// - 精确包检查不回退到 StreamingAssets。
/// - 远端元数据持久化与包激活相互独立。
/// - AA 激活外部 catalog；AB 激活为空操作。
///
/// 编排流程（HotfixManager 控制）：
/// 1. InitializeBackendAsync → 后端初始化
/// 2. InspectPackageAsync → 精确检查本地包
/// 3. FetchRemoteVersionAsync → 仅在需要时获取远端 manifest
/// 4. GetBundleDownloadList → 提取下载列表
/// 5. PersistRemoteMetadataAsync → 持久化 manifest/catalog
/// 6. ActivatePackageAsync → 激活已验证的本地内容
/// </summary>
public interface IHotfixPipeline
{
    /// <summary>
    /// 后端初始化。
    /// AA: catalog 初始化；AB: 无操作。
    /// </summary>
    Task<HotfixStepResult> InitializeBackendAsync();

    /// <summary>
    /// 精确检查单个隔离包，不回退到 StreamingAssets。
    /// </summary>
    Task<HotfixPackageInspection> InspectPackageAsync(string packageRoot, PackageIndex expectedIndex);

    /// <summary>
    /// 下载并解析远端版本信息。
    /// 后端需缓存原始数据以供 PersistRemoteMetadataAsync 使用。
    /// </summary>
    Task<HotfixVersionInfo> FetchRemoteVersionAsync(
        string remoteUrlRoot,
        HotfixDownloadOptions metadataOptions);

    /// <summary>
    /// 从统一版本视图中提取待下载 Bundle 列表。
    /// </summary>
    IReadOnlyList<BundleDownloadItem> GetBundleDownloadList(HotfixVersionInfo remoteInfo);

    /// <summary>
    /// 检查后端特定的非 manifest 元数据是否已存在。
    /// </summary>
    bool HasRequiredMetadata(string packageRoot);

    /// <summary>
    /// 持久化缓存的远端 manifest，并按需持久化后端特定元数据。
    /// </summary>
    Task<HotfixStepResult> PersistRemoteMetadataAsync(
        HotfixContext ctx,
        HotfixDownloadOptions metadataOptions,
        bool refreshRequiredMetadata);

    /// <summary>
    /// 从本地文件激活已验证的包。
    /// </summary>
    Task<HotfixStepResult> ActivatePackageAsync(string packageRoot);
}
