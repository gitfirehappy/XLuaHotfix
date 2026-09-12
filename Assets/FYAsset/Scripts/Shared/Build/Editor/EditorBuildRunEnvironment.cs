#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 编辑器侧的构建运行环境：为 Runner 准备 Context 标准键，并管理 attempt 中间目录的建、提、清。
/// </summary>
/// <remarks>
/// 这里是原 TaskPrepareContext 与 BuildProjectRunner 中“attempt 目录提升”职责的新归属：
/// Runner 只按 <see cref="IBuildRunEnvironment"/> 约定的顺序调用，不接触 Unity API；
/// 本地启动数据与构建事实（Summary/Index）的提交仍由 BuildProjectRunner 编排，
/// 因此本类不再需要后端内置包 handler。
/// </remarks>
public sealed class EditorBuildRunEnvironment : IBuildRunEnvironment
{
    /// <summary>写入后端请求、构建类型与 BuildConfig（CLI/平台/输出解析、版本兜底）。</summary>
    public void PrepareContext(BuildContext context, BuildRequest request)
    {
        BuildPackageRequest package = request.Package;

        context.Set(BuildContextKeys.BuildPackageRequest, package);
        context.Set(BuildContextKeys.BuildType, package.BuildType);
        context.Set(BuildContextKeys.DeferPackagePublication, true);

        context.Set(BuildContextKeys.BuildConfig, CreateBuildConfig(package));

        // 成品在 attempt 目录内落盘，正式出口只由 Runner 提升；OutputPath 先指向任务链写入目录。
        context.Set(BuildContextKeys.OutputPath, package.OutputDir);

        // 构建耗时的事实起点：Export 阶段据此计算 Duration 写入构建摘要。
        context.Set(BuildContextKeys.BuildStartedAtUtc, DateTime.UtcNow);
    }

    /// <summary>attempt 布局返回中间目录事务；直接写正式输出的布局返回 null。</summary>
    public IBuildAttempt BeginAttempt(BuildContext context, BuildRequest request)
    {
        if (!request.Package.IsAttemptLayout)
            return null;

        return new EditorBuildAttempt(request.Package);
    }

    private static BuildConfig CreateBuildConfig(BuildPackageRequest package)
    {
        // BuildVersionString 只用于构建摘要与日志：CLI --version 优先，否则取当前时间戳。
        string buildVersionString = GetCommandLineArg("--version")
            ?? DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");

        BuildTarget platform = ResolveTargetPlatform();

        // OutputRoot 仍是打包工作根（_temp 等中间目录的父目录），与包出口无关。
        string outputRoot = GetCommandLineArg("--output")
            ?? FYAssetPathUtility.JoinFilePath(BuildPathManager.ProjectRoot, "Build", platform.ToString());

        // 版本只来自请求：BuildProjectRunner 已在创建请求时按 Summary Index 计算候选版本，
        // 构建与交付全部成功后才写 Index，因此环境侧不再读存储兜底。
        return new BuildConfig(package.BackendKey, package.Version, buildVersionString, outputRoot, platform);
    }

    private static BuildTarget ResolveTargetPlatform()
    {
        string platformText = GetCommandLineArg("--platform");
        if (string.IsNullOrEmpty(platformText))
            return EditorUserBuildSettings.activeBuildTarget;

        if (!Enum.TryParse(platformText, true, out BuildTarget platform))
        {
            throw new BuildPipelineException(
                BuildErrorCodes.InvalidPlatform,
                $"未知 Platform '{platformText}'。可用值来自 UnityEditor.BuildTarget 枚举。");
        }

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

    /// <summary>
    /// attempt 目录事务：提升前先校验 attempt 产物确实可交付，提升失败时正式输出保持原状。
    /// </summary>
    private sealed class EditorBuildAttempt : IBuildAttempt
    {
        private readonly BuildPackageRequest _package;

        public EditorBuildAttempt(BuildPackageRequest package)
        {
            _package = package;
            PrepareAttemptDirectory();
        }

        public bool TryPromote(out IBuildDeliveryToken token, out string error)
        {
            token = null;
            error = null;
            try
            {
                ValidateAttemptPackage(_package);
                token = BuildDeliveryPromoter.Promote(_package.OutputDir, _package.DeliveryOutputDir);
                return true;
            }
            catch (Exception ex)
            {
                error = $"{ex.GetType().Name}: {ex.Message}";
                return false;
            }
        }

        public void Discard()
        {
            if (!IsUnderAttemptRoot(_package.OutputDir))
            {
                Debug.LogWarning($"[{nameof(EditorBuildRunEnvironment)}] 跳过 attempt 清理（不在 attempt 根之下）: {_package.OutputDir}");
                return;
            }

            try
            {
                FileHelper.TryDeleteDirectory(_package.OutputDir, true);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[{nameof(EditorBuildRunEnvironment)}] attempt 清理失败: {ex.Message}");
            }
        }

        /// <summary>开始前清空同名 attempt 目录，避免上一次中断运行的残留文件混进本次提升。</summary>
        private void PrepareAttemptDirectory()
        {
            if (!IsUnderAttemptRoot(_package.OutputDir))
                throw new InvalidOperationException($"attempt 目录必须位于 AttemptPackagesRoot 之下: {_package.OutputDir}");

            FileHelper.TryDeleteDirectory(_package.OutputDir, true);
            FileHelper.EnsureDirectory(_package.OutputDir);
        }

        private static void ValidateAttemptPackage(BuildPackageRequest package)
        {
            if (!FileHelper.DirectoryExists(package.OutputDir))
                throw new DirectoryNotFoundException($"attempt 包目录不存在: {package.OutputDir}");

            // 交付集合按模式不同而不同：Full/Standalone 是整包，Hotfix 是相对 Full 的累计变化内容，
            // 后者可能一个内容文件都没有（Full 之后尚未发生任何变化），因此这里只拒绝“完全没有交付文件”。
            if (FileHelper.GetFiles(package.OutputDir, "*", SearchOption.AllDirectories).Length == 0)
                throw new InvalidOperationException($"attempt 包目录为空（没有任何交付文件）: {package.OutputDir}");
        }

        private static bool IsUnderAttemptRoot(string dir)
        {
            if (string.IsNullOrEmpty(dir))
                return false;

            string attemptRoot = FYAssetPathUtility.NormalizePath(BuildPathManager.AttemptPackagesRoot);
            string normalized = FYAssetPathUtility.NormalizePath(dir);
            StringComparison comparison = Path.DirectorySeparatorChar == '\\'
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            string rootWithSeparator = attemptRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            return !string.IsNullOrEmpty(normalized) && normalized.StartsWith(rootWithSeparator, comparison);
        }
    }
}
#endif
