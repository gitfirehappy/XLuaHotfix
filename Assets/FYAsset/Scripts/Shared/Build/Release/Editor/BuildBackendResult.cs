#if UNITY_EDITOR
using System.Collections.Generic;

/// <summary>
/// 构建后端的结构化执行结果。Success 为 true 时 Error 为 null。
/// </summary>
/// <remarks>
/// 结果只承载构建事实；发布差异和交付由独立发布模块负责。
/// </remarks>
public class BuildBackendResult
{
    public bool Success { get; }
    public BuildMessage Error { get; }
    public BuildRunResult PipelineResult { get; }
    public BuildPackageRequest Request { get; }
    public string ReportPath { get; }

    /// <summary>
    /// 本次构建的 CompleteBuildSummary；构建结果面板的唯一数据源。
    /// 管线在 Export 阶段之前失败时为 null，此时面板只展示 Task 级执行结果。
    /// </summary>
    public CompleteBuildSummary Summary { get; }

    private BuildBackendResult(
        bool success,
        BuildMessage error,
        BuildRunResult pipelineResult,
        BuildPackageRequest request,
        string reportPath,
        CompleteBuildSummary summary)
    {
        Success = success;
        Error = error;
        PipelineResult = pipelineResult;
        Request = request;
        ReportPath = reportPath ?? string.Empty;
        Summary = summary;
    }

    public static BuildBackendResult Ok()
        => new BuildBackendResult(true, null, null, null, string.Empty, null);

    public static BuildBackendResult Ok(
        BuildRunResult pipelineResult,
        BuildPackageRequest request,
        string reportPath,
        CompleteBuildSummary summary = null)
        => new BuildBackendResult(true, null, pipelineResult, request, reportPath, summary);

    public static BuildBackendResult Fail(BuildMessage error)
        => Fail(error, null, null, string.Empty);

    public static BuildBackendResult Fail(
        BuildMessage error,
        BuildRunResult pipelineResult,
        BuildPackageRequest request,
        string reportPath)
        => new BuildBackendResult(false, error, pipelineResult, request, reportPath, null);
}
#endif
