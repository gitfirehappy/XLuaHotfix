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
        "TaskExportLocalBuildData",
    };

    private static readonly string[] AABackboneTaskNames =
    {
        "TaskBuildAddressablesContent",
        "TaskOrganizeAAOutput",
        "TaskWriteAAPackageManifest",
        "TaskExportLocalBuildData",
    };

    /// <summary>
    /// 确保 SO Task 列表包含全部主干 Task。
    /// 缺失的主干 Task 追加到列表末尾，Enabled=true，不强制覆盖已有条目。
    /// 返回 true 表示有变更并已保存 SO。
    /// </summary>
    public static bool EnsureBackboneTasks(BuildPipelineConfig config)
    {
        return EnsureTasks(config, BackboneTaskNames, "TaskWriteABPackageManifest");
    }

    /// <summary>
    /// 确保 AA Pipeline SO 包含完整 AA 主干 Task。
    /// </summary>
    public static bool EnsureAABackboneTasks(BuildPipelineConfig config)
    {
        return EnsureTasks(config, AABackboneTaskNames, "TaskWriteAAPackageManifest");
    }

    private static bool EnsureTasks(BuildPipelineConfig config, string[] taskNames, string localBuildDataDependency)
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
        foreach (TaskEntry entry in config.Tasks)
        {
            if (entry == null || string.IsNullOrEmpty(entry.TaskName))
                continue;

            List<string> requiredDependencies = GetDefaultDependencies(entry.TaskName, localBuildDataDependency);
            if (requiredDependencies.Count == 0)
                continue;

            entry.DependsOn ??= new List<string>();
            foreach (string dependency in requiredDependencies)
            {
                if (entry.DependsOn.Contains(dependency))
                    continue;

                entry.DependsOn.Add(dependency);
                changed = true;
            }
        }

        foreach (string taskName in taskNames)
        {
            if (existing.Contains(taskName))
                continue;

            config.Tasks.Add(new TaskEntry
            {
                TaskName = taskName,
                Enabled = true,
                DependsOn = GetDefaultDependencies(taskName, localBuildDataDependency)
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

    private static List<string> GetDefaultDependencies(string taskName, string localBuildDataDependency)
    {
        return taskName switch
        {
            "TaskExportLocalBuildData" => new List<string> { localBuildDataDependency },
            _ => new List<string>()
        };
    }

    /// <summary>判断 TaskName 是否为主干 Task（不可通过右键菜单删除）</summary>
    public static bool IsBackboneTask(string taskName)
    {
        return Array.IndexOf(BackboneTaskNames, taskName) >= 0
            || Array.IndexOf(AABackboneTaskNames, taskName) >= 0;
    }

    /// <summary>
    /// 返回 Task 在主干列表中的索引作为 DAG 展示排序依据。
    /// 非主干 Task 返回 1000（排在所有主干之后）。
    /// </summary>
    public static int GetDisplayOrder(string taskName)
    {
        int index = Array.IndexOf(BackboneTaskNames, taskName);
        if (index >= 0)
            return index;

        int aaIndex = Array.IndexOf(AABackboneTaskNames, taskName);
        return aaIndex >= 0 ? aaIndex : 1000;
    }
}
