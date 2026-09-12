using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// AB 运行时包加载入口：公共查询 + 按 Address 加载/释放 + 场景加载 + Shutdown。
/// </summary>
/// <remarks>
/// 查询与解析全部由 ABAssetIndex / AssetResolver 承担，本类只做参数校验、句柄分配与错误日志。
/// 每个 Load 返回一个独立 token；调用方必须配对 Release，最后一个 token 释放后才卸载内容。
/// Shutdown 在仍有活跃 Handle 时拒绝执行，确保热更 Apply 只能在没有资源使用时切换包根。
/// </remarks>
public sealed class ABPackageManager
{
    private static readonly object LockObject = new();
    private static ABPackageManager _instance;

    private ABAssetIndex _index;
    private IABLoadBackend _backend;
    private ABSceneLoader _sceneLoader;
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

    /// <summary>
    /// 建立索引与后端。已初始化时直接返回 true；Shutdown 之后可以重新初始化。
    /// </summary>
    public async Task<bool> InitializePackageAsync()
    {
        if (_isInitialized) return true;

#if UNITY_EDITOR
        // ABPackageManager 只能由 Compat facade 在选择 AB 后调用；PlayMode 只描述 AB 的加载方式。
        if (FYAssetABSettings.Instance.PlayMode == EPlayMode.Editor)
        {
            return InitializeEditorPlayMode();
        }
#endif

        var manifest = await ABManifestLoader.LoadAsync();
        if (manifest == null)
        {
            Debug.LogWarning(
                "[ABPackageManager] ABManifest 加载失败。请检查 AB 资源是否已构建并部署到当前激活包根。");
            return false;
        }

        var bundleLoader = new ABBundleLoader(manifest);
        return InitializeFromManifest(manifest, bundleLoader, new ABPackageBackend(manifest, bundleLoader));
    }

    /// <summary>
    /// 关闭资源管理器：清空内容引用、句柄槽位与索引，使后续 Initialize 可以重新加载。
    /// </summary>
    /// <remarks>
    /// 仍有活跃 Handle 时返回结构化错误并保持现状（不释放、不清计数），
    /// 因为此时卸载内容会让调用方手里的句柄指向已销毁对象。
    /// 成功时先卸载全部 Bundle（此时已无活跃 Handle），再重置句柄槽位。
    /// </remarks>
    public RuntimeMessage Shutdown()
    {
        if (!_isInitialized)
            return null;

        int activeCount = HandleRegistry.ActiveCount;
        if (activeCount > 0)
        {
            RuntimeMessage error = RuntimeMessage.ActiveHandlesBlockShutdown(
                activeCount,
                string.Concat(
                    "Asset=", HandleRegistry.AssetActiveCount.ToString(),
                    ", Scene=", HandleRegistry.SceneActiveCount.ToString()));
            Debug.LogWarning(error.ToString());
            return error;
        }

        _backend?.UnloadAllContent();
        _sceneLoader?.Clear();
        HandleRegistry.Reset();

        _backend = null;
        _index = null;
        _sceneLoader = null;
        _isInitialized = false;

        Debug.Log("[ABPackageManager] 已关闭，句柄与内容引用全部清空。");
        return null;
    }

    #region 公共查询

    /// <summary>指定资源类型（T 的类名）对应的公共 Address。</summary>
    public IReadOnlyList<string> GetAddressesByType<T>() where T : UnityEngine.Object
    {
        if (!_isInitialized || _index == null) return Array.Empty<string>();
        return _index.GetAddressesByType(typeof(T).Name);
    }

    /// <summary>指定 Label 对应的公共 Address（大小写不敏感）。</summary>
    public IReadOnlyList<string> GetAddressesByLabel(string label)
    {
        if (!_isInitialized || _index == null) return Array.Empty<string>();
        return _index.GetAddressesByLabel(label);
    }

    /// <summary>同时命中资源类型与 Label 的公共 Address。</summary>
    public IReadOnlyList<string> GetAddressesByTypeAndLabel<T>(string label) where T : UnityEngine.Object
    {
        if (!_isInitialized || _index == null) return Array.Empty<string>();
        return _index.GetAddressesByTypeAndLabel(typeof(T).Name, label);
    }

    /// <summary>公共 Address 是否存在（大小写不敏感）。</summary>
    public bool ContainsAddress(string address)
    {
        if (!_isInitialized || _index == null) return false;
        return _index.ContainsAddress(address);
    }

    #endregion

    #region 资源加载

    public async Task<AssetHandle<T>> LoadByAddress<T>(string address)
        where T : UnityEngine.Object
    {
        if (!TryGetIndex(address, out ABAssetIndex index, out RuntimeMessage error))
            return new AssetHandle<T>(error);

        ResolveResult result = AssetResolver.ResolveByAddress(index, address);
        if (!result.IsSuccess)
            return new AssetHandle<T>(result.Error);

        return await LoadResolvedAsync<T>(result.Entry);
    }

