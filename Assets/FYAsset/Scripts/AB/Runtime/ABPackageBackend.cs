using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// AB 加载后端的统一出口：由 ABPackageManager 按已解析的 EntryId 调用。
/// </summary>
/// <remarks>
/// Address 只用于错误文本，条目定位一律按 EntryId。
/// 所有 API 返回 tuple 错误，不抛加载异常。
/// </remarks>
internal interface IABLoadBackend
{
    /// <summary>加载 SerializedObject 资产，返回 (asset, 内容文件名, error)。</summary>
    Task<(T asset, string contentFileName, RuntimeMessage error)> LoadAssetTupleAsync<T>(
        string address, string entryId) where T : UnityEngine.Object;

    /// <summary>同步版本。</summary>
    (T asset, string contentFileName, RuntimeMessage error) LoadAssetTupleSync<T>(
        string address, string entryId) where T : UnityEngine.Object;

    /// <summary>读取 RawFile 内容字节；不经过 BundleLoader。</summary>
    Task<(byte[] data, RuntimeMessage error)> LoadRawBytesAsync(string address, string entryId);

    /// <summary>Release 回调：最后一个 token 释放后卸载该 EntryId 的资产与内容引用。</summary>
    void UnloadByEntryId(string entryId);

    /// <summary>关闭时清空全部内容引用（Bundle 或 Editor 资产缓存）。</summary>
    void UnloadAllContent();
}

/// <summary>
/// AB 资源加载后端 — 基于 ABManifest + ABBundleLoader 的 I/O 服务。
/// </summary>
/// <remarks>
/// 按 EntryId 定位 ManifestAssetEntry，再经 ContentIndex 找到 ManifestContentEntry。
/// SerializedObject 走 ABBundleLoader（含依赖与引用计数）；RawFile 直接读当前激活包根下的内容文件，
/// 不经过 BundleLoader，也不参与 Bundle 卸载。
/// Asset 级缓存以 EntryId 为键；内容引用计数由 HandleRegistry 的 EntryId 活跃 token 计数驱动。
/// </remarks>
internal sealed class ABPackageBackend : IABLoadBackend
{

    /// <summary>
    /// Asset 缓存条目 — 记录从 Bundle 中加载的资产及其归属关系。
    /// </summary>
    private class AssetCacheEntry
    {
        public UnityEngine.Object Asset;
        public string ContentFileName;
    }

    /// <summary>Asset 缓存：EntryId → CacheEntry</summary>
    private readonly Dictionary<string, AssetCacheEntry> _assetCache = new();

    /// <summary>进行中的异步加载：EntryId → Task。并发去重，避免重复 I/O。</summary>
    private readonly Dictionary<string, Task> _inflightLoads = new();

    private readonly object _inflightLock = new();

    /// <summary>ABManifest 引用，用于 Asset→Content 解析</summary>
    private readonly ABManifest _manifest;

    /// <summary>ABBundleLoader 引用，负责 Bundle 级加载/卸载</summary>
    private readonly ABBundleLoader _bundleLoader;

    /// <summary>
    /// 创建 ABPackageBackend 实例。
    /// </summary>
    /// <param name="manifest">已初始化的 ABManifest</param>
    /// <param name="bundleLoader">已创建的 ABBundleLoader</param>
    public ABPackageBackend(ABManifest manifest, ABBundleLoader bundleLoader)
    {
        _manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
        _bundleLoader = bundleLoader ?? throw new ArgumentNullException(nameof(bundleLoader));
    }

    public async Task<(T asset, string contentFileName, RuntimeMessage error)> LoadAssetTupleAsync<T>(
        string address, string entryId) where T : UnityEngine.Object
    {
        ManifestAssetEntry assetEntry = ResolveAssetEntry(address, entryId, out RuntimeMessage resolveError);
        if (assetEntry == null) return (null, null, resolveError);

        if (_assetCache.TryGetValue(assetEntry.EntryId, out var cached))
            return (cached.Asset as T, cached.ContentFileName, null);

        return await LoadAssetInternalAsync<T>(assetEntry);
    }

    public (T asset, string contentFileName, RuntimeMessage error) LoadAssetTupleSync<T>(
        string address, string entryId) where T : UnityEngine.Object
    {
        ManifestAssetEntry assetEntry = ResolveAssetEntry(address, entryId, out RuntimeMessage resolveError);
        if (assetEntry == null) return (null, null, resolveError);

        if (_assetCache.TryGetValue(assetEntry.EntryId, out var cached))
            return (cached.Asset as T, cached.ContentFileName, null);

        // 同一 Entry 的异步加载仍在进行：同步调用不得阻塞等待，也不得重复获取 Bundle，
        // 否则同一条目会同时存在两份物理加载与两份引用计数。
        if (IsInflight(assetEntry.EntryId))
            return (null, null, RuntimeMessage.LoadInProgress(assetEntry.EntryId));

        return LoadAssetInternalSync<T>(assetEntry);
    }

