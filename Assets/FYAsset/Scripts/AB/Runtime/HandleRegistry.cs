using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// 句柄归属类别。Asset 与 Scene 共用同一张槽位表，但释放时机不同：
/// Asset 槽位在最后一个 token 释放时回调后端卸载内容；Scene 槽位不携带释放回调，
/// 内容引用必须等 Scene 真正卸载后再由场景加载器释放。
/// </summary>
internal enum HandleKind
{
    /// <summary>普通资源句柄（AssetHandle{T}）</summary>
    Asset = 0,

    /// <summary>场景句柄（SceneHandle）</summary>
    Scene = 1
}

/// <summary>
/// 句柄 token 槽位表。
/// </summary>
/// <remarks>
/// 每次 Load/Retain 分配独立 token；重复 Release 幂等。槽位回收只递增 Generation，避免过期句柄再次命中。
/// Asset 在最后一个 token 释放时回调卸载，Scene 等真实卸载后由场景加载器释放；仅主线程使用。
/// </remarks>
internal static class HandleRegistry
{

    /// <summary>一个 token 的完整生命周期状态。</summary>
    private struct Slot
    {
        /// <summary>世代号。回收（Release 或 Reset）时递增，只增不减。</summary>
        public int Generation;

        /// <summary>token 是否仍然有效。false 表示已释放或从未分配。</summary>
        public bool Alive;

        /// <summary>句柄类别（Asset / Scene）</summary>
        public HandleKind Kind;

        /// <summary>资源的 EntryId（释放回调参数与泄漏诊断用）</summary>
        public string EntryId;

        /// <summary>资源所属内容文件名（诊断用）</summary>
        public string BundleName;

        /// <summary>加载错误信息；有效 token 为 null</summary>
        public RuntimeMessage Error;

        /// <summary>该 EntryId 最后一个 token 释放时的回调；Scene 槽位为 null</summary>
        public Action<string> ReleaseCallback;
    }

    /// <summary>Token 泄漏诊断一次最多打印多少个 EntryId 分组。</summary>
    private const int LeakReportTopCount = 10;

    /// <summary>
    /// 槽位表。下标即 tokenId，0 号槽位永不分配，代表 default 与失败句柄。
    /// </summary>
    private static Slot[] _slots = new Slot[64];

    /// <summary>下一个可用槽位下标（从 1 开始增长）。</summary>
    private static int _slotCount = 1;

    /// <summary>已回收、可复用的 tokenId。复用时沿用该槽位已经递增过的 Generation。</summary>
    private static readonly Stack<int> _freeList = new();

    private static int _assetActiveCount;
    private static int _sceneActiveCount;

    /// <summary>
    /// Per-EntryId 活跃 token 计数。Alloc +1，token 释放 -1，归零触发释放回调。
    /// 归零时触发回调 —— 确保同一 EntryId 的所有 token 都释放后才卸载内容。
    /// </summary>
    private static readonly Dictionary<string, int> _entryActiveCounts = new();

    /// <summary>
    /// 分配一个新 token 槽位，返回 (tokenId, generation)。优先复用 FreeList。
    /// </summary>
    public static (int tokenId, int generation) Alloc(
        string entryId,
        HandleKind kind,
        string bundleName,
        RuntimeMessage error,
        Action<string> releaseCallback)
    {
        int tokenId;
        if (_freeList.Count > 0)
        {
            tokenId = _freeList.Pop();
        }
        else
        {
            if (_slotCount >= _slots.Length)
            {
                Array.Resize(ref _slots, _slots.Length * 2);
            }

            tokenId = _slotCount++;
        }

        ref var slot = ref _slots[tokenId];
        slot.Alive = true;
        slot.Kind = kind;
        slot.EntryId = entryId;
        slot.BundleName = bundleName;
        slot.Error = error;
        slot.ReleaseCallback = releaseCallback;

        if (!string.IsNullOrEmpty(entryId))
        {
            _entryActiveCounts.TryGetValue(entryId, out int activeCount);
            _entryActiveCounts[entryId] = activeCount + 1;
        }

        if (kind == HandleKind.Scene)
            _sceneActiveCount++;
        else
            _assetActiveCount++;

        return (tokenId, slot.Generation);
    }

