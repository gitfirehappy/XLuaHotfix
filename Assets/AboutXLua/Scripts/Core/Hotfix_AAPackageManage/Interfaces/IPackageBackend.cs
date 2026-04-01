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

    #region B5-2 新增：带 EntryId 的重载（default method，不破坏现有实现）

    /// <summary>
    /// 异步加载资源，附带 EntryId 用于句柄追踪。
    /// 默认实现委托给基于 key 的加载方法。
    /// </summary>
    Task<T> LoadAssetAsync<T>(string key, string entryId) where T : UnityEngine.Object
    {
        return LoadAssetAsync<T>(key);
    }

    /// <summary>
    /// 同步加载资源，附带 EntryId。
    /// 默认实现委托给基于 key 的加载方法。
    /// </summary>
    T LoadAssetSync<T>(string key, string entryId) where T : UnityEngine.Object
    {
        return LoadAssetSync<T>(key);
    }

    /// <summary>
    /// 通过 EntryId 卸载资源。
    /// 默认实现为空操作。支持条目级追踪的 backend 重写此方法。
    /// </summary>
    void UnloadByEntryId(string entryId)
    {
    }

    #endregion
}
