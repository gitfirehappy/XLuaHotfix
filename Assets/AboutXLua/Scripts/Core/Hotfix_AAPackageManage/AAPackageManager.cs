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
}