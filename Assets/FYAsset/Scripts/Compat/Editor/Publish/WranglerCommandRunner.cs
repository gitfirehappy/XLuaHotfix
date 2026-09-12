#if UNITY_EDITOR
using System;
using System.Diagnostics;
using System.IO;
using System.Text;

/// <summary>
/// 一次 Wrangler 调用的结果。
/// </summary>
public readonly struct WranglerCommandResult
{
    /// <summary>进程是否成功退出</summary>
    public bool Success { get; }

    /// <summary>输出摘要（成功）或失败原因</summary>
    public string Message { get; }

    public WranglerCommandResult(bool success, string message)
    {
        Success = success;
        Message = message ?? string.Empty;
    }

    public static WranglerCommandResult Ok(string message) => new WranglerCommandResult(true, message);

    public static WranglerCommandResult Fail(string message) => new WranglerCommandResult(false, message);
}

/// <summary>
/// Wrangler CLI 调用接缝：把可执行文件定位与进程调用抽出，使发布门禁能注入部署失败场景。
/// </summary>
/// <remarks>
/// Cloudflare 失败时需要恢复旧 PackageIndex 和旧服务根镜像；进程边界抽象为本接口，便于纯逻辑测试注入替身。
/// 生产实现见 <see cref="ProcessWranglerCommandRunner"/>。
/// </remarks>
public interface IWranglerCommandRunner
{
    /// <summary>定位 wrangler 可执行文件；返回空串表示未找到。</summary>
    string ResolveExecutable();

    /// <summary>运行一次 wrangler 命令；超时与启动失败都表达为失败结果，不抛异常。</summary>
    WranglerCommandResult Run(string wranglerPath, string arguments, int timeoutMilliseconds);
}

/// <summary>
/// 生产实现：在 PATH 上查找 wrangler 并启动真实进程，工作目录固定为项目根。
/// </summary>
public sealed class ProcessWranglerCommandRunner : IWranglerCommandRunner
{
    /// <summary>无状态实现，可直接共享。</summary>
    public static readonly ProcessWranglerCommandRunner Instance = new ProcessWranglerCommandRunner();

    public string ResolveExecutable() => FindExecutableOnPath("wrangler");

    public WranglerCommandResult Run(string wranglerPath, string arguments, int timeoutMilliseconds)
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
                return WranglerCommandResult.Fail("Timed out.");
            }

            process.WaitForExit();
            string message = output.ToString().Trim();
            return process.ExitCode == 0
                ? WranglerCommandResult.Ok(string.IsNullOrEmpty(message) ? "Wrangler completed." : message)
                : WranglerCommandResult.Fail(string.IsNullOrEmpty(message) ? $"Exit code {process.ExitCode}." : message);
        }
        catch (Exception ex)
        {
            return WranglerCommandResult.Fail(ex.Message);
        }
    }

    /// <summary>
    /// 在 PATH 上查找可执行文件；找不到返回空串，由调用方决定报错。
    /// </summary>
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
}
#endif
