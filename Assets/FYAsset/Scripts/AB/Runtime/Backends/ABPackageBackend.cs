using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// AB 资源加载后端 — 基于自研 AssetBundle 方案的 concrete I/O service。
///
/// 设计说明：
/// - 由 ABPackageManager 提供已消歧的 EntryId，零 Addressables 依赖
/// - 通过 ABManifest 将 EntryId 解析到 ManifestAssetEntry → BundleEntry
/// - 委托 ABBundleLoader 处理 Bundle 文件加载/卸载（含依赖和引用计数）
/// - 使用 AssetBundle.LoadAsset/LoadAssetAsync 从 Bundle 中提取资产
/// - 维护 Asset 级缓存；引用计数由 HandleRegistry._entryActiveCounts 统一管理
/// - concrete API 返回 tuple errors，不抛加载异常
///
/// 引用计数流程：
/// Load → HandleRegistry.Alloc → _entryActiveCounts[entryId]++
/// Release → HandleRegistry.Release → _entryActiveCounts[entryId]-- → 归零时回调 ReleaseEntry
///
/// 由 ABPackageManager 持有本服务，仅转发已消歧的 Entry。
/// </summary>
internal interface IABLoadBackend
{
    Task<(T asset, RuntimeMessage error)> LoadAssetAsync<T>(string key, string entryId)
        where T : UnityEngine.Object;
    (T asset, RuntimeMessage error) LoadAssetSync<T>(string key, string entryId)
        where T : UnityEngine.Object;
    Task<(byte[] data, RuntimeMessage error)> LoadRawBytesAsync(string key, string entryId);
    (byte[] data, RuntimeMessage error) LoadRawBytesSync(string key, string entryId);
    Task<(T asset, string bundleName, RuntimeMessage error)> LoadAssetTupleAsync<T>(
        string key, string entryId) where T : UnityEngine.Object;
    (T asset, string bundleName, RuntimeMessage error) LoadAssetTupleSync<T>(
        string key, string entryId) where T : UnityEngine.Object;
    void UnloadByEntryId(string entryId);
}

internal sealed class ABPackageBackend : IABLoadBackend
{
    #region 内部数据结构

    /// <summary>
    /// Asset 缓存条目 — 记录从 Bundle 中加载的资产及其归属关系。
    /// </summary>
    private class AssetCacheEntry
    {
        public UnityEngine.Object Asset;
        public string BundleName;
    }

    #endregion

    #region 字段

    /// <summary>Asset 缓存：EntryId → CacheEntry</summary>
    private readonly Dictionary<string, AssetCacheEntry> _assetCache = new();

    /// <summary>进行中的异步加载：EntryId → Task。并发去重，避免重复 I/O。</summary>
    private readonly Dictionary<string, Task> _inflightLoads = new();

    private readonly object _inflightLock = new();

    /// <summary>ABManifest 引用，用于 Asset→Bundle 解析</summary>
    private readonly ABManifest _manifest;

    /// <summary>ABBundleLoader 引用，负责 Bundle 级加载/卸载</summary>
    private readonly ABBundleLoader _bundleLoader;

    #endregion

    #region 构造

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

    #endregion

    #region EntryId Load API

    public async Task<(T asset, RuntimeMessage error)> LoadAssetAsync<T>(string key, string entryId)
        where T : UnityEngine.Object
    {
        if (string.IsNullOrEmpty(key))
            return (null, RuntimeMessage.Error(RuntimeErrorCodes.InvalidArgument, "LoadAssetAsync: key 为 null 或空"));

        var assetEntry = ResolveAssetEntry(key, entryId);
        if (assetEntry == null)
            return (null, RuntimeMessage.NotFound(string.Concat("key=", key, ", entryId=", entryId ?? "")));

        if (_assetCache.TryGetValue(assetEntry.EntryId, out var cached))
            return (cached.Asset as T, null);

        var (asset, _, error) = await LoadAssetInternalAsync<T>(assetEntry);
        return (asset, error);
    }

