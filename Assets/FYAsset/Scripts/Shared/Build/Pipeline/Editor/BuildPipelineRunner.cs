using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Linear build pipeline runner.
/// The configured Task list order is the execution order; dependencies are validation-only guardrails.
/// </summary>
public static class BuildPipelineRunner
{
    #region Public API

    /// <summary>Execute all enabled build tasks in configured order.</summary>
    public static BuildResult Execute(BuildPipelineConfig config, BuildContext context)
    {
        return Execute(config, context, null);
    }

    /// <summary>Execute all enabled build tasks and report per-task status through options.</summary>
    public static BuildResult Execute(BuildPipelineConfig config, BuildContext context, BuildExecutionOptions options)
    {
        return Execute(config, context, options, null, null);
    }

    /// <summary>Execute enabled build tasks and stop after the named task completes.</summary>
    public static BuildResult Execute(
        BuildPipelineConfig config,
        BuildContext context,
        BuildExecutionOptions options,
        string stopAfterTaskName)
    {
        return Execute(config, context, options, stopAfterTaskName, null);
    }

    /// <summary>Execute enabled build tasks, optionally limited to a task whitelist.</summary>
    public static BuildResult Execute(
        BuildPipelineConfig config,
        BuildContext context,
        BuildExecutionOptions options,
        string stopAfterTaskName,
        HashSet<string> taskWhitelist)
    {
        if (config == null) throw new ArgumentNullException(nameof(config));
        if (context == null) throw new ArgumentNullException(nameof(context));

        BuildResult validation = ValidateInternal(config, taskWhitelist);
        if (!validation.Success)
            return validation;

        return ExecuteInternal(config, context, options, stopAfterTaskName, taskWhitelist);
    }

    /// <summary>Validate the configured linear task list without executing tasks.</summary>
    public static BuildResult Validate(BuildPipelineConfig config)
    {
        if (config == null) throw new ArgumentNullException(nameof(config));
        return ValidateInternal(config);
    }

    #endregion

    #region Validation

    private static BuildResult ValidateInternal(BuildPipelineConfig config, HashSet<string> taskWhitelist = null)
    {
        var errors = new List<BuildTaskResult>();
        var warnings = new List<BuildTaskResult>();
        List<TaskEntry> entries = GetTaskEntries(config, taskWhitelist);

        if (entries.Count == 0)
        {
            return ErrorResult(0, new List<BuildTaskResult>
            {
                BuildTaskResult.Fail(BuildErrorCodes.NoPipelineTasks, "管线配置中无 Task。", true)
            });
        }

        if (taskWhitelist == null)
        {
            List<string> missingBackboneTasks = BuildPipelineBackbone.GetMissingRequiredTasks(config);
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

        ValidateDependencyOrder(config, tasks, errors);
        if (errors.Count > 0)
            return ErrorResult(tasks.Count, errors);

        ValidateReadBeforeWrite(tasks, warnings);

        var result = new BuildResult { Success = true, TotalTasks = tasks.Count };
        result.TaskResults.AddRange(warnings);
        return result;
    }

    private static void ValidateDependencyOrder(
        BuildPipelineConfig config,
        List<ResolvedTask> tasks,
        List<BuildTaskResult> errors)
    {
        var enabledNames = new HashSet<string>(tasks.Select(t => t.Name), StringComparer.Ordinal);
        var producedTaskNames = new HashSet<string>(StringComparer.Ordinal);

        for (int i = 0; i < tasks.Count; i++)
        {
            ResolvedTask current = tasks[i];
            string[] dependencies = current.Task.DependsOn ?? Array.Empty<string>();
            for (int dependencyIndex = 0; dependencyIndex < dependencies.Length; dependencyIndex++)
            {
                string dependency = dependencies[dependencyIndex];
                if (!enabledNames.Contains(dependency))
                {
                    errors.Add(WithTaskName(BuildTaskResult.Fail(
                        BuildErrorCodes.MissingDependency,
                        $"'{current.Name}' depends on '{dependency}', but '{dependency}' is not in the Task list.", true), current.Name));
                    continue;
                }

                if (!producedTaskNames.Contains(dependency))
                {
                    errors.Add(WithTaskName(BuildTaskResult.Fail(
                        BuildErrorCodes.MissingDependency,
                        $"'{current.Name}' depends on '{dependency}', but '{dependency}' appears after it in the linear task list.",
                        true), current.Name));
                }
            }

            producedTaskNames.Add(current.Name);
        }
    }

    private static void ValidateReadBeforeWrite(List<ResolvedTask> tasks, List<BuildTaskResult> warnings)
    {
        var produced = new HashSet<string>(StringComparer.Ordinal);

        for (int i = 0; i < tasks.Count; i++)
        {
            IBuildTask task = tasks[i].Task;
            if (task.ReadKeys != null)
            {
                for (int keyIndex = 0; keyIndex < task.ReadKeys.Length; keyIndex++)
                {
                    string key = task.ReadKeys[keyIndex];
                    bool selfProduce = task.WriteKeys != null && task.WriteKeys.Contains(key);
                    if (!selfProduce && !produced.Contains(key))
                    {
                        warnings.Add(WithTaskName(BuildTaskResult.Fail(
                            BuildErrorCodes.UnsatisfiedReadKey,
                            $"'{task.TaskName}' 读取 '{key}'，但没有前置 Task 产出该 Key。", false), task.TaskName));
                    }
                }
            }

            if (task.WriteKeys == null)
                continue;

            for (int keyIndex = 0; keyIndex < task.WriteKeys.Length; keyIndex++)
                produced.Add(task.WriteKeys[keyIndex]);
        }
    }

    #endregion

    #region Execution

    private static BuildResult ExecuteInternal(
        BuildPipelineConfig config,
        BuildContext context,
        BuildExecutionOptions options,
        string stopAfterTaskName,
        HashSet<string> taskWhitelist)
    {
        var errors = new List<BuildTaskResult>();
        List<TaskEntry> entries = GetTaskEntries(config, taskWhitelist);
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
