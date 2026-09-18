#if UNITY_EDITOR
/// <summary>
/// Backend 完成 Task 管线后的中间结果。交付与最终结果由 BuildProjectRunner 负责。
/// </summary>
public sealed class BuildBackendResult
{
    public bool Success { get; }
    public BuildMessage Error { get; }
    public CompleteBuildSummary Summary { get; }
    public string ReportPath { get; }
    public BuildPipelineResult PipelineResult { get; }

    private BuildBackendResult(
        bool success,
        BuildMessage error,
        CompleteBuildSummary summary,
        string reportPath,
        BuildPipelineResult pipelineResult)
    {
        Success = success;
        Error = error;
        Summary = summary;
        ReportPath = reportPath ?? string.Empty;
        PipelineResult = pipelineResult;
    }

    public static BuildBackendResult Ok(
        BuildPipelineResult pipelineResult,
        CompleteBuildSummary summary,
        string reportPath)
        => new BuildBackendResult(true, null, summary, reportPath, pipelineResult);

    public static BuildBackendResult Fail(
        BuildMessage error,
        BuildPipelineResult pipelineResult = null,
        CompleteBuildSummary summary = null,
        string reportPath = null)
        => new BuildBackendResult(false, error, summary, reportPath, pipelineResult);
}
#endif
