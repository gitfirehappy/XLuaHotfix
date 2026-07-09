using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Shared runtime package manager core.
/// Concrete AA/AB managers own backend initialization; this base owns query,
/// load, raw-file, and handle APIs.
/// </summary>
public abstract class PackageManagerBase
{
    #region Fields

    protected IAssetIndex _index;
    protected IPackageBackend _backend;
    protected bool _isInitialized = false;

    private readonly Dictionary<string, string[]> _labelToKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string[]> _typeToKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _addressSet = new(StringComparer.OrdinalIgnoreCase);

    #endregion

    public async Task Initialize()
    {
        await InitializePackageAsync();
    }

    public async Task<bool> InitializePackageAsync()
    {
        if (_isInitialized) return true;

        _labelToKeys.Clear();
        _typeToKeys.Clear();
        _addressSet.Clear();

        bool success = await InitializeBackendAsync();
        _isInitialized = success;
        return success;
    }

    protected abstract Task<bool> InitializeBackendAsync();

    /// <summary>
    /// 从已加载的 AAManifest 填充 _typeToKeys / _labelToKeys / _addressSet 查询缓存。
    /// </summary>
    protected bool TryInitializeAAIndexFromAAManifest(AAManifest manifest)
    {
        try
        {
            if (!HasAAIndex(manifest))
            {
                Debug.LogWarning("[AssetPackageManager] AAManifest 缺少索引数据。");
                return false;
            }

            _index = null;

            foreach (var item in manifest.KeysByType)
            {
                if (item == null || string.IsNullOrEmpty(item.Type))
                    continue;
                _typeToKeys[item.Type] = new List<string>(item.Keys ?? new List<string>()).ToArray();
            }

            foreach (var item in manifest.KeysByLabel)
            {
                if (item == null || string.IsNullOrEmpty(item.Label))
                    continue;
                _labelToKeys[item.Label] = new List<string>(item.Keys ?? new List<string>()).ToArray();
            }

            foreach (var entry in manifest.AssetEntries)
            {
                if (entry == null || string.IsNullOrEmpty(entry.key))
                    continue;
                _addressSet.Add(entry.key);
            }

            Debug.Log($"[{GetType().Name}] AA AAManifest 索引初始化完成。Entries: {manifest.AssetEntries.Count}");
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[AssetPackageManager] AAManifest 索引读取失败: {ex.Message}");
            return false;
        }
    }

    /// <summary>检测 AAManifest 是否包含完整的 AA 资源索引数据</summary>
    private static bool HasAAIndex(AAManifest manifest)
    {
        return manifest != null
               && manifest.AssetEntries != null
               && manifest.AssetEntries.Count > 0
               && manifest.KeysByType != null
               && manifest.KeysByLabel != null;
    }

    protected void BuildQueryCaches(IReadOnlyList<RuntimeAssetEntry> entries)
    {
        var typeListBuilder = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var labelListBuilder = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            _addressSet.Add(entry.Address);

            if (!string.IsNullOrEmpty(entry.PrimaryType))
            {
                if (!typeListBuilder.TryGetValue(entry.PrimaryType, out var typeList))
                {
                    typeList = new List<string>();
                    typeListBuilder[entry.PrimaryType] = typeList;
                }
                typeList.Add(entry.Address);
            }

            var labels = entry.Labels;
            for (int j = 0; j < labels.Count; j++)
            {
                string label = labels[j];
                if (string.IsNullOrEmpty(label)) continue;
                if (!labelListBuilder.TryGetValue(label, out var labelList))
                {
                    labelList = new List<string>();
                    labelListBuilder[label] = labelList;
                }
                labelList.Add(entry.Address);
            }
        }