    /// <summary>
    /// 检查 token 是否有效（tokenId 已分配 + 世代匹配 + 尚未释放）。
    /// </summary>
    public static bool IsValid(int tokenId, int generation)
    {
        if (tokenId < 1 || tokenId >= _slotCount) return false;
        ref var slot = ref _slots[tokenId];
        return slot.Alive && slot.Generation == generation;
    }

    /// <summary>
    /// 获取 token 关联的错误信息。token 不存在、过期或已释放返回 null。
    /// </summary>
    public static RuntimeMessage GetError(int tokenId, int generation)
    {
        if (!IsValid(tokenId, generation)) return null;
        return _slots[tokenId].Error;
    }

    /// <summary>
    /// 为同一个 EntryId 再分配一个独立 token（显式共享所有权）。
    /// 失败表示源 token 已过期或已释放，调用方必须视为获取所有权失败。
    /// </summary>
    public static bool Retain(
        int tokenId,
        int generation,
        out int newTokenId,
        out int newGeneration)
    {
        newTokenId = 0;
        newGeneration = 0;
        if (!IsValid(tokenId, generation)) return false;

        // 先复制源 token 的归属信息再分配：Alloc 可能扩容槽位数组，不能跨调用持有 ref。
        string entryId;
        HandleKind kind;
        string bundleName;
        RuntimeMessage error;
        Action<string> releaseCallback;
        {
            ref var slot = ref _slots[tokenId];
            entryId = slot.EntryId;
            kind = slot.Kind;
            bundleName = slot.BundleName;
            error = slot.Error;
            releaseCallback = slot.ReleaseCallback;
        }

        (newTokenId, newGeneration) = Alloc(entryId, kind, bundleName, error, releaseCallback);
        return true;
    }

    /// <summary>
    /// 查询一个有效 token 当前所属 EntryId 的活跃所有者数量；无效 token 返回 0。
    /// </summary>
    public static int GetActiveOwnerCount(int tokenId, int generation)
    {
        if (!IsValid(tokenId, generation)) return 0;

        string entryId;
        {
            ref var slot = ref _slots[tokenId];
            entryId = slot.EntryId;
        }

        if (string.IsNullOrEmpty(entryId))
            return 0;

        return _entryActiveCounts.TryGetValue(entryId, out int count) ? count : 0;
    }

    /// <summary>当前 token 是否是其 EntryId 的唯一所有者；只有最后所有者才能触发物理卸载。</summary>
    public static bool IsLastOwner(int tokenId, int generation) =>
        GetActiveOwnerCount(tokenId, generation) == 1;

    /// <summary>
    /// 强制结算某个 EntryId 下的全部存活 token（外部卸载已经确认时使用）。
    /// 只改变句柄有效性，不触发释放回调：内容引用必须由调用方在物理卸载确认后单独释放。
    /// </summary>
    /// <returns>被结算的 token 数量。</returns>
    public static int ReleaseAllForEntry(string entryId)
    {
        if (string.IsNullOrEmpty(entryId)) return 0;

        int released = 0;
        for (int tokenId = 1; tokenId < _slotCount; tokenId++)
        {
            ref var slot = ref _slots[tokenId];
            if (!slot.Alive || !string.Equals(slot.EntryId, entryId, StringComparison.Ordinal))
                continue;

            if (slot.Kind == HandleKind.Scene)
                _sceneActiveCount--;
            else
                _assetActiveCount--;

            slot.Alive = false;
            slot.EntryId = null;
            slot.BundleName = null;
            slot.Error = null;
            slot.ReleaseCallback = null;
            slot.Generation++;

            _freeList.Push(tokenId);
            released++;
        }

        _entryActiveCounts.Remove(entryId);
        return released;
    }

