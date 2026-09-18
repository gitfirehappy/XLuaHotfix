using System;
using System.Collections.Generic;

/// <summary>
/// 构建管线组装器：把后端固定主干（<see cref="CoreTaskSlot"/> 列表）与配置里的自定义 Task
/// （<see cref="CustomTaskEntry"/> 列表）组装成 Runner 直接执行的线性 Task 序列。
/// </summary>
/// <remarks>
/// 顺序语义：
/// Input 槽 → [主干[0] 槽的自定义 Task] → 主干[0] → [主干[1] 槽的自定义 Task] → 主干[1] → …… → 主干[N-1] → Output 槽。
/// 自定义条目的 Slot 表示“插入到该槽主干任务之前”，同一槽内的多条按配置顺序稳定排列。
/// 主干 Task 由后端直接 new，只有自定义 Task 走 BuildTaskResolver 反射解析。
/// 所有校验失败都抛 <see cref="InvalidOperationException"/>，不做静默跳过。
/// </remarks>
public static class BuildPipelineComposer
{
    /// <summary>首个主干阶段之前的槽位名</summary>
    public const string InputSlot = "Input";

    /// <summary>末个主干阶段之后的槽位名</summary>
    public const string OutputSlot = "Output";

    /// <summary>
    /// 组装一次构建的 Task 序列。
    /// </summary>
    /// <param name="coreTasks">后端固定主干，按执行顺序排列，槽位名唯一且非空</param>
    /// <param name="customTasks">配置中的自定义 Task；空列表或 null 表示没有自定义插入</param>
    public static IReadOnlyList<IBuildTask> Compose(
        IReadOnlyList<CoreTaskSlot> coreTasks,
        IReadOnlyList<CustomTaskEntry> customTasks)
    {
        List<CoreTaskSlot> core = ValidateCoreTasks(coreTasks);
        var legalSlots = new HashSet<string>(StringComparer.Ordinal);
        var coreTaskNames = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < core.Count; i++)
        {
            legalSlots.Add(core[i].Slot);
            coreTaskNames.Add(core[i].Task.TaskName);
        }
        legalSlots.Add(InputSlot);
        legalSlots.Add(OutputSlot);

        Dictionary<string, List<IBuildTask>> customBySlot = ResolveCustomTasks(
            customTasks, core, legalSlots, coreTaskNames);

