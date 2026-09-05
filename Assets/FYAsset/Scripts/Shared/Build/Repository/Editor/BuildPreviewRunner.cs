#if UNITY_EDITOR
using System;
using System.Collections.Generic;

/// <summary>
/// Diff Preview 的中性执行机制：按 config 主干白名单运行到 HotfixDiffTaskName（含），
/// 提取 ArtifactDelta；不写 baseline 或 PackageIndex。
/// 各后端 preview 入口（AA/AB）在其侧持有，本类不含任何后端知识。
/// </summary>
public static class BuildPreviewRunner
{
    /// <param name="configureContext">管线执行前的 context 定制（如输出目录、后端预览标志）。</param>
    /// <param name="extractResult">管线执行后从 context 提取后端私有结果（在 delta 返回之前调用）。</param>
    public static ArtifactDelta Run(
        BuildPipelineConfig config,
        BuildPackageRequest request,
        Action<BuildContext> configureContext,
        Action<BuildContext> extractResult = null)
    {
        var previewContext = new BuildContext();
        var previewRequest = BuildPackageRequest.Create(request.Version, BuildType.Hotfix, request.BackendMode);
        previewContext.Set(BuildContextKeys.BuildPackageRequest, previewRequest);
        previewContext.Set(BuildContextKeys.BuildType, BuildType.Hotfix);
        previewContext.Set(BuildContextKeys.RepositoryPreviewMode, true);
        configureContext?.Invoke(previewContext);

        var whitelist = BuildPreviewWhitelist(config);
        BuildResult result = BuildPipelineRunner.Execute(
            config, previewContext, null, config.HotfixDiffTaskName, whitelist, null);
        if (!result.Success)
            throw new InvalidOperationException(
                FormatPreviewFailure(request.BackendMode.ToString(), result));

        extractResult?.Invoke(previewContext);
        return RequireDelta(previewContext);
    }

    /// <summary>预览执行范围：按 config 主干顺序执行到 HotfixDiffTaskName（含）为止。</summary>
    private static HashSet<string> BuildPreviewWhitelist(BuildPipelineConfig config)
    {
        var whitelist = new HashSet<string>(StringComparer.Ordinal);
        foreach (TaskEntry entry in config.Tasks)
        {
            if (entry == null || string.IsNullOrEmpty(entry.TaskName))
                continue;
            whitelist.Add(entry.TaskName);
            if (string.Equals(entry.TaskName, config.HotfixDiffTaskName, StringComparison.Ordinal))
                break;
        }
        return whitelist;
    }

    private static ArtifactDelta RequireDelta(BuildContext context)
    {
        var delta = context.Get<ArtifactDelta>(BuildContextKeys.ArtifactDelta);
        if (delta == null)
            throw new InvalidOperationException("Diff preview did not produce ArtifactDelta.");
        return delta;
    }

    private static string FormatPreviewFailure(string backendLabel, BuildResult result)
    {
        if (result == null)
            return $"{backendLabel} preview pipeline failed: result is null.";

        BuildTaskResult failed = null;
        if (result.TaskResults != null)
        {
            for (int i = 0; i < result.TaskResults.Count; i++)
            {
                var item = result.TaskResults[i];
                if (item != null && !item.Success)
                {
                    failed = item;
                    break;
                }
            }
        }

        if (failed == null)
            return $"{backendLabel} preview pipeline failed. Completed={result.CompletedTasks}, Skipped={result.SkippedTasks}.";

        string taskName = string.IsNullOrEmpty(failed.TaskName) ? "<unknown>" : failed.TaskName;
        string code = string.IsNullOrEmpty(failed.ErrorCode) ? "<no-code>" : failed.ErrorCode;
        string message = string.IsNullOrEmpty(failed.ErrorMessage) ? "<no-message>" : failed.ErrorMessage;
        return $"{backendLabel} preview pipeline failed at {taskName}: [{code}] {message} Fatal={failed.IsFatal}, Skipped={result.SkippedTasks}.";
    }
}
#endif
