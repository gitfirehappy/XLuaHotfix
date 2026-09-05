using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// 线性构建管线 runner。
/// 配置中的 Task 列表顺序就是执行顺序。
/// </summary>
public static class BuildPipelineRunner
{
    #region Public API

    /// <summary>按配置顺序执行全部已启用的构建 Task。</summary>
    public static BuildResult Execute(
        BuildPipelineConfig config,
        BuildContext context,
        IReadOnlyList<string> expectedBackboneTasks)
    {
        return Execute(config, context, null, expectedBackboneTasks);
    }

    /// <summary>执行全部已启用的构建 Task，并通过 options 报告每个 Task 的状态。</summary>
    public static BuildResult Execute(
        BuildPipelineConfig config,
        BuildContext context,
        BuildExecutionOptions options,
        IReadOnlyList<string> expectedBackboneTasks)
    {
        return Execute(config, context, options, null, null, expectedBackboneTasks);
    }

    /// <summary>执行已启用的构建 Task，并在指定 Task 完成后停止。</summary>
    public static BuildResult Execute(
        BuildPipelineConfig config,
        BuildContext context,
        BuildExecutionOptions options,
        string stopAfterTaskName,
        IReadOnlyList<string> expectedBackboneTasks)
    {
        return Execute(config, context, options, stopAfterTaskName, null, expectedBackboneTasks);
    }

    /// <summary>
    /// 执行已启用的构建 Task，可选择限制在 Task whitelist 内。
    /// expectedBackboneTasks 为调用方所属后端的主干名单（完整构建路径必校）；
    /// whitelist 预览路径不跑主干校验，可传 null。
    /// </summary>
    public static BuildResult Execute(
        BuildPipelineConfig config,
        BuildContext context,
        BuildExecutionOptions options,
        string stopAfterTaskName,
        HashSet<string> taskWhitelist,
        IReadOnlyList<string> expectedBackboneTasks)
    {
        if (config == null) throw new ArgumentNullException(nameof(config));
        if (context == null) throw new ArgumentNullException(nameof(context));

        return ExecuteInternal(config, context, options, stopAfterTaskName, taskWhitelist, expectedBackboneTasks);
    }

    #endregion

    #region Execution

    private static BuildResult ExecuteInternal(
        BuildPipelineConfig config,
        BuildContext context,
        BuildExecutionOptions options,
        string stopAfterTaskName,
        HashSet<string> taskWhitelist,
        IReadOnlyList<string> expectedBackboneTasks)
    {
        var errors = new List<BuildTaskResult>();
        List<TaskEntry> entries = GetTaskEntries(config, taskWhitelist);
        if (entries.Count == 0)
        {
            return ErrorResult(0, new List<BuildTaskResult>
            {
                BuildTaskResult.Fail(BuildErrorCodes.NoPipelineTasks, "管线配置中无 Task。", true)
            });
        }

        if (taskWhitelist == null && expectedBackboneTasks != null)
        {
            List<string> missingBackboneTasks = BuildTaskListUtility.GetMissingRequiredTasks(config, expectedBackboneTasks);
            if (missingBackboneTasks.Count > 0)
            {
                errors.Add(WithTaskName(BuildTaskResult.Fail(
                    BuildErrorCodes.MissingBackboneTask,
                    $"管线配置缺少必需主干 Task: {string.Join(", ", missingBackboneTasks)}。请更新 BuildPipelineConfig asset。",
                    true), "BuildPipelineConfig"));
                return ErrorResult(entries.Count, errors);
            }
        }

        List<ResolvedTask> tasks = ResolveTasks(entries, errors);
        if (errors.Count > 0)
            return ErrorResult(entries.Count, errors);

        for (int i = 0; i < tasks.Count; i++)
            options?.Report(tasks[i].Name, BuildTaskExecutionStatus.Pending);

        var results = new List<BuildTaskResult>();
        int executedCount = 0;
        bool fatalAbort = false;

        for (int i = 0; i < tasks.Count; i++)
        {
            ResolvedTask resolved = tasks[i];
            string taskName = resolved.Name;
            IBuildTask task = resolved.Task;
            BuildTaskResult taskResult;

            options?.Report(taskName, BuildTaskExecutionStatus.Running);
            try
            {
                taskResult = task.Execute(context) ?? BuildTaskResult.Fail(
                    BuildErrorCodes.NullTaskResult, $"'{taskName}' 返回了 null。", true);
            }
            catch (Exception ex)
            {
                taskResult = BuildTaskResult.Fail(
                    BuildErrorCodes.TaskExecutionError,
                    $"'{taskName}' 执行异常 - {ex.GetType().Name}: {ex.Message}。", true);
            }

            taskResult.TaskName = taskName;
            results.Add(taskResult);
            executedCount++;

            options?.Report(
                taskName,
                taskResult.Success ? BuildTaskExecutionStatus.Success : BuildTaskExecutionStatus.Failed,
                taskResult);

            if (taskResult.IsFatal && !taskResult.Success)
            {
                fatalAbort = true;
                break;
            }

            if (!string.IsNullOrEmpty(stopAfterTaskName)
                && string.Equals(taskName, stopAfterTaskName, StringComparison.Ordinal))
            {
                break;
            }
        }

        for (int i = executedCount; i < tasks.Count; i++)
            options?.Report(tasks[i].Name, BuildTaskExecutionStatus.Skipped);

        int completedCount = 0;
        for (int i = 0; i < results.Count; i++)
            if (results[i].Success) completedCount++;

        return new BuildResult
        {
            Success = !fatalAbort && results.TrueForAll(r => r.Success),
            TotalTasks = tasks.Count,
            CompletedTasks = completedCount,
            SkippedTasks = tasks.Count - executedCount,
            TaskResults = results
        };
    }

