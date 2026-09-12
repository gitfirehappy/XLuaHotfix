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
/// 发布目标配置。Path 为整包文件所在的服务目录根；PublicBaseUrl 为与其对应的可访问 URL。
/// 持久化在 FYAssetSettings 中，由发布面板使用。
/// </summary>
/// <remarks>
/// 该类型被 FYAssetSettings（运行时 ScriptableObject）序列化，因此数据字段必须运行时可见；
/// 只有依赖构建路径管理器的解析方法属于编辑器行为。
/// </remarks>
[Serializable]
public sealed class PushTargetConfig
{
    public string Id;
    public PushTargetType Type;
    public string Path;
    public string PublicBaseUrl;

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

#if UNITY_EDITOR
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
#endif

    /// <summary>
    /// 该后端对应的公开热更 URL；PublicBaseUrl 无效时抛出，避免发布到错误地址。
    /// </summary>
    public string GetHotfixUrl(string backendKey)
    {
        if (!FYAssetPathUtility.IsHttpUrl(PublicBaseUrl))
            throw new InvalidOperationException($"Push target public base URL is invalid: {PublicBaseUrl}");

        return FYAssetPathUtility.JoinUrl(PublicBaseUrl, backendKey) + "/";
    }
}
