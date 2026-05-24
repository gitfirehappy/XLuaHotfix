#if UNITY_EDITOR
using System.Collections.Generic;
using System;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

/// <summary>
/// ABManifest 新管线构建后端。
/// 只负责把 BuildPackageRequest 交给 AB DAG；最终包目录、Manifest、PackageIndex 都由 Task 图写入。
/// </summary>
public class ABBuildBackend : IBuildBackend
{
    public Task<BuildBackendResult> BuildAsync(BuildPackageRequest request, BuildExecutionOptions options)
    {
        var config = AssetDatabase.LoadAssetAtPath<BuildPipelineConfig>(
            FYAssetBuildSettingsProvider.GetPipelineConfigPath(BackendMode.ABManifest));
        if (config == null)
            return Task.FromResult(BuildBackendResult.Fail(
                BuildMessage.Error(BuildErrorCodes.SettingNull, "未找到 BuildPipelineConfig。", "ABBuildBackend")));

        try
        {
            request = request ?? throw new ArgumentNullException(nameof(request));
            var context = new BuildContext();
            context.Set(BuildContextKeys.BuildPackageRequest, request);
            context.Set(BuildContextKeys.BuildType, request.BuildType);
            Debug.Log($"[{nameof(ABBuildBackend)}] 启动 AB DAG。BuildType={request.BuildType}, Package={request.PackageName}");
            BuildResult result = DAGScheduler.Execute(config, context, options);
            if (!result.Success)
            {
                LogBuildResultErrors(result);
                return Task.FromResult(BuildBackendResult.Fail(
                    BuildMessage.Error(BuildErrorCodes.BuildFailed,
                        $"AB 管线构建失败。已完成: {result.CompletedTasks}/{result.TotalTasks}", "ABBuildBackend")));
            }

            var artifacts = context.Get<List<ArtifactDigest>>(BuildContextKeys.RepositoryArtifacts);
            Debug.Log($"[{nameof(ABBuildBackend)}] AB DAG 完成。Completed={result.CompletedTasks}/{result.TotalTasks}, RepositoryArtifacts={(artifacts != null ? artifacts.Count : 0)}");
            return Task.FromResult(BuildBackendResult.Ok(artifacts));
        }
        catch (Exception ex)
        {
            Debug.LogError($"[{nameof(ABBuildBackend)}] AB DAG 异常: {ex}");
            return Task.FromResult(BuildBackendResult.Fail(
                BuildMessage.Error(BuildErrorCodes.BuildFailed, $"AB 管线异常: {ex.Message}", "ABBuildBackend")));
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
}
#endif
