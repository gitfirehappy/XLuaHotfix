using UnityEngine;

/// <summary>
/// 资源句柄值，持有 HandleRegistry 的槽位与 Generation 快照，并携带加载结果。
/// 普通复制不增加引用计数，共享所有权必须显式 Retain。
/// </summary>
/// <remarks>
/// 每个 owner 应配对 Release；仍有效的复制体不能被识别为独立 owner。
/// 失败构造使用 HandleId=-1，不占用 Registry 槽位；default 不等同于失败构造。
/// </remarks>
public struct AssetHandle<T> where T : UnityEngine.Object
{

    /// <summary>HandleRegistry 中的槽位索引。失败句柄为 -1。</summary>
    internal int HandleId;

    /// <summary>创建时的 Generation 快照。与 HandleRegistry 的 Slot.Generation 比较判断有效性。</summary>
    internal int Generation;

    /// <summary>
    /// 缓存的资源引用（热路径优化：读 .Asset 时直接返回，避免查 Registry）。
    /// Release 后通过 Generation 失效保护，不需要置 null。
    /// </summary>
    private T _cachedAsset;

    /// <summary>
    /// 失败句柄的内联错误（HandleId=-1 时使用，不通过 Registry 存储）。
    /// 成功句柄的 Error 从 Registry 获取。
    /// </summary>
    private RuntimeMessage _inlineError;

    /// <summary>
    /// 成功构造：关联 HandleRegistry 槽位。
    /// </summary>
    internal AssetHandle(int handleId, int generation, T asset)
    {
        HandleId = handleId;
        Generation = generation;
        _cachedAsset = asset;
        _inlineError = null;
    }

    /// <summary>
    /// 失败构造：不占用 Registry 槽位，错误信息内联存储。
    /// </summary>
    internal AssetHandle(RuntimeMessage error)
    {
        HandleId = -1;
        Generation = -1;
        _cachedAsset = null;
        _inlineError = error;
    }

    /// <summary>
    /// 已加载的资源。Handle 无效（释放/过期/失败）时返回 null。
    /// </summary>
    public T Asset
    {
        get
        {
            if (HandleId < 0) return null;
            if (!HandleRegistry.IsValid(HandleId, Generation)) return null;
            return _cachedAsset;
        }
    }

    /// <summary>
    /// 检查 Registry 的 Generation 和引用计数，不单独检查缓存资源是否存在。
    /// </summary>
    public bool IsValid
    {
        get
        {
            if (HandleId < 0) return false;
            return HandleRegistry.IsValid(HandleId, Generation);
        }
    }

    /// <summary>
    /// 结构化错误信息。
    /// 失败句柄：返回内联错误。
    /// 成功句柄：返回 Registry 中存储的错误（通常为 null）。
    /// </summary>
    public RuntimeMessage Error
    {
        get
        {
            if (HandleId < 0) return _inlineError;
            return HandleRegistry.GetError(HandleId, Generation);
        }
    }

    /// <summary>
    /// 增加引用计数（显式共享所有权）。
    /// 返回自身，支持链式赋值：var shared = handle.Retain();
    /// Handle 过期时调用无效果。
    /// </summary>
    public AssetHandle<T> Retain()
    {
        if (HandleId >= 0)
        {
            HandleRegistry.Retain(HandleId, Generation);
        }
        return this;
    }

    /// <summary>
    /// 释放一个显式持有的引用，归零时由 Registry 尝试释放底层资源。
    /// 过期 Generation 被拒绝；尚有效的复制体重复调用仍会减少引用数。
    /// </summary>
    public void Release()
    {
        if (HandleId >= 0)
        {
            HandleRegistry.Release(HandleId, Generation);
        }
    }

    public override string ToString()
    {
        if (IsValid)
            return string.Concat("[Handle OK] id=", HandleId.ToString(),
                " gen=", Generation.ToString());
        if (HandleId < 0 && _inlineError != null)
            return string.Concat("[Handle Failed] ", _inlineError.ToString());
        return string.Concat("[Handle Invalid] id=", HandleId.ToString(),
            " gen=", Generation.ToString());
    }

}
