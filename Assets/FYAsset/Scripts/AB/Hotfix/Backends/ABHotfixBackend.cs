using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 支持精确本地 manifest 检查的自定义 AssetBundle 热更后端。
/// </summary>
public sealed class ABHotfixBackend : IHotfixPipeline
{
    private byte[] _remoteManifestData;
    private bool _remoteManifestIsBinary;
    private ABManifest _remoteManifest;
    private string _remoteManifestHash = string.Empty;

    public Task<HotfixStepResult> InitializeBackendAsync()
    {
        return Task.FromResult(HotfixStepResult.Ok);
    }

    public Task<HotfixPackageInspection> InspectPackageAsync(
        string packageRoot,
        PackageIndex expectedIndex,
        bool requirePackageDirectoryMatch = true)
    {
        try
        {
            (ABManifest manifest, string manifestHash) = LoadExactManifest(packageRoot);
            HotfixVersionInfo info = ToHotfixVersionInfo(manifest, manifestHash);
            return Task.FromResult(HotfixPackageInspection.Inspect(
                packageRoot,
                expectedIndex,
                info,
                true,
                string.Empty,
                requirePackageDirectoryMatch));
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[ABHotfixBackend] 精确包检查失败：{ex.Message}");
            return Task.FromResult(HotfixPackageInspection.Incomplete(null, ex.Message));
        }
    }

    public async Task<HotfixVersionInfo> FetchRemoteVersionAsync(
        string remoteUrlRoot,
        int timeoutSeconds,
        int maxRetryCount,
        float retryBaseDelaySeconds)
    {
        string binaryUrl = FYAssetPathUtility.JoinUrl(
            remoteUrlRoot,
            FYAssetSettings.MANIFEST_FILE_NAME_BIN);
        _remoteManifestData = await NetworkDownloader.DownloadBytes(
            binaryUrl, timeoutSeconds, maxRetryCount, retryBaseDelaySeconds);
        _remoteManifestIsBinary = _remoteManifestData != null && _remoteManifestData.Length > 0;

        if (!_remoteManifestIsBinary)
        {
            string jsonUrl = FYAssetPathUtility.JoinUrl(
                remoteUrlRoot,
                FYAssetSettings.MANIFEST_FILE_NAME);
            string json = await NetworkDownloader.DownloadText(
                jsonUrl, timeoutSeconds, maxRetryCount, retryBaseDelaySeconds);
            if (string.IsNullOrEmpty(json))
                return null;
            _remoteManifestData = Encoding.UTF8.GetBytes(json);
        }

        try
        {
            _remoteManifest = SerializationUtility.Deserialize<ABManifest>(_remoteManifestData);
            _remoteManifest.Initialize();
            _remoteManifestHash = HashGenerator.GenerateBytesHash(_remoteManifestData);
            return ToHotfixVersionInfo(_remoteManifest, _remoteManifestHash);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[ABHotfixBackend] 远端 ABManifest 解析失败：{ex.Message}");
            return null;
        }
    }

    public IReadOnlyList<BundleDownloadItem> GetBundleDownloadList(HotfixVersionInfo remoteInfo)
    {
        return remoteInfo?.Bundles ?? Array.Empty<BundleDownloadItem>();
    }

    public bool HasRequiredMetadata(string packageRoot)
    {
        return true;
    }

    public Task<HotfixStepResult> PersistRemoteMetadataAsync(
        HotfixContext ctx,
        int timeoutSeconds,
        int maxRetryCount,
        float retryBaseDelaySeconds,
        bool refreshRequiredMetadata)
    {
        if (_remoteManifestData == null || _remoteManifestData.Length == 0 || _remoteManifest == null)
        {
            return Task.FromResult(HotfixStepResult.Fail(
                RuntimeMessage.BundleNotFound("远端 ABManifest 缓存为空。")));
        }

        string fileName = _remoteManifestIsBinary
            ? FYAssetSettings.MANIFEST_FILE_NAME_BIN
            : FYAssetSettings.MANIFEST_FILE_NAME;
        string alternateFileName = _remoteManifestIsBinary
            ? FYAssetSettings.MANIFEST_FILE_NAME
            : FYAssetSettings.MANIFEST_FILE_NAME_BIN;
        try
        {
            FileHelper.WriteAllBytesAtomic(
                FYAssetPathUtility.JoinFilePath(ctx.TargetGUIDRoot, fileName),
                _remoteManifestData);
            FileHelper.TryDelete(FYAssetPathUtility.JoinFilePath(ctx.TargetGUIDRoot, alternateFileName));
            return Task.FromResult(HotfixStepResult.Ok);
        }
        catch (Exception ex)
        {
            return Task.FromResult(HotfixStepResult.Fail(
                RuntimeMessage.AssetExtractionFailed(
                    "MANIFEST_WRITE",
                    "ABManifest",
                    ex.Message)));
        }
    }

    public Task<HotfixStepResult> ActivatePackageAsync(string packageRoot)
    {
        return Task.FromResult(HotfixStepResult.Ok);
    }

    /// <summary>
    /// 读取包内精确清单，并给出清单文件本身的摘要。
    /// 清单不再自引用 Hash，因此这里对实际清单文件字节取摘要。
    /// </summary>
    private static (ABManifest manifest, string manifestHash) LoadExactManifest(string packageRoot)
    {
        string binaryPath = FYAssetPathUtility.JoinFilePath(
            packageRoot,
            FYAssetSettings.MANIFEST_FILE_NAME_BIN);
        if (FileHelper.Exists(binaryPath))
            return (ABManifest.DeserializeFromFile(binaryPath), ComputeFileHash(binaryPath));

        string jsonPath = FYAssetPathUtility.JoinFilePath(
            packageRoot,
            FYAssetSettings.MANIFEST_FILE_NAME);
        return FileHelper.Exists(jsonPath)
            ? (ABManifest.DeserializeFromFile(jsonPath), ComputeFileHash(jsonPath))
            : (null, string.Empty);
    }

    private static string ComputeFileHash(string path)
    {
        try
        {
            return HashGenerator.GenerateBytesHash(FileHelper.ReadAllBytes(path));
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    private static HotfixVersionInfo ToHotfixVersionInfo(ABManifest manifest, string manifestHash)
    {
        if (manifest == null)
            return null;

        List<ManifestContentEntry> entries = manifest.ContentEntries ?? new List<ManifestContentEntry>(0);
        var bundles = new List<BundleDownloadItem>(entries.Count);
        long totalSize = 0;
        for (int i = 0; i < entries.Count; i++)
        {
            ManifestContentEntry entry = entries[i];
            if (entry == null)
                continue;

            bundles.Add(new BundleDownloadItem
            {
                BundleName = entry.FileName,
                FileHash = entry.FileHash,
                FileCRC = entry.FileCRC,
                FileSize = entry.FileSize
            });
            totalSize += entry.FileSize;
        }

        return new HotfixVersionInfo
        {
            ManifestHash = manifestHash ?? string.Empty,
            Version = manifest.PackageVersion,
            BundleCount = bundles.Count,
            TotalSize = totalSize,
            Bundles = bundles
        };
    }
}
