using System;
using UnityEngine;

/// <summary>
/// 发布目标配置。Path 为整包文件所在的服务目录根；PublicBaseUrl 为可选的服务根公开地址。
/// 持久化在 FYAssetSettings 中，发布器直接消费该配置。
/// </summary>
[Serializable]
#if UNITY_EDITOR
public sealed class PublishTargetConfig : IPublishTarget
#else
public sealed class PublishTargetConfig
#endif
{
    public string TargetId;
    public string Name;
    public string Path;
    public string PublicBaseUrl;

#if UNITY_EDITOR
    public string Id => TargetId;

    /// <summary>服务目录根：Path 为空时回退到构建输出根，否则解析为绝对路径。</summary>
    public string ResolveServiceRoot()
    {
        return string.IsNullOrWhiteSpace(Path)
            ? BuildPathManager.OutputRoot
            : FYAssetPathUtility.ResolveFilePath(BuildPathManager.ProjectRoot, Path);
    }

    /// <summary>该后端在服务根下的发布目录，即以 AA/AB 命名的子目录。</summary>
    public string ResolveBackendRoot(string backendKey)
    {
        if (string.IsNullOrWhiteSpace(backendKey)
            || backendKey.IndexOfAny(new[] { '/', '\\', ':', '?', '#', '&' }) >= 0)
            throw new ArgumentException($"Invalid backend key: {backendKey}", nameof(backendKey));

        return FYAssetPathUtility.JoinFilePath(ResolveServiceRoot(), backendKey.ToUpperInvariant());
    }
#endif

    /// <summary>严格校验并返回不带末尾斜杠的服务根公开地址。</summary>
    public bool TryNormalizePublicBaseUrl(out string normalized, out string error)
    {
        normalized = string.Empty;
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(PublicBaseUrl))
            return true;

        if (!Uri.TryCreate(PublicBaseUrl.Trim(), UriKind.Absolute, out Uri uri)
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

    /// <summary>返回指定后端的公开热更根地址；未配置公开地址时返回空地址。</summary>
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

        if (string.IsNullOrEmpty(normalizedBase))
            return true;

        url = FYAssetPathUtility.JoinUrl(normalizedBase, backendKey) + "/";
        return true;
    }

    /// <summary>该后端对应的公开热更 URL；无效配置时抛出，未配置公开地址时返回空串。</summary>
    public string GetHotfixUrl(string backendKey)
    {
        if (!TryGetHotfixUrl(backendKey, out string url, out string error))
            throw new InvalidOperationException(error);
        return url;
    }
}
