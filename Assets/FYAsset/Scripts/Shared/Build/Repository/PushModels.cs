using System;
using System.Collections.Generic;

/// <summary>
/// Push 目标类型。
/// 当前只允许 LocalDirectory；CDN 目标留给后续计划。
/// </summary>
public enum PushTargetType
{
    LocalDirectory = 0
}

/// <summary>
/// Push 目标配置。
/// Persisted in FYAssetSettings and edited by the repository panel.
/// </summary>
[Serializable]
public sealed class PushTargetConfig
{
    public string Id;
    public PushTargetType Type;
    public string Path;
}

#if UNITY_EDITOR
/// <summary>
/// Push 操作负载。
/// 由 repository 组装，PushTarget 只发布已经构建完成的包体目录。
/// </summary>
[Serializable]
public sealed class PushPayload
{
    public RepositoryCommit FromCommit;
    public RepositoryCommit ToCommit;
    public int ChangedArtifactCount;
}

/// <summary>
/// Push 执行结果。
/// </summary>
[Serializable]
public sealed class PushReceipt
{
    public bool Success;
    public string TargetId;
    public string TargetLocation;
    public string PushedAtUtc;
    public string FailureReason;
}

/// <summary>
/// Push 目标抽象。
/// 当前版本只提供本地目录实现。
/// </summary>
public interface IPushTarget
{
    string Id { get; }
    PushReceipt Push(PushPayload payload);
}
#endif