    public async Task<(byte[] data, RuntimeMessage error)> LoadRawBytesAsync(string address, string entryId)
    {
        ManifestAssetEntry assetEntry = ResolveAssetEntry(address, entryId, out RuntimeMessage resolveError);
        if (assetEntry == null) return (null, resolveError);

        return await LoadRawBytesInternalAsync(assetEntry);
    }

    public void UnloadByEntryId(string entryId)
    {
        if (string.IsNullOrEmpty(entryId)) return;
        ReleaseEntry(entryId);
    }

    public void UnloadAllContent()
    {
        _assetCache.Clear();
        _bundleLoader.UnloadAllBundles();
    }

    /// <summary>
    /// 按 EntryId 精确定位资源条目；Address 只参与错误文本，不做回退查找。
    /// </summary>
    private ManifestAssetEntry ResolveAssetEntry(
        string address,
        string entryId,
        out RuntimeMessage error)
    {
        error = null;
        if (_manifest.TryGetAssetByEntryId(entryId, out ManifestAssetEntry assetEntry))
            return assetEntry;

        error = RuntimeMessage.NotFound(string.Concat("address=", address ?? "", ", entryId=", entryId ?? ""));
        return null;
    }

    /// <summary>
    /// 异步加载资产的内部实现（已确认 assetEntry 有效）。
    /// 并发去重：同一 EntryId 的并发请求等待同一 inflight Task，避免重复 I/O。
    /// </summary>
    private async Task<(T asset, string contentFileName, RuntimeMessage error)> LoadAssetInternalAsync<T>(
        ManifestAssetEntry assetEntry) where T : UnityEngine.Object
    {
        if (assetEntry.ContentType != AssetContentType.SerializedObject)
        {
            return (null, null,
                RuntimeMessage.InvalidPayloadKind(
                    assetEntry.EntryId,
                    AssetContentType.SerializedObject.ToString(),
                    assetEntry.ContentType.ToString()));
        }

        string entryId = assetEntry.EntryId;

        TaskCompletionSource<object> myTcs = null;
        Task inflight;
        lock (_inflightLock)
        {
            if (_inflightLoads.TryGetValue(entryId, out inflight))
            {
                // 已有 inflight，等待之（await 在锁外进行）
            }
            else
            {
                myTcs = new TaskCompletionSource<object>();
                _inflightLoads[entryId] = myTcs.Task;
            }
        }

        if (inflight != null)
        {
            await inflight;
            if (_assetCache.TryGetValue(entryId, out var existing))
                return (existing.Asset as T, existing.ContentFileName, null);
            // inflight 完成但缓存中没有 -> 之前的加载失败，本次作为新请求继续
        }

        try
        {
            var contentEntry = _manifest.GetContentForAsset(assetEntry);
            if (contentEntry == null)
            {
                return (null, null,
                    RuntimeMessage.BundleNotFound(
                        string.Concat("(asset: ", assetEntry.Address, ", EntryId=", assetEntry.EntryId, ")")));
            }

            string contentFileName = contentEntry.FileName;

            var (bundle, bundleError) = await _bundleLoader.LoadBundleAsync(contentFileName);
            if (bundleError != null)
            {
                return (null, contentFileName, bundleError);
            }

            T asset = null;
            try
            {
                var request = bundle.LoadAssetAsync<T>(assetEntry.SourcePath);
                await AssetBundleRequestToTask(request);
                asset = request.asset as T;
            }
            catch (Exception ex)
            {
                Debug.LogWarning(
                    $"[ABPackageBackend] 资源提取异常: EntryId={assetEntry.EntryId}, Bundle={contentFileName}, Path={assetEntry.SourcePath}, Error={ex.Message}");
                _bundleLoader.UnloadBundle(contentFileName);
                return (null, contentFileName,
                    RuntimeMessage.AssetExtractionFailed(assetEntry.EntryId, assetEntry.SourcePath, contentFileName));
            }

            if (asset == null)
            {
                _bundleLoader.UnloadBundle(contentFileName);
                return (null, contentFileName,
                    RuntimeMessage.AssetExtractionFailed(assetEntry.EntryId, assetEntry.SourcePath, contentFileName));
            }

            AddToAssetCache(assetEntry, asset, contentFileName);
            return (asset, contentFileName, null);
        }
        finally
        {
            if (myTcs != null)
            {
                lock (_inflightLock)
                {
                    _inflightLoads.Remove(entryId);
                }

                myTcs.TrySetResult(null);
            }
        }
    }

    /// <summary>
    /// 同步加载资产的内部实现（已确认 assetEntry 有效）。
    /// </summary>
    /// <summary>该 Entry 是否已有异步加载在进行中（同步路径据此快速失败，不阻塞等待）。</summary>
    private bool IsInflight(string entryId)
    {
        lock (_inflightLock)
        {
            return _inflightLoads.ContainsKey(entryId);
        }
    }

