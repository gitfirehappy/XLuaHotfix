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
/// 发布目标。Path 为整包文件所在的服务目录根；PublicBaseUrl 为与其对应的可访问 URL。
/// 持久化在 FYAssetSettings 中，由发布面板或 CLI 使用。
/// </summary>
[Serializable]
public sealed class PushTargetConfig
{
    public string Id;
    public PushTargetType Type;
    public string Path;
    public string PublicBaseUrl;

#if UNITY_EDITOR
    /// <summary>
    /// 在 FYAssetSettings.PushTargets 中按 Id 查找目标；未匹配返回 null。
    /// </summary>
    public static PushTargetConfig FindById(string targetId)
    {
        List<PushTargetConfig> targets = FYAssetSettings.Instance.PushTargets;
        if (targets == null)
            return null;

        for (int i = 0; i < targets.Count; i++)
        {
            PushTargetConfig config = targets[i];
            if (config != null && string.Equals(config.Id, targetId, StringComparison.OrdinalIgnoreCase))
                return config;
        }

        return null;
    }

    /// <summary>
    /// 服务目录根：Path 为空时回退到构建输出根，否则解析为绝对路径。
    /// </summary>
    public string ResolveServiceRoot()
    {
        return string.IsNullOrWhiteSpace(Path)
            ? BuildPathManager.OutputRoot
            : FYAssetPathUtility.ResolveFilePath(BuildPathManager.ProjectRoot, Path);
    }

    /// <summary>
    /// 该后端在服务根下的发布目录，即以 AA/AB 命名的子目录。
    /// </summary>
    public string ResolveBackendRoot(string backendModeName)
    {
        if (string.IsNullOrWhiteSpace(backendModeName)
            || backendModeName.IndexOfAny(new[] { '/', '\\', ':', '?', '#', '&' }) >= 0)
            throw new ArgumentException($"Invalid backend key: {backendModeName}", nameof(backendModeName));

        return FYAssetPathUtility.JoinFilePath(ResolveServiceRoot(), backendModeName.ToUpperInvariant());
    }

    /// <summary>
    /// 该后端对应的公开热更 URL；PublicBaseUrl 无效时抛出，避免发布到错误地址。
    /// </summary>
    public string GetHotfixUrl(string backendKey)
    {
        if (!FYAssetPathUtility.IsHttpUrl(PublicBaseUrl))
            throw new InvalidOperationException($"Push target public base URL is invalid: {PublicBaseUrl}");

        return FYAssetPathUtility.JoinUrl(PublicBaseUrl, backendKey) + "/";
    }
#endif
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