    #endregion

    #region Helpers

    private static List<TaskEntry> GetTaskEntries(BuildPipelineConfig config, HashSet<string> taskWhitelist)
    {
        if (config.Tasks == null)
            return new List<TaskEntry>();

        return config.Tasks
            .Where(e => e != null
                && (taskWhitelist == null || taskWhitelist.Contains(e.TaskName)))
            .ToList();
    }

    private static List<ResolvedTask> ResolveTasks(List<TaskEntry> entries, List<BuildTaskResult> errors)
    {
        var tasks = new List<ResolvedTask>(entries.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (int i = 0; i < entries.Count; i++)
        {
            TaskEntry entry = entries[i];
            if (entry == null || string.IsNullOrWhiteSpace(entry.TaskName))
            {
                errors.Add(WithTaskName(BuildTaskResult.Fail(
                    BuildErrorCodes.TaskNotFound,
                    "管线配置包含空 TaskName。", true), "BuildPipelineConfig"));
                continue;
            }

            if (!seen.Add(entry.TaskName))
            {
                errors.Add(WithTaskName(BuildTaskResult.Fail(
                    BuildErrorCodes.TaskResolutionFailed,
                    $"管线配置包含重复 TaskName: '{entry.TaskName}'。", true), entry.TaskName));
                continue;
            }

            if (!BuildTaskResolver.TryCreateTask(entry.TaskName, out IBuildTask task, out string error))
            {
                errors.Add(WithTaskName(BuildTaskResult.Fail(
                    SelectTaskResolutionErrorCode(entry.TaskName),
                    error, true), entry.TaskName));
                continue;
            }

            tasks.Add(new ResolvedTask(entry.TaskName, task));
        }

        return tasks;
    }

    private static BuildResult ErrorResult(int totalTasks, List<BuildTaskResult> errors)
    {
        return new BuildResult
        {
            Success = false,
            TotalTasks = totalTasks,
            TaskResults = errors
        };
    }

    private static BuildTaskResult WithTaskName(BuildTaskResult result, string taskName)
    {
        if (result != null)
            result.TaskName = taskName;
        return result;
    }

    private static string SelectTaskResolutionErrorCode(string taskName)
    {
        var diagnostics = BuildTaskResolver.GetDiagnostics();
        for (int i = 0; i < diagnostics.Count; i++)
        {
            var diagnostic = diagnostics[i];
            if (string.Equals(diagnostic.TaskNameHint, taskName, StringComparison.Ordinal)
                || string.Equals(diagnostic.TypeName, taskName, StringComparison.Ordinal)
                || (!string.IsNullOrEmpty(diagnostic.TypeFullName)
                    && diagnostic.TypeFullName.EndsWith("." + taskName, StringComparison.Ordinal)))
            {
                return BuildErrorCodes.TaskResolutionFailed;
            }
        }

        return BuildErrorCodes.TaskNotFound;
    }

    private readonly struct ResolvedTask
    {
        public ResolvedTask(string name, IBuildTask task)
        {
            Name = name;
            Task = task;
        }

        public string Name { get; }
        public IBuildTask Task { get; }
    }

    #endregion
}
