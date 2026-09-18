#if UNITY_EDITOR
using System;
using UnityEditor;

/// <summary>
/// 编辑器侧构建运行环境：把 BuildRequest 和编辑器构建配置写入 Context。
/// 输出目录的创建、交付和回收由 BuildProjectRunner 统一负责。
/// </summary>
public sealed class EditorBuildRunEnvironment : IBuildRunEnvironment
{
    public void PrepareContext(BuildRunContext context, BuildRequest request)
    {
        context.Set(BuildContextKeys.BuildRequest, request);
        context.Set(BuildContextKeys.BuildConfig, CreateBuildConfig(request));
        context.Set(BuildContextKeys.PipelineStartedAtUtc, DateTime.UtcNow);
    }

    private static BuildConfig CreateBuildConfig(BuildRequest request)
    {
        string buildVersionString = GetCommandLineArg("--version")
            ?? DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        BuildTarget platform = ResolveTargetPlatform();
        string workRoot = GetCommandLineArg("--output")
            ?? FYAssetPathUtility.JoinFilePath(BuildPathManager.ProjectRoot, "Build", platform.ToString());
        return new BuildConfig(request.BackendKey, request.Version, buildVersionString, workRoot, platform);
    }

    private static BuildTarget ResolveTargetPlatform()
    {
        string platformText = GetCommandLineArg("--platform");
        if (string.IsNullOrEmpty(platformText))
            return EditorUserBuildSettings.activeBuildTarget;

        if (!Enum.TryParse(platformText, true, out BuildTarget platform))
            throw new InvalidOperationException($"未知 Platform '{platformText}'。可用值来自 UnityEditor.BuildTarget 枚举。");
        return platform;
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
#endif