    public (T asset, RuntimeMessage error) LoadAssetSync<T>(string key, string entryId)
        where T : UnityEngine.Object
    {
        if (string.IsNullOrEmpty(key))
            return (null, RuntimeMessage.Error(RuntimeErrorCodes.InvalidArgument, "LoadAssetSync: key 为 null 或空"));

        var assetEntry = ResolveAssetEntry(key, entryId);
        if (assetEntry == null)
            return (null, RuntimeMessage.NotFound(string.Concat("key=", key, ", entryId=", entryId ?? "")));

        if (_assetCache.TryGetValue(assetEntry.EntryId, out var cached))
            return (cached.Asset as T, null);

        var (asset, _, error) = LoadAssetInternalSync<T>(assetEntry);
        return (asset, error);
    }

    public async Task<(byte[] data, RuntimeMessage error)> LoadRawBytesAsync(string key, string entryId)
    {
        if (string.IsNullOrEmpty(key))
            return (null, RuntimeMessage.Error(RuntimeErrorCodes.InvalidArgument, "LoadRawBytesAsync: key 为 null 或空"));

        var assetEntry = ResolveAssetEntry(key, entryId);
        if (assetEntry == null)
            return (null, RuntimeMessage.NotFound(string.Concat("key=", key, ", entryId=", entryId ?? "")));

        return await LoadRawBytesInternalAsync(assetEntry);
    }

    public (byte[] data, RuntimeMessage error) LoadRawBytesSync(string key, string entryId)
    {
        if (string.IsNullOrEmpty(key))
            return (null, RuntimeMessage.Error(RuntimeErrorCodes.InvalidArgument, "LoadRawBytesSync: key 为 null 或空"));

        var assetEntry = ResolveAssetEntry(key, entryId);
        if (assetEntry == null)
            return (null, RuntimeMessage.NotFound(string.Concat("key=", key, ", entryId=", entryId ?? "")));

        return LoadRawBytesInternalSync(assetEntry);
    }

    #endregion

    #region Lifetime

    public void UnloadByEntryId(string entryId)
    {
        if (string.IsNullOrEmpty(entryId)) return;
        ReleaseEntry(entryId);
    }

    #endregion

    #region 内部方法

    /// <summary>按已解析的 EntryId 精确定位资源条目，不做 address 回退。</summary>
    private ManifestAssetEntry ResolveAssetEntry(string address, string entryId)
    {
        if (string.IsNullOrEmpty(entryId)) return null;
        if (!_manifest.TryGetAssetByEntryId(entryId, out var assetEntry)) return null;
        return string.Equals(assetEntry.Address, address, StringComparison.Ordinal) ? assetEntry : null;
    }

    #endregion

    #region 内部元组 API（供 ABPackageManager Handle 构建路径使用）

    /// <summary>
    /// 异步加载资产，返回 (asset, bundleName, error) 元组。
    /// ABPackageManager 通过此方法获取 bundleName 以分配 HandleRegistry 槽位。
    /// </summary>
    public async Task<(T asset, string bundleName, RuntimeMessage error)> LoadAssetTupleAsync<T>(
        string key, string entryId) where T : UnityEngine.Object
    {
        var assetEntry = ResolveAssetEntry(key, entryId);

        if (assetEntry == null)
        {
            return (null, null, RuntimeMessage.NotFound(
                string.Concat("key=", key, ", entryId=", entryId ?? "")));
        }

        if (_assetCache.TryGetValue(assetEntry.EntryId, out var cached))
        {
            return (cached.Asset as T, cached.BundleName, null);
        }

        return await LoadAssetInternalAsync<T>(assetEntry);
    }

    /// <summary>
    /// 同步加载资产，返回 (asset, bundleName, error) 元组。
    /// ABPackageManager 通过此方法获取 bundleName 以分配 HandleRegistry 槽位。
    /// </summary>
    public (T asset, string bundleName, RuntimeMessage error) LoadAssetTupleSync<T>(
        string key, string entryId) where T : UnityEngine.Object
    {
        var assetEntry = ResolveAssetEntry(key, entryId);

        if (assetEntry == null)
        {
            return (null, null, RuntimeMessage.NotFound(
                string.Concat("key=", key, ", entryId=", entryId ?? "")));
        }

        if (_assetCache.TryGetValue(assetEntry.EntryId, out var cached))
        {
            return (cached.Asset as T, cached.BundleName, null);
        }

        return LoadAssetInternalSync<T>(assetEntry);
    }

    #endregion

    #region 内部实现（元组 API，不抛异常）