    public AssetHandle<T> LoadByAddressSync<T>(string address)
        where T : UnityEngine.Object
    {
        if (!TryGetIndex(address, out ABAssetIndex index, out RuntimeMessage error))
            return new AssetHandle<T>(error);

        ResolveResult result = AssetResolver.ResolveByAddress(index, address);
        if (!result.IsSuccess)
            return new AssetHandle<T>(result.Error);

        return LoadResolvedSync<T>(result.Entry);
    }

    /// <summary>
    /// 批量加载指定类型（T 的类名）的全部公共资源。
    /// </summary>
    /// <remarks>逐项独立成败，一项失败不影响其他项；调用方对每个 handle 单独 Release。</remarks>
    public Task<IReadOnlyList<AssetHandle<T>>> LoadByType<T>() where T : UnityEngine.Object
    {
        return LoadEntriesAsync<T>(GetPublicEntriesByType<T>());
    }

    /// <summary>
    /// 批量加载同时命中全部 Label 的公共资源，不做类型过滤。
    /// </summary>
    /// <remarks>
    /// 结果可能包含请求类型 T 之外的资源，这些条目的 handle 会携带引用类型不匹配的失败信息；
    /// 需要限定类型时使用 LoadByTypeAndLabels。
    /// </remarks>
    public Task<IReadOnlyList<AssetHandle<T>>> LoadByLabels<T>(IReadOnlyList<string> labels)
        where T : UnityEngine.Object
    {
        if (!_isInitialized || _index == null || labels == null || labels.Count == 0)
            return Task.FromResult<IReadOnlyList<AssetHandle<T>>>(Array.Empty<AssetHandle<T>>());

        IReadOnlyList<RuntimeAssetEntry> all = _index.GetAllEntries();
        var matched = new List<RuntimeAssetEntry>();
        for (int i = 0; i < all.Count; i++)
        {
            RuntimeAssetEntry entry = all[i];
            if (!IsPublicAddressCandidate(entry)) continue;
            if (!entry.HasAllLabels(labels)) continue;
            matched.Add(entry);
        }

        return LoadEntriesAsync<T>(matched);
    }

    /// <summary>
    /// 批量加载指定类型（T 的类名）且同时命中全部 Label 的公共资源。
    /// </summary>
    public Task<IReadOnlyList<AssetHandle<T>>> LoadByTypeAndLabels<T>(IReadOnlyList<string> labels)
        where T : UnityEngine.Object
    {
        IReadOnlyList<RuntimeAssetEntry> entries = GetPublicEntriesByType<T>();
        if (labels == null || labels.Count == 0 || entries.Count == 0)
            return Task.FromResult<IReadOnlyList<AssetHandle<T>>>(Array.Empty<AssetHandle<T>>());

        var matched = new List<RuntimeAssetEntry>();
        for (int i = 0; i < entries.Count; i++)
        {
            if (entries[i].HasAllLabels(labels))
                matched.Add(entries[i]);
        }

        return LoadEntriesAsync<T>(matched);
    }

    private async Task<IReadOnlyList<AssetHandle<T>>> LoadEntriesAsync<T>(IReadOnlyList<RuntimeAssetEntry> entries)
        where T : UnityEngine.Object
    {
        if (entries == null || entries.Count == 0)
            return Array.Empty<AssetHandle<T>>();

        var handles = new List<AssetHandle<T>>(entries.Count);
        for (int i = 0; i < entries.Count; i++)
        {
            // 逐项 await：单项失败只体现在该项的 handle 上，不释放其他成功项
            handles.Add(await LoadResolvedAsync<T>(entries[i]));
        }

        return handles;
    }

    private async Task<AssetHandle<T>> LoadResolvedAsync<T>(RuntimeAssetEntry entry)
        where T : UnityEngine.Object
    {
        var (asset, contentFileName, error) =
            await _backend.LoadAssetTupleAsync<T>(entry.Address, entry.EntryId);
        return CreateHandle(entry, asset, contentFileName, error);
    }

    private AssetHandle<T> LoadResolvedSync<T>(RuntimeAssetEntry entry)
        where T : UnityEngine.Object
    {
        var (asset, contentFileName, error) =
            _backend.LoadAssetTupleSync<T>(entry.Address, entry.EntryId);
        return CreateHandle(entry, asset, contentFileName, error);
    }

