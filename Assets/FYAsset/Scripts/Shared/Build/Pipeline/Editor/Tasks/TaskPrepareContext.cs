using UnityEditor;
using UnityEngine;
using System;
using System.IO;

/// <summary>
/// 管线起点 Task —— 初始化构建环境：后端键、版本号、输出目录、目标平台。
/// 正式构建后端键由 BuildProjectRunner 创建的 request 携带。
/// </summary>
public class TaskPrepareContext : IBuildTask
{
    public string TaskName => "TaskPrepareContext";
    public BuildTaskResult Execute(BuildContext ctx)
    {
        var request = ctx.Get<BuildPackageRequest>(BuildContextKeys.BuildPackageRequest);
        string backendKey = request != null ? request.BackendKey : string.Empty;

        // BuildVersionString: CLI --version > 时间戳（用于构建摘要；正式包目录名由 BuildPackageRequest 决定）
        string buildVersionString = GetCommandLineArg("--version")
            ?? DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");

        // CLI --version 只在没有 BuildPackageRequest 的旧/诊断路径中覆盖本次 BuildConfig，不提前写回 VersionRecord。
        var versionData = AssetDatabase.LoadAssetAtPath<VersionRecord>(
            FYAssetSettings.Instance.VersionRecordPath);
        string cliVersion = GetCommandLineArg("--version");
        VersionNumber cliParsedVersion = request == null
            && !string.IsNullOrEmpty(cliVersion)
            && VersionNumber.TryParse(cliVersion, out var cliVer)
            ? cliVer
            : null;

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
            ?? ctx.Get<string>(BuildContextKeys.RepositoryPreviewOutput)
            ?? FYAssetPathUtility.JoinFilePath(BuildPathManager.ProjectRoot, "Build", platform.ToString());

        var version = request != null && request.Version != null
            ? request.Version
            : cliParsedVersion != null
            ? cliParsedVersion
            : versionData != null
            ? versionData.CurrentVersion
            : new VersionNumber { Major = 1, Minor = 0, Patch = 0 };

        var cfg = new BuildConfig(backendKey, version, buildVersionString, outputRoot, platform);
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
