using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;

/// <summary>
/// AA 热更后端 — 封装现有 catalog 版本链路的 IHotfixPipeline 实现。
///
/// 设计说明：
/// - 保持与重构前 HotfixManager 完全相同的行为（零变更）
/// - 使用 AAManifest.bin/json 记录版本、Bundle 信息和 AA 资产索引
/// - 使用 catalog.json 作为 Addressables 资源索引
/// - 依赖 Addressables 初始化和外部 Catalog 加载
///
/// 热更流程：
/// 1. InitializeBackendAsync -> Addressables.InitializeAsync（初始化本地包）
/// 2. LoadLocalVersionAsync -> 从 currentGUIDRoot 读取 AAManifest.bin/json
/// 3. FetchRemoteVersionAsync -> 下载远端 AAManifest.bin，失败时回退 AAManifest.json 并缓存
/// 4. GetBundleDownloadList -> 从 AAManifest 提取 Bundle 列表
/// 5. PostDownloadAsync -> 下载 catalog.json + 写入 AAManifest + 加载外部 Catalog
///
/// 与 AB 后端的差异：
/// - 需要 Addressables.InitializeAsync 初始化
/// - 需要下载 catalog.json 并加载外部 Catalog
/// - 元数据文件为 2 类（manifest + catalog）
/// </summary>
public class AAHotfixBackend : IHotfixPipeline
{
    /// <summary>远端 AAManifest 原始内容，用于 PostDownload 写入本地</summary>
    private byte[] _remoteManifestData;

    /// <summary>标记远端数据是否为二进制格式，用于确定写入文件名</summary>
    private bool _remoteManifestIsBinary;

    /// <summary>解析后的远端 AAManifest 对象，用于 GetBundleDownloadList</summary>
    private AAManifest _remoteManifest;

    #region IHotfixPipeline

    /// <summary>
    /// 后端初始化。
    /// AA: catalog 初始化；AB: 无操作。
    /// </summary>
    /// <returns>初始化是否成功。</returns>
    public async Task<HotfixStepResult> InitializeBackendAsync()
    {
        // 初始化 Addressables 本地包（不自动检查更新）
        var initHandle = Addressables.InitializeAsync(false);
        try
        {
            await initHandle.Task;
            Debug.Log("[AAHotfixBackend] catalog 本地包初始化成功");
            return HotfixStepResult.Ok;
        }
        catch (Exception e)
        {
            return HotfixStepResult.Fail(
                RuntimeMessage.Error(RuntimeErrorCodes.LoadFailed, $"Addressables 初始化异常: {e.Message}"));
        }
    }

