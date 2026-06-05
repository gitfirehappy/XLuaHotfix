using UnityEditor;
using UnityEngine;
using System;
using System.IO;

/// <summary>
/// 管线起点 Task —— 初始化构建环境：后端模式、版本号、输出目录、目标平台。
/// 正式构建后端模式由 FYAssetSettings SO 决定，BuildProjectManager 在 DAG 前创建对应 request/backend。
/// </summary>
public class TaskPrepareContext : IBuildTask
{
    public string TaskName => "TaskPrepareContext";
    public string[] DependsOn => new string[0];
    public string[] ReadKeys => new string[0];
    public string[] WriteKeys => new[] { BuildContextKeys.BuildConfig };

    public BuildTaskResult Execute(BuildContext ctx)
    {
        // 读取 SO 配置；正式 Full/Hotfix 构建不允许 Task 内部再覆盖后端。
        BackendMode mode = FYAssetSettings.Instance.UseABBackend ? BackendMode.ABManifest : BackendMode.AA;

        // BuildVersionString: CLI --version > 时间戳（用于构建摘要；正式包目录名由 BuildPackageRequest 决定）
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
            ?? ctx.Get<string>(BuildContextKeys.RepositoryPreviewOutput)
            ?? FYAssetPathUtility.JoinFilePath(BuildPathManager.ProjectRoot, "Build", platform.ToString());

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
