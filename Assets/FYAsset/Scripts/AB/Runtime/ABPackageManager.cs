using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// AB 运行时包加载入口：索引查询 + Backend 加载/卸载。
/// </summary>
public sealed class ABPackageManager
{
    private static readonly object LockObject = new();
    private static ABPackageManager _instance;

    private readonly Dictionary<string, string[]> _labelToKeys =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string[]> _typeToKeys =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _addressSet =
        new(StringComparer.OrdinalIgnoreCase);

    private ABAssetIndex _index;
    private IABLoadBackend _backend;
    private bool _isInitialized;

#if UNITY_EDITOR
    private static Func<ABManifest> _editorManifestBuilder;
#endif

    public static ABPackageManager Instance
    {
        get
        {
            lock (LockObject)
            {
                return _instance ??= new ABPackageManager();
            }
        }
    }

    #region Initialization

#if UNITY_EDITOR
    public static void RegisterEditorManifestBuilder(Func<ABManifest> builder)
    {
        _editorManifestBuilder = builder;
    }
#endif

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

#if UNITY_EDITOR
        // Editor PlayMode：Collector 扫描 + AssetDatabase，不读磁盘 AB
        if (FYAssetSettings.Instance.UseABBackend
            && FYAssetSettings.Instance.PlayMode == EPlayMode.Editor)
        {
            return InitializeEditorPlayMode();
        }
#endif

        var manifest = await ABManifestLoader.LoadAsync();
        if (manifest == null)
        {
            Debug.LogWarning(
                "[ABPackageManager] ABManifest 加载失败。请检查 AB 资源是否已正确构建并部署到热更目录或 StreamingAssets。");
            return false;
        }

        _index = new ABAssetIndex(manifest);
        _backend = new ABPackageBackend(manifest, new ABBundleLoader(manifest));
        BuildQueryCaches(_index.GetAllEntries());
        _isInitialized = true;

        Debug.Log(
            $"[ABPackageManager] AB 全链路初始化完成。" +
            $"Assets: {manifest.AssetCount}, Bundles: {manifest.BundleCount}, " +
            "Index: ABAssetIndex, Backend: ABPackageBackend");
        return true;
    }

#if UNITY_EDITOR
    private bool InitializeEditorPlayMode()
    {
        ABManifest manifest = _editorManifestBuilder?.Invoke();
        if (manifest == null)
        {
            Debug.LogWarning("[ABPackageManager] Editor PlayMode 索引构建失败。");
            return false;
        }

        _index = new ABAssetIndex(manifest);
        _backend = new EditorPackageBackend(manifest);
        BuildQueryCaches(_index.GetAllEntries());
        _isInitialized = true;

        Debug.Log(
            $"[ABPackageManager] Editor PlayMode 初始化完成。" +
            $"Assets: {manifest.AssetCount}, Backend: EditorPackageBackend");
        return true;
    }
