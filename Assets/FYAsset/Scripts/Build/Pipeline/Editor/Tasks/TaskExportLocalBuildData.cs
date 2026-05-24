using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 本地启动数据导出 Task — 仅整包构建导出 BuildIndex 与 baseline manifest 到 StreamingAssets。
/// 挂在 AA/AB Task 图尾部；Hotfix 构建保持跳过。
/// </summary>
public class TaskExportLocalBuildData : IBuildTask
{
    private const string BuildIndexFileName = FYAssetSettings.BUILD_INDEX_FILENAME;
    private const string BundlesDirectoryName = "bundles";
    private const string AddressablesCatalogFileName = "catalog.json";

    public string TaskName => "TaskExportLocalBuildData";
    public string[] DependsOn => new string[0];
    public string[] ReadKeys => new[]
    {
        BuildContextKeys.BuildPackageRequest,
        BuildContextKeys.BuildType,
        BuildContextKeys.OutputPath
    };
    public string[] WriteKeys => new string[0];

    public BuildTaskResult Execute(BuildContext ctx)
    {
        var request = ctx.Require<BuildPackageRequest>(BuildContextKeys.BuildPackageRequest);
        var buildType = ctx.Require<BuildType>(BuildContextKeys.BuildType);

        if (buildType != BuildType.Full)
        {
            return BuildTaskResult.Ok(new List<string>
            {
                "[LOCAL BUILD DATA] Hotfix build skipped"
            });
        }

        string outputPath = ctx.Require<string>(BuildContextKeys.OutputPath);
        if (!string.Equals(outputPath, request.OutputDir, System.StringComparison.Ordinal))
            return BuildTaskResult.Fail(BuildErrorCodes.BuildFailed,
                $"本地构建数据导出必须基于 BuildPackageRequest 输出目录。Expected: {request.OutputDir}, Actual: {outputPath}", true);

        if (!FileHelper.DirectoryExists(request.OutputDir))
            return BuildTaskResult.Fail(BuildErrorCodes.BuildFailed,
                $"本地构建数据导出前最终输出目录不存在: {request.OutputDir}", true);

        ExportData(request);

        return BuildTaskResult.Ok(new List<string>
        {
            $"[LOCAL BUILD DATA] Version: {request.Version.GetFullVersionString()}"
        });
    }

    private static string BuildIndexStreamingPath => Path.Combine(Application.streamingAssetsPath, BuildIndexFileName);

    /// <summary>
    /// 导出启动期所需的本地构建数据。
    /// </summary>
    private static void ExportData(BuildPackageRequest request)
    {
        Debug.Log("[TaskExportLocalBuildData] 开始导出本地启动数据到 StreamingAssets...");

        FileHelper.EnsureDirectory(Application.streamingAssetsPath);

        ExportBuildIndex(request);
        ExportBaselinePackage(request);

        AssetDatabase.Refresh();
        Debug.Log("[TaskExportLocalBuildData] 本地启动数据导出完成。");
    }

    private static void ExportBuildIndex(BuildPackageRequest request)
    {
        Debug.Log("[TaskExportLocalBuildData] 正在生成 BuildIndex...");

        string buildTime = request.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss");
        var buildIndexData = new BuildIndexData
        {
            BuildGUID = request.PackageName,
            BuildTime = buildTime,
            IsDebug = EditorUserBuildSettings.development,
            Platform = EditorUserBuildSettings.activeBuildTarget.ToString(),
            BackendMode = BackendModeNames.FromBackendMode(request.BackendMode),
            Version = request.Version
        };

        SerializationUtility.WriteToFile(BuildIndexStreamingPath, buildIndexData);

        string projectPath = FYAssetBuildSettingsProvider.Shared.BuildIndexJsonPath;
        string projectDir = Path.GetDirectoryName(projectPath);
        FileHelper.EnsureDirectory(projectDir);
        SerializationUtility.WriteToFile(projectPath, buildIndexData);

        Debug.Log($"[TaskExportLocalBuildData] BuildIndex 已写入: {BuildIndexStreamingPath}");
        Debug.Log($"[TaskExportLocalBuildData] BuildIndex 副本已写入: {projectPath}");
        Debug.Log($"[TaskExportLocalBuildData] Info - GUID: {buildIndexData.BuildGUID}, Ver: {request.Version.GetVersionString()}, Backend: {buildIndexData.BackendMode}");
    }

