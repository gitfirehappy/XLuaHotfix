using System.Collections.Generic;

/// <summary>
/// 单个 Task 的执行结果 —— 包含成功/失败状态、错误码、警告列表。
/// Fatal 错误会中止调度器后续所有批次，Non-Fatal 错误仅记录继续执行。
/// 通过静态工厂方法构造。
/// </summary>
public class BuildTaskResult
{
    /// <summary>Task 名称；由调度器填充，用于上层诊断显示。</summary>
    public string TaskName;

    /// <summary>Task 是否成功执行</summary>
    public bool Success;

    /// <summary>错误码（如 "CIRCULAR_TASK_DEPENDENCY"），成功时为 null</summary>
    public string ErrorCode;

    /// <summary>人类可读的错误描述</summary>
    public string ErrorMessage;

    /// <summary>非致命警告列表，成功时可为 null 或空</summary>
    public List<string> Warnings;

    /// <summary>true -> 调度器中止所有后续批次；false -> 继续执行</summary>
    public bool IsFatal;

    private BuildTaskResult()
    {
    }

    /// <summary>构造成功的 Task 结果</summary>
    /// <param name="warnings">可选的非致命警告列表</param>
    public static BuildTaskResult Ok(List<string> warnings = null)
    {
        return new BuildTaskResult
        {
            Success = true,
            Warnings = warnings ?? new List<string>()
        };
    }

    /// <summary>构造失败的 Task 结果</summary>
    /// <param name="code">错误码，如 "TASK_EXECUTION_ERROR"</param>
    /// <param name="message">人类可读的错误描述</param>
    /// <param name="fatal">是否中止整个管线，默认 true</param>
    public static BuildTaskResult Fail(string code, string message, bool fatal = true)
    {
        return new BuildTaskResult
        {
            Success = false,
            ErrorCode = code,
            ErrorMessage = message,
            IsFatal = fatal,
            Warnings = new List<string>()
        };
    }
}
