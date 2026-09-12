#if UNITY_EDITOR
/// <summary>
/// 构建 Task 在一次调度执行中的可视状态。
/// </summary>
public enum BuildTaskExecutionStatus
{
    Pending = 0,
    Running = 1,
    Success = 2,
    Failed = 3,
    Skipped = 4
}
#endif
