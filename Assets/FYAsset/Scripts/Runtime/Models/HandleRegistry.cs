using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 句柄注册表 — AssetHandle 的集中式状态管理中心。
///
/// 设计说明：
/// - AssetHandle&lt;T&gt; 是 struct（零 GC），但 struct 不能存生命周期状态（没有析构函数）。
///   HandleRegistry 替代 struct 承担生命周期管理，类似 C++ shared_ptr 的控制块（Control Block）。
/// - 内部使用 Slot 数组 + FreeList 实现 O(1) 分配/回收，零堆分配。
/// - 每个 Slot 包含 Generation 字段，Release 时递增。
///   AssetHandle 持有创建时的 Generation 快照，过期的 Handle（含拷贝体）自动失效。
/// - 支持显式 Retain/Release 实现 shared_ptr 语义：
///   默认单 Owner（Load=1, Release→0→释放），需要共享时显式 Retain() 增加引用计数。
///
/// 线程安全：
/// - V1 仅在主线程操作（Unity API 限制：AssetBundle 加载必须主线程）。
/// - 如 Phase 9 H1 引入多线程调度，在 Alloc/Retain/Release 加 Interlocked 原子操作即可。
///
/// 访问级别：internal — 不暴露给框架消费者，仅 AssetHandle&lt;T&gt; 和 AssetPackageManager 使用。
/// </summary>
internal static class HandleRegistry
{
    #region Slot 定义

    /// <summary>
    /// 槽位 — 存储一个 Handle 的完整生命周期状态。
    /// </summary>
    private struct Slot
    {
        /// <summary>
        /// 世代号。每次 Release 归零时递增。
        /// AssetHandle 持有创建时的 Generation 快照，比较即可判断 Handle 是否过期。
        /// </summary>
        public int Generation;

        /// <summary>引用计数。Alloc=1, Retain++, Release--, 归零触发释放回调。</summary>
        public int RefCount;

        /// <summary>资源的 EntryId（释放回调参数）</summary>
        public string EntryId;

        /// <summary>资源所属 Bundle 名称（诊断用）</summary>
        public string BundleName;

        /// <summary>加载错误信息（成功时为 null）</summary>
        public AssetLoadError Error;

        /// <summary>释放回调：RefCount 归零时调用，参数为 EntryId</summary>
        public Action<string> ReleaseCallback;
    }

    #endregion

    #region 字段

    private static Slot[] _slots = new Slot[64];
    private static int _count = 0;
    private static int _activeCount = 0;
    private static readonly Stack<int> _freeList = new();

    #endregion

    #region 分配

    /// <summary>
    /// 分配一个新 Slot，返回 (handleId, generation)。
    /// 优先从 FreeList 回收，其次新增。
    /// </summary>
    public static (int handleId, int generation) Alloc(
        string entryId,
        string bundleName,
        AssetLoadError error,
        Action<string> releaseCallback)
    {
        int id;
        if (_freeList.Count > 0)
        {
            id = _freeList.Pop();
        }
        else
        {
            if (_count >= _slots.Length)
            {
                Array.Resize(ref _slots, _slots.Length * 2);
            }
            id = _count++;
        }

        // Generation 保持递增（从 FreeList 回收的 Slot 的 Generation 已在上次 Release 时递增）
        ref var slot = ref _slots[id];
        slot.RefCount = 1;
        slot.EntryId = entryId;
        slot.BundleName = bundleName;
        slot.Error = error;
        slot.ReleaseCallback = releaseCallback;

        _activeCount++;
        return (id, slot.Generation);
    }

    #endregion

    #region 查询

    /// <summary>
    /// 检查 Handle 是否有效（Generation 匹配 + RefCount &gt; 0）。
    /// </summary>
    public static bool IsValid(int handleId, int generation)
    {
        if (handleId < 0 || handleId >= _count) return false;
        ref var slot = ref _slots[handleId];
        return slot.Generation == generation && slot.RefCount > 0;
    }

