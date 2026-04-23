using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

public class AssetPackageManager : Singleton<AssetPackageManager>
{
    #region AB 索引 + 后端开关（默认关闭，coexistence validation）

    /// <summary>
    /// AB 索引 + 后端开关。
    /// true = 使用 ABManifest → ABAssetIndex → ABBundleLoader → ABPackageBackend（自研 AB 全链路）；
    /// false = 使用 AddressableLabelsConfig + AddressablesBackend（原有 Addressables 路径）。
    /// 一个开关同时控制索引源和加载后端，不存在 "AB 索引 + Addressables 后端" 的组合。
    /// </summary>
    #endregion

    private IAssetIndex _index;
    private IPackageBackend _backend = new AddressablesBackend();
    private bool _isInitialized = false;

    private readonly Dictionary<string, List<string>> _labelToKeys = new();

    public async Task Initialize()
    {
        if (Constants.USE_AB_BACKEND)
        {
            await InitializeWithABIndex();
        }
        else
        {
            await InitializeWithLegacyIndex();
        }

        if (_index == null) return;

        // 共用：从 _index 构建 _labelToKeys 缓存
        var labels = _index.GetLabels();
        for (int i = 0; i < labels.Count; i++)
        {
            _labelToKeys[labels[i]] = _index.GetKeysByLabel(labels[i]);
        }

        _isInitialized = true;
    }

    #region 初始化路径

    /// <summary>
    /// AB 索引路径：ManifestLoader → ABManifest → ABAssetIndex + ABBundleLoader + ABPackageBackend。
    /// 同时初始化索引和加载后端，一个开关控制两个维度。
    /// 加载失败视为致命错误，不回退到 Legacy。
    /// </summary>
    private async Task InitializeWithABIndex()
    {
        var manifest = await ManifestLoader.LoadAsync();
        if (manifest == null)
        {
            Debug.LogError("[AssetPackageManager] ABManifest 加载失败，管理器无法初始化。");
            return;
        }

        // 初始化 AB 索引
        _index = new ABAssetIndex(manifest);

        // 初始化 AB 加载后端
        var bundleLoader = new ABBundleLoader(manifest);
        var abBackend = new ABPackageBackend(manifest, bundleLoader);
        _backend = abBackend;

        Debug.Log(
            $"[AssetPackageManager] AB 全链路初始化完成。" +
            $"Assets: {manifest.AssetCount}, Bundles: {manifest.BundleCount}, " +
            $"Index: ABAssetIndex, Backend: ABPackageBackend");
    }

    /// <summary>
    /// Legacy 索引路径：通过 Addressables 加载 AddressableLabelsConfig（原有流程，不做任何修改）。
    /// </summary>
    private async Task InitializeWithLegacyIndex()
    {
        AsyncOperationHandle<AddressableLabelsConfig> handle =
            Addressables.LoadAssetAsync<AddressableLabelsConfig>(Constants.AA_LABELS_CONFIG);

        var config = await handle.Task;

        if (handle.Status != AsyncOperationStatus.Succeeded || config == null)
        {
            Debug.LogError($"[AssetPackageManager] 关键配置加载失败: {Constants.AA_LABELS_CONFIG}。管理器无法初始化。");
            return;
        }

        _index = config;
        Debug.Log($"[AssetPackageManager] Legacy 索引初始化完成。Entries: {config.allEntries.Count}");
    }

    #endregion

    public void SetIndex(IAssetIndex index)
    {
        _index = index;
    }

    public void SetBackend(IPackageBackend backend)
    {
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
    }

    #region 查询接口

    public List<string> GetKeysByType(string type)
    {
        return _isInitialized ? _index.GetKeysByType(type) : new List<string>();
    }

    public List<string> GetKeysByLabel(string label)
    {
        return _isInitialized ? _index.GetKeysByLabel(label) : new List<string>();
    }

    public List<string> GetKeysByLabels(string[] labels)
    {
        if (!_isInitialized || labels == null || labels.Length == 0)
            return new List<string>();

        if (labels.Length == 1)
            return GetKeysByLabel(labels[0]);

        var keys = new HashSet<string>(GetKeysByLabel(labels[0]));

        for (int i = 1; i < labels.Length; i++)
        {
            var currentKeys = new HashSet<string>(GetKeysByLabel(labels[i]));
            keys.IntersectWith(currentKeys);
        }

        var result = new List<string>(keys.Count);
        foreach (var key in keys)
        {
            result.Add(key);
        }
        return result;
    }