#endif

    #endregion

    #region Queries

    public IReadOnlyList<string> GetKeysByType(string type)
    {
        if (!_isInitialized) return Array.Empty<string>();
        return _typeToKeys.TryGetValue(type, out var keys) ? keys : Array.Empty<string>();
    }

    public IReadOnlyList<string> GetKeysByLabel(string label)
    {
        if (!_isInitialized) return Array.Empty<string>();
        return _labelToKeys.TryGetValue(label, out var keys) ? keys : Array.Empty<string>();
    }

    public List<string> GetKeysByLabels(string[] labels)
    {
        if (!_isInitialized || labels == null || labels.Length == 0)
            return new List<string>();

        var keys = new HashSet<string>(GetKeysByLabel(labels[0]));
        for (int i = 1; i < labels.Length; i++)
            keys.IntersectWith(GetKeysByLabel(labels[i]));
        return new List<string>(keys);
    }

    public List<string> GetKeysByTypeAndLabel(string type, string label)
    {
        var typeKeys = GetKeysByType(type);
        var labelKeys = new HashSet<string>(GetKeysByLabel(label));
        var result = new List<string>();
        for (int i = 0; i < typeKeys.Count; i++)
        {
            if (labelKeys.Contains(typeKeys[i]))
                result.Add(typeKeys[i]);
        }
        return result;
    }

    public bool ContainsKey(string key) => _isInitialized && _addressSet.Contains(key);

    #endregion

    #region Common Typed Address API

    public async Task<(T asset, RuntimeMessage error)> LoadAssetAsync<T>(string address)
        where T : UnityEngine.Object
    {
        if (!TryResolve<T>(address, out var entry, out var error))
            return (null, error);
        return await _backend.LoadAssetAsync<T>(entry.Address, entry.EntryId);
    }

    public (T asset, RuntimeMessage error) LoadAssetSync<T>(string address)
        where T : UnityEngine.Object
    {
        if (!TryResolve<T>(address, out var entry, out var error))
            return (null, error);
        return _backend.LoadAssetSync<T>(entry.Address, entry.EntryId);
    }

    public void UnloadAsset<T>(string address) where T : UnityEngine.Object
    {
        if (!TryResolve<T>(address, out var entry, out var error))
        {
            LogRuntimeMessage(error);
            return;
        }

        _backend.UnloadByEntryId(entry.EntryId);
    }

    #endregion

    #region RawFile API

    public async Task<byte[]> LoadRawBytesAsync(
        string address,
        IReadOnlyList<string> labels = null)
    {
        var result = ResolveRaw(address, labels);
        if (!result.IsSuccess)
        {
            LogRuntimeMessage(result.Error);
            return null;
        }

        var (data, error) = await _backend.LoadRawBytesAsync(result.Entry.Address, result.Entry.EntryId);
        if (error != null) LogRuntimeMessage(error);
        return error == null ? data : null;
    }

    public byte[] LoadRawBytesSync(string address, IReadOnlyList<string> labels = null)
    {
        var result = ResolveRaw(address, labels);
        if (!result.IsSuccess)
        {
            LogRuntimeMessage(result.Error);
            return null;
        }

        var (data, error) = _backend.LoadRawBytesSync(result.Entry.Address, result.Entry.EntryId);
        if (error != null) LogRuntimeMessage(error);
        return error == null ? data : null;
    }

    public async Task<string> LoadRawTextAsync(
        string address,
        IReadOnlyList<string> labels = null,
        Encoding encoding = null)
    {
        byte[] data = await LoadRawBytesAsync(address, labels);
        return data == null ? null : (encoding ?? Encoding.UTF8).GetString(data);
    }

    public string LoadRawTextSync(
        string address,
        IReadOnlyList<string> labels = null,
        Encoding encoding = null)
    {
        byte[] data = LoadRawBytesSync(address, labels);
        return data == null ? null : (encoding ?? Encoding.UTF8).GetString(data);
    }

    #endregion

    #region Handle API

    public async Task<AssetHandle<T>> LoadByAddress<T>(string address)
        where T : UnityEngine.Object
    {
        if (!TryResolve<T>(address, out var entry, out var error))
            return new AssetHandle<T>(error);
        return await LoadResolvedAsync<T>(entry);
    }

    public AssetHandle<T> LoadByAddressSync<T>(string address)
        where T : UnityEngine.Object
    {
        if (!TryResolve<T>(address, out var entry, out var error))
            return new AssetHandle<T>(error);
        return LoadResolvedSync<T>(entry);
    }

    public async Task<AssetHandle<T>> LoadByTypeKey<T>(
        string key,
        IReadOnlyList<string> labels = null) where T : UnityEngine.Object
    {
        ResolveResult result = ResolveTypeKey<T>(key, labels);
        if (!result.IsSuccess)
            return new AssetHandle<T>(result.Error);
        return await LoadResolvedAsync<T>(result.Entry);
    }

    public AssetHandle<T> LoadByTypeKeySync<T>(
        string key,
        IReadOnlyList<string> labels = null) where T : UnityEngine.Object
    {
        ResolveResult result = ResolveTypeKey<T>(key, labels);
        if (!result.IsSuccess)
            return new AssetHandle<T>(result.Error);
        return LoadResolvedSync<T>(result.Entry);
    }

    private async Task<AssetHandle<T>> LoadResolvedAsync<T>(RuntimeAssetEntry entry)
        where T : UnityEngine.Object
    {
        if (entry.PayloadKind == EPayloadKind.RawFile)
            return InvalidPayloadHandle<T>(entry);

        var (asset, bundleName, error) =
            await _backend.LoadAssetTupleAsync<T>(entry.Address, entry.EntryId);
        return CreateHandle(entry, asset, bundleName, error);
    }

    private AssetHandle<T> LoadResolvedSync<T>(RuntimeAssetEntry entry)
        where T : UnityEngine.Object
    {
        if (entry.PayloadKind == EPayloadKind.RawFile)
            return InvalidPayloadHandle<T>(entry);

        var (asset, bundleName, error) =
            _backend.LoadAssetTupleSync<T>(entry.Address, entry.EntryId);
        return CreateHandle(entry, asset, bundleName, error);
    }

    private AssetHandle<T> CreateHandle<T>(
        RuntimeAssetEntry entry,
        T asset,
        string bundleName,
        RuntimeMessage error) where T : UnityEngine.Object
    {
        if (error != null)
            return new AssetHandle<T>(error);
        if (asset == null)
            return new AssetHandle<T>(RuntimeMessage.LoadFailed(entry.EntryId, "Backend 返回 null"));

        var (handleId, generation) = HandleRegistry.Alloc(
            entry.EntryId,
            bundleName ?? "",
            null,
            _backend.UnloadByEntryId);
        return new AssetHandle<T>(handleId, generation, asset);
    }

    private static AssetHandle<T> InvalidPayloadHandle<T>(RuntimeAssetEntry entry)
        where T : UnityEngine.Object
    {
        return new AssetHandle<T>(RuntimeMessage.InvalidPayloadKind(
            entry.EntryId,
            EPayloadKind.Serialized.ToString(),
            entry.PayloadKind.ToString()));
    }

    #endregion

    #region Resolve Helpers

    private bool TryResolve<T>(
        string address,
        out RuntimeAssetEntry entry,
        out RuntimeMessage error) where T : UnityEngine.Object
    {
        entry = null;
        if (!_isInitialized || _index == null || _backend == null)
        {
            error = RuntimeMessage.LoadFailed(address, "ABPackageManager 未初始化");
            return false;
        }

        ResolveResult result = AssetResolver.ResolveByAddress<T>(_index, address);
        entry = result.Entry;
        error = result.Error;
        return result.IsSuccess;
    }

    private ResolveResult ResolveRaw(string address, IReadOnlyList<string> labels)
    {
        if (!_isInitialized || _index == null || _backend == null)
            return ResolveResult.NotFound("ABPackageManager 未初始化");
        return AssetResolver.ResolveRawByAddress(_index, address, labels);
    }

    private ResolveResult ResolveTypeKey<T>(string key, IReadOnlyList<string> labels)
        where T : UnityEngine.Object
    {
        if (!_isInitialized || _index == null || _backend == null)
            return ResolveResult.NotFound("ABPackageManager 未初始化");
        return AssetResolver.ResolveByTypeKey<T>(_index, key, labels);
    }

    private void BuildQueryCaches(IReadOnlyList<RuntimeAssetEntry> entries)
    {
        var typeLists = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var labelLists = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < entries.Count; i++)
        {
            RuntimeAssetEntry entry = entries[i];
            _addressSet.Add(entry.Address);
            AddQueryValue(typeLists, entry.PrimaryType, entry.Address);
            for (int j = 0; j < entry.Labels.Count; j++)
                AddQueryValue(labelLists, entry.Labels[j], entry.Address);
        }

        foreach (var item in typeLists)
            _typeToKeys[item.Key] = item.Value.ToArray();
        foreach (var item in labelLists)
            _labelToKeys[item.Key] = item.Value.ToArray();
    }

    private static void AddQueryValue(
        Dictionary<string, List<string>> values,
        string key,
        string address)
    {
        if (string.IsNullOrEmpty(key)) return;
        if (!values.TryGetValue(key, out var list))
        {
            list = new List<string>();
            values[key] = list;
        }
        list.Add(address);
    }

    private static void LogRuntimeMessage(RuntimeMessage message)
    {
        if (message == null) return;
        if (message.Severity == RuntimeSeverity.Warning)
            Debug.LogWarning(message.ToString());
        else
            Debug.LogError(message.ToString());
    }

    #endregion
}
