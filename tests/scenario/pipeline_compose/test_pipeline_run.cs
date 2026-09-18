using System;
using System.Collections.Generic;

internal static class PipelineRunTests
{
    public static void DeclareExecution(GateRun run)
    {
        run.Check("EmptyTaskListReportsNoPipelineTasks", EmptyTaskListReportsNoPipelineTasks);
        run.Check("EmptyTaskListDoesNotTouchEnvironment", EmptyTaskListDoesNotTouchEnvironment);
        run.Check("RunsEveryTaskInOrderOnSuccess", RunsEveryTaskInOrderOnSuccess);
        run.Check("ReportsPendingRunningSuccessPerTask", ReportsPendingRunningSuccessPerTask);
        run.Check("StopsAtFirstFailureAndSkipsRest", StopsAtFirstFailureAndSkipsRest);
        run.Check("NonFatalFailureAlsoStopsPipeline", NonFatalFailureAlsoStopsPipeline);
        run.Check("TaskExceptionBecomesExecutionError", TaskExceptionBecomesExecutionError);
        run.Check("NullTaskResultBecomesNullResultCode", NullTaskResultBecomesNullResultCode);
        run.Check("PrepareContextFailureStopsTasks", PrepareContextFailureStopsTasks);
        run.Check("ContextIsPreparedBeforeTasksAndReturned", ContextIsPreparedBeforeTasksAndReturned);
    }

    private static void EmptyTaskListReportsNoPipelineTasks()
    {
        BuildPipelineResult result = Run(null, new List<IBuildTask>());
        Check.False(result.Success, "空任务列表必须判定失败");
        Check.Equal(BuildErrorCodes.NoPipelineTasks, result.TaskResults[0].ErrorCode, "空任务列表错误码");
        Check.Equal(0, result.TotalTasks, "空任务列表总任务数");
    }

    private static void EmptyTaskListDoesNotTouchEnvironment()
    {
        var environment = new FakeRunEnvironment();
        BuildPipelineResult result = Run(environment, Array.Empty<IBuildTask>());
        Check.False(result.Success, "空任务列表必须判定失败");
        Check.Equal(0, environment.PrepareCalls, "空任务列表不得建立 Context 标准键");
        Check.Null(result.Context, "空任务列表不得返回 Context");
    }

    private static void RunsEveryTaskInOrderOnSuccess()
    {
        var log = new List<string>();
        var tasks = new List<IBuildTask>
        {
            new RecorderTask("First", log), new RecorderTask("Second", log), new RecorderTask("Third", log)
        };
        BuildPipelineResult result = Run(new FakeRunEnvironment(), tasks);
        Check.True(result.Success, "全部成功时管线必须成功");
        Check.Equal("First,Second,Third", string.Join(",", log), "Task 执行顺序");
    }

    private static void ReportsPendingRunningSuccessPerTask()
    {
        var events = new List<string>();
        var options = new BuildExecutionOptions { TaskStatusChanged = e => events.Add($"{e.TaskName}:{e.Status}") };
        BuildPipelineResult result = Run(new FakeRunEnvironment(), new List<IBuildTask>
        {
            new RecorderTask("First", null), new RecorderTask("Second", null)
        }, options);
        Check.True(result.Success, "全部成功时管线必须成功");
        Check.Equal("First:Pending,Second:Pending,First:Running,First:Success,Second:Running,Second:Success",
            string.Join(",", events), "Task 状态事件序列");
    }

    private static void StopsAtFirstFailureAndSkipsRest()
    {
        var log = new List<string>();
        var tasks = new List<IBuildTask>
        {
            new RecorderTask("First", log),
            new RecorderTask("Second", log, () => BuildTaskResult.Fail(BuildErrorCodes.BuildFailed, "second failed", true)),
            new RecorderTask("Third", log)
        };
        BuildPipelineResult result = Run(new FakeRunEnvironment(), tasks);
        Check.False(result.Success, "存在失败 Task 时管线必须失败");
        Check.Equal("First,Second", string.Join(",", log), "首个失败后的 Task 不得执行");
        Check.Equal(1, result.CompletedTasks, "成功任务数");
        Check.Equal(1, result.SkippedTasks, "被跳过任务数");
    }

    private static void NonFatalFailureAlsoStopsPipeline()
    {
        var log = new List<string>();
        BuildPipelineResult result = Run(new FakeRunEnvironment(), new List<IBuildTask>
        {
            new RecorderTask("First", log, () => BuildTaskResult.Fail(BuildErrorCodes.NoCollectedAssets, "soft failure", false)),
            new RecorderTask("Second", log)
        });
        Check.False(result.Success, "非致命失败同样判定构建失败");
        Check.Equal("First", string.Join(",", log), "非致命失败同样必须停止后续 Task");
    }

    private static void TaskExceptionBecomesExecutionError()
    {
        BuildPipelineResult result = Run(new FakeRunEnvironment(), new List<IBuildTask>
        {
            new RecorderTask("First", null, () => throw new InvalidOperationException("boom"))
        });
        Check.False(result.Success, "Task 抛异常时管线必须失败");
        Check.Equal(BuildErrorCodes.TaskExecutionError, result.TaskResults[0].ErrorCode, "异常转换错误码");
        Check.True(result.TaskResults[0].ErrorMessage.Contains("boom"), "异常消息必须进入结果");
    }

    private static void NullTaskResultBecomesNullResultCode()
    {
        BuildPipelineResult result = Run(new FakeRunEnvironment(), new List<IBuildTask>
        {
            new RecorderTask("First", null, () => null)
        });
        Check.Equal(BuildErrorCodes.NullTaskResult, result.TaskResults[0].ErrorCode, "null 结果错误码");
    }

    private static void PrepareContextFailureStopsTasks()
    {
        var environment = new FakeRunEnvironment { PrepareException = new InvalidOperationException("unknown platform") };
        var log = new List<string>();
        BuildPipelineResult result = Run(environment, new List<IBuildTask> { new RecorderTask("First", log) });
        Check.False(result.Success, "环境初始化失败必须判定构建失败");
        Check.Equal(BuildErrorCodes.TaskExecutionError, result.TaskResults[0].ErrorCode, "环境初始化失败错误码");
        Check.Equal(0, log.Count, "环境初始化失败时不得执行任何 Task");
    }

    private static void ContextIsPreparedBeforeTasksAndReturned()
    {
        var environment = new FakeRunEnvironment();
        BuildPipelineResult result = Run(environment, new List<IBuildTask> { new RecorderTask("First", null) });
        Check.True(result.Success, "管线必须成功");
        Check.Equal(1, environment.PrepareCalls, "PrepareContext 调用次数");
        Check.NotNull(result.Context, "结果必须携带本次运行的 Context");
        Check.True(result.Context.Get<bool>("prepared"), "Task 必须看到环境写入的 Context 标准键");
    }

    private static BuildPipelineResult Run(FakeRunEnvironment environment, IReadOnlyList<IBuildTask> tasks,
        BuildExecutionOptions options = null)
    {
        return BuildPipelineRunner.Run(new BuildRequest(), options, environment, tasks);
    }
}
