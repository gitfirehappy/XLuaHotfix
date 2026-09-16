using System;
using System.Collections.Generic;

/// <summary>
/// Runner 契约：Context/attempt 建立顺序、状态事件、首个失败即停、失败不提升、成功交付 token。
/// </summary>
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
        run.Check("PrepareContextFailureCarriesPipelineCode", PrepareContextFailureCarriesPipelineCode);
        run.Check("BeginAttemptFailureFailsBeforeTasksRun", BeginAttemptFailureFailsBeforeTasksRun);
        run.Check("ContextIsPreparedBeforeTasksAndReturned", ContextIsPreparedBeforeTasksAndReturned);
    }

    public static void DeclareAttempt(GateRun run)
    {
        run.Check("FailedRunDiscardsAttemptWithoutPromote", FailedRunDiscardsAttemptWithoutPromote);
        run.Check("SuccessfulRunPromotesOnceAndHandsToken", SuccessfulRunPromotesOnceAndHandsToken);
        run.Check("PromoteFailureFailsRunAndDiscardsAttempt", PromoteFailureFailsRunAndDiscardsAttempt);
        run.Check("NoAttemptKeepsRunSuccessful", NoAttemptKeepsRunSuccessful);
    }

    private static void EmptyTaskListReportsNoPipelineTasks()
    {
        BuildRunResult result = BuildPipelineRunner.Run(Request(null), new List<IBuildTask>());

        Check.False(result.Success, "空任务列表必须判定失败");
        Check.Equal(1, result.TaskResults.Count, "空任务列表的错误结果条数");
        Check.Equal(BuildErrorCodes.NoPipelineTasks, result.TaskResults[0].ErrorCode, "空任务列表错误码");
        Check.Equal(0, result.TotalTasks, "空任务列表总任务数");
    }

    private static void EmptyTaskListDoesNotTouchEnvironment()
    {
        var environment = new FakeRunEnvironment();
        BuildRunResult result = BuildPipelineRunner.Run(Request(environment), Array.Empty<IBuildTask>());

        Check.False(result.Success, "空任务列表必须判定失败");
        Check.Equal(0, environment.PrepareCalls, "空任务列表不得建立 Context 标准键");
        Check.Equal(0, environment.BeginCalls, "空任务列表不得建立 attempt");
        Check.Null(result.Context, "空任务列表不得返回 Context");
    }

    private static void RunsEveryTaskInOrderOnSuccess()
    {
        var log = new List<string>();
        var tasks = new List<IBuildTask>
        {
            new RecorderTask("First", log),
            new RecorderTask("Second", log),
            new RecorderTask("Third", log)
        };

        BuildRunResult result = BuildPipelineRunner.Run(Request(new FakeRunEnvironment()), tasks);

        Check.True(result.Success, "全部成功时管线必须成功");
        Check.Equal(3, result.CompletedTasks, "成功任务数");
        Check.Equal(0, result.SkippedTasks, "跳过任务数");
        Check.Equal("First,Second,Third", string.Join(",", log), "Task 执行顺序");
    }

    private static void ReportsPendingRunningSuccessPerTask()
    {
        var events = new List<string>();
        var options = new BuildExecutionOptions
        {
            TaskStatusChanged = e => events.Add($"{e.TaskName}:{e.Status}")
        };
        var tasks = new List<IBuildTask>
        {
            new RecorderTask("First", null),
            new RecorderTask("Second", null)
        };

        BuildRunResult result = BuildPipelineRunner.Run(Request(new FakeRunEnvironment(), options), tasks);

        Check.True(result.Success, "全部成功时管线必须成功");
        Check.Equal(
            "First:Pending,Second:Pending,First:Running,First:Success,Second:Running,Second:Success",
            string.Join(",", events),
            "Task 状态事件序列");
    }

    private static void StopsAtFirstFailureAndSkipsRest()
    {
        var log = new List<string>();
        var events = new List<string>();
        var options = new BuildExecutionOptions { TaskStatusChanged = e => events.Add($"{e.TaskName}:{e.Status}") };
        var tasks = new List<IBuildTask>
        {
            new RecorderTask("First", log),
            new RecorderTask("Second", log, () => BuildTaskResult.Fail(BuildErrorCodes.BuildFailed, "second failed", true)),
            new RecorderTask("Third", log)
        };

        BuildRunResult result = BuildPipelineRunner.Run(Request(new FakeRunEnvironment(), options), tasks);

        Check.False(result.Success, "存在失败 Task 时管线必须失败");
        Check.Equal("First,Second", string.Join(",", log), "首个失败后的 Task 不得执行");
        Check.Equal(1, result.CompletedTasks, "成功任务数");
        Check.Equal(1, result.SkippedTasks, "被跳过任务数");
        Check.Equal(2, result.TaskResults.Count, "结果条数只包含已执行 Task");
        Check.Equal(BuildErrorCodes.BuildFailed, result.TaskResults[1].ErrorCode, "失败 Task 的错误码");
        Check.Equal("Second", result.TaskResults[1].TaskName, "失败 Task 名称由 Runner 回填");
        Check.True(events.Contains("Third:Skipped"), "未执行 Task 必须收到 Skipped 状态");
    }

    private static void NonFatalFailureAlsoStopsPipeline()
    {
        var log = new List<string>();
        var tasks = new List<IBuildTask>
        {
            new RecorderTask("First", log, () => BuildTaskResult.Fail(BuildErrorCodes.NoCollectedAssets, "soft failure", false)),
            new RecorderTask("Second", log)
        };

        BuildRunResult result = BuildPipelineRunner.Run(Request(new FakeRunEnvironment()), tasks);

        Check.False(result.Success, "非致命失败同样判定构建失败");
        Check.Equal("First", string.Join(",", log), "非致命失败同样必须停止后续 Task");
        Check.Equal(1, result.SkippedTasks, "非致命失败的跳过任务数");
    }

    private static void TaskExceptionBecomesExecutionError()
    {
        var tasks = new List<IBuildTask>
        {
            new RecorderTask("First", null, () => throw new InvalidOperationException("boom"))
        };

        BuildRunResult result = BuildPipelineRunner.Run(Request(new FakeRunEnvironment()), tasks);

        Check.False(result.Success, "Task 抛异常时管线必须失败");
        Check.Equal(BuildErrorCodes.TaskExecutionError, result.TaskResults[0].ErrorCode, "异常转换错误码");
        Check.True(result.TaskResults[0].ErrorMessage.Contains("boom"), "异常消息必须进入结果");
    }

    private static void NullTaskResultBecomesNullResultCode()
    {
        var tasks = new List<IBuildTask> { new RecorderTask("First", null, () => null) };

        BuildRunResult result = BuildPipelineRunner.Run(Request(new FakeRunEnvironment()), tasks);

        Check.False(result.Success, "Task 返回 null 时管线必须失败");
        Check.Equal(BuildErrorCodes.NullTaskResult, result.TaskResults[0].ErrorCode, "null 结果错误码");
    }

    private static void PrepareContextFailureCarriesPipelineCode()
    {
        var environment = new FakeRunEnvironment
        {
            PrepareException = new BuildPipelineException(BuildErrorCodes.InvalidPlatform, "unknown platform")
        };
        var log = new List<string>();
        var tasks = new List<IBuildTask> { new RecorderTask("First", log) };

        BuildRunResult result = BuildPipelineRunner.Run(Request(environment), tasks);

        Check.False(result.Success, "环境初始化失败必须判定构建失败");
        Check.Equal(BuildErrorCodes.InvalidPlatform, result.TaskResults[0].ErrorCode, "环境初始化失败错误码");
        Check.Equal(0, log.Count, "环境初始化失败时不得执行任何 Task");
    }

    /// <summary>attempt 建立失败同样是致命前置错误：不得执行任何 Task，也不得触碰正式输出。</summary>
    private static void BeginAttemptFailureFailsBeforeTasksRun()
    {
        var environment = new FakeRunEnvironment
        {
            BeginException = new InvalidOperationException("attempt dir rejected")
        };
        var log = new List<string>();
        var tasks = new List<IBuildTask> { new RecorderTask("First", log) };

        BuildRunResult result = BuildPipelineRunner.Run(Request(environment), tasks);

        Check.False(result.Success, "attempt 初始化失败必须判定构建失败");
        Check.Equal(BuildErrorCodes.BuildFailed, result.TaskResults[0].ErrorCode, "attempt 初始化失败错误码");
        Check.Equal(0, environment.Attempt.PromoteCalls, "attempt 初始化失败不得提升");
        Check.Equal(0, log.Count, "attempt 初始化失败时不得执行任何 Task");
    }

    private static void ContextIsPreparedBeforeTasksAndReturned()
    {
        var environment = new FakeRunEnvironment();
        var tasks = new List<IBuildTask>
        {
            new RecorderTask("First", null, () => BuildTaskResult.Ok())
        };

        BuildRunResult result = BuildPipelineRunner.Run(Request(environment), tasks);

        Check.True(result.Success, "管线必须成功");
        Check.Equal(1, environment.PrepareCalls, "PrepareContext 调用次数");
        Check.Equal(1, environment.BeginCalls, "BeginAttempt 调用次数");
        Check.NotNull(result.Context, "结果必须携带本次运行的 Context");
        Check.True(result.Context.Get<bool>("prepared"), "Task 必须看到环境写入的 Context 标准键");
    }

    private static void FailedRunDiscardsAttemptWithoutPromote()
    {
        var environment = new FakeRunEnvironment();
        var tasks = new List<IBuildTask>
        {
            new RecorderTask("First", null, () => BuildTaskResult.Fail(BuildErrorCodes.BuildFailed, "failed", true))
        };

        BuildRunResult result = BuildPipelineRunner.Run(Request(environment), tasks);

        Check.False(result.Success, "失败构建必须判定失败");
        Check.Equal(0, environment.Attempt.PromoteCalls, "失败构建不得提升正式输出");
        Check.Equal(1, environment.Attempt.DiscardCalls, "失败构建必须清理 attempt");
        Check.Null(result.DeliveryToken, "失败构建不得返回交付 token");
    }

    private static void SuccessfulRunPromotesOnceAndHandsToken()
    {
        var environment = new FakeRunEnvironment();
        var tasks = new List<IBuildTask> { new RecorderTask("First", null) };

        BuildRunResult result = BuildPipelineRunner.Run(Request(environment), tasks);

        Check.True(result.Success, "成功构建必须判定成功");
        Check.Equal(1, environment.Attempt.PromoteCalls, "成功构建必须提升一次");
        Check.Equal(0, environment.Attempt.DiscardCalls, "成功构建不得清理 attempt");
        Check.True(ReferenceEquals(environment.Attempt.Token, result.DeliveryToken), "交付 token 必须交给调用方结算");
        Check.Equal(0, environment.Attempt.Token.CommitCalls, "Runner 不得自行 Commit 交付");
        Check.Equal(0, environment.Attempt.Token.RollbackCalls, "Runner 不得自行 Rollback 交付");
    }

    private static void PromoteFailureFailsRunAndDiscardsAttempt()
    {
        var environment = new FakeRunEnvironment();
        environment.Attempt.PromoteSucceeds = false;
        var tasks = new List<IBuildTask> { new RecorderTask("First", null) };

        BuildRunResult result = BuildPipelineRunner.Run(Request(environment), tasks);

        Check.False(result.Success, "提升失败必须判定构建失败");
        Check.Equal(1, environment.Attempt.PromoteCalls, "提升调用次数");
        Check.Equal(1, environment.Attempt.DiscardCalls, "提升失败必须清理 attempt");
        Check.Null(result.DeliveryToken, "提升失败不得返回交付 token");
        Check.Equal(1, result.CompletedTasks, "提升失败前 Task 本身已成功，成功计数保留");
        Check.Equal(2, result.TaskResults.Count, "提升失败必须在 Task 结果之后追加一条失败记录");
        Check.Equal(BuildErrorCodes.BuildFailed, result.TaskResults[1].ErrorCode, "提升失败错误码");
    }

    private static void NoAttemptKeepsRunSuccessful()
    {
        var environment = new FakeRunEnvironment { Attempt = null };
        var tasks = new List<IBuildTask> { new RecorderTask("First", null) };

        BuildRunResult result = BuildPipelineRunner.Run(Request(environment), tasks);

        Check.True(result.Success, "无 attempt 的布局在任务成功时仍必须成功");
        Check.Null(result.DeliveryToken, "无 attempt 时不产生交付 token");
    }

    private static BuildPipelineRequest Request(FakeRunEnvironment environment, BuildExecutionOptions options = null)
    {
        return new BuildPipelineRequest(new BuildRequest(), options, environment);
    }
}
