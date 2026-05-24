using System;
using System.Collections.Generic;

/// <summary>
/// BuildPipelineConfig 默认主干定义。
/// 只提供默认配置创建、主干识别和展示排序；不会在加载或构建时自动修改已有配置资产。
/// </summary>
public static class BuildPipelineBackbone
{
    /// <summary>AB 主干 Task 顺序列表，同时定义 DAG 展示的 DisplayOrder。</summary>
    private static readonly string[] ABTaskNames =
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

    /// <summary>AA 主干 Task 顺序列表，同时定义 DAG 展示的 DisplayOrder。</summary>
    private static readonly string[] AATaskNames =
    {
        "TaskScanAddressableHotfixDiff",
        "TaskMoveAddressableHotfixGroups",
        "TaskBuildAddressablesContent",
        "TaskOrganizeAAOutput",
        "TaskWriteAAPackageManifest",
        "TaskExportLocalBuildData",
    };

    /// <summary>
    /// 创建 AB 默认主干 TaskEntry 列表。
    /// </summary>
    public static List<TaskEntry> CreateABTasks()
    {
        return CreateTasks(ABTaskNames, "TaskWriteABPackageManifest");
    }

    /// <summary>
    /// 创建 AA 默认主干 TaskEntry 列表。
    /// </summary>
    public static List<TaskEntry> CreateAATasks()
    {
        return CreateTasks(AATaskNames, "TaskWriteAAPackageManifest");
    }

    /// <summary>
    /// 校验当前配置是否缺少应有主干 Task。返回空列表表示通过。
    /// </summary>
    public static List<string> GetMissingRequiredTasks(BuildPipelineConfig config)
    {
        var missing = new List<string>();
        if (config?.Tasks == null || config.Tasks.Count == 0)
            return missing;

        string[] expected = ResolveExpectedTaskNames(config);
        foreach (string taskName in expected)
        {
            TaskEntry entry = FindEntry(config.Tasks, taskName);
            if (entry == null)
            {
                missing.Add($"{taskName} (missing)");
                continue;
            }

            if (!entry.Enabled)
                missing.Add($"{taskName} (disabled)");
        }

        return missing;
    }

    private static List<TaskEntry> CreateTasks(string[] taskNames, string localBuildDataDependency)
    {
        var tasks = new List<TaskEntry>(taskNames.Length);
        foreach (string taskName in taskNames)
        {
            tasks.Add(new TaskEntry
            {
                TaskName = taskName,
                Enabled = true,
                DependsOn = GetDefaultDependencies(taskName, localBuildDataDependency)
            });
        }

        return tasks;
    }

    /// <summary>判断 TaskName 是否为主干 Task（不可通过右键菜单删除）</summary>
    public static bool IsBackboneTask(string taskName)
    {
        return Array.IndexOf(ABTaskNames, taskName) >= 0
            || Array.IndexOf(AATaskNames, taskName) >= 0;
    }

    /// <summary>
    /// 返回 Task 在主干列表中的索引作为 DAG 展示排序依据。
    /// 非主干 Task 返回 1000（排在所有主干之后）。
    /// </summary>
    public static int GetDisplayOrder(string taskName)
    {
        int index = Array.IndexOf(ABTaskNames, taskName);
        if (index >= 0)
            return index;

        int aaIndex = Array.IndexOf(AATaskNames, taskName);
        return aaIndex >= 0 ? aaIndex : 1000;
    }

    private static string[] ResolveExpectedTaskNames(BuildPipelineConfig config)
    {
        bool hasABSpecificTask = false;
        bool hasAASpecificTask = false;

        foreach (TaskEntry entry in config.Tasks)
        {
            if (entry == null || string.IsNullOrEmpty(entry.TaskName))
                continue;

            if (Array.IndexOf(ABTaskNames, entry.TaskName) >= 0
                && Array.IndexOf(AATaskNames, entry.TaskName) < 0)
                hasABSpecificTask = true;

            if (Array.IndexOf(AATaskNames, entry.TaskName) >= 0
                && Array.IndexOf(ABTaskNames, entry.TaskName) < 0)
                hasAASpecificTask = true;
        }

        if (hasAASpecificTask && !hasABSpecificTask)
            return AATaskNames;

        return ABTaskNames;
    }

    private static TaskEntry FindEntry(List<TaskEntry> tasks, string taskName)
    {
        foreach (TaskEntry entry in tasks)
        {
            if (entry != null && string.Equals(entry.TaskName, taskName, StringComparison.Ordinal))
                return entry;
        }

        return null;
    }

    private static List<string> GetDefaultDependencies(string taskName, string localBuildDataDependency)
    {
        return taskName switch
        {
            "TaskMoveAddressableHotfixGroups" => new List<string> { "TaskScanAddressableHotfixDiff" },
            "TaskBuildAddressablesContent" => new List<string> { "TaskMoveAddressableHotfixGroups" },
            "TaskExportLocalBuildData" => new List<string> { localBuildDataDependency },
            _ => new List<string>()
        };
    }
}
