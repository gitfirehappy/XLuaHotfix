using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;

/// <summary>
/// Legacy 热更后端 — 封装现有 Addressables 版本链路的 IHotfixPipeline 实现。
///
/// 设计说明：
/// - 保持与重构前 HotfixManager 完全相同的行为（零变更）
/// - 使用 version_state.json 记录版本和 Bundle 信息
/// - 使用 catalog.json 作为 Addressables 资源索引
/// - 依赖 Addressables 初始化和外部 Catalog 加载
///
/// 热更流程：
/// 1. InitializeBackendAsync → Addressables.InitializeAsync（初始化本地包）
/// 2. LoadLocalVersionAsync → 从 currentGUIDRoot 读取 version_state.json
/// 3. FetchRemoteVersionAsync → 下载远端 version_state.json 并缓存
/// 4. GetBundleDownloadList → 从 VersionState 提取 Bundle 列表
/// 5. PostDownloadAsync → 下载 catalog.json + 写入 version_state + 加载外部 Catalog
///
/// 与 AB 后端的差异：
/// - 需要 Addressables.InitializeAsync 初始化
/// - 需要下载 catalog.json 并加载外部 Catalog
/// - 元数据文件为 2 个（version_state + catalog）
/// </summary>
public class LegacyHotfixBackend : IHotfixPipeline
{
    /// <summary>远端 version_state.json 原始内容，用于 PostDownload 写入本地</summary>
    private string _remoteVersionJson;

    /// <summary>解析后的远端 VersionState 对象，用于 GetBundleDownloadList</summary>
    private VersionState _remoteVersionState;

    #region IHotfixPipeline

    /// <summary>
    /// 后端初始化。
    /// Legacy: Addressables.InitializeAsync；AB: 无操作。
    /// </summary>
    /// <returns>初始化是否成功。</returns>
    public async Task<HotfixStepResult> InitializeBackendAsync()
    {
        // 初始化 Addressables 本地包（不自动检查更新）
        var initHandle = Addressables.InitializeAsync(false);
        try
        {
            await initHandle.Task;
            Debug.Log("[LegacyHotfixBackend] Addressables 本地包初始化成功");
            return HotfixStepResult.Ok;
        }
        catch (Exception e)
        {
            Debug.LogError($"[LegacyHotfixBackend] Addressables 初始化异常: {e.Message}");
            return HotfixStepResult.Fail(
                RuntimeMessage.Error(RuntimeErrorCodes.LoadFailed, $"[LegacyHotfixBackend] Addressables 初始化异常: {e.Message}"));
        }
    }

    /// <summary>
    /// 从当前生效目录读取本地版本信息。
    /// 无本地版本时返回 null（首次安装场景）。
    /// </summary>
    /// <param name="currentGUIDRoot">当前生效目录根目录。</param>
    /// <returns>本地版本信息视图。</returns>
    public Task<HotfixVersionInfo> LoadLocalVersionAsync(string currentGUIDRoot)
    {
        string localVersionStatePath = Path.Combine(currentGUIDRoot, "version_state.json");
        if (!FileHelper.Exists(localVersionStatePath))
            return Task.FromResult<HotfixVersionInfo>(null);

        try
        {
            var localVersionState = SerializationUtility.ReadFromFile<VersionState>(localVersionStatePath);
            localVersionState?.MigrateLegacyVersionField();
            Debug.Log($"[LegacyHotfixBackend] 本地版本: {localVersionState?.Version.GetVersionString()}, Hash: {localVersionState?.FileHash}");
            return Task.FromResult(ToHotfixVersionInfo(localVersionState));
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[LegacyHotfixBackend] 本地 version_state 读取失败: {ex.Message}");
            return Task.FromResult<HotfixVersionInfo>(null);
        }
    }

