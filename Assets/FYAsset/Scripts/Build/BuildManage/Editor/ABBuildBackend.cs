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
/// </summary>
public class ABBuildBackend : IBuildBackend
{
    private BuildContext _context;
    private ABManifest _manifest;
    private string _pipelineOutputDir;

    public Task<BuildBackendResult> BuildAsync(VersionNumber version, BuildType buildType)
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
            BuildResult result = DAGScheduler.Execute(config, _context);
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

    public void GenerateVersionState(string outputDir, VersionNumber version)
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

        Debug.Log($"[ABBuildBackend] Version state generated: {outputDir}, assets: {_manifest.AssetEntries.Count}, bundles: {_manifest.BundleEntries.Count}, manifest: {manifestPath}");
    }

    private static void CopyFileIfExists(string sourcePath, string targetPath)
    {
        if (FileHelper.Exists(sourcePath))
            FileHelper.CopyFile(sourcePath, targetPath, true);
    }

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
