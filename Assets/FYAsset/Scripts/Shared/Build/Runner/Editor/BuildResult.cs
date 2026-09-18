#if UNITY_EDITOR
/// <summary>
/// BuildProjectRunner 的最终结果，只承载构建事实和报告定位。
/// </summary>
public sealed class BuildResult
{
    public bool Success { get; }
    public BuildMessage Error { get; }
    public CompleteBuildSummary Summary { get; }
    public string ReportPath { get; }

    private BuildResult(bool success, BuildMessage error, CompleteBuildSummary summary, string reportPath)
    {
        Success = success;
        Error = error;
        Summary = summary;
        ReportPath = reportPath ?? string.Empty;
    }

    public static BuildResult Ok(CompleteBuildSummary summary, string reportPath)
        => new BuildResult(true, null, summary, reportPath);

    public static BuildResult Fail(BuildMessage error, CompleteBuildSummary summary = null, string reportPath = null)
        => new BuildResult(false, error, summary, reportPath);
}
#endif
