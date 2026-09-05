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

        string wranglerPath = PushTargetUtility.FindExecutableOnPath("wrangler");
        if (string.IsNullOrEmpty(wranglerPath))
            return Fail("Wrangler was not found on PATH. Install and authenticate it before Cloudflare Push.", string.Empty);

        ProcessResult versionResult = RunWrangler(wranglerPath, "--version", 30_000);
        if (!versionResult.Success)
            return Fail($"Wrangler preflight failed: {versionResult.Message}", string.Empty);

        string serviceRoot = PushTargetUtility.ResolveServiceRoot(_config);
        string backendRoot = PushTargetUtility.ResolveBackendRoot(_config, payload.Release.BackendMode);
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

            string arguments = PushTargetUtility.BuildWranglerDeployArguments(serviceRoot, projectName);
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
                TargetLocation = PushTargetUtility.GetBackendHotfixUrl(
                    _config,
                    string.Equals(payload.Release.BackendMode, BackendModeNames.AB, StringComparison.OrdinalIgnoreCase)
                        ? BackendMode.ABManifest
                        : BackendMode.AA),
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
/// Compat 侧的 target 创建器：在原厂 LocalDirectory 之外补充 CloudflarePages 等部署胶水 target。
/// </summary>
public static class CompatPushTargetFactory
{
    public static IPushTarget CreateFull(PushTargetConfig config)
    {
        return PushTargetUtility.Create(config, cfg =>
            cfg.Type == PushTargetType.CloudflarePages ? new CloudflarePagesPushTarget(cfg) : null);
    }
}
#endif
