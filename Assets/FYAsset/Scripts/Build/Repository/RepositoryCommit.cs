#if UNITY_EDITOR
using System;
using System.Collections.Generic;

/// <summary>
/// Build Repository 的持久化 commit 对象。
/// 只记录 HEAD/objects 所需的最小信息，不保存构建策略状态。
/// </summary>
[Serializable]
public sealed class RepositoryCommit
{
    public VersionNumber Version;
    public string ChannelKey;
    public string BackendMode;
    public string BuildType;
    public string BuildTarget;
    public string PackageName;
    public string CreatedAtUtc;
    public string GitCommitHash;
    public bool IsDirty;
    public string PackageRootDir;
    public string ParentVersion;
    public ArtifactDelta CommitDelta;
    public List<ArtifactDigest> Artifacts = new();
}

/// <summary>
/// Repository HEAD 指针状态。
/// 只保存 HeadVersion，object path 始终由目录布局推导，避免双重事实源。
/// </summary>
[Serializable]
public sealed class RepositoryHeadState
{
    public string HeadVersion;
}

/// <summary>
/// Repository 状态摘要。
/// 用于 Editor UI 的 status 展示。
/// </summary>
[Serializable]
public sealed class RepositoryStatus
{
    public string ChannelKey;
    public bool HasHead;
    public bool HasHeadError;
    public string HeadVersion;
    public string PackageName;
    public int ArtifactCount;
    public string LastPushTargetId;
    public string LastPushAtUtc;
    public string HeadErrorReason;
}

/// <summary>
/// Repository HEAD 读取异常。
/// 缺失 HEAD 由调用方按无 HEAD 处理；损坏 HEAD 必须显式报错。
/// </summary>
public sealed class RepositoryHeadException : Exception
{
    public RepositoryHeadException(string message) : base(message)
    {
    }
}
#endif