    public List<string> GetKeysByTypeAndLabel(string type, string label)
    {
        if (!_isInitialized) return new List<string>();

        var typeKeys = _index.GetKeysByType(type);
        var labelKeys = new HashSet<string>(_index.GetKeysByLabel(label));

        var result = new List<string>(typeKeys.Count);
        for (int i = 0; i < typeKeys.Count; i++)
        {
            var key = typeKeys[i];
            if (!labelKeys.Contains(key))
                continue;
            result.Add(key);
        }
        return result;
    }

    public bool ContainsKey(string key)
    {
        return _isInitialized && _index.ContainsKey(key);
    }

    #endregion

    #region 上层统一资源加载卸载接口

    public async Task<T> LoadAssetAsync<T>(string key) where T : UnityEngine.Object
    {
        if (!_isInitialized) Debug.LogError("AssetPackageManager 未初始化");

        return await _backend.LoadAssetAsync<T>(key);
    }

    public async Task<List<T>> LoadAssetByLabelAsync<T>(string label) where T : UnityEngine.Object
    {
        if (!_isInitialized)
        {
            Debug.LogError("AssetPackageManager 未初始化");
            return new List<T>();
        }

        var keys = GetKeysByLabel(label);
        if (keys.Count == 0)
        {
            Debug.LogError($"[AssetPackageManager] 找不到标签: {label}");
            return new List<T>();
        }

        var results = new List<T>();
        foreach (var key in keys)
        {
            var asset = await LoadAssetAsync<T>(key);
            if (asset == null) continue;
            results.Add(asset);
        }

        return results;
    }

    public async Task<List<T>> LoadAssetByLabelsAsync<T>(string[] labels) where T : UnityEngine.Object
    {
        if (!_isInitialized)
        {
            Debug.LogError("AssetPackageManager 未初始化");
            return new List<T>();
        }

        var keys = GetKeysByLabels(labels);
        if (keys.Count == 0)
        {
            Debug.LogWarning($"[AssetPackageManager] 未找到标签组合 '{string.Join(",", labels)}' 的资源");
            return new List<T>();
        }

        var results = new List<T>();
        foreach (var key in keys)
        {
            var asset = await LoadAssetAsync<T>(key);
            if (asset == null) continue;
            results.Add(asset);
        }

        return results;
    }

    public void UnloadAsset(string key)
    {
        _backend.UnloadAsset(key);
    }

    public void UnloadAssetByLabel(string label)
    {
        if (!_labelToKeys.TryGetValue(label, out var keys)) return;

        foreach (var key in keys)
        {
            UnloadAsset(key);
        }
    }

    public void UnloadAssetsByLabels(string[] labels)
    {
        if (!_isInitialized || labels == null || labels.Length == 0)
            return;

        var keys = GetKeysByLabels(labels);
        foreach (var key in keys)
        {
            UnloadAsset(key);
        }
    }

    public T LoadAssetSync<T>(string key) where T : UnityEngine.Object
    {
        if (!_isInitialized)
        {
            Debug.LogError("AssetPackageManager 未初始化");
            return null;
        }

        return _backend.LoadAssetSync<T>(key);
    }

    #endregion

    #region Resolve / Load API（基于条目解析的加载接口）

    /// <summary>
    /// 通过 Address 异步加载资源，返回 AssetHandle。
    /// 内部先 Resolve 得到唯一条目，再通过 backend 加载，通过 HandleRegistry 分配句柄。
    /// </summary>
    public async Task<AssetHandle<T>> LoadByAddress<T>(string address) where T : UnityEngine.Object
    {
        if (!_isInitialized)
            return new AssetHandle<T>(
                AssetLoadError.LoadFailed("", "AssetPackageManager 未初始化"));

        var result = AssetResolver.ResolveByAddress<T>(_index, address);
        if (!result.IsSuccess)
            return new AssetHandle<T>(result.Error);

        return await LoadResolvedAsync<T>(result.Entry);
    }

    /// <summary>
    /// 通过 Address 同步加载资源，返回 AssetHandle。
    /// </summary>
    public AssetHandle<T> LoadByAddressSync<T>(string address) where T : UnityEngine.Object
    {
        if (!_isInitialized)
            return new AssetHandle<T>(
                AssetLoadError.LoadFailed("", "AssetPackageManager 未初始化"));

        var result = AssetResolver.ResolveByAddress<T>(_index, address);
        if (!result.IsSuccess)
            return new AssetHandle<T>(result.Error);

        return LoadResolvedSync<T>(result.Entry);
    }

    /// <summary>
    /// 通过 TypeKey 异步加载资源，返回 AssetHandle。
    /// 可选传入 Labels 进行消歧。
    /// </summary>
    public async Task<AssetHandle<T>> LoadByTypeKey<T>(
        string key, IReadOnlyList<string> labels = null) where T : UnityEngine.Object
    {
        if (!_isInitialized)
            return new AssetHandle<T>(
                AssetLoadError.LoadFailed("", "AssetPackageManager 未初始化"));

        var result = AssetResolver.ResolveByTypeKey<T>(_index, key, labels);
        if (!result.IsSuccess)
            return new AssetHandle<T>(result.Error);

        return await LoadResolvedAsync<T>(result.Entry);
    }

