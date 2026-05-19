using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// AB 资源加载后端 — 基于自研 AssetBundle 方案的 IPackageBackend 实现。
///
/// 设计说明：
/// - 直接替代 AddressablesBackend，外部行为一致，零 Addressables 依赖
/// - 通过 ABManifest 将 address/entryId 解析到 ManifestAssetEntry → BundleEntry
/// - 委托 ABBundleLoader 处理 Bundle 文件加载/卸载（含依赖和引用计数）
/// - 使用 AssetBundle.LoadAsset/LoadAssetAsync 从 Bundle 中提取资产
/// - 维护 Asset 级缓存；引用计数由 HandleRegistry._entryActiveCounts 统一管理
/// - 支持扩展接口：LoadAssetAsync(key, entryId) / LoadAssetSync(key, entryId) / UnloadByEntryId
/// - 公开 API 统一返回 (T, RuntimeMessage) 元组，不抛异常
///
/// 引用计数流程：
/// Load → HandleRegistry.Alloc → _entryActiveCounts[entryId]++
/// Release → HandleRegistry.Release → _entryActiveCounts[entryId]-- → 归零时回调 ReleaseEntry
///
/// 使用方式：
/// 1. AssetPackageManager.Initialize() 创建 ABBundleLoader + ABPackageBackend
/// 2. AssetPackageManager.SetBackend(abBackend) 替换 AddressablesBackend
/// 3. 所有 LoadAssetAsync/Sync/Unload 调用自动路由到此 Backend
/// </summary>
public class ABPackageBackend : IPackageBackend
{
    #region 内部数据结构

    /// <summary>
    /// Asset 缓存条目 — 记录从 Bundle 中加载的资产及其归属关系。
    /// </summary>
    private class AssetCacheEntry
    {
        public UnityEngine.Object Asset;
        public string BundleName;
        public string EntryId;
        public string Address;
    }

    #endregion

    #region 字段

    /// <summary>Asset 缓存：EntryId → CacheEntry</summary>
    private readonly Dictionary<string, AssetCacheEntry> _assetCache = new();

    /// <summary>address → 已加载的 EntryId 列表（支持 AA 风格按 key 卸载）</summary>
    private readonly Dictionary<string, HashSet<string>> _addressToEntryIds = new();

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

    #region IPackageBackend · 初始化

    /// <summary>
    /// 初始化（无操作）。实际初始化在构造函数中完成。
    /// </summary>
    public Task InitializeAsync()
    {
        return Task.CompletedTask;
    }

    #endregion

    #region IPackageBackend · 异步加载

    /// <summary>
    /// 异步加载资产（按 address/key）。
    /// 链路：key -> ABManifest 解析 -> ABBundleLoader 加载 Bundle -> bundle.LoadAssetAsync 提取
    /// </summary>
    public async Task<(T asset, RuntimeMessage error)> LoadAssetAsync<T>(string key) where T : UnityEngine.Object
    {
        if (string.IsNullOrEmpty(key))
            return (null, RuntimeMessage.Error(RuntimeErrorCodes.InvalidArgument, "LoadAssetAsync: key 为 null 或空"));

        var assetEntry = ResolveAssetEntryByAddress(key);
        if (assetEntry == null)
            return (null, RuntimeMessage.NotFound(key));

        if (_assetCache.TryGetValue(assetEntry.EntryId, out var cached))
            return (cached.Asset as T, null);

        var (asset, _, error) = await LoadAssetInternalAsync<T>(assetEntry);
        return (asset, error);
    }

    /// <summary>
    /// 异步加载资产（扩展：附带 EntryId）。
    /// 优先按 EntryId 精确查找，回退到 address 查找。
    /// </summary>
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

    #endregion

    #region IPackageBackend · 同步加载

    /// <summary>
    /// 同步加载资产（按 address/key）。
    /// </summary>
    public (T asset, RuntimeMessage error) LoadAssetSync<T>(string key) where T : UnityEngine.Object
    {
        if (string.IsNullOrEmpty(key))
            return (null, RuntimeMessage.Error(RuntimeErrorCodes.InvalidArgument, "LoadAssetSync: key 为 null 或空"));

        var assetEntry = ResolveAssetEntryByAddress(key);
        if (assetEntry == null)
            return (null, RuntimeMessage.NotFound(key));

        if (_assetCache.TryGetValue(assetEntry.EntryId, out var cached))
            return (cached.Asset as T, null);

        var (asset, _, error) = LoadAssetInternalSync<T>(assetEntry);
        return (asset, error);
    }

    /// <summary>
    /// 同步加载资产（扩展：附带 EntryId）。
    /// </summary>
    public (T asset, RuntimeMessage error) LoadAssetSync<T>(string key, string entryId) where T : UnityEngine.Object
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

    #endregion

    #region IPackageBackend · 卸载

