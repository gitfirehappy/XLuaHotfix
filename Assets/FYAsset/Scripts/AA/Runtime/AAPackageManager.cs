using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

/// <summary>
/// AA 运行时包加载入口：Addressables 句柄票据 + Manifest 索引查询。
/// </summary>
public sealed class AAPackageManager
{
    private static readonly object LockObject = new();
    private static AAPackageManager _instance;

    private readonly Dictionary<(string address, Type type), Stack<AsyncOperationHandle>> _handleTickets =
        new();
    private readonly Dictionary<string, string[]> _labelToKeys =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string[]> _typeToKeys =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _addressSet =
        new(StringComparer.OrdinalIgnoreCase);

    private bool _isInitialized;

    public static AAPackageManager Instance
    {
        get
        {
            lock (LockObject)
            {
                return _instance ??= new AAPackageManager();
            }
        }
    }

    #region Initialization

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

        AAManifest manifest = await AAManifestLoader.LoadAsync();
        if (!HasAAIndex(manifest))
        {
            Debug.LogError("[AAPackageManager] AA AAManifest 索引初始化失败。");
            return false;
        }

        try
        {
            for (int i = 0; i < manifest.KeysByType.Count; i++)
            {
                TypeToKeys item = manifest.KeysByType[i];
                if (item == null || string.IsNullOrEmpty(item.Type)) continue;
                _typeToKeys[item.Type] = new List<string>(item.Keys ?? new List<string>()).ToArray();
            }

            for (int i = 0; i < manifest.KeysByLabel.Count; i++)
            {
                LabelToKeys item = manifest.KeysByLabel[i];
                if (item == null || string.IsNullOrEmpty(item.Label)) continue;
                _labelToKeys[item.Label] = new List<string>(item.Keys ?? new List<string>()).ToArray();
            }

            for (int i = 0; i < manifest.AssetEntries.Count; i++)
            {
                PackageEntry entry = manifest.AssetEntries[i];
                if (entry != null && !string.IsNullOrEmpty(entry.key))
                    _addressSet.Add(entry.key);
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[AAPackageManager] AAManifest 索引读取失败: {ex.Message}");
            return false;
        }

        _isInitialized = true;
        Debug.Log($"[AAPackageManager] AA AAManifest 索引初始化完成。Entries: {manifest.AssetEntries.Count}");
        return true;
    }

    private static bool HasAAIndex(AAManifest manifest)
    {
        return manifest != null
               && manifest.AssetEntries != null
               && manifest.AssetEntries.Count > 0
               && manifest.KeysByType != null
               && manifest.KeysByLabel != null;
    }

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

    #region Typed Address API

    public async Task<(T asset, RuntimeMessage error)> LoadAssetAsync<T>(string address)
        where T : UnityEngine.Object
    {
        RuntimeMessage validationError = ValidateLoad(address, "LoadAssetAsync");
        if (validationError != null) return (null, validationError);

        AsyncOperationHandle<T> handle = default;
        try
        {
            handle = Addressables.LoadAssetAsync<T>(address);
            await handle.Task;
            if (handle.Status != AsyncOperationStatus.Succeeded || handle.Result == null)
            {
                ReleaseFailedHandle(handle);
                return (null, RuntimeMessage.LoadFailed(address, $"Addressables 加载失败: {address}"));
            }

            RetainHandleTicket((address, typeof(T)), handle);
            return (handle.Result, null);
        }
        catch (Exception ex)
        {
            ReleaseFailedHandle(handle);
            return (null, RuntimeMessage.LoadFailed(address, ex.Message));
        }
    }

    public (T asset, RuntimeMessage error) LoadAssetSync<T>(string address)
        where T : UnityEngine.Object
    {
        RuntimeMessage validationError = ValidateLoad(address, "LoadAssetSync");
        if (validationError != null) return (null, validationError);

        AsyncOperationHandle<T> handle = default;
        try
        {
            handle = Addressables.LoadAssetAsync<T>(address);
            T result = handle.WaitForCompletion();
            if (handle.Status != AsyncOperationStatus.Succeeded || result == null)
            {
                ReleaseFailedHandle(handle);
                return (null, RuntimeMessage.LoadFailed(address, $"Addressables 同步加载失败: {address}"));
            }

            RetainHandleTicket((address, typeof(T)), handle);
            return (result, null);
        }
        catch (Exception ex)
        {
            ReleaseFailedHandle(handle);
            return (null, RuntimeMessage.LoadFailed(address, ex.Message));
        }
    }

    public void UnloadAsset<T>(string address) where T : UnityEngine.Object
    {
        var ticketKey = (address, typeof(T));
        if (!_handleTickets.TryGetValue(ticketKey, out var tickets) || tickets.Count == 0)
            return;

        AsyncOperationHandle handle = tickets.Pop();
        if (tickets.Count == 0)
            _handleTickets.Remove(ticketKey);

        if (handle.IsValid())
            Addressables.Release(handle);
    }

    private void RetainHandleTicket(
        (string address, Type type) ticketKey,
        AsyncOperationHandle handle)
    {
        if (!_handleTickets.TryGetValue(ticketKey, out var tickets))
        {
            tickets = new Stack<AsyncOperationHandle>();
            _handleTickets.Add(ticketKey, tickets);
        }

        tickets.Push(handle);
    }

    private RuntimeMessage ValidateLoad(string address, string operation)
    {
        if (!_isInitialized)
            return RuntimeMessage.LoadFailed(address, "AAPackageManager 未初始化");
        if (string.IsNullOrEmpty(address))
            return RuntimeMessage.Error(RuntimeErrorCodes.InvalidArgument, $"{operation}: address 为 null 或空");
        return null;
    }

    private static void ReleaseFailedHandle<T>(AsyncOperationHandle<T> handle)
    {
        if (handle.IsValid())
            Addressables.Release(handle);
    }

    #endregion
}
