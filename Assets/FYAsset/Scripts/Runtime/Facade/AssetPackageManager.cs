using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

public class AssetPackageManager : Singleton<AssetPackageManager>
{
    #region Fields

    private IAssetIndex _index;
    private IPackageBackend _backend = new AddressablesBackend();
    private bool _isInitialized = false;

    private readonly Dictionary<string, string[]> _labelToKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string[]> _typeToKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _addressSet = new(StringComparer.OrdinalIgnoreCase);

    #endregion

    public async Task Initialize()
    {
        if (_isInitialized) return;

        _labelToKeys.Clear();
        _typeToKeys.Clear();
        _addressSet.Clear();

        if (FYAssetSettings.Instance.UseABBackend)
            await InitializeWithABIndex();
        else
            await InitializeWithLegacyIndex();

        _isInitialized = true;
    }

    #region 初始化路径

    /// <summary>
    /// AB 索引路径：ManifestLoader -> ABManifest -> ABAssetIndex + ABBundleLoader + ABPackageBackend。
    /// 同时初始化索引和加载后端，一个开关控制两个维度。
    /// 加载失败回退到 Legacy 路径并发出结构化警告，避免静默进入不可用状态。
    /// </summary>
    private async Task InitializeWithABIndex()
    {
        var manifest = await ManifestLoader.LoadAsync();
        if (manifest == null)
        {
            Debug.LogWarning(
                "[AssetPackageManager] ABManifest 加载失败，回退到 Legacy (Addressables) 路径。" +
                "请检查 AB 资源是否已正确构建并部署到热更目录或 StreamingAssets。");
            await InitializeWithLegacyIndex();
            return;
        }

        // 初始化 AB 索引
        _index = new ABAssetIndex(manifest);

        // 初始化 AB 加载后端
        var bundleLoader = new ABBundleLoader(manifest);
        var abBackend = new ABPackageBackend(manifest, bundleLoader);
        _backend = abBackend;

        // 从索引自建 query 缓存
        BuildQueryCaches(_index.GetAllEntries());

        Debug.Log(
            $"[AssetPackageManager] AB 全链路初始化完成。" +
            $"Assets: {manifest.AssetCount}, Bundles: {manifest.BundleCount}, " +
            $"Index: ABAssetIndex, Backend: ABPackageBackend");
    }

    /// <summary>
    /// Legacy 索引路径：从当前包目录的 AAManifest 构建查询缓存。
    /// </summary>
    private Task InitializeWithLegacyIndex()
    {
        if (!TryInitializeLegacyIndexFromAAManifest())
            Debug.LogError("[AssetPackageManager] Legacy AAManifest 索引初始化失败。");

        return Task.CompletedTask;
    }

    #endregion

    /// <summary>
    /// 从当前 GUID 目录读取 AAManifest（优先 .bin，回退 .json），
    /// 填充 _typeToKeys / _labelToKeys / _addressSet 查询缓存。
    /// </summary>
    private bool TryInitializeLegacyIndexFromAAManifest()
    {
        if (string.IsNullOrEmpty(PathManager.CurrentGUIDRoot))
        {
            Debug.LogWarning("[AssetPackageManager] PathManager.CurrentGUIDRoot 为空，无法读取 AAManifest。");
            return false;
        }

        string manifestPath = GetAAManifestPath(PathManager.CurrentGUIDRoot);
        if (!FileHelper.Exists(manifestPath))
        {
            Debug.LogWarning($"[AssetPackageManager] 未找到 AAManifest: {PathManager.CurrentGUIDRoot}");
            return false;
        }

        try
        {
            var manifest = SerializationUtility.ReadFromFile<AAManifest>(manifestPath);
            if (!HasLegacyIndex(manifest))
            {
                Debug.LogWarning($"[AssetPackageManager] AAManifest 缺少索引数据: {manifestPath}");
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

            Debug.Log($"[AssetPackageManager] Legacy AAManifest 索引初始化完成。Entries: {manifest.AssetEntries.Count}");
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[AssetPackageManager] AAManifest 索引读取失败: {ex.Message}");
            return false;
        }
    }

    /// <summary>检测 AAManifest 是否包含完整的 AA 资源索引数据</summary>
    private static bool HasLegacyIndex(AAManifest manifest)
    {
        return manifest != null
               && manifest.AssetEntries != null
               && manifest.AssetEntries.Count > 0
               && manifest.KeysByType != null
               && manifest.KeysByLabel != null;
    }

    /// <summary>获取 AAManifest 路径：优先 .bin 二进制格式，回退 .json</summary>
    private static string GetAAManifestPath(string packageRoot)
    {
        string binPath = Path.Combine(packageRoot, FYAssetSettings.AA_MANIFEST_FILE_NAME_BIN);
        if (FileHelper.Exists(binPath))
            return binPath;

        return Path.Combine(packageRoot, FYAssetSettings.AA_MANIFEST_FILE_NAME);
    }

    private void BuildQueryCaches(IReadOnlyList<RuntimeAssetEntry> entries)
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
        var (asset, loadError) = await _backend.LoadAssetAsync<T>(entry.Address, entry.EntryId);
        if (loadError != null)
            return new AssetHandle<T>(loadError);

        if (asset == null)
            return new AssetHandle<T>(RuntimeMessage.LoadFailed(entry.EntryId, "Backend 返回 null"));

        return CreateLegacyHandle(entry, asset);
    }

    private AssetHandle<T> LoadResolvedWithLegacySync<T>(RuntimeAssetEntry entry)
        where T : UnityEngine.Object
    {
        var (asset, loadError) = _backend.LoadAssetSync<T>(entry.Address, entry.EntryId);
        if (loadError != null)
            return new AssetHandle<T>(loadError);

        if (asset == null)
            return new AssetHandle<T>(RuntimeMessage.LoadFailed(entry.EntryId, "Backend 返回 null"));

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
