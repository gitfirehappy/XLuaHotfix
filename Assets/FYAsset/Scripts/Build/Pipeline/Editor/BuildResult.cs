using System.Collections.Generic;

/// <summary>
/// 整条构建管线的聚合执行结果。
/// 由 DAGScheduler.Execute 返回，包含所有 Task 的逐个结果、跳过计数和总计。
/// </summary>
public class BuildResult
{
    /// <summary>管线整体是否成功（所有 Task 均成功且无 Fatal 中止）</summary>
    public bool Success;

    /// <summary>参与调度的 Task 总数（仅包含 Enabled=true 的 Task）</summary>
    public int TotalTasks;

    /// <summary>成功完成的 Task 数量</summary>
    public int CompletedTasks;

    /// <summary>因前序 Fatal 错误而被跳过的 Task 数量</summary>
    public int SkippedTasks;

    /// <summary>每个 Task 的独立执行结果，按拓扑执行顺序排列</summary>
    public List<BuildTaskResult> TaskResults = new();
}