    /// <summary>
    /// 通过 TypeKey 同步加载资源，返回 AssetHandle。
    /// </summary>
    public AssetHandle<T> LoadByTypeKeySync<T>(
        string key, IReadOnlyList<string> labels = null) where T : UnityEngine.Object
    {
        if (!_isInitialized)
            return new AssetHandle<T>(
                AssetLoadError.LoadFailed("", "AssetPackageManager 未初始化"));

        var result = AssetResolver.ResolveByTypeKey<T>(_index, key, labels);
        if (!result.IsSuccess)
            return new AssetHandle<T>(result.Error);

        return LoadResolvedSync<T>(result.Entry);
    }

    #endregion

    #region Resolve / Load Helpers

    private async Task<AssetHandle<T>> LoadResolvedAsync<T>(RuntimeAssetEntry entry) where T : UnityEngine.Object
    {
        var abBackend = _backend as ABPackageBackend;
        if (abBackend != null)
            return await LoadResolvedWithABAsync<T>(abBackend, entry);

        return await LoadResolvedWithLegacyAsync<T>(entry);
    }

    private AssetHandle<T> LoadResolvedSync<T>(RuntimeAssetEntry entry) where T : UnityEngine.Object
    {
        var abBackend = _backend as ABPackageBackend;
        if (abBackend != null)
            return LoadResolvedWithABSync<T>(abBackend, entry);

        return LoadResolvedWithLegacySync<T>(entry);
    }

    private async Task<AssetHandle<T>> LoadResolvedWithABAsync<T>(
        ABPackageBackend abBackend,
        RuntimeAssetEntry entry) where T : UnityEngine.Object
    {
        var (asset, bundleName, loadErr) = await abBackend.LoadAssetTupleAsync<T>(entry.Address, entry.EntryId);
        if (loadErr != null)
            return new AssetHandle<T>(loadErr);

        return CreateABHandle(entry, asset, bundleName, abBackend);
    }

    private AssetHandle<T> LoadResolvedWithABSync<T>(
        ABPackageBackend abBackend,
        RuntimeAssetEntry entry) where T : UnityEngine.Object
    {
        var (asset, bundleName, loadErr) = abBackend.LoadAssetTupleSync<T>(entry.Address, entry.EntryId);
        if (loadErr != null)
            return new AssetHandle<T>(loadErr);

        return CreateABHandle(entry, asset, bundleName, abBackend);
    }

    private async Task<AssetHandle<T>> LoadResolvedWithLegacyAsync<T>(RuntimeAssetEntry entry)
        where T : UnityEngine.Object
    {
        T asset;
        try
        {
            asset = await _backend.LoadAssetAsync<T>(entry.Address, entry.EntryId);
        }
        catch (Exception ex)
        {
            return new AssetHandle<T>(AssetLoadError.LoadFailed(entry.EntryId, ex.Message));
        }

        if (asset == null)
            return new AssetHandle<T>(AssetLoadError.LoadFailed(entry.EntryId, "Backend 返回 null"));

        return CreateLegacyHandle(entry, asset);
    }

    private AssetHandle<T> LoadResolvedWithLegacySync<T>(RuntimeAssetEntry entry)
        where T : UnityEngine.Object
    {
        var asset = _backend.LoadAssetSync<T>(entry.Address, entry.EntryId);
        if (asset == null)
            return new AssetHandle<T>(AssetLoadError.LoadFailed(entry.EntryId, "Backend 返回 null"));

        return CreateLegacyHandle(entry, asset);
    }

    private static AssetHandle<T> CreateABHandle<T>(
        RuntimeAssetEntry entry,
        T asset,
        string bundleName,
        ABPackageBackend abBackend) where T : UnityEngine.Object
    {
        var (handleId, generation) = HandleRegistry.Alloc(
            entry.EntryId,
            bundleName ?? "",
            null,
            id => abBackend.UnloadByEntryId(id));

        return new AssetHandle<T>(handleId, generation, asset);
    }

    private AssetHandle<T> CreateLegacyHandle<T>(RuntimeAssetEntry entry, T asset)
        where T : UnityEngine.Object
    {
        var releaseAddress = entry.Address;
        var (handleId, generation) = HandleRegistry.Alloc(
            entry.EntryId,
            "",
            null,
            _ => _backend.UnloadAsset(releaseAddress));

        return new AssetHandle<T>(handleId, generation, asset);
    }

    #endregion
}
