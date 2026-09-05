#if UNITY_EDITOR
using System;
using System.Collections.Generic;

/// <summary>
/// 已交付构建的 baseline 快照：hotfix diff 的历史基准。
/// 由 BuildBaselineStore 持久化为 VCS 跟踪的滚动文件；历史与审计由 git 承担。
/// </summary>
[Serializable]
public sealed class BuildBaseline
{
    public VersionNumber Version;
    public string BuildType;
    public string PackageName;
    public string BackendMode;
    public string PackageRootDir;
    public string ParentVersion;
    public ArtifactDelta CommitDelta;
    /// <summary>
    /// 后端 manifest 文件名清单（由交付时的 IBaselinePackageHandler.RequiredManifestFileNames 落进基线，随基线走）。
    /// 发布事务据此校验包完整性，不需要理解后端类型。
    /// </summary>
    public List<string> ManifestFileNames;
    public string CreatedAtUtc;
    public List<ArtifactDigest> Artifacts = new();
}

/// <summary>
/// 双槽 baseline 状态：Latest=最近一次成功交付，LatestFull=最近一次成功 Full（cumulative hotfix 基准）。
/// </summary>
[Serializable]
public sealed class BuildBaselineState
{
    public BuildBaseline Latest;
    public BuildBaseline LatestFull;
}

/// <summary>
/// baseline 文件损坏读取异常。文件缺失按无历史处理；损坏必须显式失败。
/// </summary>
public sealed class BuildBaselineException : Exception
{
    public BuildBaselineException(string message) : base(message) { }
}
#endif
