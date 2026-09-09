using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 管理资源句柄槽位、Generation 与同 EntryId 的活动槽位计数。
/// 回收槽位时增加 Generation，普通分配复用 FreeList。
/// </summary>
/// <remarks>
/// 由 ABPackageManager 与 AssetHandle{T} 在主线程使用，不提供并发保护。
/// Reset 清空状态且不调用资源释放回调，调用方须先处理底层资源。
/// </remarks>
internal static class HandleRegistry
{

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
        public RuntimeMessage Error;

        /// <summary>释放回调：RefCount 归零时调用，参数为 EntryId</summary>
        public Action<string> ReleaseCallback;
    }

    private static Slot[] _slots = new Slot[64];
    private static int _count = 0;
    private static int _activeCount = 0;
    private static readonly Stack<int> _freeList = new();

    /// <summary>
    /// Per-EntryId 活跃 Handle 计数。Alloc +1，Slot.RefCount 归零时 -1。
    /// 归零时触发释放回调——确保同一 EntryId 的所有 Handle 都释放后才卸载 Asset。
    /// </summary>
    private static readonly Dictionary<string, int> _entryActiveCounts = new();

    /// <summary>
    /// 分配一个新 Slot，返回 (handleId, generation)。
    /// 优先从 FreeList 回收，其次新增。
    /// </summary>
    public static (int handleId, int generation) Alloc(
        string entryId,
        string bundleName,
        RuntimeMessage error,
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

        if (!string.IsNullOrEmpty(entryId))
        {
            if (_entryActiveCounts.TryGetValue(entryId, out int c))
                _entryActiveCounts[entryId] = c + 1;
            else
                _entryActiveCounts[entryId] = 1;
        }

        _activeCount++;
        return (id, slot.Generation);
    }

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
    public static RuntimeMessage GetError(int handleId, int generation)
    {
        if (handleId < 0 || handleId >= _count) return null;
        ref var slot = ref _slots[handleId];
        if (slot.Generation != generation) return null;
        return slot.Error;
    }

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
    /// 减少引用计数。归零时检查 _entryActiveCounts，仅当该 EntryId 的所有 Handle 都释放后才触发回调。
    /// 之后递增 Generation + 回收 Slot。
    /// Handle 过期（拷贝体或已释放）时输出警告并返回 false。
    /// 返回 true 表示 Slot 引用计数归零并已回收。
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
            // EntryId-aware: 仅当该 EntryId 的所有 Handle 都释放后才触发回调
            bool shouldFireCallback = false;
            string eid = slot.EntryId;

            if (!string.IsNullOrEmpty(eid) && _entryActiveCounts.TryGetValue(eid, out int activeCount))
            {
                activeCount--;
                if (activeCount <= 0)
                {
                    _entryActiveCounts.Remove(eid);
                    shouldFireCallback = true;
                }
                else
                {
                    _entryActiveCounts[eid] = activeCount;
                }
            }

            if (shouldFireCallback && slot.ReleaseCallback != null)
            {
                slot.ReleaseCallback(eid);
            }

            slot.EntryId = null;
            slot.BundleName = null;
            slot.Error = null;
            slot.ReleaseCallback = null;
            slot.Generation++;

            _activeCount--;

            _freeList.Push(handleId);
            return true;
        }

        return false;
    }

    /// <summary>
    /// 重置所有 Slot（资源管理器销毁时调用）。
    /// 不触发释放回调 — 调用方应先通过 ABBundleLoader.UnloadAllBundles() 清理。
    /// 保留 _slots 数组容量，避免下次使用时重新扩容。
    /// </summary>
    public static void Reset()
    {
        if (ActiveCount > 0)
        {
            Debug.LogWarning(string.Concat(
                "[HandleRegistry] Reset 时仍有 ", ActiveCount.ToString(),
                " 个活跃 Handle，可能存在 Bundle 引用泄漏。请先释放所有 Handle 再调用 Reset。"));
        }

        for (int i = 0; i < _count; i++)
        {
            _slots[i] = default;
        }
        _count = 0;
        _activeCount = 0;
        _freeList.Clear();
        _entryActiveCounts.Clear();
    }

    /// <summary>
    /// 当前活跃 Handle 数量（诊断用）。O(1)。
    /// </summary>
    public static int ActiveCount => _activeCount;
}
