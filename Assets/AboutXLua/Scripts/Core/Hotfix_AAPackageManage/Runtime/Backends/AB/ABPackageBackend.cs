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
/// - 维护 Asset 级缓存与引用计数，卸载时联动 ABBundleLoader 的 Bundle 引用计数
/// - 支持扩展接口：LoadAssetAsync(key, entryId) / LoadAssetSync(key, entryId) / UnloadByEntryId
///
/// 引用计数流程：
/// Load → AssetCache[key].RefCount++ → BundleLoader.LoadBundle(bundleName).RefCount++
/// Unload → AssetCache[key].RefCount-- → 归零时移除 → BundleLoader.UnloadBundle(bundleName).RefCount--
///
/// 使用方式：
/// 1. AAPackageManager.Initialize() 创建 ABBundleLoader + ABPackageBackend
/// 2. AAPackageManager.SetBackend(abBackend) 替换 AddressablesBackend
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
        /// <summary>已加载的资产实例</summary>
        public UnityEngine.Object Asset;

        /// <summary>该资产所属的 Bundle 名称（卸载时用于联动 Bundle 引用计数）</summary>
        public string BundleName;

        /// <summary>资产的 EntryId（用于 UnloadByEntryId 反查）</summary>
        public string EntryId;

        /// <summary>引用计数</summary>
        public int RefCount;
    }

    #endregion

    #region 字段

    /// <summary>Asset 缓存：address(key) → CacheEntry</summary>
    private readonly Dictionary<string, AssetCacheEntry> _assetCache = new();

    /// <summary>EntryId → address 反向映射（支持 UnloadByEntryId）</summary>
    private readonly Dictionary<string, string> _entryIdToAddress = new();

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
    /// 链路：key → ABManifest 解析 → ABBundleLoader 加载 Bundle → bundle.LoadAssetAsync 提取
    /// </summary>
    public async Task<T> LoadAssetAsync<T>(string key) where T : UnityEngine.Object
    {
        if (string.IsNullOrEmpty(key))
            throw new ArgumentException("[ABPackageBackend] LoadAssetAsync: key is null or empty");

        // 缓存命中
        if (_assetCache.TryGetValue(key, out var cached))
        {
            cached.RefCount++;
            return cached.Asset as T;
        }

        // 解析 Asset → Bundle
        var assetEntry = ResolveAssetEntryByAddress(key);
        if (assetEntry == null)
            throw new Exception(string.Concat("[ABPackageBackend] Asset not found in manifest: ", key));

        var (asset, bundleName, error) = await LoadAssetInternalAsync<T>(key, assetEntry);
        if (error != null)
            throw new Exception(string.Concat("[ABPackageBackend] ", error.ToString()));

        return asset;
    }

    /// <summary>
    /// 异步加载资产（扩展：附带 EntryId）。
    /// 优先按 EntryId 精确查找，回退到 address 查找。
    /// </summary>
    public async Task<T> LoadAssetAsync<T>(string key, string entryId) where T : UnityEngine.Object
    {
        if (string.IsNullOrEmpty(key))
            throw new ArgumentException("[ABPackageBackend] LoadAssetAsync: key is null or empty");

        // 缓存命中
        if (_assetCache.TryGetValue(key, out var cached))
        {
            cached.RefCount++;
            return cached.Asset as T;
        }

        // 优先按 EntryId 精确查找
        ManifestAssetEntry assetEntry = null;
        if (!string.IsNullOrEmpty(entryId))
        {
            _manifest.TryGetAssetByEntryId(entryId, out assetEntry);
        }

        // 回退到 address 查找
        if (assetEntry == null)
        {
            assetEntry = ResolveAssetEntryByAddress(key);
        }

        if (assetEntry == null)
            throw new Exception(string.Concat("[ABPackageBackend] Asset not found: key=", key, ", entryId=", entryId));

        var (asset, bundleName, error) = await LoadAssetInternalAsync<T>(key, assetEntry);
        if (error != null)
            throw new Exception(string.Concat("[ABPackageBackend] ", error.ToString()));

        return asset;
    }

    #endregion

    #region IPackageBackend · 同步加载

    /// <summary>
    /// 同步加载资产（按 address/key）。
    /// </summary>
    public T LoadAssetSync<T>(string key) where T : UnityEngine.Object
    {
        if (string.IsNullOrEmpty(key))
        {
            Debug.LogError("[ABPackageBackend] LoadAssetSync: key is null or empty");
            return null;
        }

        // 缓存命中
        if (_assetCache.TryGetValue(key, out var cached))
        {
            cached.RefCount++;
            return cached.Asset as T;
        }

        // 解析 Asset → Bundle
        var assetEntry = ResolveAssetEntryByAddress(key);
        if (assetEntry == null)
        {
            Debug.LogError(string.Concat("[ABPackageBackend] Asset not found in manifest: ", key));
            return null;
        }

        var (asset, bundleName, error) = LoadAssetInternalSync<T>(key, assetEntry);
        if (error != null)
        {
            Debug.LogError(string.Concat("[ABPackageBackend] ", error.ToString()));
            return null;
        }

        return asset;
    }

    /// <summary>
    /// 同步加载资产（扩展：附带 EntryId）。
    /// </summary>
    public T LoadAssetSync<T>(string key, string entryId) where T : UnityEngine.Object
    {
        if (string.IsNullOrEmpty(key))
        {
            Debug.LogError("[ABPackageBackend] LoadAssetSync: key is null or empty");
            return null;
        }

        // 缓存命中
        if (_assetCache.TryGetValue(key, out var cached))
        {
            cached.RefCount++;
            return cached.Asset as T;
        }

        // 优先按 EntryId 精确查找
        ManifestAssetEntry assetEntry = null;
        if (!string.IsNullOrEmpty(entryId))
        {
            _manifest.TryGetAssetByEntryId(entryId, out assetEntry);
        }

        if (assetEntry == null)
        {
            assetEntry = ResolveAssetEntryByAddress(key);
        }

        if (assetEntry == null)
        {
            Debug.LogError(string.Concat("[ABPackageBackend] Asset not found: key=", key, ", entryId=", entryId));
            return null;
        }

        var (asset, bundleName, error) = LoadAssetInternalSync<T>(key, assetEntry);
        if (error != null)
        {
            Debug.LogError(string.Concat("[ABPackageBackend] ", error.ToString()));
            return null;
        }

        return asset;
    }

    #endregion

    #region IPackageBackend · 卸载

    /// <summary>
    /// 按 address/key 卸载资产。引用计数 -1，归零时移除缓存并联动 Bundle 卸载。
    /// </summary>
    public void UnloadAsset(string key)
    {
        if (string.IsNullOrEmpty(key)) return;
        if (!_assetCache.TryGetValue(key, out var entry)) return;

        entry.RefCount--;
        if (entry.RefCount <= 0)
        {
            _assetCache.Remove(key);

            // 清理 EntryId 反向映射
            if (!string.IsNullOrEmpty(entry.EntryId))
            {
                _entryIdToAddress.Remove(entry.EntryId);
            }

            // 联动 Bundle 引用计数
            _bundleLoader.UnloadBundle(entry.BundleName);
        }
    }

    /// <summary>
    /// 按 EntryId 卸载资产。
    /// 通过 EntryId → address 反查，委托给 UnloadAsset(key)。
    /// </summary>
    public void UnloadByEntryId(string entryId)
    {
        if (string.IsNullOrEmpty(entryId)) return;
        if (_entryIdToAddress.TryGetValue(entryId, out string key))
        {
            UnloadAsset(key);
        }
    }

    #endregion

    #region IPackageBackend · 查询

    /// <summary>
    /// 检查资产是否已加载并缓存。
    /// </summary>
    public bool ContainsKey(string key)
    {
        return !string.IsNullOrEmpty(key) && _assetCache.ContainsKey(key);
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

    #endregion

    #region 内部元组 API（供 AAPackageManager Handle 构建路径使用）

    /// <summary>
    /// 异步加载资产，返回 (asset, bundleName, error) 元组。
    /// AAPackageManager 通过此方法获取 bundleName 以分配 HandleRegistry 槽位。
    /// </summary>
    internal async Task<(T asset, string bundleName, AssetLoadError error)> LoadAssetTupleAsync<T>(
        string key, string entryId) where T : UnityEngine.Object
    {
        // 缓存命中
        if (_assetCache.TryGetValue(key, out var cached))
        {
            cached.RefCount++;
            return (cached.Asset as T, cached.BundleName, null);
        }

        // 优先按 EntryId 精确查找
        ManifestAssetEntry assetEntry = null;
        if (!string.IsNullOrEmpty(entryId))
        {
            _manifest.TryGetAssetByEntryId(entryId, out assetEntry);
        }

        // 回退到 address 查找
        if (assetEntry == null)
        {
            assetEntry = ResolveAssetEntryByAddress(key);
        }

        if (assetEntry == null)
        {
            return (null, null, AssetLoadError.NotFound(
                string.Concat("key=", key, ", entryId=", entryId ?? "")));
        }

        return await LoadAssetInternalAsync<T>(key, assetEntry);
    }

    /// <summary>
    /// 同步加载资产，返回 (asset, bundleName, error) 元组。
    /// AAPackageManager 通过此方法获取 bundleName 以分配 HandleRegistry 槽位。
    /// </summary>
    internal (T asset, string bundleName, AssetLoadError error) LoadAssetTupleSync<T>(
        string key, string entryId) where T : UnityEngine.Object
    {
        // 缓存命中
        if (_assetCache.TryGetValue(key, out var cached))
        {
            cached.RefCount++;
            return (cached.Asset as T, cached.BundleName, null);
        }

        // 优先按 EntryId 精确查找
        ManifestAssetEntry assetEntry = null;
        if (!string.IsNullOrEmpty(entryId))
        {
            _manifest.TryGetAssetByEntryId(entryId, out assetEntry);
        }

        if (assetEntry == null)
        {
            assetEntry = ResolveAssetEntryByAddress(key);
        }

        if (assetEntry == null)
        {
            return (null, null, AssetLoadError.NotFound(
                string.Concat("key=", key, ", entryId=", entryId ?? "")));
        }

        return LoadAssetInternalSync<T>(key, assetEntry);
    }

    #endregion

    #region 内部实现（元组 API，不抛异常）

    /// <summary>
    /// 异步加载资产的内部实现（已确认 assetEntry 有效）。
    /// 返回 (asset, bundleName, error) 元组 — 内部 API，不抛异常。
    /// </summary>
    private async Task<(T asset, string bundleName, AssetLoadError error)> LoadAssetInternalAsync<T>(
        string cacheKey, ManifestAssetEntry assetEntry) where T : UnityEngine.Object
    {
        // 获取 Bundle 信息
        var bundleEntry = _manifest.GetBundleForAsset(assetEntry);
        if (bundleEntry == null)
        {
            return (null, null,
                AssetLoadError.BundleNotFound(
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
            _bundleLoader.UnloadBundle(bundleName);
            return (null, bundleName,
                AssetLoadError.AssetExtractionFailed(assetEntry.EntryId, assetEntry.SourcePath, bundleName));
        }

        if (asset == null)
        {
            _bundleLoader.UnloadBundle(bundleName);
            return (null, bundleName,
                AssetLoadError.AssetExtractionFailed(assetEntry.EntryId, assetEntry.SourcePath, bundleName));
        }

        // 加入 Asset 缓存
        AddToAssetCache(cacheKey, asset, bundleName, assetEntry.EntryId);
        return (asset, bundleName, null);
    }

    /// <summary>
    /// 同步加载资产的内部实现（已确认 assetEntry 有效）。
    /// 返回 (asset, bundleName, error) 元组 — 内部 API，不抛异常。
    /// </summary>
    private (T asset, string bundleName, AssetLoadError error) LoadAssetInternalSync<T>(
        string cacheKey, ManifestAssetEntry assetEntry) where T : UnityEngine.Object
    {
        // 获取 Bundle 信息
        var bundleEntry = _manifest.GetBundleForAsset(assetEntry);
        if (bundleEntry == null)
        {
            return (null, null,
                AssetLoadError.BundleNotFound(
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
                AssetLoadError.AssetExtractionFailed(assetEntry.EntryId, assetEntry.SourcePath, bundleName));
        }

        // 加入 Asset 缓存
        AddToAssetCache(cacheKey, asset, bundleName, assetEntry.EntryId);
        return (asset, bundleName, null);
    }

    /// <summary>
    /// 将资产加入缓存并建立 EntryId 反向映射。
    /// </summary>
    private void AddToAssetCache(string key, UnityEngine.Object asset, string bundleName, string entryId)
    {
        _assetCache[key] = new AssetCacheEntry
        {
            Asset = asset,
            BundleName = bundleName,
            EntryId = entryId,
            RefCount = 1
        };

        if (!string.IsNullOrEmpty(entryId))
        {
            _entryIdToAddress[entryId] = key;
        }
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
