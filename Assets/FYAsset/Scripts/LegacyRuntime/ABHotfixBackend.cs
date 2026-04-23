using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// AB 热更后端，使用 ABManifest 替代 version_state + catalog。
/// </summary>
public class ABHotfixBackend : IHotfixPipeline
{
    private byte[] _remoteManifestData;
    private bool _remoteManifestIsBinary;
    private ABManifest _remoteManifest;

    #region IHotfixPipeline

    public Task<bool> InitializeBackendAsync()
    {
        return Task.FromResult(true);
    }

    public Task<HotfixVersionInfo> LoadLocalVersionAsync(string currentGUIDRoot)
    {
        string manifestBinPath = Path.Combine(currentGUIDRoot, Constants.MANIFEST_FILE_NAME_BIN);
        string manifestJsonPath = Path.Combine(currentGUIDRoot, Constants.MANIFEST_FILE_NAME);

        try
        {
            ABManifest manifest = null;
            if (File.Exists(manifestBinPath))
            {
                manifest = ABManifest.DeserializeFromFile(manifestBinPath);
                Debug.Log($"[ABHotfixBackend] 从本地二进制清单加载版本: {manifest.PackageVersion?.GetVersionString()}");
            }
            else if (File.Exists(manifestJsonPath))
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

    public async Task<HotfixVersionInfo> FetchRemoteVersionAsync(string remoteUrlRoot)
    {
        string manifestBinUrl = $"{remoteUrlRoot}/{Constants.MANIFEST_FILE_NAME_BIN}";
        _remoteManifestData = await NetworkDownloader.DownloadBytes(manifestBinUrl);
        _remoteManifestIsBinary = _remoteManifestData != null && _remoteManifestData.Length > 0;

        if (!_remoteManifestIsBinary)
        {
            string manifestJsonUrl = $"{remoteUrlRoot}/{Constants.MANIFEST_FILE_NAME}";
            string manifestJson = await NetworkDownloader.DownloadText(manifestJsonUrl);
            if (string.IsNullOrEmpty(manifestJson))
                return null;

            _remoteManifestData = System.Text.Encoding.UTF8.GetBytes(manifestJson);
        }

        try
        {
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

    public IReadOnlyList<BundleDownloadItem> GetBundleDownloadList(HotfixVersionInfo remoteInfo)
    {
        return remoteInfo?.Bundles ?? Array.Empty<BundleDownloadItem>();
    }

    public Task<bool> PostDownloadAsync(HotfixContext ctx)
    {
        if (_remoteManifestData == null || _remoteManifestData.Length == 0)
        {
            Debug.LogError("[ABHotfixBackend] 远端 ABManifest 缓存为空，无法写入本地");
            return Task.FromResult(false);
        }

        string fileName = _remoteManifestIsBinary ? Constants.MANIFEST_FILE_NAME_BIN : Constants.MANIFEST_FILE_NAME;
        string filePath = Path.Combine(ctx.TargetGUIDRoot, fileName);
        string alternateFileName = _remoteManifestIsBinary ? Constants.MANIFEST_FILE_NAME : Constants.MANIFEST_FILE_NAME_BIN;
        string alternateFilePath = Path.Combine(ctx.TargetGUIDRoot, alternateFileName);

        try
        {
            if (File.Exists(alternateFilePath))
                File.Delete(alternateFilePath);

            File.WriteAllBytes(filePath, _remoteManifestData);
            Debug.Log($"[ABHotfixBackend] 已写入热更清单: {filePath}");
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[ABHotfixBackend] 写入热更清单失败: {ex.Message}");
            return Task.FromResult(false);
        }
    }

    #endregion

    #region Helpers

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
