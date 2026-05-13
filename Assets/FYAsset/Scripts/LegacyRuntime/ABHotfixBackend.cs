using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// AB 热更后端 — 基于自研 ABManifest 方案的 IHotfixPipeline 实现。
///
/// 设计说明：
/// - 直接替代 LegacyHotfixBackend，无需 Addressables 依赖
/// - 使用 ABManifest 替代 version_state.json + catalog.json 双文件结构
/// - 通过 SerializationUtility 支持二进制(.bin)和 JSON 两种格式
/// - 优先读取二进制格式（体积更小、解析更快），回退到 JSON 格式
///
/// 热更流程：
/// 1. InitializeBackendAsync → 无操作（AB 方案无需初始化）
/// 2. LoadLocalVersionAsync → 从 currentGUIDRoot 读取本地 ABManifest
/// 3. FetchRemoteVersionAsync → 下载远端 ABManifest 并缓存原始数据
/// 4. GetBundleDownloadList → 从 ABManifest 提取 Bundle 列表
/// 5. PostDownloadAsync → 将缓存的 ABManifest 写入目标目录
///
/// 与 Legacy 后端的差异：
/// - 无需 Addressables.InitializeAsync
/// - 无需下载 catalog.json
/// - 元数据文件从 2 个减少到 1 个
/// </summary>
public class ABHotfixBackend : IHotfixPipeline
{
    /// <summary>远端 ABManifest 原始数据（二进制或 JSON），用于 PostDownload 写入本地</summary>
    private byte[] _remoteManifestData;

    /// <summary>标记远端数据是否为二进制格式，用于确定写入时的文件扩展名</summary>
    private bool _remoteManifestIsBinary;

    /// <summary>解析后的远端 ABManifest 对象，用于 GetBundleDownloadList</summary>
    private ABManifest _remoteManifest;

    #region IHotfixPipeline

    /// <inheritdoc/>
    public Task<HotfixStepResult> InitializeBackendAsync()
    {
        return Task.FromResult(HotfixStepResult.Ok);
    }

    /// <inheritdoc/>
    public Task<HotfixVersionInfo> LoadLocalVersionAsync(string currentGUIDRoot)
    {
        string manifestBinPath = Path.Combine(currentGUIDRoot, FYAssetSettings.MANIFEST_FILE_NAME_BIN);
        string manifestJsonPath = Path.Combine(currentGUIDRoot, FYAssetSettings.MANIFEST_FILE_NAME);

        try
        {
            ABManifest manifest = null;

            // 优先读取二进制格式（体积小、解析快）
            if (FileHelper.Exists(manifestBinPath))
            {
                manifest = ABManifest.DeserializeFromFile(manifestBinPath);
                Debug.Log($"[ABHotfixBackend] 从本地二进制清单加载版本: {manifest.PackageVersion?.GetVersionString()}");
            }
            // 回退到 JSON 格式
            else if (FileHelper.Exists(manifestJsonPath))
            {
                manifest = ABManifest.DeserializeFromFile(manifestJsonPath);
                Debug.Log($"[ABHotfixBackend] 从本地 JSON 清单加载版本: {manifest.PackageVersion?.GetVersionString()}");
            }

            return Task.FromResult(ToHotfixVersionInfo(manifest));
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[ABHotfixBackend] 本地 ABManifest 读取失败: {ex.Message}");
            return Task.FromResult<HotfixVersionInfo>(null);
        }
    }

