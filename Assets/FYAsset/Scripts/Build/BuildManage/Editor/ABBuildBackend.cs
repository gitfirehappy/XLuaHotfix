#if UNITY_EDITOR
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

/// <summary>
/// ABManifest 新管线构建后端。
/// 通过 DAGScheduler 驱动已落地的 7 个 Task，并在 BuildProjectManager 统一入口下对齐旧包目录结构。
///
/// 构建流程：DAGScheduler.Execute -> 从 BuildContext 提取 ABManifest 与输出路径 -> 拷贝产物到目标目录 -> 生成 PackageManifest。
/// </summary>
public class ABBuildBackend : IBuildBackend
{
    #region State

    private BuildContext _context;
    private ABManifest _manifest;
    private string _pipelineOutputDir;

    #endregion

    /// <summary>
    /// 便捷重载，无额外执行选项。
    /// </summary>
    public Task<BuildBackendResult> BuildAsync(VersionNumber version, BuildType buildType)
    {
        return BuildAsync(version, buildType, null);
    }

    /// <summary>
    /// 加载 BuildPipelineConfig -> 创建 BuildContext -> DAGScheduler.Execute 执行管线。
    /// 成功后从 Context 中提取 ABManifest 和输出路径供后续 OrganizeOutput 使用。
    /// </summary>
    public Task<BuildBackendResult> BuildAsync(VersionNumber version, BuildType buildType, BuildExecutionOptions options)
    {
        var config = AssetDatabase.LoadAssetAtPath<BuildPipelineConfig>(FYAssetSettings.Instance.PipelineConfigPath);
        if (config == null)
        {
            Debug.LogError($"[ABBuildBackend] 未找到 BuildPipelineConfig: {FYAssetSettings.Instance.PipelineConfigPath}");
            return Task.FromResult(BuildBackendResult.Fail(
                BuildMessage.Error(BuildErrorCodes.SettingNull, "BuildPipelineConfig not found.", "ABBuildBackend")));
        }

        try
        {
            _context = new BuildContext();
            BuildResult result = DAGScheduler.Execute(config, _context, options);
            if (!result.Success)
            {
                LogBuildResultErrors(result);
                return Task.FromResult(BuildBackendResult.Fail(
                    BuildMessage.Error(BuildErrorCodes.BuildFailed,
                        $"AB pipeline failed. Completed: {result.CompletedTasks}/{result.TotalTasks}", "ABBuildBackend")));
            }

            _manifest = _context.Require<ABManifest>(BuildContextKeys.ABManifest);
            _pipelineOutputDir = _context.Require<string>(BuildContextKeys.OutputPath);
            return Task.FromResult(BuildBackendResult.Ok());
        }
        catch (Exception ex)
        {
            Debug.LogError($"[ABBuildBackend] 构建过程中出现异常: {ex}");
            return Task.FromResult(BuildBackendResult.Fail(
                BuildMessage.Error(BuildErrorCodes.BuildFailed, $"AB pipeline exception: {ex.Message}", "ABBuildBackend")));
        }
    }

    /// <summary>
    /// 从管线临时输出目录拷贝 .bundle、build_summary.txt、manifest.json 到目标发布目录。
    /// 目标目录如有旧内容会先清空。
    /// </summary>
    public void OrganizeOutput(string outputDir, VersionNumber version)
    {
        if (string.IsNullOrEmpty(_pipelineOutputDir))
            throw new InvalidOperationException("AB pipeline output is not ready. Call BuildAsync first.");

        if (!FileHelper.DirectoryExists(_pipelineOutputDir))
            throw new DirectoryNotFoundException($"AB pipeline output directory not found: {_pipelineOutputDir}");

        if (FileHelper.DirectoryExists(outputDir))
            FileHelper.TryDeleteDirectory(outputDir, true);

        FileHelper.EnsureDirectory(outputDir);
        string bundleOutputDir = Path.Combine(outputDir, "bundles");
        FileHelper.EnsureDirectory(bundleOutputDir);

        foreach (var bundleEntry in _manifest.BundleEntries)
        {
            string sourcePath = Path.Combine(_pipelineOutputDir, bundleEntry.BundleName);
            string destinationPath = Path.Combine(bundleOutputDir, bundleEntry.BundleName);
            if (!FileHelper.Exists(sourcePath))
                throw new FileNotFoundException($"Bundle file missing from pipeline output: {sourcePath}", sourcePath);

            FileHelper.CopyFile(sourcePath, destinationPath, true);
        }

        CopyFileIfExists(Path.Combine(_pipelineOutputDir, "build_summary.txt"), Path.Combine(outputDir, "build_summary.txt"));
        CopyFileIfExists(Path.Combine(_pipelineOutputDir, FYAssetSettings.MANIFEST_FILE_NAME), Path.Combine(outputDir, FYAssetSettings.MANIFEST_FILE_NAME));

        Debug.Log($"[ABBuildBackend] Output organized: {outputDir}, assets: {_manifest.AssetEntries.Count}, bundles: {_manifest.BundleEntries.Count}");
    }

    /// <summary>
    /// 将 ABManifest 序列化为 JSON 写入输出目录。
    /// 如文件已存在则跳过（保护已有发布产物）。
    /// </summary>
    public void GeneratePackageManifest(string outputDir, VersionNumber version)
    {
        if (_manifest == null)
            throw new InvalidOperationException("AB manifest is not ready. Call BuildAsync first.");

        string manifestPath = Path.Combine(outputDir, FYAssetSettings.MANIFEST_FILE_NAME);
        if (!FileHelper.Exists(manifestPath))
        {
            if (!FileHelper.DirectoryExists(outputDir))
                FileHelper.EnsureDirectory(outputDir);
            FileHelper.WriteAllTextAtomic(manifestPath, _manifest.SerializeToJson(), Encoding.UTF8);
        }

        Debug.Log($"[ABBuildBackend] Package manifest generated: {outputDir}, assets: {_manifest.AssetEntries.Count}, bundles: {_manifest.BundleEntries.Count}, manifest: {manifestPath}");
    }

    private static void CopyFileIfExists(string sourcePath, string targetPath)
    {
        if (FileHelper.Exists(sourcePath))
            FileHelper.CopyFile(sourcePath, targetPath, true);
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

            Debug.LogError($"[ABBuildBackend] {taskResult.ErrorCode}: {taskResult.ErrorMessage}");
        }
    }
}
#endif
