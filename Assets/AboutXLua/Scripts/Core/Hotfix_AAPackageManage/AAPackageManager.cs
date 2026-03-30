using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

public class AAPackageManager : Singleton<AAPackageManager>
{
    private IAssetIndex _index;
    private IPackageBackend _backend = new AddressablesBackend();
    private bool _isInitialized = false;

    private readonly Dictionary<string, List<string>> _labelToKeys = new();

    public async Task Initialize()
    {
        AsyncOperationHandle<AddressableLabelsConfig> handle =
            Addressables.LoadAssetAsync<AddressableLabelsConfig>(Constants.AA_LABELS_CONFIG);

        var config = await handle.Task;

        if (handle.Status != AsyncOperationStatus.Succeeded || config == null)
        {
            Debug.LogError($"[AAPackageManager] 关键配置加载失败: {Constants.AA_LABELS_CONFIG}。管理器无法初始化。");
            return;
        }

        _index = config;

        foreach (var label in _index.GetLabels())
        {
            _labelToKeys[label] = _index.GetKeysByLabel(label);
        }

        _isInitialized = true;
        Debug.Log($"[AAPackageManager] 初始化完成。Entries: {config.allEntries.Count}");
    }

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

        return keys.ToList();
    }

    public List<string> GetKeysByTypeAndLabel(string type, string label)
    {
        if (!_isInitialized) return new List<string>();

        var typeKeys = _index.GetKeysByType(type);
        var labelKeys = new HashSet<string>(_index.GetKeysByLabel(label));

        return typeKeys.Where(k => labelKeys.Contains(k)).ToList();
    }

    public bool ContainsKey(string key)
    {
        return _isInitialized && _index.ContainsKey(key);
    }

    #endregion

    #region 上层统一资源加载卸载接口

    public async Task<T> LoadAssetAsync<T>(string key) where T : UnityEngine.Object
    {
        if (!_isInitialized) Debug.LogError("AAPackageManager 未初始化");

        return await _backend.LoadAssetAsync<T>(key);
    }

    public async Task<List<T>> LoadAssetByLabelAsync<T>(string label) where T : UnityEngine.Object
    {
        if (!_isInitialized)
        {
            Debug.LogError("AAPackageManager 未初始化");
            return new List<T>();
        }

        var keys = GetKeysByLabel(label);
        if (keys.Count == 0)
        {
            Debug.LogError($"[AAPackageManager] 找不到标签: {label}");
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
            Debug.LogError("AAPackageManager 未初始化");
            return new List<T>();
        }

        var keys = GetKeysByLabels(labels);
        if (keys.Count == 0)
        {
            Debug.LogWarning($"[AAPackageManager] 未找到标签组合 '{string.Join(",", labels)}' 的资源");
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
            Debug.LogError("AAPackageManager 未初始化");
            return null;
        }

        return _backend.LoadAssetSync<T>(key);
    }

    #endregion

    #region B5-2 新增：Resolve / Load API

    /// <summary>
    /// 通过 Address 异步加载资源，返回 AssetHandle。
    /// 内部先 Resolve 得到唯一条目，再通过 backend 加载。
    /// </summary>
    public async Task<AssetHandle<T>> LoadByAddress<T>(string address) where T : UnityEngine.Object
    {
        if (!_isInitialized)
            return new AssetHandle<T>(
                AssetLoadError.LoadFailed("", "AAPackageManager 未初始化"), address);

        var result = AssetResolver.ResolveByAddress<T>(_index, address);
        if (!result.IsSuccess)
            return new AssetHandle<T>(result.Error, address);

        var entry = result.Entry;
        T asset;
        try
        {
            asset = await _backend.LoadAssetAsync<T>(entry.Address, entry.EntryId);
        }
        catch (Exception ex)
        {
            return new AssetHandle<T>(
                AssetLoadError.LoadFailed(entry.EntryId, ex.Message), address);
        }

        if (asset == null)
            return new AssetHandle<T>(
                AssetLoadError.LoadFailed(entry.EntryId, "Backend 返回 null"), address);

        return new AssetHandle<T>(asset, entry, id => _backend.UnloadByEntryId(id));
    }

    /// <summary>
    /// 通过 Address 同步加载资源，返回 AssetHandle。
    /// </summary>
    public AssetHandle<T> LoadByAddressSync<T>(string address) where T : UnityEngine.Object
    {
        if (!_isInitialized)
            return new AssetHandle<T>(
                AssetLoadError.LoadFailed("", "AAPackageManager 未初始化"), address);

        var result = AssetResolver.ResolveByAddress<T>(_index, address);
        if (!result.IsSuccess)
            return new AssetHandle<T>(result.Error, address);

        var entry = result.Entry;
        var asset = _backend.LoadAssetSync<T>(entry.Address, entry.EntryId);
        if (asset == null)
            return new AssetHandle<T>(
                AssetLoadError.LoadFailed(entry.EntryId, "Backend 返回 null"), address);

        return new AssetHandle<T>(asset, entry, id => _backend.UnloadByEntryId(id));
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
                AssetLoadError.LoadFailed("", "AAPackageManager 未初始化"), key);

        var result = AssetResolver.ResolveByTypeKey<T>(_index, key, labels);
        if (!result.IsSuccess)
            return new AssetHandle<T>(result.Error, key);

        var entry = result.Entry;
        T asset;
        try
        {
            asset = await _backend.LoadAssetAsync<T>(entry.Address, entry.EntryId);
        }
        catch (Exception ex)
        {
            return new AssetHandle<T>(
                AssetLoadError.LoadFailed(entry.EntryId, ex.Message), key);
        }

        if (asset == null)
            return new AssetHandle<T>(
                AssetLoadError.LoadFailed(entry.EntryId, "Backend 返回 null"), key);

        return new AssetHandle<T>(asset, entry, id => _backend.UnloadByEntryId(id));
    }

    /// <summary>
    /// 通过 TypeKey 同步加载资源，返回 AssetHandle。
    /// </summary>
    public AssetHandle<T> LoadByTypeKeySync<T>(
        string key, IReadOnlyList<string> labels = null) where T : UnityEngine.Object
    {
        if (!_isInitialized)
            return new AssetHandle<T>(
                AssetLoadError.LoadFailed("", "AAPackageManager 未初始化"), key);

        var result = AssetResolver.ResolveByTypeKey<T>(_index, key, labels);
        if (!result.IsSuccess)
            return new AssetHandle<T>(result.Error, key);

        var entry = result.Entry;
        var asset = _backend.LoadAssetSync<T>(entry.Address, entry.EntryId);
        if (asset == null)
            return new AssetHandle<T>(
                AssetLoadError.LoadFailed(entry.EntryId, "Backend 返回 null"), key);

        return new AssetHandle<T>(asset, entry, id => _backend.UnloadByEntryId(id));
    }

    #endregion
}