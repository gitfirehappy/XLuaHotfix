using System;
using System.Threading.Tasks;
using UnityEngine;

public class ABPackageBackend : IPackageBackend
{
    public Task InitializeAsync()
    {
        throw new NotImplementedException("ABPackageBackend: B4 阶段实现");
    }

    public Task<T> LoadAssetAsync<T>(string key) where T : UnityEngine.Object
    {
        throw new NotImplementedException("ABPackageBackend: B4 阶段实现");
    }

    public T LoadAssetSync<T>(string key) where T : UnityEngine.Object
    {
        throw new NotImplementedException("ABPackageBackend: B4 阶段实现");
    }

    public void UnloadAsset(string key)
    {
        throw new NotImplementedException("ABPackageBackend: B4 阶段实现");
    }

    public bool ContainsKey(string key)
    {
        throw new NotImplementedException("ABPackageBackend: B4 阶段实现");
    }
}
