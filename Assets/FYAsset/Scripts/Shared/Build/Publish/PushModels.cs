using System;
using System.Collections.Generic;

/// <summary>
/// Push 目标类型。
/// </summary>
public enum PushTargetType
{
    LocalDirectory = 0,
    CloudflarePages = 1
}

/// <summary>
/// Push 目标配置。
/// 持久化在 FYAssetSettings 中，并由 Repository 面板编辑。
/// </summary>
[Serializable]
public sealed class PushTargetConfig
{
    public string Id;
    public PushTargetType Type;
    public string Path;
    public string PublicBaseUrl;
}

#if UNITY_EDITOR
/// <summary>
/// Push 操作负载。
/// 由发布器从 baseline 组装；PushTarget 只负责把已构建完成的包体目录发布到远端镜像。
/// </summary>
[Serializable]
public sealed class PushPayload
{
    public BuildBaseline Release;
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
/// </summary>
public interface IPushTarget
{
    string Id { get; }
    PushReceipt Push(PushPayload payload);
}
#endif