        var result = new List<IBuildTask>(core.Count);
        AppendSlot(result, customBySlot, InputSlot);
        for (int i = 0; i < core.Count; i++)
        {
            // 槽位语义是“插入到该槽主干任务之前”，因此先放该槽的自定义 Task，再放主干任务。
            AppendSlot(result, customBySlot, core[i].Slot);
            result.Add(core[i].Task);
        }
        AppendSlot(result, customBySlot, OutputSlot);
        return result;
    }

    /// <summary>校验主干定义：非空、槽名非空且唯一、主干 Task 实例非空。</summary>
    private static List<CoreTaskSlot> ValidateCoreTasks(IReadOnlyList<CoreTaskSlot> coreTasks)
    {
        var core = new List<CoreTaskSlot>();
        if (coreTasks == null)
            return core;

        var seenSlots = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < coreTasks.Count; i++)
        {
            CoreTaskSlot slot = coreTasks[i];
            if (string.IsNullOrWhiteSpace(slot.Slot))
            {
                throw Failure(
                    BuildErrorCodes.CoreTaskSlotEmpty,
                    $"主干阶段[{i}]没有槽位名。主干槽位名必须是稳定的阶段标识符。");
            }

            if (!seenSlots.Add(slot.Slot))
            {
                throw Failure(
                    BuildErrorCodes.CoreTaskSlotDuplicate,
                    $"主干槽位名重复: '{slot.Slot}'。同一主干内槽位名必须唯一，否则自定义 Task 的插入点有歧义。");
            }

            if (slot.Task == null)
            {
                throw Failure(
                    BuildErrorCodes.CoreTaskSlotMissing,
                    $"主干阶段 '{slot.Slot}' 没有对应的主干 Task 实例。");
            }

            core.Add(slot);
        }

        if (core.Count == 0)
        {
            throw Failure(
                BuildErrorCodes.NoPipelineTasks,
                "主干阶段定义为空，没有可执行的构建阶段。请检查后端 PipelineBackbone 定义。");
        }

        return core;
    }

    /// <summary>解析并校验自定义 Task：槽位合法、TaskName 非空/不重复/不与主干同名、实现可解析。</summary>
    private static Dictionary<string, List<IBuildTask>> ResolveCustomTasks(
        IReadOnlyList<CustomTaskEntry> customTasks,
        List<CoreTaskSlot> core,
        HashSet<string> legalSlots,
        HashSet<string> coreTaskNames)
    {
        var customBySlot = new Dictionary<string, List<IBuildTask>>(StringComparer.Ordinal);
        if (customTasks == null)
            return customBySlot;

        var seenTaskNames = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < customTasks.Count; i++)
        {
            CustomTaskEntry entry = customTasks[i];
            string taskName = entry.TaskName;

            if (string.IsNullOrWhiteSpace(taskName))
            {
                throw Failure(
                    BuildErrorCodes.CustomTaskNameEmpty,
                    $"管线配置第 {i} 条自定义 Task 的 TaskName 为空。");
            }

            if (string.IsNullOrWhiteSpace(entry.Slot))
            {
                throw Failure(
                    BuildErrorCodes.CustomTaskSlotUnknown,
                    $"自定义 Task '{taskName}' 未声明 Slot，无法确定插入位置。合法槽位: {DescribeLegalSlots(core)}。");
            }

            if (!legalSlots.Contains(entry.Slot))
            {
                throw Failure(
                    BuildErrorCodes.CustomTaskSlotUnknown,
                    $"自定义 Task '{taskName}' 的 Slot '{entry.Slot}' 不是合法槽位。合法槽位: {DescribeLegalSlots(core)}。");
            }

            if (!seenTaskNames.Add(taskName))
            {
                throw Failure(
                    BuildErrorCodes.CustomTaskNameDuplicate,
                    $"管线配置包含重复的自定义 TaskName: '{taskName}'。同一 Task 每次构建只能插入一次。");
            }

            if (coreTaskNames.Contains(taskName))
            {
                throw Failure(
                    BuildErrorCodes.CustomTaskNameConflictsCore,
                    $"自定义 Task 名 '{taskName}' 与主干阶段同名。主干由后端固定定义，不能由配置重复声明。");
            }

            if (!BuildTaskResolver.TryCreateTask(taskName, out IBuildTask task, out string error))
            {
                throw Failure(
                    SelectResolutionErrorCode(taskName),
                    $"自定义 Task 解析失败: {error}");
            }

            if (!customBySlot.TryGetValue(entry.Slot, out List<IBuildTask> list))
            {
                list = new List<IBuildTask>();
                customBySlot[entry.Slot] = list;
            }
            list.Add(task);
        }

        return customBySlot;
    }

    private static void AppendSlot(List<IBuildTask> target, Dictionary<string, List<IBuildTask>> customBySlot, string slot)
    {
        if (!customBySlot.TryGetValue(slot, out List<IBuildTask> list))
            return;

        for (int i = 0; i < list.Count; i++)
            target.Add(list[i]);
    }

    private static string DescribeLegalSlots(List<CoreTaskSlot> core)
    {
        var names = new List<string>(core.Count + 2) { InputSlot };
        for (int i = 0; i < core.Count; i++)
            names.Add(core[i].Slot);
        names.Add(OutputSlot);
        return string.Join(" / ", names);
    }

    private static InvalidOperationException Failure(string code, string message)
    {
        return new InvalidOperationException($"[{code}] {message}");
    }

    /// <summary>配置引用的 TaskName 缺失与“存在但构造失败”用不同错误码，便于区分配置问题与实现问题。</summary>
    private static string SelectResolutionErrorCode(string taskName)
    {
        IReadOnlyList<TaskResolutionDiagnostic> diagnostics = BuildTaskResolver.GetDiagnostics();
        for (int i = 0; i < diagnostics.Count; i++)
        {
            TaskResolutionDiagnostic diagnostic = diagnostics[i];
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
}
