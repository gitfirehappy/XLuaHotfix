#if UNITY_EDITOR
/// <summary>
/// DAGScheduler 发出的单个 Task 状态事件。
/// </summary>
public readonly struct BuildTaskExecutionEvent
{
    public string TaskName { get; }
    public BuildTaskExecutionStatus Status { get; }
    public BuildTaskResult Result { get; }

    public BuildTaskExecutionEvent(string taskName, BuildTaskExecutionStatus status, BuildTaskResult result = null)
    {
        TaskName = taskName;
        Status = status;
        Result = result;
    }
}
#endif