    /// <summary>
    /// 按 address/key 卸载资产。释放该地址下所有已加载条目（确定性行为）。
    /// 存在重复 Address 时全部释放，不会非确定性地只取第一个。
    /// 注：Handle 路径的调用方应先通过 HandleRegistry 确保所有 Handle 已释放。
    /// </summary>
    public void UnloadAsset(string key)
    {
        if (string.IsNullOrEmpty(key)) return;
        if (!_addressToEntryIds.TryGetValue(key, out var entryIds) || entryIds.Count == 0) return;

        // 收集所有 entryId 后逐个释放，避免在迭代中修改集合
        var ids = new List<string>(entryIds);
        foreach (var entryId in ids)
        {
            ReleaseEntry(entryId);
        }
    }

    /// <summary>
    /// 按 EntryId 卸载资产。直接移除缓存并联动 Bundle 卸载。
    /// 由 HandleRegistry 回调触发；仅在 _entryActiveCounts 归零时调用。
    /// </summary>
    public void UnloadByEntryId(string entryId)
    {
        if (string.IsNullOrEmpty(entryId)) return;
        ReleaseEntry(entryId);
    }

    #endregion

    #region IPackageBackend · 查询

    /// <summary>
    /// 检查资产是否已加载并缓存。
    /// </summary>
    public bool ContainsKey(string key)
    {
        if (string.IsNullOrEmpty(key)) return false;
        if (!_addressToEntryIds.TryGetValue(key, out var entryIds) || entryIds.Count == 0) return false;

        foreach (var entryId in entryIds)
        {
            if (_assetCache.ContainsKey(entryId))
                return true;
        }

        return false;
    }

    #endregion

    #region 内部方法

    /// <summary>
    /// 按 address 从 ABManifest 解析 ManifestAssetEntry。
    /// V1 策略：取第一个匹配项（类型消歧由上层 AssetResolver 处理）。
    /// </summary>
    private ManifestAssetEntry ResolveAssetEntryByAddress(string address)
    {
        if (_manifest.TryGetAssetsByAddress(address, out var entries) && entries.Count > 0)
        {
            return entries[0];
        }

        return null;
    }

    /// <summary>
    /// 优先按 EntryId 精确定位资源条目，回退到 address 首个匹配。
    /// </summary>
    private ManifestAssetEntry ResolveAssetEntry(string address, string entryId)
    {
        ManifestAssetEntry assetEntry = null;
        if (!string.IsNullOrEmpty(entryId))
        {
            _manifest.TryGetAssetByEntryId(entryId, out assetEntry);
        }

        if (assetEntry == null)
        {
            assetEntry = ResolveAssetEntryByAddress(address);
        }

        return assetEntry;
    }

    #endregion

    #region 内部元组 API（供 AssetPackageManager Handle 构建路径使用）

    /// <summary>
    /// 异步加载资产，返回 (asset, bundleName, error) 元组。
    /// AssetPackageManager 通过此方法获取 bundleName 以分配 HandleRegistry 槽位。
    /// </summary>
    internal async Task<(T asset, string bundleName, RuntimeMessage error)> LoadAssetTupleAsync<T>(
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
    /// AssetPackageManager 通过此方法获取 bundleName 以分配 HandleRegistry 槽位。
    /// </summary>
    internal (T asset, string bundleName, RuntimeMessage error) LoadAssetTupleSync<T>(
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

    /// <summary>
    /// 将资产加入缓存并建立 EntryId 反向映射。
    /// </summary>
    private void AddToAssetCache(ManifestAssetEntry assetEntry, UnityEngine.Object asset, string bundleName)
    {
        _assetCache[assetEntry.EntryId] = new AssetCacheEntry
        {
            Asset = asset,
            BundleName = bundleName,
            EntryId = assetEntry.EntryId,
            Address = assetEntry.Address
        };

        if (string.IsNullOrEmpty(assetEntry.Address))
            return;

        if (!_addressToEntryIds.TryGetValue(assetEntry.Address, out var entryIds))
        {
            entryIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _addressToEntryIds[assetEntry.Address] = entryIds;
        }

        entryIds.Add(assetEntry.EntryId);
    }

    /// <summary>
    /// 按 EntryId 移除资产缓存并联动 Bundle 卸载。调用方负责确保无活跃 Handle 引用。
    /// </summary>
    private void ReleaseEntry(string entryId)
    {
        if (string.IsNullOrEmpty(entryId)) return;
        if (!_assetCache.TryGetValue(entryId, out var entry)) return;

        _assetCache.Remove(entryId);

        // 直接用缓存条目里的 Address，无需回查 Manifest
        // 注：address 可重复，_addressToEntryIds[address] 是 HashSet，只移除本 entryId
        if (!string.IsNullOrEmpty(entry.Address))
        {
            if (_addressToEntryIds.TryGetValue(entry.Address, out var entryIds))
            {
                entryIds.Remove(entryId);
                if (entryIds.Count == 0)
                {
                    _addressToEntryIds.Remove(entry.Address);
                }
            }
        }

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
