using System;
using System.Collections.Generic;

/// <summary>
/// 构建管线执行器：顺序执行 Composer 产出的 Task 序列，管理 Context 与 attempt，成功后提升正式输出。
/// </summary>
/// <remarks>
/// 职责边界（计划 T6）：
/// 1. 只接受已经组装好的 Task 列表，不读配置、不认识后端、不解析主干；
/// 2. 通过 <see cref="IBuildRunEnvironment"/> 建立 Context 标准键与 attempt 中间目录；
/// 3. 逐个执行并按 Pending/Running/Success|Failed/Skipped 报状态；
/// 4. 首个失败即停：非致命失败同样中止，因为后续 Task 会消费不完整的 Context；
/// 5. 成功后调用环境完成 attempt→正式输出的提升，并把交付 token 交给调用方结算；
/// 6. 不做差异、发布、PackageIndex、baseline、版本回滚和 UI 操作。
/// 不提供 whitelist、stop-after、可编辑主干或生产管线截断式预览。
/// </remarks>
public static class BuildPipelineRunner
{
    /// <summary>
    /// 执行一次构建。
    /// </summary>
    /// <param name="request">运行请求；Environment 为 null 时只执行任务，不建 Context 标准键、不提升</param>
    /// <param name="tasks">Composer 产出的 Task 序列，顺序即执行顺序</param>
    public static BuildRunResult Run(BuildRequest request, IReadOnlyList<IBuildTask> tasks)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));

        if (tasks == null || tasks.Count == 0)
        {
            return SingleErrorResult(
                BuildErrorCodes.NoPipelineTasks,
                "没有可执行的 Task。主干由后端固定定义，请检查 Composer 输入与后端 PipelineBackbone。");
        }

        var context = new BuildContext();
        IBuildRunEnvironment environment = request.Environment;
        try
        {
            environment?.PrepareContext(context, request);
        }
        catch (BuildPipelineException ex)
        {
            return SingleErrorResult(ex.Code, ex.Message);
        }
        catch (Exception ex)
        {
            return SingleErrorResult(
                BuildErrorCodes.TaskExecutionError,
                $"构建环境初始化失败 - {ex.GetType().Name}: {ex.Message}。");
        }

        IBuildAttempt attempt;
        try
        {
            attempt = environment?.BeginAttempt(context, request);
        }
        catch (Exception ex)
        {
            return SingleErrorResult(
                BuildErrorCodes.BuildFailed,
                $"构建 attempt 初始化失败 - {ex.GetType().Name}: {ex.Message}。");
        }

        BuildRunResult result = ExecuteTasks(context, tasks, request.Options);
        if (!result.Success)
        {
            // 失败不提升：attempt 中间产物直接回收，正式输出与构建缓存保持运行前状态。
            attempt?.Discard();
            return result;
        }

        if (attempt == null)
            return result;

        if (!attempt.TryPromote(out IBuildDeliveryToken token, out string promoteError))
        {
            attempt.Discard();
            return WithError(
                result,
                BuildErrorCodes.BuildFailed,
                $"构建产物提升失败，正式输出保持原状: {promoteError}");
        }

        result.DeliveryToken = token;
        return result;
    }

    /// <summary>顺序执行 Task：先全部报 Pending，再逐个执行，首个失败后其余标 Skipped。</summary>
    private static BuildRunResult ExecuteTasks(
        BuildContext context,
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

        return new BuildRunResult
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

    private static BuildRunResult SingleErrorResult(string code, string message)
    {
        return new BuildRunResult
        {
            Success = false,
            TotalTasks = 0,
            CompletedTasks = 0,
            SkippedTasks = 0,
            TaskResults = new List<BuildTaskResult> { BuildTaskResult.Fail(code, message, true) }
        };
    }

    private static BuildRunResult WithError(BuildRunResult result, string code, string message)
    {
        result.Success = false;
        result.TaskResults.Add(BuildTaskResult.Fail(code, message, true));
        return result;
    }
}
