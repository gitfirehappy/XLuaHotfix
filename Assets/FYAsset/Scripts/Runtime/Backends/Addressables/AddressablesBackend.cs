using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

public class AddressablesBackend : IPackageBackend
{
    private readonly Dictionary<string, ResourceEntry> _resourceCache = new();

    private class ResourceEntry
    {
        public AsyncOperationHandle Handle;
        public int ReferenceCount = 1;
        public bool IsValid => ReferenceCount > 0;
    }

    public Task InitializeAsync()
    {
        return Task.CompletedTask;
    }

    public async Task<(T asset, RuntimeMessage error)> LoadAssetAsync<T>(string key) where T : UnityEngine.Object
    {
        if (_resourceCache.TryGetValue(key, out var entry) && entry.IsValid)
        {
            entry.ReferenceCount++;
            return (entry.Handle.Result as T, null);
        }

        var handle = Addressables.LoadAssetAsync<T>(key);
        await handle.Task;

        if (handle.IsDone && handle.Status == AsyncOperationStatus.Succeeded)
        {
            AddToCache(key, handle);
            return (handle.Result as T, null);
        }

        Addressables.Release(handle);
        return (null, RuntimeMessage.LoadFailed(key, $"加载资源失败: {key}"));
    }

    public (T asset, RuntimeMessage error) LoadAssetSync<T>(string key) where T : UnityEngine.Object
    {
        if (_resourceCache.TryGetValue(key, out var entry) && entry.IsValid)
        {
            entry.ReferenceCount++;
            return (entry.Handle.Result as T, null);
        }

        var handle = Addressables.LoadAssetAsync<T>(key);
        T result = handle.WaitForCompletion();

        if (handle.Status == AsyncOperationStatus.Succeeded)
        {
            AddToCache(key, handle);
            return (result, null);
        }

        Addressables.Release(handle);
        return (null, RuntimeMessage.LoadFailed(key, $"同步加载失败: {key}"));
    }

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

    public bool ContainsKey(string key)
    {
        return _resourceCache.ContainsKey(key) && _resourceCache[key].IsValid;
    }

    private void AddToCache(string key, AsyncOperationHandle handle)
    {
        _resourceCache[key] = new ResourceEntry()
        {
            Handle = handle,
            ReferenceCount = 1
        };
    }
}
