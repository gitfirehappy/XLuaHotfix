using System.Collections.Generic;

/// <summary>
/// BuildPipelineRunner 的 Task 执行结果。
/// </summary>
public class BuildPipelineResult
{
    public bool Success;
    public int TotalTasks;
    public int CompletedTasks;
    public int SkippedTasks;
    public List<BuildTaskResult> TaskResults = new();
    public BuildRunContext Context;
}