    /// <summary>
    /// 释放一个 token。重复释放或过期 token 是静默 no-op：
    /// 不抛异常、不改变任何计数、不会误扣同 EntryId 其他 token 的释放权。
    /// </summary>
    /// <returns>true 表示本次调用消费了一个有效 token。</returns>
    public static bool Release(int tokenId, int generation)
    {
        if (!IsValid(tokenId, generation)) return false;

        bool isScene;
        string entryId;
        Action<string> releaseCallback;
        {
            // 槽位写入与释放来源复制放在同一块内，避免跨回调持有数组元素 ref。
            ref var slot = ref _slots[tokenId];
            isScene = slot.Kind == HandleKind.Scene;
            entryId = slot.EntryId;
            releaseCallback = slot.ReleaseCallback;
            slot.Alive = false;
            slot.EntryId = null;
            slot.BundleName = null;
            slot.Error = null;
            slot.ReleaseCallback = null;
            slot.Generation++;
        }

        if (isScene)
            _sceneActiveCount--;
        else
            _assetActiveCount--;

        if (!string.IsNullOrEmpty(entryId) && _entryActiveCounts.TryGetValue(entryId, out int activeCount))
        {
            activeCount--;
            if (activeCount <= 0)
            {
                _entryActiveCounts.Remove(entryId);
                releaseCallback?.Invoke(entryId);
            }
            else
            {
                _entryActiveCounts[entryId] = activeCount;
            }
        }

        _freeList.Push(tokenId);
        return true;
    }

    /// <summary>
    /// 清零所有 token 槽位（资源管理器 Shutdown 时调用）。
    /// </summary>
    /// <remarks>
    /// 仍有活跃 token 时拒绝执行并返回 false，状态完全不变 ——
    /// 静默清空会让调用方继续持有永远无法释放的句柄，也会掩盖内容引用泄漏。
    /// 成功时保留槽位数组容量与 tokenId 复用列表，但递增全部槽位的 Generation，
    /// 使 Shutdown 之前取得的句柄副本在重新初始化后不会重新命中新 token。
    /// </remarks>
    public static bool Reset()
    {
        if (ActiveCount > 0)
        {
            Debug.LogWarning(string.Concat(
                "[HandleRegistry] Reset 被拒绝：仍有 ", ActiveCount.ToString(),
                " 个活跃 token（Asset=", _assetActiveCount.ToString(),
                ", Scene=", _sceneActiveCount.ToString(),
                "），可能存在内容引用泄漏。请先释放全部 Handle。\n",
                DescribeActiveTokens()));
            return false;
        }

        _freeList.Clear();
        for (int tokenId = 1; tokenId < _slotCount; tokenId++)
        {
            ref var slot = ref _slots[tokenId];
            slot.Generation++;
            slot.Alive = false;
            slot.Kind = HandleKind.Asset;
            slot.EntryId = null;
            slot.BundleName = null;
            slot.Error = null;
            slot.ReleaseCallback = null;
            _freeList.Push(tokenId);
        }

        _entryActiveCounts.Clear();
        _assetActiveCount = 0;
        _sceneActiveCount = 0;
        return true;
    }

    /// <summary>
    /// 统一活跃 token 数量（Asset + Scene）。热更 Apply 门禁用它判断是否允许切换包根。
    /// </summary>
    public static int ActiveCount => _assetActiveCount + _sceneActiveCount;

    /// <summary>活跃 AssetHandle 数量（诊断用）。</summary>
    public static int AssetActiveCount => _assetActiveCount;

    /// <summary>活跃 SceneHandle 数量（诊断用）。</summary>
    public static int SceneActiveCount => _sceneActiveCount;

    /// <summary>
    /// 按 EntryId + Kind 汇总活跃 token，打印数量最多的前若干个，用于定位泄漏来源。
    /// </summary>
    private static string DescribeActiveTokens()
    {
        var groups = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int tokenId = 1; tokenId < _slotCount; tokenId++)
        {
            Slot slot = _slots[tokenId];
            if (!slot.Alive) continue;

            string key = string.Concat(
                slot.Kind == HandleKind.Scene ? "Scene" : "Asset",
                " | ",
                string.IsNullOrEmpty(slot.EntryId) ? "<无 EntryId>" : slot.EntryId);
            groups.TryGetValue(key, out int count);
            groups[key] = count + 1;
        }

        var ordered = new List<KeyValuePair<string, int>>(groups);
        ordered.Sort((left, right) => right.Value.CompareTo(left.Value));

        var builder = new StringBuilder("[HandleRegistry] 活跃 token 分组（Top ");
        builder.Append(Math.Min(LeakReportTopCount, ordered.Count).ToString());
        builder.Append("）:");
        int printed = 0;
        for (int i = 0; i < ordered.Count && printed < LeakReportTopCount; i++, printed++)
        {
            builder.Append("\n  ");
            builder.Append(ordered[i].Value.ToString());
            builder.Append(" × ");
            builder.Append(ordered[i].Key);
        }

        return builder.ToString();
    }
}