    /// <inheritdoc/>
    public async Task<HotfixVersionInfo> FetchRemoteVersionAsync(string remoteUrlRoot)
    {
        // 优先下载二进制格式
        string manifestBinUrl = $"{remoteUrlRoot}/{FYAssetSettings.MANIFEST_FILE_NAME_BIN}";
        _remoteManifestData = await NetworkDownloader.DownloadBytes(manifestBinUrl);
        _remoteManifestIsBinary = _remoteManifestData != null && _remoteManifestData.Length > 0;

        // 二进制下载失败，回退到 JSON 格式
        if (!_remoteManifestIsBinary)
        {
            string manifestJsonUrl = $"{remoteUrlRoot}/{FYAssetSettings.MANIFEST_FILE_NAME}";
            string manifestJson = await NetworkDownloader.DownloadText(manifestJsonUrl);
            if (string.IsNullOrEmpty(manifestJson))
                return null;

            _remoteManifestData = System.Text.Encoding.UTF8.GetBytes(manifestJson);
        }

        try
        {
            // 解析 ABManifest（自动识别二进制/JSON 格式）
            _remoteManifest = SerializationUtility.Deserialize<ABManifest>(_remoteManifestData);
            _remoteManifest.Initialize();
            Debug.Log($"[ABHotfixBackend] 远端版本: {_remoteManifest.PackageVersion?.GetVersionString()}");
            return ToHotfixVersionInfo(_remoteManifest);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[ABHotfixBackend] 远端 ABManifest 解析失败: {ex.Message}");
            return null;
        }
    }

    /// <inheritdoc/>
    public IReadOnlyList<BundleDownloadItem> GetBundleDownloadList(HotfixVersionInfo remoteInfo)
    {
        return remoteInfo?.Bundles ?? Array.Empty<BundleDownloadItem>();
    }

    /// <inheritdoc/>
    public Task<HotfixStepResult> PostDownloadAsync(HotfixContext ctx)
    {
        if (_remoteManifestData == null || _remoteManifestData.Length == 0)
        {
            Debug.LogError("[ABHotfixBackend] 远端 ABManifest 缓存为空，无法写入本地");
            return Task.FromResult(HotfixStepResult.Fail(
                RuntimeMessage.Error(RuntimeErrorCodes.BundleNotFound, "[ABHotfixBackend] 远端 ABManifest 缓存为空")));
        }

        // 根据下载格式确定写入文件名
        string fileName = _remoteManifestIsBinary ? FYAssetSettings.MANIFEST_FILE_NAME_BIN : FYAssetSettings.MANIFEST_FILE_NAME;
        string filePath = Path.Combine(ctx.TargetGUIDRoot, fileName);

        // 删除异格式旧文件（避免残留）
        string alternateFileName = _remoteManifestIsBinary ? FYAssetSettings.MANIFEST_FILE_NAME : FYAssetSettings.MANIFEST_FILE_NAME_BIN;
        string alternateFilePath = Path.Combine(ctx.TargetGUIDRoot, alternateFileName);

        try
        {
            FileHelper.TryDelete(alternateFilePath);

            FileHelper.WriteAllBytesAtomic(filePath, _remoteManifestData);
            Debug.Log($"[ABHotfixBackend] 已写入热更清单: {filePath}");
            return Task.FromResult(HotfixStepResult.Ok);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[ABHotfixBackend] 写入热更清单失败: {ex.Message}");
            return Task.FromResult(HotfixStepResult.Fail(
                RuntimeMessage.Error(RuntimeErrorCodes.AssetExtractionFailed, $"[ABHotfixBackend] 写入热更清单失败: {ex.Message}")));
        }
    }

    #endregion

    #region Helpers

    /// <summary>
    /// 将 ABManifest 数据模型转换为统一热更版本视图。
    /// </summary>
    private static HotfixVersionInfo ToHotfixVersionInfo(ABManifest manifest)
    {
        if (manifest == null)
            return null;

        var bundleEntries = manifest.BundleEntries ?? new List<ManifestBundleEntry>(0);
        var bundles = new List<BundleDownloadItem>(bundleEntries.Count);
        long totalSize = 0;

        for (int i = 0; i < bundleEntries.Count; i++)
        {
            var bundleEntry = bundleEntries[i];
            bundles.Add(new BundleDownloadItem
            {
                BundleName = bundleEntry.BundleName,
                FileHash = bundleEntry.FileHash,
                FileSize = bundleEntry.FileSize
            });
            totalSize += bundleEntry.FileSize;
        }

        return new HotfixVersionInfo
        {
            Version = manifest.PackageVersion,
            BundleCount = bundles.Count,
            TotalSize = totalSize,
            Bundles = bundles
        };
    }

    #endregion
}
