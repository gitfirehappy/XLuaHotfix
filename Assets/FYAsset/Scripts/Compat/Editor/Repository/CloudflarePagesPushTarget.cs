#if UNITY_EDITOR
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>
/// 先在本地发布后端镜像，再通过 Wrangler 部署完整服务根目录。
/// </summary>
public sealed class CloudflarePagesPushTarget : IPushTarget
{
    private const int WranglerTimeoutMilliseconds = 10 * 60 * 1000;
    private const string HeadersFileName = "_headers";
    private readonly PushTargetConfig _config;

    public string Id => string.IsNullOrEmpty(_config.Id) ? "cloudflare" : _config.Id;

    public CloudflarePagesPushTarget(PushTargetConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
    }

    public PushReceipt Push(PushPayload payload)
    {
        if (payload == null)
            throw new ArgumentNullException(nameof(payload));
        if (payload.Release == null)
            throw new ArgumentException("Release 不能为空。", nameof(payload));
        if (string.IsNullOrEmpty(payload.Release.PackageRootDir))
            return Fail("PackageRootDir is empty.", string.Empty);

        string projectName = FYAssetSettings.Instance.ProjectName;
        if (string.IsNullOrWhiteSpace(projectName))
            return Fail("FYAssetSettings.ProjectName is required for Cloudflare Pages.", string.Empty);

        string wranglerPath = FindExecutableOnPath("wrangler");
        if (string.IsNullOrEmpty(wranglerPath))
            return Fail("Wrangler was not found on PATH. Install and authenticate it before Cloudflare Push.", string.Empty);

        ProcessResult versionResult = RunWrangler(wranglerPath, "--version", 30_000);
        if (!versionResult.Success)
            return Fail($"Wrangler preflight failed: {versionResult.Message}", string.Empty);

        string serviceRoot = _config.ResolveServiceRoot();
        string backendRoot = _config.ResolveBackendRoot(payload.Release.BackendMode);
        string headersPath = FYAssetPathUtility.JoinFilePath(serviceRoot, HeadersFileName);
        bool headersExisted = FileHelper.Exists(headersPath);
        string oldHeaders = headersExisted ? FileHelper.ReadAllText(headersPath) : string.Empty;

        try
        {
            using var transaction = new PackagePublishTransaction(
                payload.Release,
                payload.Release.PackageRootDir,
                backendRoot);
            transaction.Apply();
            WriteHeaders(headersPath);

            string arguments = BuildDeployArguments(serviceRoot, projectName);
            ProcessResult deployResult = RunWrangler(wranglerPath, arguments, WranglerTimeoutMilliseconds);
            if (!deployResult.Success)
            {
                RestoreHeaders(headersPath, headersExisted, oldHeaders);
                transaction.Rollback();
                return Fail($"Wrangler deploy failed: {deployResult.Message}", backendRoot);
            }

            transaction.Commit();
            Debug.Log($"[CloudflarePagesPushTarget] {deployResult.Message}");
            return new PushReceipt
            {
                Success = true,
                TargetId = Id,
                TargetLocation = _config.GetHotfixUrl(payload.Release.BackendMode),
                PushedAtUtc = DateTime.UtcNow.ToString("o")
            };
        }
        catch (Exception ex)
        {
            RestoreHeaders(headersPath, headersExisted, oldHeaders);
            Debug.LogError($"[CloudflarePagesPushTarget] Push 失败：{ex}");
            return Fail(ex.Message, backendRoot);
        }
    }

    private static void WriteHeaders(string path)
    {
        const string content =
            "/AA/PackageIndex.json\n" +
            "  Cache-Control: no-store\n\n" +
            "/AB/PackageIndex.json\n" +
            "  Cache-Control: no-store\n\n" +
            "/AA/Packages/*\n" +
            "  Cache-Control: public, max-age=31536000, immutable\n\n" +
            "/AB/Packages/*\n" +
            "  Cache-Control: public, max-age=31536000, immutable\n";
        FileHelper.WriteAllTextAtomic(path, content);
    }

    private static void RestoreHeaders(string path, bool existed, string content)
    {
        if (existed)
            FileHelper.WriteAllTextAtomic(path, content);
        else
            FileHelper.TryDelete(path);
    }

