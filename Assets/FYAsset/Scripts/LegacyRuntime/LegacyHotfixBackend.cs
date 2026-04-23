using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;

/// <summary>
/// Legacy 热更后端，封装现有 Addressables 版本链路。
/// </summary>
public class LegacyHotfixBackend : IHotfixPipeline
{
    private string _remoteVersionJson;
    private VersionState _remoteVersionState;

    #region IHotfixPipeline

    public async Task<bool> InitializeBackendAsync()
    {
        var initHandle = Addressables.InitializeAsync(false);
        try
        {
            await initHandle.Task;
            Debug.Log("[LegacyHotfixBackend] Addressables 本地包初始化成功");
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"[LegacyHotfixBackend] Addressables 初始化异常: {e.Message}");
            return false;
        }
    }

    public Task<HotfixVersionInfo> LoadLocalVersionAsync(string currentGUIDRoot)
    {
        string localVersionStatePath = Path.Combine(currentGUIDRoot, "version_state.json");
        if (!File.Exists(localVersionStatePath))
            return Task.FromResult<HotfixVersionInfo>(null);

        try
        {
            var localVersionState = SerializationUtility.ReadFromFile<VersionState>(localVersionStatePath);
            Debug.Log($"[LegacyHotfixBackend] 本地版本: {localVersionState?.version.GetVersionString()}, Hash: {localVersionState?.hash}");
            return Task.FromResult(ToHotfixVersionInfo(localVersionState));
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[LegacyHotfixBackend] 本地 version_state 读取失败: {ex.Message}");
            return Task.FromResult<HotfixVersionInfo>(null);
        }
    }

    public async Task<HotfixVersionInfo> FetchRemoteVersionAsync(string remoteUrlRoot)
    {
        string remoteVersionUrl = $"{remoteUrlRoot}/version_state.json";
        _remoteVersionJson = await NetworkDownloader.DownloadText(remoteVersionUrl);
        if (string.IsNullOrEmpty(_remoteVersionJson))
            return null;

        try
        {
            _remoteVersionState = SerializationUtility.DeserializeJson<VersionState>(_remoteVersionJson);
            Debug.Log($"[LegacyHotfixBackend] 远端版本: {_remoteVersionState?.version.GetVersionString()}");
            return ToHotfixVersionInfo(_remoteVersionState);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[LegacyHotfixBackend] 远端 version_state 解析失败: {ex.Message}");
            return null;
        }
    }

    public IReadOnlyList<BundleDownloadItem> GetBundleDownloadList(HotfixVersionInfo remoteInfo)
    {
        return remoteInfo?.Bundles ?? Array.Empty<BundleDownloadItem>();
    }

    public async Task<bool> PostDownloadAsync(HotfixContext ctx)
    {
        string catalogUrl = $"{ctx.RemoteUrlRoot}/catalog.json";
        string catalogSavePath = Path.Combine(ctx.TargetGUIDRoot, "catalog.json");
        bool catalogOk = await NetworkDownloader.DownloadFile(catalogUrl, catalogSavePath);
        if (!catalogOk)
        {
            Debug.LogError("[LegacyHotfixBackend] catalog.json 下载失败");
            return false;
        }

        if (string.IsNullOrEmpty(_remoteVersionJson))
        {
            Debug.LogError("[LegacyHotfixBackend] 远端 version_state 缓存为空，无法写入本地");
            return false;
        }

        File.WriteAllText(
            Path.Combine(ctx.TargetGUIDRoot, "version_state.json"),
            _remoteVersionJson);

        string localCatalogPath = Path.Combine(ctx.TargetGUIDRoot, "catalog.json");
        bool catalogLoaded = await CatalogUpdater.LoadExternalCatalog(localCatalogPath);
        if (!catalogLoaded)
        {
            Debug.LogError("[LegacyHotfixBackend] 外部 Catalog 加载失败");
            return false;
        }

        Debug.Log("[LegacyHotfixBackend] Catalog 下载并加载成功");
        return true;
    }

    #endregion

    #region Helpers

    private static HotfixVersionInfo ToHotfixVersionInfo(VersionState versionState)
    {
        if (versionState == null)
            return null;

        var bundles = new List<BundleDownloadItem>(versionState.bundles?.Count ?? 0);
        if (versionState.bundles != null)
        {
            for (int i = 0; i < versionState.bundles.Count; i++)
            {
                var bundle = versionState.bundles[i];
                bundles.Add(new BundleDownloadItem
                {
                    BundleName = bundle.bundleName,
                    FileHash = bundle.hash,
                    FileSize = bundle.size
                });
            }
        }

        return new HotfixVersionInfo
        {
            Version = versionState.version,
            BundleCount = bundles.Count,
            TotalSize = versionState.totalSize,
            Bundles = bundles
        };
    }

    #endregion
}
