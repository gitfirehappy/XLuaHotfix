#if UNITY_EDITOR
using System.Collections.Generic;
using System;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using Stopwatch = System.Diagnostics.Stopwatch;

/// <summary>
/// ABManifest 新管线构建后端。
/// 只负责把 BuildPackageRequest 交给 AB DAG；最终包目录、Manifest、PackageIndex 都由 Task 图写入。
/// </summary>
public class ABBuildBackend : IBuildBackend
{
    public Task<BuildBackendResult> BuildAsync(BuildPackageRequest request, BuildExecutionOptions options)
    {
        var stopwatch = Stopwatch.StartNew();
        BuildContext context = null;
        BuildResult result = null;

        var config = AssetDatabase.LoadAssetAtPath<BuildPipelineConfig>(
            FYAssetBuildSettingsProvider.GetPipelineConfigPath(BackendMode.ABManifest));
        if (config == null)
        {
            var error = BuildMessage.Error(BuildErrorCodes.SettingNull, "未找到 BuildPipelineConfig。", nameof(ABBuildBackend));
            string reportPath = TryWriteReport(request, result, context, stopwatch, error);
            return Task.FromResult(BuildBackendResult.Fail(error, result, request, reportPath));
        }

        try
        {
            request = request ?? throw new ArgumentNullException(nameof(request));
            context = new BuildContext();
            context.Set(BuildContextKeys.BuildPackageRequest, request);
            context.Set(BuildContextKeys.BuildType, request.BuildType);
            Debug.Log($"[{nameof(ABBuildBackend)}] 启动 AB DAG。BuildType={request.BuildType}, Package={request.PackageName}");
            result = DAGScheduler.Execute(config, context, options);
            if (!result.Success)
            {
                LogBuildResultErrors(result);
                var error = BuildMessage.Error(BuildErrorCodes.BuildFailed,
                    $"AB 管线构建失败。已完成: {result.CompletedTasks}/{result.TotalTasks}", nameof(ABBuildBackend));
                string reportPath = TryWriteReport(request, result, context, stopwatch, error);
                return Task.FromResult(BuildBackendResult.Fail(error, result, request, reportPath));
            }

            var artifacts = context.Get<List<ArtifactDigest>>(BuildContextKeys.RepositoryArtifacts);
            Debug.Log($"[{nameof(ABBuildBackend)}] AB DAG 完成。Completed={result.CompletedTasks}/{result.TotalTasks}, RepositoryArtifacts={(artifacts != null ? artifacts.Count : 0)}");
            string successReportPath = TryWriteReport(request, result, context, stopwatch, null);
            return Task.FromResult(BuildBackendResult.Ok(artifacts, result, request, successReportPath));
        }
        catch (Exception ex)
        {
            Debug.LogError($"[{nameof(ABBuildBackend)}] AB DAG 异常: {ex}");
            var error = BuildMessage.Error(BuildErrorCodes.BuildFailed, $"AB 管线异常: {ex.Message}", nameof(ABBuildBackend));
            string reportPath = TryWriteReport(request, result, context, stopwatch, error);
            return Task.FromResult(BuildBackendResult.Fail(error, result, request, reportPath));
        }
    }

    /// <summary>
    /// 遍历 BuildResult 中所有失败 Task 并输出 Error 日志。
    /// </summary>
    private static void LogBuildResultErrors(BuildResult result)
    {
        if (result?.TaskResults == null)
            return;

        foreach (var taskResult in result.TaskResults)
        {
            if (taskResult == null || taskResult.Success)
                continue;

            Debug.LogWarning($"[{nameof(ABBuildBackend)}] DAG Task 失败: Code={taskResult.ErrorCode}, Message={taskResult.ErrorMessage}");
        }
    }

    /// <summary>
    /// Best-effort 写入 AB 构建报告。报告失败不能覆盖原始构建结果。
    /// </summary>
    private static string TryWriteReport(
        BuildPackageRequest request,
        BuildResult result,
        BuildContext context,
        Stopwatch stopwatch,
        BuildMessage error)
    {
        if (request == null)
            return string.Empty;

        try
        {
            ABBuildReport report = ABBuildReportBuilder.Build(request, result, context, stopwatch, error);
            string path = ABBuildReportStore.CreateReportPath(request);
            ABBuildReportStore.Write(report, path);
            Debug.Log($"[{nameof(ABBuildBackend)}] AB 构建报告已写入: {path}");
            return path;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[{nameof(ABBuildBackend)}] AB 构建报告写入失败: {ex.Message}");
            return string.Empty;
        }
    }
}
#endif
