using System;
using System.Collections.Generic;
using UnityEditor;

/// <summary>
/// BuildPipelineConfig 修复工具：保证主干 TaskEntry 存在，右键菜单只负责追加可选 Task。
/// 每次 PipelinePanel 加载配置时调用 EnsureBackboneTasks，确保 SO 包含完整核心 Task 列表。
/// </summary>
public static class BuildPipelineConfigRepair
{
    /// <summary>主干 Task 顺序列表，同时定义 DAG 展示的 DisplayOrder</summary>
    private static readonly string[] BackboneTaskNames =
    {
        "TaskPrepareContext",
        "TaskCollectAssets",
        "TaskAnalyzeDependencies",
        "TaskCollectBuiltins",
        "TaskBuildBundles",
        "TaskGenerateManifest",
        "TaskVerifyBuildResult",
        "TaskOrganizeOutput",
        "TaskWriteABPackageManifest",
    };

    /// <summary>
    /// 确保 SO Task 列表包含全部主干 Task。
    /// 缺失的主干 Task 追加到列表末尾，Enabled=true，不强制覆盖已有条目。
    /// 返回 true 表示有变更并已保存 SO。
    /// </summary>
    public static bool EnsureBackboneTasks(BuildPipelineConfig config)
    {
        if (config == null)
            return false;

        config.Tasks ??= new List<TaskEntry>();
        HashSet<string> existing = new HashSet<string>(StringComparer.Ordinal);
        foreach (TaskEntry entry in config.Tasks)
        {
            if (entry != null && !string.IsNullOrEmpty(entry.TaskName))
                existing.Add(entry.TaskName);
        }

        bool changed = false;
        foreach (string taskName in BackboneTaskNames)
        {
            if (existing.Contains(taskName))
                continue;

            config.Tasks.Add(new TaskEntry
            {
                TaskName = taskName,
                Enabled = true,
                DependsOn = new List<string>()
            });
            changed = true;
        }

        if (changed)
        {
            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();
        }

        return changed;
    }

    /// <summary>判断 TaskName 是否为主干 Task（不可通过右键菜单删除）</summary>
    public static bool IsBackboneTask(string taskName)
    {
        return Array.IndexOf(BackboneTaskNames, taskName) >= 0;
    }

    /// <summary>
    /// 返回 Task 在主干列表中的索引作为 DAG 展示排序依据。
    /// 非主干 Task 返回 1000（排在所有主干之后）。
    /// </summary>
    public static int GetDisplayOrder(string taskName)
    {
        int index = Array.IndexOf(BackboneTaskNames, taskName);
        return index >= 0 ? index : 1000;
    }
}
