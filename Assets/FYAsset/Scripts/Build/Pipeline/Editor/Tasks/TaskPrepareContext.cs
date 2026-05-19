using UnityEditor;
using UnityEngine;
using System;
using System.IO;

/// <summary>
/// 管线起点 Task —— 初始化构建环境：后端模式、版本号、输出目录、目标平台。
/// 优先级：命令行参数 > BuildPipelineConfig SO > 默认值。
/// CLI 参数先写入 SO 再读取，保持 SO 唯一来源原则。
/// </summary>
public class TaskPrepareContext : IBuildTask
{
    public string TaskName => "TaskPrepareContext";
    public string[] DependsOn => new string[0];
    public string[] ReadKeys => new string[0];
    public string[] WriteKeys => new[] { BuildContextKeys.BuildConfig };

    public BuildTaskResult Execute(BuildContext ctx)
    {
        // 读取 SO 配置
        var config = AssetDatabase.LoadAssetAtPath<BuildPipelineConfig>(
            FYAssetSettings.Instance.PipelineConfigPath);
        BackendMode mode = FYAssetSettings.Instance.UseABBackend ? BackendMode.ABManifest : BackendMode.AAAddressable;

        // CLI 覆盖: --backend AAAddressable | ABManifest
        string cliBackend = GetCommandLineArg("--backend");
        if (!string.IsNullOrEmpty(cliBackend))
        {
            if (!Enum.TryParse(cliBackend, true, out mode))
                return BuildTaskResult.Fail(BuildErrorCodes.InvalidBackend,
                $"未知 Backend '{cliBackend}'。有效值: AAAddressable, ABManifest。", true);
        }

        // BuildVersionString: CLI --version > 时间戳（用于目录命名）
        string buildVersionString = GetCommandLineArg("--version")
            ?? DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");

        // CLI --version 若为有效 SemVer → 写 SO 再读回（保持 SO 唯一来源）
        var versionData = AssetDatabase.LoadAssetAtPath<VersionDataBase>(
            FYAssetSettings.Instance.VersionDataBasePath);
        string cliVersion = GetCommandLineArg("--version");
        if (!string.IsNullOrEmpty(cliVersion) && VersionNumber.TryParse(cliVersion, out var cliVer))
        {
            if (versionData != null)
            {
                versionData.CurrentVersion = cliVer;
                EditorUtility.SetDirty(versionData);
                AssetDatabase.SaveAssets();
            }
        }

        // TargetPlatform: CLI --platform > Editor 当前设置
        BuildTarget platform;
        string platformStr = GetCommandLineArg("--platform");
        if (!string.IsNullOrEmpty(platformStr))
        {
            if (!Enum.TryParse(platformStr, true, out platform))
                return BuildTaskResult.Fail(BuildErrorCodes.InvalidPlatform,
                    $"未知 Platform '{platformStr}'。", true);
        }
        else
        {
            platform = EditorUserBuildSettings.activeBuildTarget;
        }

        // OutputRoot: CLI --output > 默认路径
        string outputRoot = GetCommandLineArg("--output")
            ?? Path.Combine(Application.dataPath, "..", "Build", platform.ToString());

        var version = versionData != null
            ? versionData.CurrentVersion
            : new VersionNumber { Major = 1, Minor = 0, Patch = 0 };

        var cfg = new BuildConfig(mode, version, buildVersionString, outputRoot, platform);
        ctx.Set(BuildContextKeys.BuildConfig, cfg);

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
