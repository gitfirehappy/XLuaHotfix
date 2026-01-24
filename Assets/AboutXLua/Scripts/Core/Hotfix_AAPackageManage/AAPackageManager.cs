using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

/// <summary>
/// 资源库索引：负责管理资源的元数据（Type, Label），提供查询功能
/// 资源池：负责加载、缓存资源，提供资源加载、卸载接口
/// </summary>
public class AAPackageManager : Singleton<AAPackageManager>
{
    private AddressableLabelsConfig _config;

    private bool _isInitialized = false;
    
    /// <summary> 资源缓存 </summary>
    private readonly Dictionary<string, ResourceEntry> _resourceCache = new();
    private readonly Dictionary<string, List<string>> _labelToKeys = new();

    private class ResourceEntry
    {
        public AsyncOperationHandle Handle;
        public int ReferenceCount = 1;
        public bool IsValid => ReferenceCount > 0;
    }

    /// <summary>
    /// 加载 AddressableLabelsConfig 获取资源索引
    /// </summary>
    public async Task Initialize()
    {
        // 异步加载配置SO
        AsyncOperationHandle<AddressableLabelsConfig> handle = 
            Addressables.LoadAssetAsync<AddressableLabelsConfig>(Constants.AA_LABELS_CONFIG);
        
        _config = await handle.Task;

        if (handle.Status != AsyncOperationStatus.Succeeded || _config == null)
        {
            Debug.LogError($"[AAPackageManager] 关键配置加载失败: {Constants.AA_LABELS_CONFIG}。管理器无法初始化。");
            return;
        }

        foreach (var label in _config.GetLabels())
        {
            _labelToKeys[label] = _config.GetKeysByLabel(label);
        }
        
        _isInitialized = true;
        Debug.Log($"[AAPackageManager] 初始化完成。Entries: {_config.allEntries.Count}");
    }

    #region 查询接口

    /// <summary>
    /// 获取某类型的所有 Key
    /// </summary>
    public List<string> GetKeysByType(string type)
    {
        return _isInitialized ? _config.GetKeysByType(type) : new List<string>();
    }

    /// <summary>
    /// 获取某标签的所有 Key
    /// </summary>
    public List<string> GetKeysByLabel(string label)
    {
        return _isInitialized ? _config.GetKeysByLabel(label) : new List<string>();
    }
    
    /// <summary>
    /// 获取同时具有多个标签的资源Key（求交集）
    /// </summary>
    public List<string> GetKeysByLabels(string[] labels)
    {
        if (!_isInitialized || labels == null || labels.Length == 0) 
            return new List<string>();
        
        // 如果只有一个标签，直接使用GetKeysByLabel
        if (labels.Length == 1)
            return GetKeysByLabel(labels[0]);
        
        // 多个标签求交集
        var keys = new HashSet<string>(GetKeysByLabel(labels[0]));
        
        for (int i = 1; i < labels.Length; i++)
        {
            var currentKeys = new HashSet<string>(GetKeysByLabel(labels[i]));
            keys.IntersectWith(currentKeys);
        }
        
        return keys.ToList();
    }

    /// <summary>
    /// 复合查询：获取同时满足 Type 和 Label 的 Key
    /// </summary>
    public List<string> GetKeysByTypeAndLabel(string type, string label)
    {
        if (!_isInitialized) return new List<string>();
        
        // 获取Type的所有Key
        var typeKeys = _config.GetKeysByType(type);
        // 获取Label的所有Key (利用HashSet优化交集查找)
        var labelKeys = new HashSet<string>(_config.GetKeysByLabel(label));
        
        return typeKeys.Where(k => labelKeys.Contains(k)).ToList();
    }

    /// <summary>
    /// 检查资源是否存在
    /// </summary>
    public bool ContainsKey(string key)
    {
        return _isInitialized && _config.allEntries.Any(e => e.key == key);
    }

    #endregion

    #region 上层统一资源加载卸载接口

    /// <summary>
    /// key加载资源
    /// </summary>
    /// <param name="key">AA资源的key</param>
    /// <typeparam name="T">要加载的类型</typeparam>
    /// <returns>资源handle</returns>
    public async Task<T> LoadAssetAsync<T>(string key) where T : UnityEngine.Object
    {
        if(!_isInitialized) Debug.LogError("AAPackageManager 未初始化");

        if (_resourceCache.TryGetValue(key, out var entry) && entry.IsValid)
        {
            entry.ReferenceCount++;
            return entry.Handle.Result as T;
        }
            
        var handle = Addressables.LoadAssetAsync<T>(key);
        if (handle.IsDone && handle.Status == AsyncOperationStatus.Succeeded)
        {
            AddToCache(key, handle);
            return handle.Result as T;
        }

        throw new Exception($"[AAPackageManager] 加载资源失败: {key}");
    }

    /// <summary>
    /// 按标签加载资源
    /// </summary>
    /// <param name="label">标签</param>
    /// <typeparam name="T">要加载的类型</typeparam>
    /// <returns>所有匹配的资源</returns>
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

    /// <summary>
    /// 按多个标签加载资源（求交集）
    /// </summary>
    /// <param name="labels">标签列表</param>
    /// <typeparam name="T">类型</typeparam>
    /// <returns>所有匹配的资源</returns>
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
    
    /// <summary>
    /// 按key卸载资源
    /// </summary>
    /// <param name="key">AA资源的key</param>
    public void UnloadAsset(string key)
    {
        if (!_resourceCache.TryGetValue(key, out var entry) || !entry.IsValid) return;
        
        entry.ReferenceCount--;
        if (entry.ReferenceCount <= 0)
        {
            Addressables.Release(entry.Handle);
            _resourceCache.Remove(key);
        }
    }

    /// <summary>
    /// 按标签卸载资源
    /// </summary>
    /// <param name="label">AA资源的标签</param>
    public void UnloadAssetByLabel(string label)
    {
        if(!_labelToKeys.TryGetValue(label, out var keys)) return;
        
        foreach (var key in keys)
        {
            UnloadAsset(key);
        }
    }
    
    /// <summary>
    /// 按多个标签卸载所有资源（求交集）
    /// </summary>
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

    #region 同步加载

    /// <summary>
    /// 同步加载资源 (用于 Lua require 等必须同步的场景)
    /// </summary>
    public T LoadAssetSync<T>(string key) where T : UnityEngine.Object
    {
        if (!_isInitialized)
        {
            Debug.LogError("AAPackageManager 未初始化");
            return null;
        }

        // 检查缓存
        if (_resourceCache.TryGetValue(key, out var entry) && entry.IsValid)
        {
            entry.ReferenceCount++;
            return entry.Handle.Result as T;
        }

        // 启动异步操作
        var handle = Addressables.LoadAssetAsync<T>(key);
        
        // 强制主线程等待直到完成
        T result = handle.WaitForCompletion();

        if (handle.Status == AsyncOperationStatus.Succeeded)
        {
            AddToCache(key, handle);
            return result;
        }
        else
        {
            Debug.LogError($"[AAPackageManager] 同步加载失败: {key}");
            Addressables.Release(handle); // 失败释放
            return null;
        }
    }

    #endregion
    
    #region 辅助方法

    /// <summary>
    /// 添加资源到缓存
    /// </summary>
    private void AddToCache(string key, AsyncOperationHandle handle)
    {
        _resourceCache[key] = new ResourceEntry()
        {
            Handle = handle,
            ReferenceCount = 1
        };
    }

    #endregion
    #endregion
}
