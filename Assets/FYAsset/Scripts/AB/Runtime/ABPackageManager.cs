using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>AB 运行时唯一组合根：解析 Address，并协调 Bundle、Asset、Scene 与 RawFile 加载。</summary>
public sealed class ABPackageManager
{
    private static readonly object LockObject = new();
    private static ABPackageManager _instance;

    private ABManifest _manifest;
    private ABAssetIndex _index;
    private IABAssetLoader _assetLoader;
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
                return _instance ??= new ABPackageManager();
        }
    }

#if UNITY_EDITOR
    public static void RegisterEditorManifestBuilder(Func<ABManifest> builder) => _editorManifestBuilder = builder;
#endif

    public async Task Initialize() => await InitializePackageAsync();

    public async Task<bool> InitializePackageAsync()
    {
        if (_isInitialized)
            return true;
#if UNITY_EDITOR
        if (FYAssetABSettings.Instance.PlayMode == EPlayMode.Editor)
            return InitializeEditorPlayMode();
#endif
        ABManifest manifest = await LoadActiveManifestAsync();
        if (manifest == null)
            return false;
        var bundleLoader = new ABBundleLoader(manifest);
        return InitializeFromManifest(manifest, new ABAssetLoader(bundleLoader), bundleLoader);
    }

    public RuntimeMessage Shutdown()
    {
        if (!_isInitialized)
            return null;
        if (HandleRegistry.ActiveCount > 0)
            return RuntimeMessage.ActiveHandlesBlockShutdown(HandleRegistry.ActiveCount,
                $"Asset={HandleRegistry.AssetActiveCount}, Scene={HandleRegistry.SceneActiveCount}");
        if (_sceneLoader != null && _sceneLoader.HasLoadedScenes)
            return RuntimeMessage.LoadFailed("ABPackageManager", "仍有已加载场景，必须先 ReleaseAsync");

        _assetLoader?.UnloadAllContent();
        HandleRegistry.Reset();
        _manifest = null;
        _index = null;
        _assetLoader = null;
        _sceneLoader = null;
        _isInitialized = false;
        return null;
    }

    public IReadOnlyList<string> GetAddressesByType<T>() where T : UnityEngine.Object
        => !_isInitialized ? Array.Empty<string>() : _index.GetAddressesByType(AssetTypeKey.FromType(typeof(T)));

    public IReadOnlyList<string> GetAddressesByLabel(string label)
        => !_isInitialized ? Array.Empty<string>() : _index.GetAddressesByLabel(label);

    public bool ContainsAddress(string address) => _isInitialized && _index.ContainsAddress(address);

    public async Task<AssetHandle<T>> LoadByAddress<T>(string address) where T : UnityEngine.Object
    {
        if (!TryResolve(address, AssetContentType.SerializedObject, out ManifestAssetEntry entry, out ManifestContentEntry content, out RuntimeMessage error))
            return new AssetHandle<T>(error);
        var (asset, _, loadError) = await _assetLoader.LoadAssetTupleAsync<T>(entry, content);
        return CreateAssetHandle(entry, asset, loadError);
    }

    public AssetHandle<T> LoadByAddressSync<T>(string address) where T : UnityEngine.Object
    {
        if (!TryResolve(address, AssetContentType.SerializedObject, out ManifestAssetEntry entry, out ManifestContentEntry content, out RuntimeMessage error))
            return new AssetHandle<T>(error);
        var (asset, _, loadError) = _assetLoader.LoadAssetTupleSync<T>(entry, content);
        return CreateAssetHandle(entry, asset, loadError);
    }

    public async Task<IReadOnlyList<AssetHandle<T>>> LoadByType<T>() where T : UnityEngine.Object
    {
        IReadOnlyList<ManifestAssetEntry> entries = _isInitialized
            ? _index.GetEntriesByType(AssetTypeKey.FromType(typeof(T)))
            : Array.Empty<ManifestAssetEntry>();
        var handles = new List<AssetHandle<T>>(entries.Count);
        for (int i = 0; i < entries.Count; i++)
            handles.Add(await LoadByAddress<T>(entries[i].Address));
        return handles;
    }

    public async Task<IReadOnlyList<AssetHandle<T>>> LoadByLabel<T>(string label) where T : UnityEngine.Object
    {
        IReadOnlyList<ManifestAssetEntry> entries = _isInitialized
            ? _index.GetEntriesByLabel(label)
            : Array.Empty<ManifestAssetEntry>();
        var handles = new List<AssetHandle<T>>(entries.Count);
        for (int i = 0; i < entries.Count; i++)
            handles.Add(await LoadByAddress<T>(entries[i].Address));
        return handles;
    }

    public async Task<byte[]> LoadRawBytesAsync(string address)
    {
        if (!TryResolve(address, AssetContentType.RawFile, out ManifestAssetEntry entry, out ManifestContentEntry content, out RuntimeMessage error))
            return null;
        var (bytes, loadError) = await _assetLoader.LoadRawBytesAsync(entry, content);
        return loadError == null ? bytes : null;
    }

    public async Task<string> LoadRawTextAsync(string address, Encoding encoding = null)
    {
        byte[] bytes = await LoadRawBytesAsync(address);
        return bytes == null ? null : (encoding ?? Encoding.UTF8).GetString(bytes);
    }

    public async Task<SceneHandle> LoadSceneAsync(string address, LoadSceneMode mode)
    {
        if (!TryResolve(address, AssetContentType.Scene, out ManifestAssetEntry entry, out ManifestContentEntry content, out RuntimeMessage error))
            return new SceneHandle(error, address, null);
        return await _sceneLoader.LoadSceneAsync(entry, content, mode);
    }

    private AssetHandle<T> CreateAssetHandle<T>(ManifestAssetEntry entry, T asset, RuntimeMessage error)
        where T : UnityEngine.Object
    {
        if (error != null)
            return new AssetHandle<T>(error);
        if (asset == null)
            return new AssetHandle<T>(RuntimeMessage.LoadFailed(entry.Address, "加载器返回 null"));
        var (tokenId, generation) = HandleRegistry.Alloc(entry.Address, HandleKind.Asset, null, _assetLoader.UnloadByAddress);
        return new AssetHandle<T>(tokenId, generation, asset);
    }

    private bool TryResolve(
        string address,
        AssetContentType expected,
        out ManifestAssetEntry entry,
        out ManifestContentEntry content,
        out RuntimeMessage error)
    {
        entry = null;
        content = null;
        error = null;
        if (!_isInitialized || _index == null || _assetLoader == null)
        {
            error = RuntimeMessage.LoadFailed(address, "ABPackageManager 未初始化");
            return false;
        }
        entry = _index.GetEntryByAddress(address);
        if (entry == null)
        {
            error = RuntimeMessage.NotFound(address);
            return false;
        }
        content = _manifest.GetContentForAsset(entry);
        if (content == null)
        {
            error = RuntimeMessage.NotFound($"ContentIndex={entry.ContentIndex}");
            return false;
        }
        if (content.ContentType != expected)
        {
            error = RuntimeMessage.InvalidPayloadKind(address, expected.ToString(), content.ContentType.ToString());
            return false;
        }
        return true;
    }

    private bool InitializeFromManifest(ABManifest manifest, IABAssetLoader assetLoader, ABBundleLoader bundleLoader)
    {
        try
        {
            manifest.Initialize();
        }
        catch (Exception ex)
        {
            Debug.LogError($"[ABPackageManager] Manifest 初始化失败: {ex.Message}");
            return false;
        }

        var index = new ABAssetIndex(manifest);
        if (!index.IsValid)
            return false;
        _manifest = manifest;
        _index = index;
        _assetLoader = assetLoader;
        _sceneLoader = new ABSceneLoader(manifest, bundleLoader);
        _isInitialized = true;
        return true;
    }

    private static async Task<ABManifest> LoadActiveManifestAsync()
    {
        string root = RuntimePathManager.ActivePackageRoot;
        if (string.IsNullOrEmpty(root))
            return null;
        string binary = FYAssetPathUtility.JoinFilePath(root, FYAssetSettings.MANIFEST_FILE_NAME_BIN);
        string json = FYAssetPathUtility.JoinFilePath(root, FYAssetSettings.MANIFEST_FILE_NAME);
        try
        {
            if (FileHelper.Exists(binary))
                return ABManifest.DeserializeFromFile(binary);
            if (FileHelper.Exists(json))
                return ABManifest.DeserializeFromFile(json);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[ABPackageManager] Manifest 读取失败: {ex.Message}");
        }
        await Task.CompletedTask;
        return null;
    }

#if UNITY_EDITOR
    private bool InitializeEditorPlayMode()
    {
        ABManifest manifest = _editorManifestBuilder?.Invoke();
        return manifest != null && InitializeFromManifest(manifest, new EditorAssetLoader(), null);
    }
#endif
}