    /// <summary>
    /// 导出当前整包 baseline 到 StreamingAssets。
    /// </summary>
    private static void ExportBaselinePackage(BuildPackageRequest request)
    {
        if (request.BackendMode == BackendMode.ABManifest)
        {
            ExportABBaselinePackage(request);
            CleanAABaselineFiles();
            return;
        }

        Debug.Log("[TaskExportLocalBuildData] AA baseline 由 player build 既有流程处理，跳过额外复制。");
        CleanABManifest();
    }

    private static void ExportABBaselinePackage(BuildPackageRequest request)
    {
        Debug.Log("[TaskExportLocalBuildData] 正在复制 AB baseline package...");

        CopyPackageFileIfExists(request.OutputDir, Application.streamingAssetsPath, FYAssetSettings.MANIFEST_FILE_NAME);
        CopyPackageFileIfExists(request.OutputDir, Application.streamingAssetsPath, FYAssetSettings.MANIFEST_FILE_NAME_BIN);
        CopyPackageBundles(request);

        Debug.Log($"[TaskExportLocalBuildData] AB baseline 已复制: {request.OutputDir} -> {Application.streamingAssetsPath}");
    }

    private static void CopyPackageFileIfExists(string sourceDir, string targetDir, string fileName)
    {
        string sourcePath = Path.Combine(sourceDir, fileName);
        string targetPath = Path.Combine(targetDir, fileName);
        if (FileHelper.Exists(sourcePath))
        {
            FileHelper.CopyFile(sourcePath, targetPath, true);
            return;
        }

        FileHelper.TryDelete(targetPath);
    }

    private static void CopyPackageBundles(BuildPackageRequest request)
    {
        string targetBundlesDir = Path.Combine(Application.streamingAssetsPath, BundlesDirectoryName);
        if (FileHelper.DirectoryExists(targetBundlesDir))
            FileHelper.TryDeleteDirectory(targetBundlesDir, true);

        if (!FileHelper.DirectoryExists(request.BundlesDir))
            return;

        FileHelper.EnsureDirectory(targetBundlesDir);
        string[] bundleFiles = FileHelper.GetFiles(request.BundlesDir, "*", SearchOption.AllDirectories);
        for (int i = 0; i < bundleFiles.Length; i++)
        {
            string relativePath = bundleFiles[i].Substring(request.BundlesDir.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string targetPath = Path.Combine(targetBundlesDir, relativePath);
            FileHelper.CopyFile(bundleFiles[i], targetPath, true);
        }
    }

    private static void CleanAABaselineFiles()
    {
        FileHelper.TryDelete(Path.Combine(Application.streamingAssetsPath, AddressablesCatalogFileName));
        FileHelper.TryDelete(Path.Combine(Application.streamingAssetsPath, FYAssetSettings.AA_MANIFEST_FILE_NAME));
        FileHelper.TryDelete(Path.Combine(Application.streamingAssetsPath, FYAssetSettings.AA_MANIFEST_FILE_NAME_BIN));
    }

    private static void CleanABManifest()
    {
        bool cleaned = false;
        if (FileHelper.TryDelete(Path.Combine(Application.streamingAssetsPath, FYAssetSettings.MANIFEST_FILE_NAME)))
            cleaned = true;

        if (FileHelper.TryDelete(Path.Combine(Application.streamingAssetsPath, FYAssetSettings.MANIFEST_FILE_NAME_BIN)))
            cleaned = true;

        if (cleaned)
            Debug.Log("[TaskExportLocalBuildData] 已清理旧的 ABManifest 文件");
    }
}
