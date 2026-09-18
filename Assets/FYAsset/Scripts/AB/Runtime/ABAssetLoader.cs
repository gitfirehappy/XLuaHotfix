using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>已完成 Address 解析后的 AB 资产加载契约。</summary>
internal interface IABAssetLoader
{
    Task<(T asset, string contentFileName, RuntimeMessage error)> LoadAssetTupleAsync<T>(
        ManifestAssetEntry entry, ManifestContentEntry content) where T : UnityEngine.Object;
    (T asset, string contentFileName, RuntimeMessage error) LoadAssetTupleSync<T>(
        ManifestAssetEntry entry, ManifestContentEntry content) where T : UnityEngine.Object;
    void UnloadByAddress(string address);
    void UnloadAllContent();
}

/// <summary>AB Asset loader：只处理已解析条目的对象提取、缓存和 Bundle 引用归还。</summary>
internal sealed class ABAssetLoader : IABAssetLoader
{
    private sealed class AssetCacheEntry
    {
        public UnityEngine.Object Asset;
        public string ContentFileName;
    }

    private readonly Dictionary<string, AssetCacheEntry> _assetCache =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Task> _inflightLoads =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly object _inflightLock = new();
    private readonly ABBundleLoader _bundleLoader;

    public ABAssetLoader(ABBundleLoader bundleLoader)
    {
        _bundleLoader = bundleLoader ?? throw new ArgumentNullException(nameof(bundleLoader));
    }

    public async Task<(T asset, string contentFileName, RuntimeMessage error)> LoadAssetTupleAsync<T>(
        ManifestAssetEntry entry, ManifestContentEntry content) where T : UnityEngine.Object
    {
        if (!ValidateSerializedEntry(entry, content, out RuntimeMessage validationError))
            return (null, content?.FileName, validationError);

        string address = entry.Address;
        if (_assetCache.TryGetValue(address, out AssetCacheEntry cached))
            return (cached.Asset as T, cached.ContentFileName, null);

        TaskCompletionSource<object> leader = null;
        lock (_inflightLock)
        {
            if (_inflightLoads.TryGetValue(address, out Task follower))
            {
                leader = null;
            }
            else
            {
                leader = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
                _inflightLoads[address] = leader.Task;
            }
        }

        if (leader == null)
        {
            await _inflightLoads[address];
            return _assetCache.TryGetValue(address, out cached)
                ? (cached.Asset as T, cached.ContentFileName, null)
                : (null, content.FileName, RuntimeMessage.LoadFailed(address, "并发加载失败"));
        }

        try
        {
            var (bundle, bundleError) = await _bundleLoader.LoadBundleAsync(content.FileName);
            if (bundleError != null)
                return (null, content.FileName, bundleError);

            T asset = null;
            try
            {
                AssetBundleRequest request = bundle.LoadAssetAsync<T>(entry.AssetPath);
                await AssetBundleRequestToTask(request);
                asset = request.asset as T;
            }
            catch (Exception)
            {
                _bundleLoader.UnloadBundle(content.FileName);
                return (null, content.FileName,
                    RuntimeMessage.AssetExtractionFailed(address, entry.AssetPath, content.FileName));
            }

            if (asset == null)
            {
                _bundleLoader.UnloadBundle(content.FileName);
                return (null, content.FileName,
                    RuntimeMessage.AssetExtractionFailed(address, entry.AssetPath, content.FileName));
            }

            _assetCache[address] = new AssetCacheEntry { Asset = asset, ContentFileName = content.FileName };
            return (asset, content.FileName, null);
        }
        finally
        {
            lock (_inflightLock)
                _inflightLoads.Remove(address);
            leader.TrySetResult(null);
        }
    }

    public (T asset, string contentFileName, RuntimeMessage error) LoadAssetTupleSync<T>(
        ManifestAssetEntry entry, ManifestContentEntry content) where T : UnityEngine.Object
    {
        if (!ValidateSerializedEntry(entry, content, out RuntimeMessage validationError))
            return (null, content?.FileName, validationError);
        if (_assetCache.TryGetValue(entry.Address, out AssetCacheEntry cached))
            return (cached.Asset as T, cached.ContentFileName, null);
        lock (_inflightLock)
        {
            if (_inflightLoads.ContainsKey(entry.Address))
                return (null, content.FileName, RuntimeMessage.LoadInProgress(entry.Address));
        }

        var (bundle, bundleError) = _bundleLoader.LoadBundle(content.FileName);
        if (bundleError != null)
            return (null, content.FileName, bundleError);

        T asset = bundle.LoadAsset<T>(entry.AssetPath);
        if (asset == null)
        {
            _bundleLoader.UnloadBundle(content.FileName);
            return (null, content.FileName,
                RuntimeMessage.AssetExtractionFailed(entry.Address, entry.AssetPath, content.FileName));
        }

        _assetCache[entry.Address] = new AssetCacheEntry { Asset = asset, ContentFileName = content.FileName };
        return (asset, content.FileName, null);
    }

    public void UnloadByAddress(string address)
    {
        if (string.IsNullOrEmpty(address) || !_assetCache.TryGetValue(address, out AssetCacheEntry cached))
            return;
        _assetCache.Remove(address);
        _bundleLoader.UnloadBundle(cached.ContentFileName);
    }

    public void UnloadAllContent()
    {
        _assetCache.Clear();
        _bundleLoader.UnloadAllBundles();
    }

    private static bool ValidateSerializedEntry(ManifestAssetEntry entry, ManifestContentEntry content, out RuntimeMessage error)
    {
        error = null;
        if (entry == null || content == null)
        {
            error = RuntimeMessage.NotFound("ManifestAssetEntry");
            return false;
        }
        if (content.ContentType != AssetContentType.SerializedObject)
        {
            error = RuntimeMessage.InvalidPayloadKind(entry.Address,
                AssetContentType.SerializedObject.ToString(), content.ContentType.ToString());
            return false;
        }
        if (string.IsNullOrEmpty(entry.AssetPath))
        {
            error = RuntimeMessage.LoadFailed(entry.Address, "ManifestAssetEntry 缺少 AssetPath");
            return false;
        }
        return true;
    }

    private static Task AssetBundleRequestToTask(AssetBundleRequest request)
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (request == null)
            completion.SetException(new InvalidOperationException("AssetBundleRequest 为空"));
        else if (request.isDone)
            completion.SetResult(true);
        else
            request.completed += _ => completion.TrySetResult(true);
        return completion.Task;
    }
}