    private AssetHandle<T> CreateHandle<T>(
        RuntimeAssetEntry entry,
        T asset,
        string contentFileName,
        RuntimeMessage error) where T : UnityEngine.Object
    {
        if (error != null)
            return new AssetHandle<T>(error);
        if (asset == null)
            return new AssetHandle<T>(RuntimeMessage.LoadFailed(entry.EntryId, "Backend 返回 null"));

        var (tokenId, generation) = HandleRegistry.Alloc(
            entry.EntryId,
            HandleKind.Asset,
            contentFileName ?? "",
            null,
            _backend.UnloadByEntryId);
        return new AssetHandle<T>(tokenId, generation, asset);
    }

    #endregion

    #region RawFile

    /// <summary>读取公共 RawFile 的原始字节。返回 null 表示失败，失败原因已输出日志。</summary>
    public async Task<byte[]> LoadRawBytesAsync(string address)
    {
        if (!TryGetIndex(address, out ABAssetIndex index, out RuntimeMessage error))
        {
            LogRuntimeMessage(error);
            return null;
        }

        ResolveResult result = AssetResolver.ResolveRawByAddress(index, address);
        if (!result.IsSuccess)
        {
            LogRuntimeMessage(result.Error);
            return null;
        }

        var (data, rawError) = await _backend.LoadRawBytesAsync(result.Entry.Address, result.Entry.EntryId);
        if (rawError != null)
        {
            LogRuntimeMessage(rawError);
            return null;
        }

        return data;
    }

    /// <summary>读取公共 RawFile 并按 encoding（默认 UTF-8）解码。</summary>
    public async Task<string> LoadRawTextAsync(string address, Encoding encoding = null)
    {
        byte[] data = await LoadRawBytesAsync(address);
        return data == null ? null : (encoding ?? Encoding.UTF8).GetString(data);
    }

    #endregion

    #region 场景加载

    /// <summary>
    /// 加载公共 Scene 资源。
    /// </summary>
    /// <param name="address">公共 Scene Address</param>
    /// <param name="mode">Single 会替换当前场景；Additive 由调用方通过 SceneHandle.UnloadAsync 卸载</param>
    /// <param name="activateOnLoad">false 时先做有界预载再激活，不会无限等待</param>
    public async Task<SceneHandle> LoadSceneAsync(
        string address,
        LoadSceneMode mode,
        bool activateOnLoad = true)
    {
        if (!_isInitialized || _index == null || _backend == null || _sceneLoader == null)
            return new SceneHandle(
                RuntimeMessage.LoadFailed(address, "ABPackageManager 未初始化"), address, null);

        ResolveResult result = AssetResolver.ResolveSceneByAddress(_index, address);
        if (!result.IsSuccess)
        {
            LogRuntimeMessage(result.Error);
            return new SceneHandle(result.Error, address, null);
        }

        SceneHandle handle = await _sceneLoader.LoadSceneAsync(result.Entry, mode, activateOnLoad);
        if (!handle.IsValid)
            LogRuntimeMessage(handle.Error);
        return handle;
    }

    #endregion

    #region 内部实现

    private bool InitializeFromManifest(ABManifest manifest, ABBundleLoader bundleLoader, IABLoadBackend backend)
    {
        var index = new ABAssetIndex(manifest);
        if (!index.IsValid)
        {
            // 索引不可用时拒绝初始化：继续加载会在不确定的地址集合上工作
            Debug.LogError($"[ABPackageManager] ABAssetIndex 构建失败: {index.BuildError}");
            return false;
        }

        _index = index;
        _backend = backend;
        // 场景加载与资源加载共用同一个 BundleLoader，否则两者会各自持有缓存与引用计数
        _sceneLoader = new ABSceneLoader(manifest, bundleLoader);
        _isInitialized = true;

        Debug.Log(
            $"[ABPackageManager] AB 全链路初始化完成。" +
            $"Assets: {manifest.AssetCount}, Contents: {manifest.ContentCount}, " +
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

        // Editor PlayMode 经 AssetDatabase 加载：没有 BundleLoader，场景同样按工程路径直接加载
        return InitializeFromManifest(manifest, null, new EditorPackageBackend(manifest));
    }
#endif

    private bool TryGetIndex(string address, out ABAssetIndex index, out RuntimeMessage error)
    {
        index = null;
        if (!_isInitialized || _index == null || _backend == null)
        {
            error = RuntimeMessage.LoadFailed(address, "ABPackageManager 未初始化");
            return false;
        }

        index = _index;
        error = null;
        return true;
    }

    private IReadOnlyList<RuntimeAssetEntry> GetPublicEntriesByType<T>() where T : UnityEngine.Object
    {
        if (!_isInitialized || _index == null) return Array.Empty<RuntimeAssetEntry>();
        return _index.GetEntriesByType(typeof(T).Name);
    }

    private static bool IsPublicAddressCandidate(RuntimeAssetEntry entry)
    {
        return entry != null && entry.IsPublic && !string.IsNullOrEmpty(entry.Address);
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
