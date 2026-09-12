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
/// 发布目标配置。Path 为整包文件所在的服务目录根；PublicBaseUrl 为服务根公开地址。
/// 持久化在 FYAssetSettings 中，由发布面板使用。
/// </summary>
[Serializable]
public sealed class PushTargetConfig
{
    public string TargetId;
    public string Name;
    public PushTargetType Type;
    public string Path;
    public string PublicBaseUrl;

    /// <summary>在 FYAssetSettings.PushTargets 中按稳定 TargetId 查找目标。</summary>
    public static PushTargetConfig FindById(string targetId)
    {
        List<PushTargetConfig> targets = FYAssetSettings.Instance.PushTargets;
        if (targets == null)
            return null;

        for (int i = 0; i < targets.Count; i++)
        {
            PushTargetConfig config = targets[i];
            if (config != null && string.Equals(config.TargetId, targetId, StringComparison.OrdinalIgnoreCase))
                return config;
        }

        return null;
    }

    /// <summary>在 FYAssetSettings.PushTargets 中按显示名称查找目标。</summary>
    public static PushTargetConfig FindByName(string name)
    {
        List<PushTargetConfig> targets = FYAssetSettings.Instance.PushTargets;
        if (targets == null)
            return null;

        for (int i = 0; i < targets.Count; i++)
        {
            PushTargetConfig config = targets[i];
            if (config != null && string.Equals(config.Name, name, StringComparison.OrdinalIgnoreCase))
                return config;
        }

        return null;
    }

    /// <summary>校验目标集合，并按稳定 ID 返回唯一目标。</summary>
    public static bool TryResolveById(
        IReadOnlyList<PushTargetConfig> targets,
        string targetId,
        out PushTargetConfig target,
        out string error)
    {
        target = null;
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(targetId))
        {
            error = "当前发布目标未选择。";
            return false;
        }
        if (targets == null || targets.Count == 0)
        {
            error = "发布目标列表为空。";
            return false;
        }

        var targetIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < targets.Count; i++)
        {
            PushTargetConfig candidate = targets[i];
            if (candidate == null)
            {
                error = $"发布目标列表包含空项（索引 {i}）。";
                return false;
            }
            if (string.IsNullOrWhiteSpace(candidate.TargetId)
                || !Guid.TryParse(candidate.TargetId, out _)
                || !targetIds.Add(candidate.TargetId))
            {
                error = $"发布目标 TargetId 必须是非重复 GUID：{candidate.TargetId}";
                return false;
            }
            if (string.IsNullOrWhiteSpace(candidate.Name) || !names.Add(candidate.Name))
            {
                error = $"发布目标名称为空或重复：{candidate.Name}";
                return false;
            }
            if (string.Equals(candidate.TargetId, targetId, StringComparison.OrdinalIgnoreCase))
                target = candidate;
        }

        if (target == null)
        {
            error = $"当前发布目标不存在：{targetId}";
            return false;
        }
        if (!target.TryNormalizePublicBaseUrl(out _, out string urlError))
        {
            error = $"当前发布目标 '{target.Name}' 的公开地址无效：{urlError}";
            target = null;
            return false;
        }
        return true;
    }

#if UNITY_EDITOR
    /// <summary>服务目录根：Path 为空时回退到构建输出根，否则解析为绝对路径。</summary>
    public string ResolveServiceRoot()
    {
        return string.IsNullOrWhiteSpace(Path)
            ? BuildPathManager.OutputRoot
            : FYAssetPathUtility.ResolveFilePath(BuildPathManager.ProjectRoot, Path);
    }

    /// <summary>该后端在服务根下的发布目录，即以 AA/AB 命名的子目录。</summary>
    public string ResolveBackendRoot(string backendModeName)
    {
        if (string.IsNullOrWhiteSpace(backendModeName)
            || backendModeName.IndexOfAny(new[] { '/', '\\', ':', '?', '#', '&' }) >= 0)
            throw new ArgumentException($"Invalid backend key: {backendModeName}", nameof(backendModeName));

        return FYAssetPathUtility.JoinFilePath(ResolveServiceRoot(), backendModeName.ToUpperInvariant());
    }
#endif

    /// <summary>严格校验并返回不带末尾斜杠的服务根公开地址。</summary>
    public bool TryNormalizePublicBaseUrl(out string normalized, out string error)
    {
        normalized = string.Empty;
        error = string.Empty;
        if (!Uri.TryCreate(PublicBaseUrl?.Trim(), UriKind.Absolute, out Uri uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || string.IsNullOrEmpty(uri.Host)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment))
        {
            error = "必须是绝对 HTTP/HTTPS URL，且不能包含查询字符串或片段。";
            return false;
        }

        string path = uri.AbsolutePath.Trim('/');
        normalized = uri.GetLeftPart(UriPartial.Authority)
                     + (string.IsNullOrEmpty(path) ? "/" : "/" + path + "/");
        return true;
    }

    /// <summary>返回指定后端的公开热更根地址；无效配置时返回错误。</summary>
    public bool TryGetHotfixUrl(string backendKey, out string url, out string error)
    {
        url = string.Empty;
        if (!TryNormalizePublicBaseUrl(out string normalizedBase, out error))
            return false;

        if (string.IsNullOrWhiteSpace(backendKey)
            || backendKey.IndexOfAny(new[] { '/', '\\', ':', '?', '#', '&' }) >= 0)
        {
            error = $"后端键无效：{backendKey}";
            return false;
        }

        url = FYAssetPathUtility.JoinUrl(normalizedBase, backendKey) + "/";
        return true;
    }

    /// <summary>该后端对应的公开热更 URL；配置无效时抛出，避免发布到错误地址。</summary>
    public string GetHotfixUrl(string backendKey)
    {
        if (!TryGetHotfixUrl(backendKey, out string url, out string error))
            throw new InvalidOperationException(error);
        return url;
    }
}
