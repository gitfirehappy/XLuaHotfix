using System.Collections.Generic;
using System.Threading.Tasks;

/// <summary>
/// 热更后端接口。HotfixManager 负责公共编排；后端只实现自身差异步骤。
///
/// 设计说明：
/// - 将热更流程中的后端特定操作抽象为 5 个方法
/// - Legacy 后端：封装 Addressables 初始化、AAManifest/catalog 下载
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
    /// Legacy: Addressables.InitializeAsync；AB: 无操作。
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
    /// Legacy: 下载 catalog + 写入 AAManifest + 加载外部 Catalog。
    /// AB: 写入缓存的 ABManifest 数据。
    /// </summary>
    Task<HotfixStepResult> PostDownloadAsync(HotfixContext ctx);
}

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

/// <summary>
/// 跨后端统一的 Bundle 下载项。
/// 包含下载和校验所需的最小信息集。
/// </summary>
public struct BundleDownloadItem
{
    /// <summary>Bundle 文件名（含扩展名）</summary>
    public string BundleName;

    /// <summary>文件哈希值，用于增量更新校验</summary>
    public string FileHash;

    /// <summary>文件 CRC32 校验码。0 表示旧元数据缺字段，跳过 CRC 校验。</summary>
    public uint FileCRC;

    /// <summary>文件大小（字节）</summary>
    public long FileSize;
}

/// <summary>
/// 热更流程所需的上下文参数。
/// 由 HotfixManager 构建并传递给后端方法。
/// </summary>
public class HotfixContext
{
    /// <summary>构建索引数据，包含包名到路径的映射</summary>
    public BuildIndexData BuildIndex;

    /// <summary>目标包名（如 "hotfix"）</summary>
    public string TargetPackageName;

    /// <summary>远端 URL 根路径</summary>
    public string RemoteUrlRoot;

    /// <summary>下载目标目录（GUID 命名）</summary>
    public string TargetGUIDRoot;
}
