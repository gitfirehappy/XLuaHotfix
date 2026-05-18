using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// BuildGraph 确定性分层布局引擎。
/// 从合并依赖计算拓扑层，按 TaskName 排序，产出每个节点的 (x, y) 坐标。
/// 无依赖的根节点在 Layer 0；无法解析的节点放入独立的 fallback 列。
/// </summary>
public static class BuildGraphLayoutEngine
{
    #region Layout

    /// <summary>节点水平间距（像素）</summary>
    private const float HorizontalSpacing = 320f;

    /// <summary>节点垂直间距（像素）</summary>
    private const float VerticalSpacing = 140f;

    /// <summary>孤立节点列 X 偏移，避免与正常拓扑层重叠</summary>
    private const float FallbackX = 1400f;

    /// <summary>
    /// 计算每个 Task 节点的画布坐标。
    /// 流程：构建邻接表 → BFS 分层 → 层内排序 → 坐标映射 → 孤立节点 fallback 列。
    /// </summary>
    public static Dictionary<string, Vector2> ComputeLayout(
        List<TaskEntry> tasks,
        Dictionary<string, string[]> mergedDeps)
    {
        var positions = new Dictionary<string, Vector2>(StringComparer.Ordinal);
        if (tasks == null || tasks.Count == 0) return positions;

        // ── 构建名称集合与邻接表 ──
        var names = new HashSet<string>(tasks.Select(e => e.TaskName), StringComparer.Ordinal);
        var successors = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var indegree = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var name in names)
        {
            successors[name] = new List<string>();
            indegree[name] = 0;
        }

        foreach (var kv in mergedDeps)
        {
            if (!names.Contains(kv.Key)) continue;
            foreach (var dep in kv.Value)
            {
                if (!names.Contains(dep)) continue;
                // 依赖边方向：dep → task，即 dep 是前置，task 是后继
                successors[dep].Add(kv.Key);
                indegree[kv.Key]++;
            }
        }

        // ── BFS 分层：从入度为 0 的根节点开始，逐层扩散 ──
        var layers = new Dictionary<string, int>(StringComparer.Ordinal);
        var queue = new Queue<string>();
        foreach (var kv in indegree)
        {
            if (kv.Value == 0)
            {
                layers[kv.Key] = 0;
                queue.Enqueue(kv.Key);
            }
        }

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            int currentLayer = layers[current];
            foreach (var succ in successors[current])
            {
                // 层号取最大值，保证节点排在所有前置依赖之后
                int candidateLayer = currentLayer + 1;
                if (!layers.TryGetValue(succ, out int existing) || candidateLayer > existing)
                    layers[succ] = candidateLayer;
                indegree[succ]--;
                if (indegree[succ] == 0)
                    queue.Enqueue(succ);
            }
        }

        // ── 未被 BFS 访问到的孤立节点（如缺失依赖或自环）放入 fallback 列 ──
        var disconnected = names.Where(n => !layers.ContainsKey(n))
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        // ── 按层分组，层内按 DisplayOrder 排序 ──
        var layerGroups = new Dictionary<int, List<string>>();
        foreach (var kv in layers)
        {
            if (!layerGroups.TryGetValue(kv.Value, out var list))
            {
                list = new List<string>();
                layerGroups[kv.Value] = list;
            }
            list.Add(kv.Key);
        }

        foreach (var kv in layerGroups)
            kv.Value.Sort(CompareDisplayOrder);

        // ── 映射坐标：Layer → X，层内序号 → Y ──
        foreach (var kv in layerGroups)
        {
            float x = kv.Key * HorizontalSpacing;
            for (int i = 0; i < kv.Value.Count; i++)
                positions[kv.Value[i]] = new Vector2(x, i * VerticalSpacing);
        }

        // ── Fallback 列：垂直排列无法拓扑排序的节点 ──
        for (int i = 0; i < disconnected.Count; i++)
            positions[disconnected[i]] = new Vector2(FallbackX, i * VerticalSpacing);

        return positions;
    }

    #endregion

    #region Helpers

    /// <summary>按 BuildPipelineConfigRepair 预定义顺序排列，同序时按字母序</summary>
    private static int CompareDisplayOrder(string left, string right)
    {
        int orderCompare = BuildPipelineConfigRepair.GetDisplayOrder(left)
            .CompareTo(BuildPipelineConfigRepair.GetDisplayOrder(right));
        return orderCompare != 0 ? orderCompare : string.Compare(left, right, StringComparison.Ordinal);
    }

    #endregion
}
