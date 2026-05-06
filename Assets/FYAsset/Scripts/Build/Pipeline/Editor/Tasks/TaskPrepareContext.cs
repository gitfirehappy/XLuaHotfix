using UnityEditor;
using UnityEngine;
using System;
using System.IO;

/// <summary>
/// 管线起点 Task —— 初始化构建环境：后端模式、版本号、输出目录、目标平台。
/// 优先级：命令行参数 > BuildPipelineConfig SO > 默认值。
/// </summary>
public class TaskPrepareContext : IBuildTask
{
    public string TaskName => "TaskPrepareContext";
    public string[] DependsOn => new string[0];
    public string[] ReadKeys => new string[0];
    public string[] WriteKeys => new[]
    {
        BuildContextKeys.BackendMode,
        BuildContextKeys.BuildVersion,
        BuildContextKeys.OutputRoot,
        BuildContextKeys.TargetPlatform
    };

    public BuildTaskResult Execute(BuildContext ctx)
    {
        // 读取 SO 配置
        var config = AssetDatabase.LoadAssetAtPath<BuildPipelineConfig>(
            "Assets/Build/BuildPipelineConfig.asset");
        BackendMode mode = config != null ? config.DefaultBackendMode : BackendMode.ABManifest;

        // CLI 覆盖: --backend LegacyAddressable | ABManifest
        string cliBackend = GetCommandLineArg("--backend");
        if (!string.IsNullOrEmpty(cliBackend))
        {
            if (!Enum.TryParse(cliBackend, true, out mode))
                return BuildTaskResult.Fail("INVALID_BACKEND",
                    $"Unknown backend '{cliBackend}'. Valid: LegacyAddressable, ABManifest.", true);
        }

        // BuildVersion: CLI --version > 时间戳
        string version = GetCommandLineArg("--version")
            ?? DateTime.Now.ToString("yyyyMMdd-HHmmss");

        // TargetPlatform: CLI --platform > Editor 当前设置
        BuildTarget platform;
        string platformStr = GetCommandLineArg("--platform");
        if (!string.IsNullOrEmpty(platformStr))
        {
            if (!Enum.TryParse(platformStr, true, out platform))
                return BuildTaskResult.Fail("INVALID_PLATFORM",
                    $"Unknown platform '{platformStr}'.", true);
        }
        else
        {
            platform = EditorUserBuildSettings.activeBuildTarget;
        }

        // OutputRoot: CLI --output > 默认路径
        string outputRoot = GetCommandLineArg("--output")
            ?? Path.Combine(Application.dataPath, "..", "Build", platform.ToString());

        ctx.Set(BuildContextKeys.BackendMode, mode);
        ctx.Set(BuildContextKeys.BuildVersion, version);
        ctx.Set(BuildContextKeys.OutputRoot, outputRoot);
        ctx.Set(BuildContextKeys.TargetPlatform, platform);

        return BuildTaskResult.Ok();
    }

    private static string GetCommandLineArg(string name)
    {
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        }
        return null;
    }
}
