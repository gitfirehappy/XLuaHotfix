#if UNITY_EDITOR
using System;

/// <summary>
/// 构建执行过程的可选参数，当前用于向编辑器 UI 回传 Task 状态。
/// </summary>
public sealed class BuildExecutionOptions
{
    public Action<BuildTaskExecutionEvent> TaskStatusChanged;

    public void Report(string taskName, BuildTaskExecutionStatus status, BuildTaskResult result = null)
    {
        TaskStatusChanged?.Invoke(new BuildTaskExecutionEvent(taskName, status, result));
    }
}
#endif
