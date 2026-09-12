using System;
using System.Collections.Generic;
using UnityEditor;

/// <summary>
/// 配置升级策略：由各后端提供默认落点和已知自定义 Task 的槽位映射。
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
/// BuildPipelineConfig 升级器：把未带槽位的 Task 条目转换为自定义 Task 插入槽位。
/// </summary>
/// <remarks>
/// 升级会保留已有槽位；无槽位的旧条目按已知映射或默认槽位处理，无法解析和重复条目会被丢弃并告警。
/// 只在配置实际变化时保存。固定主干由后端提供，不由配置声明。
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