    /// <summary>
    /// 获取 Handle 关联的错误信息。Handle 过期或无效返回 null。
    /// </summary>
    public static AssetLoadError GetError(int handleId, int generation)
    {
        if (handleId < 0 || handleId >= _count) return null;
        ref var slot = ref _slots[handleId];
        if (slot.Generation != generation) return null;
        return slot.Error;
    }

    /// <summary>
    /// 获取 Handle 当前引用计数。Handle 过期或无效返回 0。
    /// </summary>
    public static int GetRefCount(int handleId, int generation)
    {
        if (handleId < 0 || handleId >= _count) return 0;
        ref var slot = ref _slots[handleId];
        if (slot.Generation != generation) return 0;
        return slot.RefCount;
    }

    #endregion

    #region 引用计数操作

    /// <summary>
    /// 增加引用计数（显式共享所有权）。
    /// Handle 过期返回 false。
    /// </summary>
    public static bool Retain(int handleId, int generation)
    {
        if (handleId < 0 || handleId >= _count) return false;
        ref var slot = ref _slots[handleId];
        if (slot.Generation != generation || slot.RefCount <= 0) return false;

        slot.RefCount++;
        return true;
    }

    /// <summary>
    /// 减少引用计数。归零时执行释放回调 + 递增 Generation + 回收 Slot。
    /// Handle 过期（拷贝体或已释放）时输出警告并返回 false。
    /// 返回 true 表示引用计数归零并触发了释放。
    /// </summary>
    public static bool Release(int handleId, int generation)
    {
        if (handleId < 0 || handleId >= _count) return false;
        ref var slot = ref _slots[handleId];

        if (slot.Generation != generation)
        {
            Debug.LogWarning(string.Concat(
                "[HandleRegistry] Release 被过期 Handle 调用（可能是拷贝体）: handleId=",
                handleId.ToString(), ", handle.Generation=", generation.ToString(),
                ", slot.Generation=", slot.Generation.ToString()));
            return false;
        }

        if (slot.RefCount <= 0)
        {
            Debug.LogWarning(string.Concat(
                "[HandleRegistry] Release 被重复调用: handleId=", handleId.ToString(),
                ", EntryId='", slot.EntryId ?? "", "'"));
            return false;
        }

        slot.RefCount--;

        if (slot.RefCount <= 0)
        {
            // 执行释放回调
            if (slot.ReleaseCallback != null && !string.IsNullOrEmpty(slot.EntryId))
            {
                slot.ReleaseCallback(slot.EntryId);
            }

            // 清理 Slot 状态，递增 Generation
            slot.EntryId = null;
            slot.BundleName = null;
            slot.Error = null;
            slot.ReleaseCallback = null;
            slot.Generation++;

            _activeCount--;

            // 归还 FreeList
            _freeList.Push(handleId);
            return true;
        }

        return false;
    }

    #endregion

    #region 生命周期

    /// <summary>
    /// 重置所有 Slot（资源管理器销毁时调用）。
    /// 不触发释放回调 — 调用方应先通过 ABBundleLoader.UnloadAllBundles() 清理。
    /// 保留 _slots 数组容量，避免下次使用时重新扩容。
    /// </summary>
    public static void Reset()
    {
        // 检查是否有未释放的 Handle，提示调用方可能存在引用泄漏
        if (ActiveCount > 0)
        {
            Debug.LogWarning(string.Concat(
                "[HandleRegistry] Reset 时仍有 ", ActiveCount.ToString(),
                " 个活跃 Handle，可能存在 Bundle 引用泄漏。请先释放所有 Handle 再调用 Reset。"));
        }

        // 清零已分配的 Slot，保留数组容量
        for (int i = 0; i < _count; i++)
        {
            _slots[i] = default;
        }
        _count = 0;
        _activeCount = 0;
        _freeList.Clear();
    }

    /// <summary>
    /// 当前活跃 Handle 数量（诊断用）。O(1)。
    /// </summary>
    public static int ActiveCount => _activeCount;

    #endregion
}
