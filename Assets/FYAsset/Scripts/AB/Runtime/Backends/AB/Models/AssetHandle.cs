using System;
using UnityEngine;

/// <summary>
/// 泛型资源句柄 — Load 操作的主要返回类型（值类型，零 GC）。
///
/// 设计说明：
/// - struct 语义：赋值即拷贝，不产生堆分配。
/// - 生命周期由 HandleRegistry 集中管理（类似 C++ shared_ptr 控制块模式）。
/// - 每个 Handle 持有 (HandleId, Generation) 元组，通过 Generation 校验判断有效性。
/// - Release() 递减引用计数，归零时通过 HandleRegistry 回调释放底层资源。
/// - 普通 struct 拷贝不会增加引用计数，也不会创建独立 owner。
/// - 显式 Retain() 支持共享所有权；每个 Retain 成功后的 owner 都需要 Release。
///
/// 双重职责：
/// 1. Result 容器（IsValid + Error 用于 Result 风格错误处理）
/// 2. Release 身份（Release() 通过 HandleRegistry 递减引用计数）
///
/// 使用合同：
/// - 默认单 Owner：Load 返回 Handle(refCount=1)，Release 归零释放。
/// - 共享所有权：显式调用 Retain() 增加引用计数，每个 Owner 各自 Release。
/// - 普通拷贝不应作为独立 owner 调用 Release；需要共享时先调用 Retain()。
/// - 已释放或过期的 Handle 再次 Release 会被 Generation 拦截并输出 Warning。
/// - 加载失败时返回 IsValid=false、Error 已填充的句柄（HandleId=-1，不占用 Registry 槽位）。
/// </summary>
public struct AssetHandle<T> where T : UnityEngine.Object
{
    #region 内部状态

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

    #endregion

    #region 构造函数（internal — 仅由 AssetPackageManager / 加载方法创建）

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

    #endregion

    #region 公开属性

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
    /// Handle 是否有效（资源存在 + Registry 确认未释放/未过期）。
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

    #endregion

    #region 引用计数操作

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
    /// 释放此句柄的引用。引用计数 -1，归零时通过 HandleRegistry 回调释放底层资源。
    /// 已释放或过期的 Handle 调用时为安全的空操作（Generation 校验拦截）。
    /// </summary>
    public void Release()
    {
        if (HandleId >= 0)
        {
            HandleRegistry.Release(HandleId, Generation);
        }
    }

    #endregion

    #region 便捷方法

    /// <summary>
    /// 如果此句柄表示加载失败，抛出 InvalidOperationException。
    /// 用于加载失败不可预期、需要 fail-fast 的场景。
    /// 注：已释放的 Handle（HandleId >= 0 但 Generation 不匹配）也会抛异常，防止 use-after-free 静默通过。
    /// </summary>
    public AssetHandle<T> ThrowIfFailed()
    {
        if (HandleId < 0 && Error != null)
        {
            // 加载失败句柄
            throw new InvalidOperationException(
                string.Concat("资源加载失败: ", Error.ToString()));
        }

        if (HandleId >= 0 && !HandleRegistry.IsValid(HandleId, Generation))
        {
            // Handle 已释放或过期（use-after-free）
            throw new InvalidOperationException(
                string.Concat("Handle 已释放或过期（use-after-free）: id=",
                    HandleId.ToString(), ", gen=", Generation.ToString()));
        }

        return this;
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

    #endregion
}
