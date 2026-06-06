#if UNITY_EDITOR
using System.Collections.Generic;
using System;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

/// <summary>
/// AA 构建后端。
/// 只负责把 BuildPackageRequest 交给 AA DAG；Addressables build、Manifest、PackageIndex 都由 Task 图处理。
/// </summary>
public class AABuildBackend : IBuildBackend
{
    public Task<BuildBackendResult> BuildAsync(BuildPackageRequest request, BuildExecutionOptions options)
    {
        var config = AssetDatabase.LoadAssetAtPath<BuildPipelineConfig>(
            FYAssetBuildSettingsProvider.GetPipelineConfigPath(BackendMode.AA));
        if (config == null)
            return Task.FromResult(BuildBackendResult.Fail(
                BuildMessage.Error(BuildErrorCodes.SettingNull, "未找到 AA BuildPipelineConfig。", nameof(AABuildBackend))));

        try
        {
            request = request ?? throw new ArgumentNullException(nameof(request));
            var context = new BuildContext();
            context.Set(BuildContextKeys.BuildPackageRequest, request);
            context.Set(BuildContextKeys.BuildType, request.BuildType);
            Debug.Log($"[{nameof(AABuildBackend)}] 启动 AA DAG。BuildType={request.BuildType}, Package={request.PackageName}");
            BuildResult result = DAGScheduler.Execute(config, context, options);
            if (!result.Success)
            {
                LogBuildResultErrors(result);
                return Task.FromResult(BuildBackendResult.Fail(
                    BuildMessage.Error(BuildErrorCodes.BuildFailed,
                        $"AA 管线构建失败。已完成: {result.CompletedTasks}/{result.TotalTasks}", nameof(AABuildBackend))));
            }

            var artifacts = context.Get<List<ArtifactDigest>>(BuildContextKeys.RepositoryArtifacts);
            Debug.Log($"[{nameof(AABuildBackend)}] AA DAG 完成。Completed={result.CompletedTasks}/{result.TotalTasks}, RepositoryArtifacts={(artifacts != null ? artifacts.Count : 0)}");
            return Task.FromResult(BuildBackendResult.Ok(artifacts, result, request, string.Empty));
        }
        catch (Exception ex)
        {
            Debug.LogError($"[{nameof(AABuildBackend)}] AA DAG 异常: {ex}");
            return Task.FromResult(BuildBackendResult.Fail(
                BuildMessage.Error(BuildErrorCodes.BuildFailed, ex.Message, nameof(AABuildBackend))));
        }
    }

    /// <summary>
    /// 遍历 BuildResult 中所有失败 Task 并输出 Warning 日志。
    /// </summary>
    private static void LogBuildResultErrors(BuildResult result)
    {
        if (result?.TaskResults == null)
            return;

        foreach (var taskResult in result.TaskResults)
        {
            if (taskResult == null || taskResult.Success)
                continue;

            Debug.LogWarning($"[{nameof(AABuildBackend)}] DAG Task 失败: Code={taskResult.ErrorCode}, Message={taskResult.ErrorMessage}");
        }
    }
}
#endif
