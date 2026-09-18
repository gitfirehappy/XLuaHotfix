using System;
using System.Collections.Generic;

/// <summary>
/// 构建管线执行器：顺序执行调用方提供的 Task 序列。
/// </summary>
public static class BuildPipelineRunner
{
    /// <summary>准备 Context 后顺序执行 Task；输出交付由 BuildProjectRunner 负责。</summary>
    public static BuildPipelineResult Run(
        BuildRequest request,
        BuildExecutionOptions options,
        IBuildRunEnvironment environment,
        IReadOnlyList<IBuildTask> tasks)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));

        if (tasks == null || tasks.Count == 0)
        {
            return SingleErrorResult(
                BuildErrorCodes.NoPipelineTasks,
                "没有可执行的 Task。主干由后端固定定义，请检查 Composer 输入与后端 PipelineBackbone。");
        }

        var context = new BuildRunContext();
        try
        {
            environment?.PrepareContext(context, request);
        }
        catch (Exception ex)
        {
            return SingleErrorResult(
                BuildErrorCodes.TaskExecutionError,
                $"构建环境初始化失败 - {ex.GetType().Name}: {ex.Message}。");
        }

        return ExecuteTasks(context, tasks, options);
    }

    /// <summary>顺序执行 Task：先全部报 Pending，再逐个执行，首个失败后其余标 Skipped。</summary>
    private static BuildPipelineResult ExecuteTasks(
        BuildRunContext context,
        IReadOnlyList<IBuildTask> tasks,
        BuildExecutionOptions options)
    {
        for (int i = 0; i < tasks.Count; i++)
            options?.Report(ResolveTaskName(tasks[i], i), BuildTaskExecutionStatus.Pending);

        var results = new List<BuildTaskResult>(tasks.Count);
        int executedCount = 0;
        bool failed = false;

        for (int i = 0; i < tasks.Count; i++)
        {
            IBuildTask task = tasks[i];
            string taskName = ResolveTaskName(task, i);

            options?.Report(taskName, BuildTaskExecutionStatus.Running);

            BuildTaskResult taskResult;
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

            // 首个失败即停：即使 Task 声明为非致命，后续 Task 也会读到不完整的 Context。
            if (!taskResult.Success)
            {
                failed = true;
                break;
            }
        }

        for (int i = executedCount; i < tasks.Count; i++)
            options?.Report(ResolveTaskName(tasks[i], i), BuildTaskExecutionStatus.Skipped);

        int completedCount = 0;
        for (int i = 0; i < results.Count; i++)
        {
            if (results[i].Success)
                completedCount++;
        }

        return new BuildPipelineResult
        {
            Success = !failed && completedCount == tasks.Count,
            TotalTasks = tasks.Count,
            CompletedTasks = completedCount,
            SkippedTasks = tasks.Count - executedCount,
            TaskResults = results,
            Context = context
        };
    }

    private static string ResolveTaskName(IBuildTask task, int index)
    {
        if (task == null)
            return $"<null-task-{index}>";

        return string.IsNullOrEmpty(task.TaskName) ? task.GetType().Name : task.TaskName;
    }

    private static BuildPipelineResult SingleErrorResult(string code, string message)
    {
        return new BuildPipelineResult
        {
            Success = false,
            TotalTasks = 0,
            CompletedTasks = 0,
            SkippedTasks = 0,
            TaskResults = new List<BuildTaskResult> { BuildTaskResult.Fail(code, message, true) }
        };
    }
}
