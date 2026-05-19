using System.Collections.Generic;
using System.Threading.Tasks;

/// <summary>
/// 热更后端接口。HotfixManager 负责公共编排；后端只实现自身差异步骤。
///
/// 设计说明：
/// - 将热更流程中的后端特定操作抽象为 5 个方法
/// - AA 后端：封装 Addressables 初始化、AAManifest/catalog 下载
/// - AB 后端：使用 ABManifest 替代双文件结构，无需 Addressables 依赖
///
/// 编排流程（HotfixManager 控制）：
/// 1. InitializeBackendAsync → 后端初始化
/// 2. LoadLocalVersionAsync → 读取本地版本
/// 3. FetchRemoteVersionAsync → 获取远端版本
/// 4. GetBundleDownloadList → 提取下载列表
/// 5. PostDownloadAsync → 下载后处理
/// </summary>
public interface IHotfixPipeline
{
    /// <summary>
    /// 后端初始化。
    /// AA: Addressables.InitializeAsync；AB: 无操作。
    /// </summary>
    Task<HotfixStepResult> InitializeBackendAsync();

    /// <summary>
    /// 从当前生效目录读取本地版本信息。
    /// 无本地版本时返回 null（首次安装场景）。
    /// </summary>
    Task<HotfixVersionInfo> LoadLocalVersionAsync(string currentGUIDRoot);

    /// <summary>
    /// 下载并解析远端版本信息。
    /// 后端需缓存原始数据以供 PostDownloadAsync 使用。
    /// </summary>
    Task<HotfixVersionInfo> FetchRemoteVersionAsync(string remoteUrlRoot);

    /// <summary>
    /// 从统一版本视图中提取待下载 Bundle 列表。
    /// </summary>
    IReadOnlyList<BundleDownloadItem> GetBundleDownloadList(HotfixVersionInfo remoteInfo);

    /// <summary>
    /// 下载完成后的后处理。
    /// AA: 下载 catalog + 写入 AAManifest + 加载外部 Catalog。
    /// AB: 写入缓存的 ABManifest 数据。
    /// </summary>
    Task<HotfixStepResult> PostDownloadAsync(HotfixContext ctx);
}