        foreach (var kv in typeListBuilder)
            _typeToKeys[kv.Key] = kv.Value.ToArray();
        foreach (var kv in labelListBuilder)
            _labelToKeys[kv.Key] = kv.Value.ToArray();
    }

    #region 查询接口

    public IReadOnlyList<string> GetKeysByType(string type)
    {
        if (!_isInitialized) return Array.Empty<string>();
        return _typeToKeys.TryGetValue(type, out var list) ? list : Array.Empty<string>();
    }

    public IReadOnlyList<string> GetKeysByLabel(string label)
    {
        if (!_isInitialized) return Array.Empty<string>();
        return _labelToKeys.TryGetValue(label, out var list) ? list : Array.Empty<string>();
    }

    public List<string> GetKeysByLabels(string[] labels)
    {
        if (!_isInitialized || labels == null || labels.Length == 0)
            return new List<string>();

        if (labels.Length == 1)
            return new List<string>(GetKeysByLabel(labels[0]));

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

        var typeKeys = GetKeysByType(type);
        var labelKeys = new HashSet<string>(GetKeysByLabel(label));

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
        return _isInitialized && _addressSet.Contains(key);
    }

    #endregion

    #region 上层统一资源加载卸载接口

    public async Task<T> LoadAssetAsync<T>(string key) where T : UnityEngine.Object
    {
        if (!_isInitialized)
        {
            Debug.LogError("[AssetPackageManager] 未初始化");
            return null;
        }

        var (asset, error) = await _backend.LoadAssetAsync<T>(key);
        if (error != null)
            LogRuntimeMessage(error);
        return asset;
    }

    public async Task<List<T>> LoadAssetByLabelAsync<T>(string label) where T : UnityEngine.Object
    {
        if (!_isInitialized)
        {
            Debug.LogError("[AssetPackageManager] 未初始化");
            return new List<T>();
        }

        var keys = GetKeysByLabel(label);
        if (keys.Count == 0)
        {
            Debug.LogWarning($"[AssetPackageManager] 找不到标签: {label}");
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
            Debug.LogError("[AssetPackageManager] 未初始化");
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
            Debug.LogError("[AssetPackageManager] 未初始化");
            return null;
        }

        var (asset, error) = _backend.LoadAssetSync<T>(key);
        if (error != null)
            LogRuntimeMessage(error);
        return asset;
    }

    public async Task<byte[]> LoadRawBytesAsync(string address, IReadOnlyList<string> labels = null)
    {
        if (!_isInitialized || _index == null)
        {
            LogRuntimeMessage(RuntimeMessage.LoadFailed(address, "AssetPackageManager 未初始化或当前后端不支持 RawFile 条目查询"));
            return null;
        }

        var result = AssetResolver.ResolveRawByAddress(_index, address, labels);
        if (!result.IsSuccess)
        {
            LogRuntimeMessage(result.Error);
            return null;
        }

        var (data, error) = await _backend.LoadRawBytesAsync(result.Entry.Address, result.Entry.EntryId);
        if (error != null)
        {
            LogRuntimeMessage(error);
            return null;
        }

        return data;
    }

    public byte[] LoadRawBytesSync(string address, IReadOnlyList<string> labels = null)
    {
        if (!_isInitialized || _index == null)
        {
            LogRuntimeMessage(RuntimeMessage.LoadFailed(address, "AssetPackageManager 未初始化或当前后端不支持 RawFile 条目查询"));
            return null;
        }

        var result = AssetResolver.ResolveRawByAddress(_index, address, labels);
        if (!result.IsSuccess)
        {
            LogRuntimeMessage(result.Error);
            return null;
        }

        var (data, error) = _backend.LoadRawBytesSync(result.Entry.Address, result.Entry.EntryId);
        if (error != null)
        {
            LogRuntimeMessage(error);
            return null;
        }

        return data;
    }

    public async Task<string> LoadRawTextAsync(
        string address,
        IReadOnlyList<string> labels = null,
        Encoding encoding = null)
    {
        byte[] data = await LoadRawBytesAsync(address, labels);
        return data != null ? (encoding ?? Encoding.UTF8).GetString(data) : null;
    }

    public string LoadRawTextSync(
        string address,
        IReadOnlyList<string> labels = null,
        Encoding encoding = null)
    {
        byte[] data = LoadRawBytesSync(address, labels);
        return data != null ? (encoding ?? Encoding.UTF8).GetString(data) : null;
    }

    #endregion

    #region Resolve / Load API（基于条目解析的加载接口）

    /// <summary>
    /// 通过 Address 异步加载资源，返回 AssetHandle。
    /// 内部先 Resolve 得到唯一条目，再通过 backend 加载，通过 HandleRegistry 分配句柄。
    /// </summary>
    public async Task<AssetHandle<T>> LoadByAddress<T>(string address) where T : UnityEngine.Object
    {
        if (!_isInitialized || _index == null)
            return new AssetHandle<T>(
                RuntimeMessage.LoadFailed("", "AssetPackageManager 未初始化或不支持条目级查询"));

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
        if (!_isInitialized || _index == null)
            return new AssetHandle<T>(
                RuntimeMessage.LoadFailed("", "AssetPackageManager 未初始化或不支持条目级查询"));

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
        if (!_isInitialized || _index == null)
            return new AssetHandle<T>(
                RuntimeMessage.LoadFailed("", "AssetPackageManager 未初始化或不支持条目级查询"));

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
        if (!_isInitialized || _index == null)
            return new AssetHandle<T>(
                RuntimeMessage.LoadFailed("", "AssetPackageManager 未初始化或不支持条目级查询"));

        var result = AssetResolver.ResolveByTypeKey<T>(_index, key, labels);
        if (!result.IsSuccess)
            return new AssetHandle<T>(result.Error);

        return LoadResolvedSync<T>(result.Entry);
    }

    #endregion

    #region Resolve / Load Helpers

    private async Task<AssetHandle<T>> LoadResolvedAsync<T>(RuntimeAssetEntry entry) where T : UnityEngine.Object
    {
        if (entry.PayloadKind == EPayloadKind.RawFile)
            return new AssetHandle<T>(
                RuntimeMessage.InvalidPayloadKind(entry.EntryId, EPayloadKind.Serialized, entry.PayloadKind));

        var abBackend = _backend as ABPackageBackend;
        if (abBackend != null)
            return await LoadResolvedWithABAsync<T>(abBackend, entry);

        return await LoadResolvedWithAAAsync<T>(entry);
    }

    private AssetHandle<T> LoadResolvedSync<T>(RuntimeAssetEntry entry) where T : UnityEngine.Object
    {
        if (entry.PayloadKind == EPayloadKind.RawFile)
            return new AssetHandle<T>(
                RuntimeMessage.InvalidPayloadKind(entry.EntryId, EPayloadKind.Serialized, entry.PayloadKind));

        var abBackend = _backend as ABPackageBackend;
        if (abBackend != null)
            return LoadResolvedWithABSync<T>(abBackend, entry);

        return LoadResolvedWithAASync<T>(entry);
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

    private async Task<AssetHandle<T>> LoadResolvedWithAAAsync<T>(RuntimeAssetEntry entry)
        where T : UnityEngine.Object
    {
        var (asset, loadError) = await _backend.LoadAssetAsync<T>(entry.Address, entry.EntryId);
        if (loadError != null)
            return new AssetHandle<T>(loadError);

        if (asset == null)
            return new AssetHandle<T>(RuntimeMessage.LoadFailed(entry.EntryId, "Backend 返回 null"));

        return CreateAAHandle(entry, asset);
    }

    private AssetHandle<T> LoadResolvedWithAASync<T>(RuntimeAssetEntry entry)
        where T : UnityEngine.Object
    {
        var (asset, loadError) = _backend.LoadAssetSync<T>(entry.Address, entry.EntryId);
        if (loadError != null)
            return new AssetHandle<T>(loadError);

        if (asset == null)
            return new AssetHandle<T>(RuntimeMessage.LoadFailed(entry.EntryId, "Backend 返回 null"));

        return CreateAAHandle(entry, asset);
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

    private AssetHandle<T> CreateAAHandle<T>(RuntimeAssetEntry entry, T asset)
        where T : UnityEngine.Object
    {
        var releaseEntryId = entry.EntryId;
        var (handleId, generation) = HandleRegistry.Alloc(
            entry.EntryId,
            "",
            null,
            _ => _backend.UnloadByEntryId(releaseEntryId));

        return new AssetHandle<T>(handleId, generation, asset);
    }

    #endregion

    private static void LogRuntimeMessage(RuntimeMessage message)
    {
        if (message == null)
            return;

        if (message.Severity == RuntimeSeverity.Warning)
            Debug.LogWarning(message.ToString());
        else
            Debug.LogError(message.ToString());
    }
}
