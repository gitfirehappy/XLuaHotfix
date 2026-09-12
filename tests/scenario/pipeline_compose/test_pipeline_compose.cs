using System;
using System.Collections.Generic;

/// <summary>
/// Composer 契约：槽位顺序、同槽多条稳定性，以及全部组装失败的致命语义。
/// </summary>
internal static class PipelineComposeTests
{
    public static void DeclareOrder(GateRun run)
    {
        run.Check("InputThenCoreThenSlotCustomThenOutput", InputThenCoreThenSlotCustomThenOutput);
        run.Check("SameSlotKeepsConfiguredOrder", SameSlotKeepsConfiguredOrder);
        run.Check("CustomTasksAreResolvedByReflection", CustomTasksAreResolvedByReflection);
    }

    public static void DeclareFailures(GateRun run)
    {
        run.Check("EmptyCoreTasksIsFatal", EmptyCoreTasksIsFatal);
        run.Check("EmptyCoreSlotNameIsFatal", EmptyCoreSlotNameIsFatal);
        run.Check("DuplicateCoreSlotNameIsFatal", DuplicateCoreSlotNameIsFatal);
        run.Check("MissingCoreTaskIsFatal", MissingCoreTaskIsFatal);
        run.Check("UnknownSlotIsFatal", UnknownSlotIsFatal);
        run.Check("EmptySlotIsFatal", EmptySlotIsFatal);
        run.Check("EmptyCustomTaskNameIsFatal", EmptyCustomTaskNameIsFatal);
        run.Check("DuplicateCustomTaskNameIsFatal", DuplicateCustomTaskNameIsFatal);
        run.Check("CustomTaskNameConflictsCoreIsFatal", CustomTaskNameConflictsCoreIsFatal);
        run.Check("UnresolvableCustomTaskIsFatal", UnresolvableCustomTaskIsFatal);
        run.Check("BrokenCustomTaskConstructorIsFatal", BrokenCustomTaskConstructorIsFatal);
    }

    /// <summary>顺序：Input 槽 → 主干[0] 槽自定义 → 主干[0] → …… → 主干[N-1] → Output 槽。</summary>
    private static void InputThenCoreThenSlotCustomThenOutput()
    {
        var log = new List<string>();
        IReadOnlyList<CoreTaskSlot> core = new List<CoreTaskSlot>
        {
            new CoreTaskSlot("CoreA", new RecorderTask("CoreA", log)),
            new CoreTaskSlot("CoreB", new RecorderTask("CoreB", log))
        };
        IReadOnlyList<CustomTaskEntry> custom = new List<CustomTaskEntry>
        {
            Entry("ComposeTestInput", BuildPipelineComposer.InputSlot),
            Entry("ComposeTestBeforeA", "CoreA"),
            Entry("ComposeTestBeforeB", "CoreB"),
            Entry("ComposeTestOutput", BuildPipelineComposer.OutputSlot)
        };

        IReadOnlyList<IBuildTask> composed = BuildPipelineComposer.Compose(core, custom);
        AssertNames(
            composed,
            "ComposeTestInput", "ComposeTestBeforeA", "CoreA", "ComposeTestBeforeB", "CoreB", "ComposeTestOutput");
    }

    /// <summary>同一槽内多条按配置顺序稳定排列，且都插在该槽主干任务之前。</summary>
    private static void SameSlotKeepsConfiguredOrder()
    {
        IReadOnlyList<CoreTaskSlot> core = new List<CoreTaskSlot>
        {
            new CoreTaskSlot("CoreA", new RecorderTask("CoreA", null))
        };
        IReadOnlyList<CustomTaskEntry> custom = new List<CustomTaskEntry>
        {
            Entry("ComposeTestFirst", "CoreA"),
            Entry("ComposeTestSecond", "CoreA"),
            Entry("ComposeTestThird", "CoreA")
        };

        AssertNames(
            BuildPipelineComposer.Compose(core, custom),
            "ComposeTestFirst", "ComposeTestSecond", "ComposeTestThird", "CoreA");
    }