    private (T asset, string contentFileName, RuntimeMessage error) LoadAssetInternalSync<T>(
        ManifestAssetEntry assetEntry) where T : UnityEngine.Object
    {
        if (assetEntry.ContentType != AssetContentType.SerializedObject)
        {
            return (null, null,
                RuntimeMessage.InvalidPayloadKind(
                    assetEntry.EntryId,
                    AssetContentType.SerializedObject.ToString(),
                    assetEntry.ContentType.ToString()));
        }

        var contentEntry = _manifest.GetContentForAsset(assetEntry);
        if (contentEntry == null)
        {
            return (null, null,
                RuntimeMessage.BundleNotFound(
                    string.Concat("(asset: ", assetEntry.Address, ", EntryId=", assetEntry.EntryId, ")")));
        }

        string contentFileName = contentEntry.FileName;

        var (bundle, bundleError) = _bundleLoader.LoadBundle(contentFileName);
        if (bundleError != null)
        {
            return (null, contentFileName, bundleError);
        }

        T asset = bundle.LoadAsset<T>(assetEntry.SourcePath);
        if (asset == null)
        {
            _bundleLoader.UnloadBundle(contentFileName);
            return (null, contentFileName,
                RuntimeMessage.AssetExtractionFailed(assetEntry.EntryId, assetEntry.SourcePath, contentFileName));
        }

        AddToAssetCache(assetEntry, asset, contentFileName);
        return (asset, contentFileName, null);
    }

    /// <summary>
    /// RawFile 读取：FileName 来自 ManifestContentEntry，路径固定在当前激活包根下。
    /// Android 等平台的 StreamingAssets 是 jar: URI，FileHelper 在该平台改用异步 UWR 读取。
    /// </summary>
    private async Task<(byte[] data, RuntimeMessage error)> LoadRawBytesInternalAsync(ManifestAssetEntry assetEntry)
    {
        if (assetEntry.ContentType != AssetContentType.RawFile)
        {
            return (null, RuntimeMessage.InvalidPayloadKind(
                assetEntry.EntryId,
                AssetContentType.RawFile.ToString(),
                assetEntry.ContentType.ToString()));
        }

        var contentEntry = _manifest.GetContentForAsset(assetEntry);
        if (contentEntry == null)
        {
            return (null,
                RuntimeMessage.BundleNotFound(
                    string.Concat("(asset: ", assetEntry.Address, ", EntryId=", assetEntry.EntryId, ")")));
        }

        string path = BuildContentPath(contentEntry.FileName);
        if (string.IsNullOrEmpty(path))
        {
            return (null, RuntimeMessage.BundleNotFound(contentEntry.FileName));
        }

        return await TryReadRawBytesAsync(path, assetEntry.EntryId);
    }

    private static async Task<(byte[] data, RuntimeMessage error)> TryReadRawBytesAsync(string path, string entryId)
    {
        try
        {
            return (await FileHelper.ReadAllBytesAsync(path), null);
        }
        catch (System.IO.FileNotFoundException)
        {
            return (null, RuntimeMessage.BundleNotFound(path));
        }
        catch (Exception ex)
        {
            return (null, RuntimeMessage.LoadFailed(entryId, ex.Message));
        }
    }

    private static string BuildContentPath(string contentFileName)
    {
        string root = RuntimePathManager.ActivePackageRoot;
        if (string.IsNullOrEmpty(root) || string.IsNullOrEmpty(contentFileName))
            return null;

        return FYAssetPathUtility.JoinFilePath(root, FYAssetSettings.BUNDLES_DIRECTORY_NAME, contentFileName);
    }

    /// <summary>
    /// 将资产加入 EntryId 缓存。
    /// </summary>
    private void AddToAssetCache(ManifestAssetEntry assetEntry, UnityEngine.Object asset, string contentFileName)
    {
        _assetCache[assetEntry.EntryId] = new AssetCacheEntry
        {
            Asset = asset,
            ContentFileName = contentFileName
        };
    }

    /// <summary>
    /// 按 EntryId 移除资产缓存并联动 Bundle 卸载。调用方负责确保无活跃 Handle 引用。
    /// </summary>
    private void ReleaseEntry(string entryId)
    {
        if (!_assetCache.TryGetValue(entryId, out var entry)) return;

        _assetCache.Remove(entryId);

        _bundleLoader.UnloadBundle(entry.ContentFileName);
    }

    /// <summary>
    /// 将 AssetBundleRequest 转为 Task 以支持 async/await。
    /// </summary>
    private static Task AssetBundleRequestToTask(AssetBundleRequest request)
    {
        var tcs = new TaskCompletionSource<bool>();
        if (request.isDone)
        {
            tcs.SetResult(true);
        }
        else
        {
            request.completed += _ => tcs.SetResult(true);
        }

        return tcs.Task;
    }
}
