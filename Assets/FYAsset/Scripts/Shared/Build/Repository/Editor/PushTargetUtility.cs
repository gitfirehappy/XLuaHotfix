#if UNITY_EDITOR
using System;
using System.IO;

/// <summary>
/// 负责创建 Push Target、解析后端根目录并推导公开 URL。
/// </summary>
public static class PushTargetUtility
{
    public static IPushTarget Create(PushTargetConfig config)
    {
        if (config == null)
            throw new ArgumentNullException(nameof(config));

        return config.Type switch
        {
            PushTargetType.LocalDirectory => new LocalDirectoryPushTarget(config),
            PushTargetType.CloudflarePages => new CloudflarePagesPushTarget(config),
            _ => throw new ArgumentOutOfRangeException(nameof(config.Type), config.Type, "Unsupported push target type.")
        };
    }

    public static string ResolveServiceRoot(PushTargetConfig config)
    {
        if (config == null)
            throw new ArgumentNullException(nameof(config));

        return string.IsNullOrWhiteSpace(config.Path)
            ? BuildPathManager.OutputRoot
            : FYAssetPathUtility.ResolveFilePath(BuildPathManager.ProjectRoot, config.Path);
    }

    public static string ResolveBackendRoot(PushTargetConfig config, string backendModeName)
    {
        if (!BackendModeNames.IsValid(backendModeName))
            throw new ArgumentException($"Invalid backend mode: {backendModeName}", nameof(backendModeName));

        return FYAssetPathUtility.JoinFilePath(ResolveServiceRoot(config), backendModeName.ToUpperInvariant());
    }

    public static string GetBackendHotfixUrl(PushTargetConfig config, BackendMode backendMode)
    {
        if (config == null)
            throw new ArgumentNullException(nameof(config));
        if (!FYAssetPathUtility.IsHttpUrl(config.PublicBaseUrl))
            throw new InvalidOperationException($"Push target public base URL is invalid: {config.PublicBaseUrl}");

        return FYAssetPathUtility.JoinUrl(config.PublicBaseUrl, BackendModeNames.FromBackendMode(backendMode)) + "/";
    }

    public static PushTargetConfig FindConfig(string targetId)
    {
        FYAssetSettings settings = FYAssetSettings.Instance;
        for (int i = 0; settings.PushTargets != null && i < settings.PushTargets.Count; i++)
        {
            PushTargetConfig config = settings.PushTargets[i];
            if (config != null && string.Equals(config.Id, targetId, StringComparison.OrdinalIgnoreCase))
                return config;
        }

        return null;
    }

    public static string FindExecutableOnPath(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
            return string.Empty;

        if (Path.IsPathRooted(command) && File.Exists(command))
            return FYAssetPathUtility.NormalizePath(command);

        string pathValue = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        string[] pathEntries = pathValue.Split(Path.PathSeparator);
        string[] extensions = Path.DirectorySeparatorChar == '\\'
            ? (Environment.GetEnvironmentVariable("PATHEXT") ?? ".COM;.EXE;.BAT;.CMD").Split(';')
            : new[] { string.Empty };

        for (int i = 0; i < pathEntries.Length; i++)
        {
            string directory = pathEntries[i].Trim().Trim('"');
            if (string.IsNullOrEmpty(directory))
                continue;

            if (Path.HasExtension(command))
            {
                string exactPath = Path.Combine(directory, command);
                if (File.Exists(exactPath))
                    return FYAssetPathUtility.NormalizePath(exactPath);
                continue;
            }

            for (int j = 0; j < extensions.Length; j++)
            {
                string candidate = Path.Combine(directory, command + extensions[j].ToLowerInvariant());
                if (File.Exists(candidate))
                    return FYAssetPathUtility.NormalizePath(candidate);
            }
        }

        return string.Empty;
    }

    public static string BuildWranglerDeployArguments(string serviceRoot, string projectName)
    {
        if (string.IsNullOrWhiteSpace(serviceRoot))
            throw new ArgumentException("Cloudflare service root is empty.", nameof(serviceRoot));
        if (string.IsNullOrWhiteSpace(projectName))
            throw new ArgumentException("FYAssetSettings.ProjectName is empty.", nameof(projectName));

        return $"pages deploy {QuoteArgument(serviceRoot)} --project-name {QuoteArgument(projectName)} --branch main";
    }

    public static string QuoteArgument(string value)
    {
        return "\"" + (value ?? string.Empty).Replace("\"", "\\\"") + "\"";
    }
}
#endif