    /// <summary>
    /// 下载并解析远端版本信息。
    /// 后端需缓存原始数据以供 PostDownloadAsync 使用。
    /// </summary>
    /// <param name="remoteUrlRoot">远端版本信息 URL 根目录。</param>
    /// <returns>远端版本信息视图。</returns>
    public async Task<HotfixVersionInfo> FetchRemoteVersionAsync(string remoteUrlRoot)
    {
        // 下载远端 version_state.json
        string remoteVersionUrl = $"{remoteUrlRoot}/version_state.json";
        _remoteVersionJson = await NetworkDownloader.DownloadText(remoteVersionUrl);
        if (string.IsNullOrEmpty(_remoteVersionJson))
            return null;

        try
        {
            _remoteVersionState = SerializationUtility.DeserializeJson<VersionState>(_remoteVersionJson);
            _remoteVersionState?.MigrateLegacyVersionField();
            Debug.Log($"[LegacyHotfixBackend] 远端版本: {_remoteVersionState?.Version.GetVersionString()}");
            return ToHotfixVersionInfo(_remoteVersionState);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[LegacyHotfixBackend] 远端 version_state 解析失败: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 从统一版本视图中提取待下载 Bundle 列表。
    /// </summary>
    /// <param name="remoteInfo">远端版本信息视图。</param>
    /// <returns>待下载的 Bundle 列表。</returns>
    public IReadOnlyList<BundleDownloadItem> GetBundleDownloadList(HotfixVersionInfo remoteInfo)
    {
        return remoteInfo?.Bundles ?? Array.Empty<BundleDownloadItem>();
    }

    /// <summary>
    /// 热更后端的 PostDownloadAsync 方法，负责下载 catalog.json 并加载外部 Catalog。
    /// </summary>
    /// <param name="ctx">热更上下文，包含远程 URL、目标 GUID 根目录等信息。</param>
    /// <returns>热更操作是否成功。</returns>
    public async Task<HotfixStepResult> PostDownloadAsync(HotfixContext ctx)
    {
        // 下载 catalog.json
        string catalogUrl = $"{ctx.RemoteUrlRoot}/catalog.json";
        string catalogSavePath = Path.Combine(ctx.TargetGUIDRoot, "catalog.json");
        bool catalogOk = await NetworkDownloader.DownloadFile(catalogUrl, catalogSavePath);
        if (!catalogOk)
        {
            Debug.LogError("[LegacyHotfixBackend] catalog.json 下载失败");
            return HotfixStepResult.Fail(
                RuntimeMessage.Error(RuntimeErrorCodes.BundleNotFound, "[LegacyHotfixBackend] catalog.json 下载失败"));
        }

        // 写入 version_state.json
        if (string.IsNullOrEmpty(_remoteVersionJson))
        {
            Debug.LogError("[LegacyHotfixBackend] 远端 version_state 缓存为空，无法写入本地");
            return HotfixStepResult.Fail(
                RuntimeMessage.Error(RuntimeErrorCodes.BundleNotFound, "[LegacyHotfixBackend] 远端 version_state 缓存为空"));
        }

        FileHelper.WriteAllTextAtomic(
            Path.Combine(ctx.TargetGUIDRoot, "version_state.json"),
            _remoteVersionJson);

        // 加载外部 Catalog（使 Addressables 识别热更资源）
        string localCatalogPath = Path.Combine(ctx.TargetGUIDRoot, "catalog.json");
        bool catalogLoaded = await CatalogUpdater.LoadExternalCatalog(localCatalogPath);
        if (!catalogLoaded)
        {
            Debug.LogError("[LegacyHotfixBackend] 外部 Catalog 加载失败");
            return HotfixStepResult.Fail(
                RuntimeMessage.Error(RuntimeErrorCodes.LoadFailed, "[LegacyHotfixBackend] 外部 Catalog 加载失败"));
        }

        Debug.Log("[LegacyHotfixBackend] Catalog 下载并加载成功");
        return HotfixStepResult.Ok;
    }

    #endregion

    #region Helpers

    /// <summary>
    /// 将旧版 VersionState 数据模型转换为统一热更版本视图。
    /// </summary>
    private static HotfixVersionInfo ToHotfixVersionInfo(VersionState versionState)
    {
        if (versionState == null)
            return null;

        var bundles = new List<BundleDownloadItem>(versionState.Bundles?.Count ?? 0);
        if (versionState.Bundles != null)
        {
            for (int i = 0; i < versionState.Bundles.Count; i++)
            {
                var bundle = versionState.Bundles[i];
                bundles.Add(new BundleDownloadItem
                {
                    BundleName = bundle.BundleName,
                    FileHash = bundle.FileHash,
                    FileSize = bundle.FileSize
                });
            }
        }

        return new HotfixVersionInfo
        {
            Version = versionState.Version,
            BundleCount = bundles.Count,
            TotalSize = versionState.TotalSize,
            Bundles = bundles
        };
    }

    #endregion
}