    /// <summary>自定义 Task 通过 BuildTaskResolver 解析；主干 Task 直接用给定实例。</summary>
    private static void CustomTasksAreResolvedByReflection()
    {
        var coreTask = new RecorderTask("CoreA", null);
        IReadOnlyList<CoreTaskSlot> core = new List<CoreTaskSlot> { new CoreTaskSlot("CoreA", coreTask) };
        IReadOnlyList<CustomTaskEntry> custom = new List<CustomTaskEntry> { Entry("ComposeTestAlpha", "CoreA") };

        IReadOnlyList<IBuildTask> composed = BuildPipelineComposer.Compose(core, custom);
        Check.Equal(2, composed.Count, "Input/CoreA 组装结果条数");
        Check.True(ReferenceEquals(coreTask, composed[1]), "主干 Task 必须直接使用后端传入的实例，不经反射重建");
        Check.Equal("ComposeTestAlpha", composed[0].TaskName, "自定义 Task 的 TaskName 必须来自解析出的实现");
        Check.Equal("ComposeTestAlphaTask", composed[0].GetType().Name, "自定义 Task 必须解析成注册该 TaskName 的实现类型");
    }

    private static void EmptyCoreTasksIsFatal() =>
        ExpectComposeFailure(BuildErrorCodes.NoPipelineTasks, new List<CoreTaskSlot>(), null);

    private static void EmptyCoreSlotNameIsFatal() =>
        ExpectComposeFailure(
            BuildErrorCodes.CoreTaskSlotEmpty,
            new List<CoreTaskSlot> { new CoreTaskSlot("  ", new RecorderTask("CoreA", null)) },
            null);

    private static void DuplicateCoreSlotNameIsFatal() =>
        ExpectComposeFailure(
            BuildErrorCodes.CoreTaskSlotDuplicate,
            new List<CoreTaskSlot>
            {
                new CoreTaskSlot("CoreA", new RecorderTask("CoreA", null)),
                new CoreTaskSlot("CoreA", new RecorderTask("CoreB", null))
            },
            null);

    private static void MissingCoreTaskIsFatal() =>
        ExpectComposeFailure(
            BuildErrorCodes.CoreTaskSlotMissing,
            new List<CoreTaskSlot> { new CoreTaskSlot("CoreA", null) },
            null);

    private static void UnknownSlotIsFatal() =>
        ExpectComposeFailure(
            BuildErrorCodes.CustomTaskSlotUnknown,
            DefaultCore(),
            new List<CustomTaskEntry> { Entry("ComposeTestAlpha", "NoSuchStage") });

    private static void EmptySlotIsFatal() =>
        ExpectComposeFailure(
            BuildErrorCodes.CustomTaskSlotUnknown,
            DefaultCore(),
            new List<CustomTaskEntry> { Entry("ComposeTestAlpha", string.Empty) });

    private static void EmptyCustomTaskNameIsFatal() =>
        ExpectComposeFailure(
            BuildErrorCodes.CustomTaskNameEmpty,
            DefaultCore(),
            new List<CustomTaskEntry> { Entry("  ", "CoreA") });

    private static void DuplicateCustomTaskNameIsFatal() =>
        ExpectComposeFailure(
            BuildErrorCodes.CustomTaskNameDuplicate,
            DefaultCore(),
            new List<CustomTaskEntry>
            {
                Entry("ComposeTestAlpha", "CoreA"),
                Entry("ComposeTestAlpha", BuildPipelineComposer.InputSlot)
            });

    private static void CustomTaskNameConflictsCoreIsFatal() =>
        ExpectComposeFailure(
            BuildErrorCodes.CustomTaskNameConflictsCore,
            DefaultCore(),
            new List<CustomTaskEntry> { Entry("CoreA", "CoreA") });

    private static void UnresolvableCustomTaskIsFatal() =>
        ExpectComposeFailure(
            BuildErrorCodes.TaskNotFound,
            DefaultCore(),
            new List<CustomTaskEntry> { Entry("ComposeTestNotRegistered", "CoreA") });

    private static void BrokenCustomTaskConstructorIsFatal() =>
        ExpectComposeFailure(
            BuildErrorCodes.TaskResolutionFailed,
            DefaultCore(),
            new List<CustomTaskEntry> { Entry("ComposeTestBrokenCtor", "CoreA") });

