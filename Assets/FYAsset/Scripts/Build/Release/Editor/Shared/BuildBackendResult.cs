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

    private BuildBackendResult(bool success, BuildMessage error, List<ArtifactDigest> artifacts)
    {
        Success = success;
        Error = error;
        Artifacts = artifacts ?? new List<ArtifactDigest>();
    }

    public static BuildBackendResult Ok()
        => new BuildBackendResult(true, null, new List<ArtifactDigest>());

    public static BuildBackendResult Ok(IReadOnlyList<ArtifactDigest> artifacts)
        => new BuildBackendResult(true, null, artifacts != null ? new List<ArtifactDigest>(artifacts) : new List<ArtifactDigest>());

    public static BuildBackendResult Fail(BuildMessage error)
        => new BuildBackendResult(false, error, new List<ArtifactDigest>());
}
#endif
