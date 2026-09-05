using System;
using System.Collections.Generic;

/// <summary>
/// 构建管线 Task 列表的通用机制：按给定名单创建 TaskEntry 列表、按给定主干名单校验缺失项。
/// 不持有任何后端（AA/AB）名单；名单一律由调用方注入，后端主干定义见各后端 PipelineBackbone。
/// 不会在加载或构建时自动修改已有配置资产。
/// </summary>
public static class BuildTaskListUtility
{
    /// <summary>
    /// 按给定 Task 名单创建默认 TaskEntry 列表。
    /// </summary>
    public static List<TaskEntry> CreateTasks(IReadOnlyList<string> taskNames)
    {
        var tasks = new List<TaskEntry>(taskNames.Count);
        foreach (string taskName in taskNames)
        {
            tasks.Add(new TaskEntry
            {
                TaskName = taskName
            });
        }

        return tasks;
    }

    /// <summary>
    /// 校验当前配置是否缺少给定主干 Task。返回空列表表示通过。
    /// </summary>
    public static List<string> GetMissingRequiredTasks(
        BuildPipelineConfig config,
        IReadOnlyList<string> expectedTaskNames)
    {
        var missing = new List<string>();
        if (config?.Tasks == null || config.Tasks.Count == 0 || expectedTaskNames == null)
            return missing;

        foreach (string taskName in expectedTaskNames)
        {
            TaskEntry entry = FindEntry(config.Tasks, taskName);
            if (entry == null)
            {
                missing.Add($"{taskName} (missing)");
                continue;
            }

        }

        return missing;
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
}
