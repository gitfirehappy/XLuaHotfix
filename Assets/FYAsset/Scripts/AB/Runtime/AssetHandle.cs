using UnityEngine;

/// <summary>
/// 资源句柄值，持有一个 HandleRegistry token 的快照与加载结果。
/// </summary>
/// <remarks>
/// 所有权语义：
/// 1. 一次 Load 或一次 Retain 对应一个独立 token；普通 struct 复制不增加所有权，
///    复制体与原件是同一个 token，谁先 Release 谁生效，另一次为 no-op。
/// 2. default(AssetHandle{T}) 永远无效；构造失败（未初始化、解析失败、加载失败）的句柄
///    同样不占用 Registry 槽位，只携带内联错误。
/// 3. 同一 EntryId 的最后一个 token 释放时才回调后端卸载内容。
/// </remarks>
public struct AssetHandle<T> where T : UnityEngine.Object
{

    /// <summary>Registry tokenId。0 表示 default 或失败句柄，不占用槽位。</summary>
    internal int HandleId;

    /// <summary>创建时的 Generation 快照。与 HandleRegistry 槽位比较判断是否过期。</summary>
    internal int Generation;

    /// <summary>
    /// 缓存的资源引用（热路径优化：读 .Asset 时直接返回，避免查 Registry）。
    /// token 失效后由 Generation 比较保护，不需要置 null。
    /// </summary>
    private T _cachedAsset;

    /// <summary>
    /// 失败句柄的内联错误（HandleId=0 时使用，不通过 Registry 存储）。
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
        HandleId = 0;
        Generation = 0;
        _cachedAsset = null;
        _inlineError = error;
    }

    /// <summary>
    /// 已加载的资源。句柄无效（default / 失败 / 已释放 / 已过期）时返回 null。
    /// </summary>
    public T Asset
    {
        get
        {
            if (!IsValid) return null;
            return _cachedAsset;
        }
    }

    /// <summary>
    /// 检查 Registry token 是否仍然有效，不检查缓存资源是否存在。
    /// </summary>
    public bool IsValid => HandleId >= 1 && HandleRegistry.IsValid(HandleId, Generation);

    /// <summary>
    /// 结构化错误信息。
    /// 失败句柄（含 default）返回内联错误；有效 token 返回 Registry 中存储的错误（成功时为 null）。
    /// </summary>
    public RuntimeMessage Error
    {
        get
        {
            if (HandleId < 1) return _inlineError;
            return HandleRegistry.GetError(HandleId, Generation);
        }
    }

    /// <summary>
    /// 为同一资源分配一个新的独立 token，并返回携带该 token 的新句柄。
    /// </summary>
    /// <remarks>
    /// 失败句柄原样返回（保留错误信息）；default 句柄或已过期 token 返回 default(AssetHandle{T})。
    /// 调用方必须对自己取得的每个 token 各调用一次 Release。
    /// </remarks>
    public AssetHandle<T> Retain()
    {
        if (HandleId < 1)
            return _inlineError != null ? this : default;

        if (!HandleRegistry.Retain(HandleId, Generation, out int tokenId, out int generation))
            return default;

        return new AssetHandle<T>(tokenId, generation, _cachedAsset);
    }

    /// <summary>
    /// 消费当前 token 一次。default 句柄、失败句柄、过期或重复释放都是静默 no-op，
    /// 不会影响同 EntryId 的其他 token。
    /// </summary>
    public void Release()
    {
        if (HandleId < 1) return;
        HandleRegistry.Release(HandleId, Generation);
    }

    public override string ToString()
    {
        if (IsValid)
            return string.Concat("[Handle OK] token=", HandleId.ToString(),
                " gen=", Generation.ToString());
        if (_inlineError != null)
            return string.Concat("[Handle Failed] ", _inlineError.ToString());
        return string.Concat("[Handle Invalid] token=", HandleId.ToString(),
            " gen=", Generation.ToString());
    }

}
