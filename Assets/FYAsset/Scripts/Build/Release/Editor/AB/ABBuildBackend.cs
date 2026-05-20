#if UNITY_EDITOR
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

/// <summary>
/// ABManifest 新管线构建后端。
/// 通过 DAGScheduler 驱动 Task 在 BuildProjectManager 统一入口导出
///
/// 构建流程：DAGScheduler.Execute -> 从 BuildContext 提取 ABManifest 与输出路径 -> 拷贝产物到目标目录 -> 生成 PackageManifest。
/// </summary>
public class ABBuildBackend : IBuildBackend
{
    #region State

    private BuildContext _context;
    private ABManifest _manifest;
    private string _pipelineOutputDir;
    private BuildPackageRequest _request;

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
        var request = BuildPackageRequest.Create(version, buildType, BackendMode.ABManifest);
        return BuildAsync(request, options);
    }

    public Task<BuildBackendResult> BuildAsync(BuildPackageRequest request, BuildExecutionOptions options)
    {
        var config = AssetDatabase.LoadAssetAtPath<BuildPipelineConfig>(FYAssetSettings.Instance.PipelineConfigPath);
        if (config == null)
            return Task.FromResult(BuildBackendResult.Fail(
                BuildMessage.Error(BuildErrorCodes.SettingNull, "未找到 BuildPipelineConfig。", "ABBuildBackend")));

        try
        {
            _request = request ?? throw new ArgumentNullException(nameof(request));
            _context = new BuildContext();
            _context.Set(BuildContextKeys.BuildPackageRequest, _request);
            BuildResult result = DAGScheduler.Execute(config, _context, options);
            if (!result.Success)
            {
                LogBuildResultErrors(result);
                return Task.FromResult(BuildBackendResult.Fail(
                    BuildMessage.Error(BuildErrorCodes.BuildFailed,
                        $"AB 管线构建失败。已完成: {result.CompletedTasks}/{result.TotalTasks}", "ABBuildBackend")));
            }

            _manifest = _context.Require<ABManifest>(BuildContextKeys.ABManifest);
            _pipelineOutputDir = _context.Require<string>(BuildContextKeys.OutputPath);
            return Task.FromResult(BuildBackendResult.Ok());
        }
        catch (Exception ex)
        {
            return Task.FromResult(BuildBackendResult.Fail(
                BuildMessage.Error(BuildErrorCodes.BuildFailed, $"AB 管线异常: {ex.Message}", "ABBuildBackend")));
        }
    }

    /// <summary>
    /// 从管线临时输出目录拷贝 .bundle、build_summary.txt、manifest.json 到目标发布目录。
    /// 目标目录如有旧内容会先清空。
    /// </summary>
    public void OrganizeOutput(string outputDir, VersionNumber version)
    {
        ValidateRequestOutput(outputDir);

        if (string.IsNullOrEmpty(_pipelineOutputDir))
            throw new InvalidOperationException("AB 管线输出尚未就绪，请先调用 BuildAsync。");

        if (!FileHelper.DirectoryExists(_pipelineOutputDir))
            throw new DirectoryNotFoundException($"AB 管线输出目录不存在: {_pipelineOutputDir}");

        if (FileHelper.DirectoryExists(outputDir))
            FileHelper.TryDeleteDirectory(outputDir, true);

        FileHelper.EnsureDirectory(outputDir);
        string bundleOutputDir = BuildPathManager.GetBundlesDir(outputDir);
        FileHelper.EnsureDirectory(bundleOutputDir);

        foreach (var bundleEntry in _manifest.BundleEntries)
        {
            string sourcePath = Path.Combine(_pipelineOutputDir, bundleEntry.BundleName);
            string destinationPath = Path.Combine(bundleOutputDir, bundleEntry.BundleName);
            if (!FileHelper.Exists(sourcePath))
                throw new FileNotFoundException($"管线输出中缺少 Bundle 文件: {sourcePath}", sourcePath);

            FileHelper.CopyFile(sourcePath, destinationPath, true);
        }

        CopyFileIfExists(Path.Combine(_pipelineOutputDir, "build_summary.txt"), Path.Combine(outputDir, "build_summary.txt"));
        CopyFileIfExists(Path.Combine(_pipelineOutputDir, FYAssetSettings.MANIFEST_FILE_NAME), Path.Combine(outputDir, FYAssetSettings.MANIFEST_FILE_NAME));

        Debug.Log($"[ABBuildBackend] Output 整理完毕: {outputDir}, Assets: {_manifest.AssetEntries.Count}, Bundles: {_manifest.BundleEntries.Count}");
    }

    /// <summary>
    /// 将 ABManifest 序列化为 JSON 写入输出目录。
    /// 如文件已存在则跳过（保护已有发布产物）。
    /// </summary>
    public void GeneratePackageManifest(string outputDir, VersionNumber version)
    {
        ValidateRequestOutput(outputDir);

        if (_manifest == null)
            throw new InvalidOperationException("AB Manifest 尚未就绪，请先调用 BuildAsync。");

        long totalSize = 0;
        for (int i = 0; i < _manifest.BundleEntries.Count; i++)
            totalSize += _manifest.BundleEntries[i].FileSize;

        if (!HotfixPackageSizeGuard.ValidateOrAbort(totalSize, "ABBuildBackend"))
            return;

        ManifestOutputFormat outputFormat = FYAssetSettings.Instance.ManifestOutputFormat;
        string manifestPath = Path.Combine(outputDir, FYAssetSettings.MANIFEST_FILE_NAME);
        string manifestBinPath = Path.Combine(outputDir, FYAssetSettings.MANIFEST_FILE_NAME_BIN);

        if (!FileHelper.DirectoryExists(outputDir))
            FileHelper.EnsureDirectory(outputDir);

        if (outputFormat != ManifestOutputFormat.BinaryOnly)
        {
            FileHelper.WriteAllTextAtomic(manifestPath, _manifest.SerializeToJson(), Encoding.UTF8);
        }
        else
        {
            FileHelper.TryDelete(manifestPath);
        }

        if (outputFormat != ManifestOutputFormat.JsonOnly)
        {
            SerializationUtility.WriteToFile(manifestBinPath, _manifest, "binary", false);
        }
        else
        {
            FileHelper.TryDelete(manifestBinPath);
        }

        Debug.Log($"[ABBuildBackend] Package Manifest 已生成: {outputDir}, Assets: {_manifest.AssetEntries.Count}, Bundles: {_manifest.BundleEntries.Count}, JSON: {manifestPath}, Binary: {manifestBinPath}");
    }

    private static void CopyFileIfExists(string sourcePath, string targetPath)
    {
        if (FileHelper.Exists(sourcePath))
            FileHelper.CopyFile(sourcePath, targetPath, true);
    }

    private void ValidateRequestOutput(string outputDir)
    {
        if (_request == null)
            throw new InvalidOperationException("AB 构建请求尚未就绪，请先调用 BuildAsync。");
        if (!string.Equals(_request.OutputDir, outputDir, StringComparison.Ordinal))
            throw new InvalidOperationException($"AB 输出目录必须来自 BuildPackageRequest。Expected: {_request.OutputDir}, Actual: {outputDir}");
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

            Debug.LogWarning($"[ABBuildBackend] {taskResult.ErrorCode}: {taskResult.ErrorMessage}");
        }
    }
}
#endif
