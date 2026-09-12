#if UNITY_EDITOR
using System;

/// <summary>
/// 构建执行过程的可选参数，当前用于向编辑器 UI 回传 Task 状态。
/// </summary>
public sealed class BuildExecutionOptions
{
    public Action<BuildTaskExecutionEvent> TaskStatusChanged;

    /// <summary>
    /// 构建确认界面显式选择的通道（alpha/beta/rc/release）；null 表示继承当前全局通道。
    /// 空字符串表示显式切到无后缀正式版。
    /// </summary>
    public string RequestedChannel;

    public void Report(string taskName, BuildTaskExecutionStatus status, BuildTaskResult result = null)
    {
        TaskStatusChanged?.Invoke(new BuildTaskExecutionEvent(taskName, status, result));
    }
}
#endif
