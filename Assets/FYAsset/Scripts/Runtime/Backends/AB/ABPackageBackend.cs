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
        /// <summary>已加载的资产实例</summary>
        public UnityEngine.Object Asset;

        /// <summary>该资产所属的 Bundle 名称（卸载时用于联动 Bundle 引用计数）</summary>
        public string BundleName;

        /// <summary>资产的 EntryId（用于 UnloadByEntryId 反查）</summary>
        public string EntryId;

        /// <summary>
        /// 资产的 address（用于 ReleaseEntry 清理 _addressToEntryIds，避免回查 Manifest）。
        /// 注：address 可重复（多个 EntryId 共享同一 address），EntryId 才是唯一键。
        /// </summary>
        public string Address;

        /// <summary>引用计数</summary>
        public int RefCount;
    }

    #endregion

    #region 字段

    /// <summary>Asset 缓存：EntryId → CacheEntry</summary>
    private readonly Dictionary<string, AssetCacheEntry> _assetCache = new();

    /// <summary>address → 已加载的 EntryId 列表（支持 Legacy 风格按 key 卸载）</summary>
    private readonly Dictionary<string, HashSet<string>> _addressToEntryIds = new();

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
        var assetEntry = ResolveAssetEntryByAddress(key);
        if (assetEntry == null)
            throw new Exception(string.Concat("[ABPackageBackend] Asset not found in manifest: ", key));

        if (_assetCache.TryGetValue(assetEntry.EntryId, out var cached))
        {
            cached.RefCount++;
            return cached.Asset as T;
        }

        var (asset, bundleName, error) = await LoadAssetInternalAsync<T>(assetEntry);
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

        var assetEntry = ResolveAssetEntry(key, entryId);

        if (assetEntry == null)
            throw new Exception(string.Concat("[ABPackageBackend] Asset not found: key=", key, ", entryId=", entryId));

        if (_assetCache.TryGetValue(assetEntry.EntryId, out var cached))
        {
            cached.RefCount++;
            return cached.Asset as T;
        }

        var (asset, bundleName, error) = await LoadAssetInternalAsync<T>(assetEntry);
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

        var assetEntry = ResolveAssetEntryByAddress(key);
        if (assetEntry == null)
        {
            Debug.LogError(string.Concat("[ABPackageBackend] Asset not found in manifest: ", key));
            return null;
        }

        if (_assetCache.TryGetValue(assetEntry.EntryId, out var cached))
        {
            cached.RefCount++;
            return cached.Asset as T;
        }

        var (asset, bundleName, error) = LoadAssetInternalSync<T>(assetEntry);
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

        var assetEntry = ResolveAssetEntry(key, entryId);

        if (assetEntry == null)
        {
            Debug.LogError(string.Concat("[ABPackageBackend] Asset not found: key=", key, ", entryId=", entryId));
            return null;
        }

        if (_assetCache.TryGetValue(assetEntry.EntryId, out var cached))
        {
            cached.RefCount++;
            return cached.Asset as T;
        }

        var (asset, bundleName, error) = LoadAssetInternalSync<T>(assetEntry);
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
        if (!_addressToEntryIds.TryGetValue(key, out var entryIds) || entryIds.Count == 0) return;

        string targetEntryId = null;
        // HashSet 无索引器，用 foreach+break 取第一个元素（C# 无非 LINQ 的 First()）
        foreach (var entryId in entryIds)
        {
            targetEntryId = entryId;
            break;
        }

        if (string.IsNullOrEmpty(targetEntryId)) return;
        ReleaseEntry(targetEntryId);
    }

    /// <summary>
    /// 按 EntryId 卸载资产。
    /// 通过 EntryId → address 反查，委托给 UnloadAsset(key)。
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
            cached.RefCount++;
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
            cached.RefCount++;
            return (cached.Asset as T, cached.BundleName, null);
        }

        return LoadAssetInternalSync<T>(assetEntry);
    }

    #endregion

    #region 内部实现（元组 API，不抛异常）

    /// <summary>
    /// 异步加载资产的内部实现（已确认 assetEntry 有效）。
    /// 返回 (asset, bundleName, error) 元组 — 内部 API，不抛异常。
    /// </summary>
    private async Task<(T asset, string bundleName, RuntimeMessage error)> LoadAssetInternalAsync<T>(
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
        catch (Exception)
        {
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
            Address = assetEntry.Address,
            RefCount = 1
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
    /// 按 EntryId 释放资产缓存并联动 Bundle 引用计数。
    /// </summary>
    private void ReleaseEntry(string entryId)
    {
        if (string.IsNullOrEmpty(entryId)) return;
        if (!_assetCache.TryGetValue(entryId, out var entry)) return;

        entry.RefCount--;
        if (entry.RefCount > 0) return;

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
