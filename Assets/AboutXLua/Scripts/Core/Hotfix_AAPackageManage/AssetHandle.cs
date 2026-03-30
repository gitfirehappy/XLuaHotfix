using System;
using UnityEngine;

/// <summary>
/// 泛型资源句柄 — Load 操作的主要返回类型。
///
/// 双重职责：
/// 1. Result 容器（IsValid + Error 用于 Result 风格错误处理）
/// 2. Release 身份（Release() 通过 backend 递减引用计数）
///
/// 设计合同：
/// - Release() 幂等；二次调用输出 Debug.LogWarning 但不抛异常
/// - Release() 后 Asset 返回 null，IsValid 返回 false
/// - 加载失败时返回 IsValid=false、Error 已填充的句柄
/// </summary>
public class AssetHandle<T> where T : UnityEngine.Object
{
    #region 内部状态

    private T _asset;
    private bool _released;
    private readonly Action<string> _releaseCallback; // entryId -> backend 卸载

    #endregion

    #region 公开属性

    /// <summary> 已加载的资源。加载失败或句柄已释放时为 null。 </summary>
    public T Asset => _released ? null : _asset;

    /// <summary> 加载成功且句柄未释放时为 true。 </summary>
    public bool IsValid => _asset != null && !_released;

    /// <summary> EntryId（Unity GUID）— 稳定身份标识，用于诊断和缓存。 </summary>
    public string EntryId { get; }

    /// <summary> 加载此资源时使用的 Address。 </summary>
    public string Address { get; }

    /// <summary> 来自 RuntimeAssetEntry 的 PrimaryType 字符串。 </summary>
    public string PrimaryType { get; }

    /// <summary> 结构化错误信息。成功时为 null。 </summary>
    public AssetLoadError Error { get; }

    #endregion

    #region 构造函数（internal — 仅由加载方法创建）

    /// <summary> 成功构造 </summary>
    internal AssetHandle(T asset, RuntimeAssetEntry entry, Action<string> releaseCallback)
    {
        _asset = asset;
        EntryId = entry.EntryId;
        Address = entry.Address;
        PrimaryType = entry.PrimaryType;
        _releaseCallback = releaseCallback;
    }

    /// <summary> 失败构造 </summary>
    internal AssetHandle(AssetLoadError error, string address = null)
    {
        _asset = null;
        Error = error;
        Address = address;
        EntryId = null;
        PrimaryType = null;
        _releaseCallback = null;
    }

    #endregion

    #region 释放

    /// <summary>
    /// 释放此句柄，通过 backend 递减引用计数。
    /// 幂等：二次调用输出警告但不抛异常。
    /// </summary>
    public void Release()
    {
        if (_released)
        {
            Debug.LogWarning(string.Concat(
                "[AssetHandle] Release 被重复调用: Address='", Address, "', EntryId='", EntryId, "'。已忽略。"));
            return;
        }

        _released = true;

        if (_asset != null && _releaseCallback != null)
        {
            _releaseCallback(EntryId);
        }

        _asset = null;
    }

    #endregion

    #region 便捷方法

    /// <summary>
    /// 如果此句柄表示加载失败，抛出 InvalidOperationException。
    /// 用于加载失败不可预期、需要 fail-fast 的场景。
    /// </summary>
    public AssetHandle<T> ThrowIfFailed()
    {
        if (!IsValid && Error != null)
        {
            throw new InvalidOperationException(string.Concat("资源加载失败: ", Error.ToString()));
        }
        return this;
    }

    public override string ToString()
    {
        if (IsValid)
            return string.Concat("[Handle OK] ", Address, " (", PrimaryType, ") EntryId=", EntryId);
        if (_released)
            return string.Concat("[Handle Released] ", Address);
        return string.Concat("[Handle Failed] ", Error != null ? Error.ToString() : "unknown");
    }

    #endregion
}