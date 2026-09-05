#if UNITY_EDITOR
using System.Collections.Generic;

/// <summary>
/// 构建后端的结构化执行结果 —— 替代裸 bool + BuildSummary。
/// Success 为 true 时 Error 为 null。
/// </summary>
public class BuildBackendResult
{
    public bool Success { get; }
    public BuildMessage Error { get; }
    public List<ArtifactDigest> Artifacts { get; }
    public BuildResult PipelineResult { get; }
    public BuildPackageRequest Request { get; }
    public string ReportPath { get; }
    public ArtifactDelta Delta { get; }

    private BuildBackendResult(
        bool success,
        BuildMessage error,
        List<ArtifactDigest> artifacts,
        BuildResult pipelineResult,
        BuildPackageRequest request,
        string reportPath,
        ArtifactDelta delta)
    {
        Success = success;
        Error = error;
        Artifacts = artifacts ?? new List<ArtifactDigest>();
        PipelineResult = pipelineResult;
        Request = request;
        ReportPath = reportPath ?? string.Empty;
        Delta = delta;
    }

    public static BuildBackendResult Ok()
        => new BuildBackendResult(true, null, new List<ArtifactDigest>(), null, null, string.Empty, null);

    public static BuildBackendResult Ok(IReadOnlyList<ArtifactDigest> artifacts)
        => Ok(artifacts, null, null, string.Empty);

    public static BuildBackendResult Ok(
        IReadOnlyList<ArtifactDigest> artifacts,
        BuildResult pipelineResult,
        BuildPackageRequest request,
        string reportPath,
        ArtifactDelta delta = null)
        => new BuildBackendResult(
            true,
            null,
            artifacts != null ? new List<ArtifactDigest>(artifacts) : new List<ArtifactDigest>(),
            pipelineResult,
            request,
            reportPath,
            delta);

    public static BuildBackendResult Fail(BuildMessage error)
        => Fail(error, null, null, string.Empty);

    public static BuildBackendResult Fail(
        BuildMessage error,
        BuildResult pipelineResult,
        BuildPackageRequest request,
        string reportPath)
        => new BuildBackendResult(false, error, new List<ArtifactDigest>(), pipelineResult, request, reportPath, null);
}
#endif
