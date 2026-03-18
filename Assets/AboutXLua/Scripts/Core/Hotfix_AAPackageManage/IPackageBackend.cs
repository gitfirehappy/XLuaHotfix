using System;
using System.Threading.Tasks;
using UnityEngine;

public interface IPackageBackend
{
    #region 初始化

    Task InitializeAsync();

    #endregion

    #region 资源加载

    Task<T> LoadAssetAsync<T>(string key) where T : UnityEngine.Object;

    T LoadAssetSync<T>(string key) where T : UnityEngine.Object;

    #endregion

    #region 资源卸载

    void UnloadAsset(string key);

    #endregion

    #region 查询

    bool ContainsKey(string key);

    #endregion
}