    private static ProcessResult RunWrangler(string wranglerPath, string arguments, int timeoutMilliseconds)
    {
        bool isCommandScript = string.Equals(Path.GetExtension(wranglerPath), ".cmd", StringComparison.OrdinalIgnoreCase)
                               || string.Equals(Path.GetExtension(wranglerPath), ".bat", StringComparison.OrdinalIgnoreCase);
        string executable = isCommandScript
            ? (Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe")
            : wranglerPath;
        string processArguments = isCommandScript
            ? $"/d /s /c \"\"{wranglerPath}\" {arguments}\""
            : arguments;

        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            Arguments = processArguments,
            WorkingDirectory = BuildPathManager.ProjectRoot,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        using var process = new Process { StartInfo = startInfo };
        var output = new StringBuilder();
        process.OutputDataReceived += (_, args) =>
        {
            if (!string.IsNullOrEmpty(args.Data)) output.AppendLine(args.Data);
        };
        process.ErrorDataReceived += (_, args) =>
        {
            if (!string.IsNullOrEmpty(args.Data)) output.AppendLine(args.Data);
        };

        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            if (!process.WaitForExit(timeoutMilliseconds))
            {
                process.Kill();
                return ProcessResult.Fail("Timed out.");
            }
            process.WaitForExit();
            string message = output.ToString().Trim();
            return process.ExitCode == 0
                ? ProcessResult.Ok(string.IsNullOrEmpty(message) ? "Wrangler completed." : message)
                : ProcessResult.Fail(string.IsNullOrEmpty(message) ? $"Exit code {process.ExitCode}." : message);
        }
        catch (Exception ex)
        {
            return ProcessResult.Fail(ex.Message);
        }
    }

    private PushReceipt Fail(string reason, string location)
    {
        return new PushReceipt
        {
            Success = false,
            TargetId = Id,
            TargetLocation = location,
            PushedAtUtc = DateTime.UtcNow.ToString("o"),
            FailureReason = reason
        };
    }

    /// <summary>
    /// Wrangler 部署命令参数。供 HotfixPublishSelfCheck / BuildTestState 验证或复用。
    /// </summary>
    internal static string BuildDeployArguments(string serviceRoot, string projectName)
    {
        if (string.IsNullOrWhiteSpace(serviceRoot))
            throw new ArgumentException("Cloudflare service root is empty.", nameof(serviceRoot));
        if (string.IsNullOrWhiteSpace(projectName))
            throw new ArgumentException("FYAssetSettings.ProjectName is empty.", nameof(projectName));

        return $"pages deploy {QuoteArgument(serviceRoot)} --project-name {QuoteArgument(projectName)} --branch main";
    }

    /// <summary>
    /// 在 PATH 上查找可执行文件；找不到返回空串，由调用方决定报错。
    /// </summary>
    internal static string FindExecutableOnPath(string command)
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

    private static string QuoteArgument(string value)
    {
        return "\"" + (value ?? string.Empty).Replace("\"", "\\\"") + "\"";
    }

    private readonly struct ProcessResult
    {
        public bool Success { get; }
        public string Message { get; }

        private ProcessResult(bool success, string message)
        {
            Success = success;
            Message = message;
        }

        public static ProcessResult Ok(string message) => new(true, message);
        public static ProcessResult Fail(string message) => new(false, message);
    }
}

/// <summary>
/// 为 CLI 与测试创建发布目标：包含 LocalDirectory 与 Compat 侧 Cloudflare Pages 部署胶水。
/// 构建窗口的发布面板仅支持 LocalDirectory，与该入口保持分离。
/// </summary>
public static class CompatPushTargetFactory
{
    public static IPushTarget CreateFull(PushTargetConfig config)
    {
        if (config == null)
            throw new ArgumentNullException(nameof(config));

        return config.Type switch
        {
            PushTargetType.LocalDirectory => new LocalDirectoryPushTarget(config),
            PushTargetType.CloudflarePages => new CloudflarePagesPushTarget(config),
            _ => throw new ArgumentOutOfRangeException(nameof(config.Type), config.Type, "Unsupported push target type."),
        };
    }
}
#endif