    private static List<CoreTaskSlot> DefaultCore()
    {
        return new List<CoreTaskSlot>
        {
            new CoreTaskSlot("CoreA", new RecorderTask("CoreA", null)),
            new CoreTaskSlot("CoreB", new RecorderTask("CoreB", null))
        };
    }

    private static CustomTaskEntry Entry(string taskName, string slot)
    {
        return new CustomTaskEntry { TaskName = taskName, Slot = slot };
    }

    private static void AssertNames(IReadOnlyList<IBuildTask> composed, params string[] expected)
    {
        Check.Equal(expected.Length, composed.Count, "组装结果条数");
        for (int i = 0; i < expected.Length; i++)
            Check.Equal(expected[i], composed[i].TaskName, $"组装结果第 {i} 条");
    }

    private static void ExpectComposeFailure(
        string expectedCode,
        IReadOnlyList<CoreTaskSlot> core,
        IReadOnlyList<CustomTaskEntry> custom)
    {
        try
        {
            BuildPipelineComposer.Compose(core, custom);
        }
        catch (BuildPipelineException ex)
        {
            Check.Equal(expectedCode, ex.Code, "组装失败错误码");
            return;
        }

        throw new InvalidOperationException($"组装本应失败并抛出 {expectedCode}，实际成功返回");
    }
}

/// <summary>供反射解析的测试 Task：TaskName 与类型名一致，便于诊断匹配。</summary>
internal sealed class ComposeTestAlphaTask : IBuildTask
{
    public string TaskName => "ComposeTestAlpha";
    public BuildTaskResult Execute(BuildContext ctx) => BuildTaskResult.Ok();
}

/// <summary>供反射解析的测试 Task。</summary>
internal sealed class ComposeTestInputTask : IBuildTask
{
    public string TaskName => "ComposeTestInput";
    public BuildTaskResult Execute(BuildContext ctx) => BuildTaskResult.Ok();
}

/// <summary>供反射解析的测试 Task。</summary>
internal sealed class ComposeTestBeforeATask : IBuildTask
{
    public string TaskName => "ComposeTestBeforeA";
    public BuildTaskResult Execute(BuildContext ctx) => BuildTaskResult.Ok();
}

/// <summary>供反射解析的测试 Task。</summary>
internal sealed class ComposeTestBeforeBTask : IBuildTask
{
    public string TaskName => "ComposeTestBeforeB";
    public BuildTaskResult Execute(BuildContext ctx) => BuildTaskResult.Ok();
}

/// <summary>供反射解析的测试 Task。</summary>
internal sealed class ComposeTestOutputTask : IBuildTask
{
    public string TaskName => "ComposeTestOutput";
    public BuildTaskResult Execute(BuildContext ctx) => BuildTaskResult.Ok();
}

/// <summary>供反射解析的测试 Task。</summary>
internal sealed class ComposeTestFirstTask : IBuildTask
{
    public string TaskName => "ComposeTestFirst";
    public BuildTaskResult Execute(BuildContext ctx) => BuildTaskResult.Ok();
}

/// <summary>供反射解析的测试 Task。</summary>
internal sealed class ComposeTestSecondTask : IBuildTask
{
    public string TaskName => "ComposeTestSecond";
    public BuildTaskResult Execute(BuildContext ctx) => BuildTaskResult.Ok();
}

/// <summary>供反射解析的测试 Task。</summary>
internal sealed class ComposeTestThirdTask : IBuildTask
{
    public string TaskName => "ComposeTestThird";
    public BuildTaskResult Execute(BuildContext ctx) => BuildTaskResult.Ok();
}

/// <summary>构造即失败的 Task：反射解析应给出 TASK_RESOLUTION_FAILED 而不是 TASK_NOT_FOUND。</summary>
internal sealed class ComposeTestBrokenCtor : IBuildTask
{
    public ComposeTestBrokenCtor()
    {
        throw new InvalidOperationException("intentional constructor failure");
    }

    public string TaskName => "ComposeTestBrokenCtor";
    public BuildTaskResult Execute(BuildContext ctx) => BuildTaskResult.Ok();
}