    /// <summary>
    /// 从当前生效目录读取本地版本信息。
    /// 无本地版本时返回 null（首次安装场景）。
    /// </summary>
    /// <param name="currentGUIDRoot">当前生效目录根目录。</param>
    /// <returns>本地版本信息视图。</returns>
    public async Task<HotfixVersionInfo> LoadLocalVersionAsync(string currentGUIDRoot)
    {
        try
        {
            var localManifest = await AAManifestLoader.LoadFromDirectoryAsync(currentGUIDRoot)
                                ?? await AAManifestLoader.LoadFromDirectoryAsync(Application.streamingAssetsPath);
            Debug.Log(
                $"[AAHotfixBackend] 本地版本: {localManifest?.Version.GetVersionString()}, Hash: {localManifest?.FileHash}");
            return ToHotfixVersionInfo(localManifest);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[AAHotfixBackend] 本地 AAManifest 读取失败: {ex.Message}");
            return null;
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
        string remoteManifestBinUrl = FYAssetPathUtility.JoinUrl(remoteUrlRoot, FYAssetSettings.AA_MANIFEST_FILE_NAME_BIN);
        _remoteManifestData = await NetworkDownloader.DownloadBytes(remoteManifestBinUrl);
        _remoteManifestIsBinary = _remoteManifestData != null && _remoteManifestData.Length > 0;

        if (!_remoteManifestIsBinary)
        {
            string remoteManifestUrl = FYAssetPathUtility.JoinUrl(remoteUrlRoot, FYAssetSettings.AA_MANIFEST_FILE_NAME);
            string remoteManifestJson = await NetworkDownloader.DownloadText(remoteManifestUrl);
            if (string.IsNullOrEmpty(remoteManifestJson))
                return null;

            _remoteManifestData = Encoding.UTF8.GetBytes(remoteManifestJson);
        }

        try
        {
            _remoteManifest = SerializationUtility.Deserialize<AAManifest>(_remoteManifestData);
            Debug.Log($"[AAHotfixBackend] 远端版本: {_remoteManifest?.Version.GetVersionString()}");
            return ToHotfixVersionInfo(_remoteManifest);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[AAHotfixBackend] 远端 AAManifest 解析失败: {ex.Message}");
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
        string catalogUrl = FYAssetPathUtility.JoinUrl(ctx.RemoteUrlRoot, FYAssetSettings.ADDRESSABLES_CATALOG_FILE_NAME);
        string catalogSavePath = FYAssetPathUtility.JoinFilePath(ctx.TargetGUIDRoot, FYAssetSettings.ADDRESSABLES_CATALOG_FILE_NAME);
        bool catalogOk = await NetworkDownloader.DownloadFile(catalogUrl, catalogSavePath);
        if (!catalogOk)
            return HotfixStepResult.Fail(
                RuntimeMessage.Error(RuntimeErrorCodes.BundleNotFound, "catalog.json 下载失败"));

        // 写入 AAManifest
        if (_remoteManifestData == null || _remoteManifestData.Length == 0)
            return HotfixStepResult.Fail(
                RuntimeMessage.Error(RuntimeErrorCodes.BundleNotFound, "远端 AAManifest 缓存为空"));

        string fileName = _remoteManifestIsBinary
            ? FYAssetSettings.AA_MANIFEST_FILE_NAME_BIN
            : FYAssetSettings.AA_MANIFEST_FILE_NAME;
        string alternateFileName = _remoteManifestIsBinary
            ? FYAssetSettings.AA_MANIFEST_FILE_NAME
            : FYAssetSettings.AA_MANIFEST_FILE_NAME_BIN;
        FileHelper.TryDelete(FYAssetPathUtility.JoinFilePath(ctx.TargetGUIDRoot, alternateFileName));
        FileHelper.WriteAllBytesAtomic(FYAssetPathUtility.JoinFilePath(ctx.TargetGUIDRoot, fileName), _remoteManifestData);

        // 加载外部 Catalog（使 Addressables 识别热更资源）
        string localCatalogPath = FYAssetPathUtility.JoinFilePath(ctx.TargetGUIDRoot, FYAssetSettings.ADDRESSABLES_CATALOG_FILE_NAME);
        bool catalogLoaded = await CatalogUpdater.LoadExternalCatalog(localCatalogPath);
        if (!catalogLoaded)
            return HotfixStepResult.Fail(
                RuntimeMessage.Error(RuntimeErrorCodes.LoadFailed, "外部 Catalog 加载失败"));

        Debug.Log("[AAHotfixBackend] Catalog 下载并加载成功");
        return HotfixStepResult.Ok;
    }

    #endregion

    #region Helpers

    /// <summary>
    /// 将 AAManifest 数据模型转换为统一热更版本视图。
    /// </summary>
    private static HotfixVersionInfo ToHotfixVersionInfo(AAManifest manifest)
    {
        if (manifest == null)
            return null;

        var bundles = new List<BundleDownloadItem>(manifest.Bundles?.Count ?? 0);
        if (manifest.Bundles != null)
        {
            for (int i = 0; i < manifest.Bundles.Count; i++)
            {
                var bundle = manifest.Bundles[i];
                bundles.Add(new BundleDownloadItem
                {
                    BundleName = bundle.BundleName,
                    FileHash = bundle.FileHash,
                    FileCRC = bundle.FileCRC,
                    FileSize = bundle.FileSize
                });
            }
        }

        return new HotfixVersionInfo
        {
            Version = manifest.Version,
            BundleCount = bundles.Count,
            TotalSize = manifest.TotalSize,
            Bundles = bundles
        };
    }

    #endregion
}
