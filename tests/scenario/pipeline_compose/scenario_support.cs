using System;
using System.Collections.Generic;

/// <summary>
/// Runner、Composer 与模式输出的纯 .NET 场景入口。
/// 通过 FakeEnvironment 提供编辑器动作，直接验证生产侧 Runner、Composer、Context 和结果类型。
/// </summary>
internal static class Program
{
    private static int Main()
    {
        var groups = new (string Name, Action<GateRun> Declare)[]
        {
            ("ComposeOrder", PipelineComposeTests.DeclareOrder),
            ("ComposeFailures", PipelineComposeTests.DeclareFailures),
            ("RunExecutionContract", PipelineRunTests.DeclareExecution),
            ("RunAttemptContract", PipelineRunTests.DeclareAttempt)
        };

        int failures = 0;
        int checks = 0;
        int passedChecks = 0;
        for (int i = 0; i < groups.Length; i++)
        {
            var run = new GateRun();
            try
            {
                groups[i].Declare(run);
                run.Execute(groups[i].Name);
                checks += run.Total;
                passedChecks += run.Passed;
                Console.WriteLine($"PASS {groups[i].Name} ({run.Passed}/{run.Total})");
            }
            catch (Exception ex)
            {
                checks += run.Total;
                passedChecks += run.Passed;
                failures++;
                Console.Error.WriteLine($"FAIL {groups[i].Name}: {ex.Message}");
            }
        }

        Console.WriteLine($"pipeline compose assertions: {passedChecks}/{checks} passed");
        Console.WriteLine($"pipeline compose scenario groups: {groups.Length - failures}/{groups.Length} passed");
        return failures == 0 && checks > 0 && passedChecks == checks ? 0 : 1;
    }
}

/// <summary>
/// 子检查执行器：每个断言独立执行并单独报告 GREEN/RED，失败信息汇总后抛出。
/// </summary>
internal sealed class GateRun
{
    private readonly List<(string Name, Action Run)> _checks = new List<(string, Action)>();

    public int Total { get; private set; }
    public int Passed { get; private set; }

    public void Check(string name, Action run) => _checks.Add((name, run));

    public void Execute(string groupName)
    {
        var failures = new List<string>();
        for (int i = 0; i < _checks.Count; i++)
        {
            try
            {
                _checks[i].Run();
                Passed++;
                Total++;
                Console.WriteLine($"  GREEN {_checks[i].Name}");
            }
            catch (Exception ex)
            {
                Total++;
                failures.Add($"{_checks[i].Name}: {ex.Message}");
                Console.WriteLine($"  RED   {_checks[i].Name}: {ex.Message}");
            }
        }

        if (failures.Count > 0)
        {
            throw new InvalidOperationException(
                $"{groupName}: {failures.Count}/{_checks.Count} 个子检查未满足 -> {string.Join(" | ", failures)}");
        }
    }
}

/// <summary>场景断言。失败时抛出携带定位信息的异常，由入口汇总。</summary>
internal static class Check
{
    public static void True(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    public static void False(bool value, string message) => True(!value, message);

    public static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{message}（期望={expected}, 实际={actual}）");
    }

    public static void NotNull(object value, string message) => True(value != null, message);

    public static void Null(object value, string message) => True(value == null, message);
}

/// <summary>
/// BuildPackageRequest 的最小替身：Runner 不解释包结构，只把它透传给运行环境，
/// 因此场景只需提供一个可构造的占位类型。
/// </summary>
public sealed class BuildPackageRequest
{
    public bool IsAttemptLayout;
    public string OutputDir = string.Empty;
    public string DeliveryOutputDir = string.Empty;
    public string PackageName = "Build_test";
}

/// <summary>记录调用顺序的运行环境替身；PrepareContext 与 BeginAttempt 都可记录调用次数。</summary>
internal sealed class FakeRunEnvironment : IBuildRunEnvironment
{
    public int PrepareCalls;
    public int BeginCalls;
    public FakeAttempt Attempt = new FakeAttempt();
    public Exception PrepareException;
    public Exception BeginException;

    public void PrepareContext(BuildContext context, BuildRequest request)
    {
        PrepareCalls++;
        if (PrepareException != null)
            throw PrepareException;

        context.Set("prepared", true);
    }

    public IBuildAttempt BeginAttempt(BuildContext context, BuildRequest request)
    {
        BeginCalls++;
        if (BeginException != null)
            throw BeginException;

        return Attempt;
    }
}

/// <summary>记录提升与清理调用的 attempt 替身。</summary>
internal sealed class FakeAttempt : IBuildAttempt
{
    public int PromoteCalls;
    public int DiscardCalls;
    public bool PromoteSucceeds = true;
    public string PromoteError = "promote failed";
    public FakeDeliveryToken Token = new FakeDeliveryToken();

    public bool TryPromote(out IBuildDeliveryToken token, out string error)
    {
        PromoteCalls++;
        if (!PromoteSucceeds)
        {
            token = null;
            error = PromoteError;
            return false;
        }

        token = Token;
        error = null;
        return true;
    }

    public void Discard() => DiscardCalls++;
}

internal sealed class FakeDeliveryToken : IBuildDeliveryToken
{
    public int CommitCalls;
    public int RollbackCalls;

    public void Commit() => CommitCalls++;
    public void Rollback() => RollbackCalls++;
}

/// <summary>记录执行顺序的 Task 替身；返回结果可由测试定制。</summary>
internal sealed class RecorderTask : IBuildTask
{
    private readonly Func<BuildTaskResult> _execute;

    public RecorderTask(string taskName, List<string> log, Func<BuildTaskResult> execute = null)
    {
        TaskName = taskName;
        Log = log;
        _execute = execute;
    }

    public string TaskName { get; }

    public List<string> Log { get; }

    public BuildTaskResult Execute(BuildContext ctx)
    {
        Log?.Add(TaskName);
        return _execute != null ? _execute() : BuildTaskResult.Ok();
    }
}
