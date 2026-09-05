using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;

/// <summary>
/// 支持精确本地检查与显式 catalog 激活的 Addressables 热更后端。
/// </summary>
public sealed class AAHotfixBackend : IHotfixPipeline
{
    private byte[] _remoteManifestData;
    private bool _remoteManifestIsBinary;
    private AAManifest _remoteManifest;

    public async Task<HotfixStepResult> InitializeBackendAsync()
    {
        var initHandle = Addressables.InitializeAsync(false);
        try
        {
            await initHandle.Task;
            CatalogUpdater.InstallInternalIdRedirect();
            Debug.Log("[AAHotfixBackend] 内置 catalog 初始化完成。");
            return HotfixStepResult.Ok;
        }
        catch (Exception ex)
        {
            return HotfixStepResult.Fail(
                RuntimeMessage.Error(RuntimeErrorCodes.LoadFailed, $"Addressables 初始化失败：{ex.Message}"));
        }
    }

    public async Task<HotfixPackageInspection> InspectPackageAsync(
        string packageRoot,
        PackageIndex expectedIndex,
        bool requirePackageDirectoryMatch = true)
    {
        try
        {
            AAManifest manifest = await AAManifestLoader.LoadFromDirectoryAsync(packageRoot);
            HotfixVersionInfo info = ToHotfixVersionInfo(manifest);
            bool hasCatalog = HasRequiredMetadata(packageRoot);
            return HotfixPackageInspection.Inspect(
                packageRoot,
                expectedIndex,
                info,
                hasCatalog,
                "缺少 catalog.json。",
                requirePackageDirectoryMatch);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[AAHotfixBackend] 精确包检查失败：{ex.Message}");
            return HotfixPackageInspection.Incomplete(null, ex.Message);
        }
    }

    public async Task<HotfixVersionInfo> FetchRemoteVersionAsync(
        string remoteUrlRoot,
        HotfixDownloadOptions metadataOptions)
    {
        string binaryUrl = FYAssetPathUtility.JoinUrl(
            remoteUrlRoot,
            FYAssetSettings.AA_MANIFEST_FILE_NAME_BIN);
        _remoteManifestData = await NetworkDownloader.DownloadBytes(binaryUrl, metadataOptions);
        _remoteManifestIsBinary = _remoteManifestData != null && _remoteManifestData.Length > 0;

        if (!_remoteManifestIsBinary)
        {
            string jsonUrl = FYAssetPathUtility.JoinUrl(
                remoteUrlRoot,
                FYAssetSettings.AA_MANIFEST_FILE_NAME);
            string json = await NetworkDownloader.DownloadText(jsonUrl, metadataOptions);
            if (string.IsNullOrEmpty(json))
                return null;
            _remoteManifestData = Encoding.UTF8.GetBytes(json);
        }

        try
        {
            _remoteManifest = SerializationUtility.Deserialize<AAManifest>(_remoteManifestData);
            return ToHotfixVersionInfo(_remoteManifest);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[AAHotfixBackend] 远端 AAManifest 解析失败：{ex.Message}");
            return null;
        }
    }

    public IReadOnlyList<BundleDownloadItem> GetBundleDownloadList(HotfixVersionInfo remoteInfo)
    {
        return remoteInfo?.Bundles ?? Array.Empty<BundleDownloadItem>();
    }

    public bool HasRequiredMetadata(string packageRoot)
    {
        string path = FYAssetPathUtility.JoinFilePath(
            packageRoot,
            FYAssetSettings.ADDRESSABLES_CATALOG_FILE_NAME);
        return FileHelper.Exists(path);
    }

    public async Task<HotfixStepResult> PersistRemoteMetadataAsync(
        HotfixContext ctx,
        HotfixDownloadOptions metadataOptions,
        bool refreshRequiredMetadata)
    {
        if (_remoteManifestData == null || _remoteManifestData.Length == 0 || _remoteManifest == null)
        {
            return HotfixStepResult.Fail(
                RuntimeMessage.Error(RuntimeErrorCodes.BundleNotFound, "远端 AAManifest 缓存为空。"));
        }

        string catalogPath = FYAssetPathUtility.JoinFilePath(
            ctx.TargetGUIDRoot,
            FYAssetSettings.ADDRESSABLES_CATALOG_FILE_NAME);
        if (refreshRequiredMetadata)
        {
            string catalogUrl = FYAssetPathUtility.JoinUrl(
                ctx.RemoteUrlRoot,
                FYAssetSettings.ADDRESSABLES_CATALOG_FILE_NAME);
            string catalogTempPath = catalogPath + ".download";
            FileHelper.TryDelete(catalogTempPath);
            bool downloaded = await NetworkDownloader.DownloadFile(catalogUrl, catalogTempPath, metadataOptions);
            if (!downloaded)
            {
                FileHelper.TryDelete(catalogTempPath);
                return HotfixStepResult.Fail(
                    RuntimeMessage.Error(RuntimeErrorCodes.BundleNotFound, "catalog.json 下载失败。"));
            }

            try
            {
                FileHelper.ReplaceFile(catalogTempPath, catalogPath);
            }
            catch (Exception ex)
            {
                FileHelper.TryDelete(catalogTempPath);
                return HotfixStepResult.Fail(
                    RuntimeMessage.Error(RuntimeErrorCodes.LoadFailed, $"catalog.json 替换失败：{ex.Message}"));
            }
        }

        try
        {
            string fileName = _remoteManifestIsBinary
                ? FYAssetSettings.AA_MANIFEST_FILE_NAME_BIN
                : FYAssetSettings.AA_MANIFEST_FILE_NAME;
            string alternateFileName = _remoteManifestIsBinary
                ? FYAssetSettings.AA_MANIFEST_FILE_NAME
                : FYAssetSettings.AA_MANIFEST_FILE_NAME_BIN;
            string manifestPath = FYAssetPathUtility.JoinFilePath(ctx.TargetGUIDRoot, fileName);
            FileHelper.WriteAllBytesAtomic(manifestPath, _remoteManifestData);
            FileHelper.TryDelete(FYAssetPathUtility.JoinFilePath(ctx.TargetGUIDRoot, alternateFileName));
            return HotfixStepResult.Ok;
        }
        catch (Exception ex)
        {
            return HotfixStepResult.Fail(
                RuntimeMessage.Error(RuntimeErrorCodes.LoadFailed, $"AAManifest 持久化失败：{ex.Message}"));
        }
    }

    public async Task<HotfixStepResult> ActivatePackageAsync(string packageRoot)
    {
        string catalogPath = FYAssetPathUtility.JoinFilePath(
            packageRoot,
            FYAssetSettings.ADDRESSABLES_CATALOG_FILE_NAME);
        bool loaded = await CatalogUpdater.LoadExternalCatalog(catalogPath);
        return loaded
            ? HotfixStepResult.Ok
            : HotfixStepResult.Fail(
                RuntimeMessage.Error(RuntimeErrorCodes.LoadFailed, "外部 catalog 激活失败。"));
    }

    private static HotfixVersionInfo ToHotfixVersionInfo(AAManifest manifest)
    {
        if (manifest == null)
            return null;

        var bundles = new List<BundleDownloadItem>(manifest.Bundles?.Count ?? 0);
        if (manifest.Bundles != null)
        {
            for (int i = 0; i < manifest.Bundles.Count; i++)
            {
                BundleInfo bundle = manifest.Bundles[i];
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
            ManifestHash = manifest.FileHash,
            Version = manifest.Version,
            BundleCount = bundles.Count,
            TotalSize = manifest.TotalSize,
            Bundles = bundles
        };
    }
}