    /// <summary>
    /// 异步加载资产的内部实现（已确认 assetEntry 有效）。
    /// 返回 (asset, bundleName, error) 元组 — 内部 API，不抛异常。
    /// 并发去重：同一 EntryId 的并发请求等待同一 inflight Task，避免重复 I/O。
    /// </summary>
    private async Task<(T asset, string bundleName, RuntimeMessage error)> LoadAssetInternalAsync<T>(
        ManifestAssetEntry assetEntry) where T : UnityEngine.Object
    {
        if (assetEntry.PayloadKind == EPayloadKind.RawFile)
        {
            return (null, null,
                RuntimeMessage.InvalidPayloadKind(
                    assetEntry.EntryId,
                    EPayloadKind.Serialized.ToString(),
                    assetEntry.PayloadKind.ToString()));
        }

        string entryId = assetEntry.EntryId;

        // 并发去重：如果已有进行中的加载，等待其完成后从缓存读取
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
                return (existing.Asset as T, existing.BundleName, null);
            // inflight 完成但缓存中没有 -> 之前的加载失败，本次作为新请求继续
        }

        try
        {
            // 获取 Bundle 信息
            var bundleEntry = _manifest.GetBundleForAsset(assetEntry);
            if (bundleEntry == null)
            {
                return (null, null,
                    RuntimeMessage.BundleNotFound(
                        string.Concat("(asset: ", assetEntry.Address, ", EntryId=", assetEntry.EntryId, ")")));
            }

            string bundleName = bundleEntry.BundleName;

            // 加载 Bundle（含依赖）
            var (bundle, bundleError) = await _bundleLoader.LoadBundleAsync(bundleName);
            if (bundleError != null)
            {
                return (null, bundleName, bundleError);
            }

            // 从 Bundle 中异步提取资产
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
                    $"[ABPackageBackend] 资源提取异常: EntryId={assetEntry.EntryId}, Bundle={bundleName}, Path={assetEntry.SourcePath}, Error={ex.Message}");
                _bundleLoader.UnloadBundle(bundleName);
                return (null, bundleName,
                    RuntimeMessage.AssetExtractionFailed(assetEntry.EntryId, assetEntry.SourcePath, bundleName));
            }

            if (asset == null)
            {
                _bundleLoader.UnloadBundle(bundleName);
                return (null, bundleName,
                    RuntimeMessage.AssetExtractionFailed(assetEntry.EntryId, assetEntry.SourcePath, bundleName));
            }

            // 加入 Asset 缓存
            AddToAssetCache(assetEntry, asset, bundleName);
            return (asset, bundleName, null);
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
    /// 返回 (asset, bundleName, error) 元组 — 内部 API，不抛异常。
    /// </summary>
    private (T asset, string bundleName, RuntimeMessage error) LoadAssetInternalSync<T>(
        ManifestAssetEntry assetEntry) where T : UnityEngine.Object
    {
        if (assetEntry.PayloadKind == EPayloadKind.RawFile)
        {
            return (null, null,
                RuntimeMessage.InvalidPayloadKind(
                    assetEntry.EntryId,
                    EPayloadKind.Serialized.ToString(),
                    assetEntry.PayloadKind.ToString()));
        }

        // 获取 Bundle 信息
        var bundleEntry = _manifest.GetBundleForAsset(assetEntry);
        if (bundleEntry == null)
        {
            return (null, null,
                RuntimeMessage.BundleNotFound(
                    string.Concat("(asset: ", assetEntry.Address, ", EntryId=", assetEntry.EntryId, ")")));
        }

        string bundleName = bundleEntry.BundleName;

        // 加载 Bundle（含依赖）
        var (bundle, bundleError) = _bundleLoader.LoadBundle(bundleName);
        if (bundleError != null)
        {
            return (null, bundleName, bundleError);
        }

        // 从 Bundle 中同步提取资产
        T asset = bundle.LoadAsset<T>(assetEntry.SourcePath);
        if (asset == null)
        {
            _bundleLoader.UnloadBundle(bundleName);
            return (null, bundleName,
                RuntimeMessage.AssetExtractionFailed(assetEntry.EntryId, assetEntry.SourcePath, bundleName));
        }

        // 加入 Asset 缓存
        AddToAssetCache(assetEntry, asset, bundleName);
        return (asset, bundleName, null);
    }

    private async Task<(byte[] data, RuntimeMessage error)> LoadRawBytesInternalAsync(ManifestAssetEntry assetEntry)
    {
        if (assetEntry.PayloadKind != EPayloadKind.RawFile)
            return (null, RuntimeMessage.InvalidPayloadKind(
                assetEntry.EntryId,
                EPayloadKind.RawFile.ToString(),
                assetEntry.PayloadKind.ToString()));

        var bundleEntry = _manifest.GetBundleForAsset(assetEntry);
        if (bundleEntry == null)
        {
            return (null,
                RuntimeMessage.BundleNotFound(
                    string.Concat("(asset: ", assetEntry.Address, ", EntryId=", assetEntry.EntryId, ")")));
        }

        string bundleName = bundleEntry.BundleName;
        string primaryPath = BuildBundlePath(RuntimePathManager.CurrentGUIDRoot, bundleName);
        if (FileHelper.Exists(primaryPath))
            return await TryReadRawBytesAsync(primaryPath, assetEntry.EntryId);

        string fallbackPath = BuildBundlePath(Application.streamingAssetsPath, bundleName);
        return await TryReadRawBytesAsync(fallbackPath, assetEntry.EntryId);
    }

    private (byte[] data, RuntimeMessage error) LoadRawBytesInternalSync(ManifestAssetEntry assetEntry)
    {
        if (assetEntry.PayloadKind != EPayloadKind.RawFile)
            return (null, RuntimeMessage.InvalidPayloadKind(
                assetEntry.EntryId,
                EPayloadKind.RawFile.ToString(),
                assetEntry.PayloadKind.ToString()));

        var bundleEntry = _manifest.GetBundleForAsset(assetEntry);
        if (bundleEntry == null)
        {
            return (null,
                RuntimeMessage.BundleNotFound(
                    string.Concat("(asset: ", assetEntry.Address, ", EntryId=", assetEntry.EntryId, ")")));
        }

        string bundleName = bundleEntry.BundleName;
        string primaryPath = BuildBundlePath(RuntimePathManager.CurrentGUIDRoot, bundleName);
        if (FileHelper.Exists(primaryPath))
            return TryReadRawBytesSync(primaryPath, assetEntry.EntryId);

        string fallbackPath = BuildBundlePath(Application.streamingAssetsPath, bundleName);
        if (FileHelper.Exists(fallbackPath))
            return TryReadRawBytesSync(fallbackPath, assetEntry.EntryId);

        if (IsNonFileSystemPath(fallbackPath))
        {
            return (null,
                RuntimeMessage.UnsupportedOperation(
                    "ABPackageBackend.LoadRawBytesSync",
                    "RawFile 同步读取只支持真实文件系统路径，请使用异步 API 读取 StreamingAssets URI"));
        }

        return (null, RuntimeMessage.BundleNotFound(bundleName));
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

    private static (byte[] data, RuntimeMessage error) TryReadRawBytesSync(string path, string entryId)
    {
        try
        {
            return (FileHelper.ReadAllBytes(path), null);
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

    private static string BuildBundlePath(string root, string bundleName)
    {
        return FYAssetPathUtility.JoinFilePath(root, FYAssetSettings.BUNDLES_DIRECTORY_NAME, bundleName);
    }

    private static bool IsNonFileSystemPath(string path)
    {
        if (string.IsNullOrEmpty(path))
            return false;

        return path.StartsWith("jar:", StringComparison.OrdinalIgnoreCase) ||
               path.IndexOf("://", StringComparison.Ordinal) >= 0;
    }

    /// <summary>
    /// 将资产加入 EntryId 缓存。
    /// </summary>
    private void AddToAssetCache(ManifestAssetEntry assetEntry, UnityEngine.Object asset, string bundleName)
    {
        _assetCache[assetEntry.EntryId] = new AssetCacheEntry
        {
            Asset = asset,
            BundleName = bundleName
        };
    }

    /// <summary>
    /// 按 EntryId 移除资产缓存并联动 Bundle 卸载。调用方负责确保无活跃 Handle 引用。
    /// </summary>
    private void ReleaseEntry(string entryId)
    {
        if (string.IsNullOrEmpty(entryId)) return;
        if (!_assetCache.TryGetValue(entryId, out var entry)) return;

        _assetCache.Remove(entryId);

        _bundleLoader.UnloadBundle(entry.BundleName);
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

    #endregion
}
