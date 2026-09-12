using System;
using System.Collections.Generic;
using UnityEditor;

/// <summary>
/// 一次性配置升级策略：由各后端提供，声明合法主干槽位之外还需要的默认落点与已知自定义 Task 的槽位。
/// </summary>
public sealed class BuildPipelineConfigUpgradePolicy
{
    /// <summary>没有显式槽位映射的自定义 Task 的落点</summary>
    public string DefaultSlot = BuildPipelineComposer.InputSlot;

    /// <summary>已知自定义 Task 名称到槽位的映射（例如项目胶水层 Task 的既有插入位置）</summary>
    public IReadOnlyDictionary<string, string> KnownCustomTaskSlots =
        new Dictionary<string, string>(StringComparer.Ordinal);
}

/// <summary>
/// BuildPipelineConfig 的一次性升级器：把旧的“主干 Task 顺序列表”迁移成“自定义 Task + 插入槽位”。
/// </summary>
/// <remarks>
/// 迁移规则（T6）：
/// 1. Slot 已填写的条目原样保留，因此对已升级配置重复调用不产生任何改动（幂等）；
/// 2. Slot 为空的条目视为旧形状：能解析到自定义 Task 实现的，按已知映射写槽，
///    未知名称落 DefaultSlot 并 Warning；解析不到的旧主干/已删除条目直接丢弃并 Warning；
/// 3. TaskName 为空的条目丢弃；同名条目只保留第一条；
/// 4. 只要有改动就立即保存资产，保证升级结果落盘。
/// 主干阶段本身不存在“缺失”概念：它由后端 PipelineBackbone 固定提供，不由配置声明。
/// </remarks>
public static class BuildPipelineConfigUpgrader
{
    /// <summary>执行升级；返回 true 表示本次改动了配置并已保存。</summary>
    public static bool Upgrade(
        BuildPipelineConfig config,
        IReadOnlyList<CoreTaskSlot> coreSlots,
        BuildPipelineConfigUpgradePolicy policy)
    {
        if (config == null)
            throw new ArgumentNullException(nameof(config));

        policy ??= new BuildPipelineConfigUpgradePolicy();
        var legalSlots = new HashSet<string>(StringComparer.Ordinal) { BuildPipelineComposer.InputSlot, BuildPipelineComposer.OutputSlot };
        if (coreSlots != null)
        {
            for (int i = 0; i < coreSlots.Count; i++)
            {
                if (!string.IsNullOrWhiteSpace(coreSlots[i].Slot))
                    legalSlots.Add(coreSlots[i].Slot);
            }
        }

        List<CustomTaskEntry> current = config.Tasks ?? new List<CustomTaskEntry>();
        var upgraded = new List<CustomTaskEntry>(current.Count);
        var keptNames = new HashSet<string>(StringComparer.Ordinal);
        bool changed = false;

        for (int i = 0; i < current.Count; i++)
        {
            CustomTaskEntry entry = current[i];
            string taskName = entry.TaskName;

            if (string.IsNullOrWhiteSpace(taskName))
            {
                UnityEngine.Debug.LogWarning($"[{nameof(BuildPipelineConfigUpgrader)}] 丢弃空 TaskName 条目（index={i}）。");
                changed = true;
                continue;
            }

            if (!string.IsNullOrWhiteSpace(entry.Slot))
            {
                if (!keptNames.Add(taskName))
                {
                    UnityEngine.Debug.LogWarning($"[{nameof(BuildPipelineConfigUpgrader)}] 丢弃重复自定义 Task '{taskName}'。");
                    changed = true;
                    continue;
                }

                upgraded.Add(entry);
                continue;
            }

            // Slot 为空 = 旧配置形状：要么是自定义 Task，要么是已经被吸收/删除的旧主干 Task。
            if (!BuildTaskResolver.Exists(taskName))
            {
                UnityEngine.Debug.LogWarning(
                    $"[{nameof(BuildPipelineConfigUpgrader)}] 丢弃旧主干或已删除 Task '{taskName}'："
                    + "主干阶段现在由后端 PipelineBackbone 固定定义。");
                changed = true;
                continue;
            }

            if (!keptNames.Add(taskName))
            {
                UnityEngine.Debug.LogWarning($"[{nameof(BuildPipelineConfigUpgrader)}] 丢弃重复自定义 Task '{taskName}'。");
                changed = true;
                continue;
            }

            string slot = policy.DefaultSlot;
            if (policy.KnownCustomTaskSlots != null
                && policy.KnownCustomTaskSlots.TryGetValue(taskName, out string knownSlot)
                && !string.IsNullOrWhiteSpace(knownSlot))
            {
                slot = knownSlot;
            }
            else
            {
                UnityEngine.Debug.LogWarning(
                    $"[{nameof(BuildPipelineConfigUpgrader)}] 自定义 Task '{taskName}' 没有已知槽位，"
                    + $"按默认落点写入 Slot '{slot}'。");
            }

            upgraded.Add(new CustomTaskEntry
            {
                TaskName = taskName,
                Slot = slot
            });
            changed = true;
        }

        if (!changed)
            return false;

        config.Tasks = upgraded;
        EditorUtility.SetDirty(config);
        AssetDatabase.SaveAssets();
        UnityEngine.Debug.Log($"[{nameof(BuildPipelineConfigUpgrader)}] 配置已升级: {config.name}，自定义 Task {upgraded.Count} 条。");
        return true;
    }
}
