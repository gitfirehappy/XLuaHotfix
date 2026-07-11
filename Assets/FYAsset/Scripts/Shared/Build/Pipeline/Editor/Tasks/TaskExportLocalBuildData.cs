using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 本地启动数据导出 Task — 仅整包构建导出 BuildIndex 与 baseline manifest 到 StreamingAssets。
/// 挂在 AA/AB Task 列表尾部；正式构建会延迟到 Repository commit 成功后发布，Hotfix 构建保持跳过。
/// </summary>
public class TaskExportLocalBuildData : IBuildTask
{
    private const string BuildIndexFileName = FYAssetSettings.BUILD_INDEX_FILENAME;

    private sealed class BackupEntry
    {
        public string TargetPath;
        public string BackupPath;
        public bool IsDirectory;
        public bool Existed;
    }

    public string TaskName => "TaskExportLocalBuildData";
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

        if (ctx.Get<bool>(BuildContextKeys.DeferPackagePublication))
        {
            return BuildTaskResult.Ok(new List<string>
            {
                "[LOCAL BUILD DATA] Deferred until repository commit"
            });
        }

        Publish(request);

        return BuildTaskResult.Ok(new List<string>
        {
            $"[LOCAL BUILD DATA] Version: {request.Version.GetReleaseVersionString()}"
        });
    }

    private static string BuildIndexStreamingPath => FYAssetPathUtility.JoinFilePath(Application.streamingAssetsPath, BuildIndexFileName);

    /// <summary>
    /// 导出启动期所需的本地构建数据。
    /// </summary>
    public static void Publish(BuildPackageRequest request)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));
        if (request.BuildType != BuildType.Full)
        {
            Debug.Log("[TaskExportLocalBuildData] Hotfix build 不导出本地启动数据。");
            return;
        }
        if (!FileHelper.DirectoryExists(request.OutputDir))
            throw new DirectoryNotFoundException($"本地构建数据导出前最终输出目录不存在: {request.OutputDir}");

        ExportData(request);
    }

    private static void ExportData(BuildPackageRequest request)
    {
        Debug.Log("[TaskExportLocalBuildData] 开始导出本地启动数据到 StreamingAssets...");

        FileHelper.EnsureDirectory(Application.streamingAssetsPath);
        string workRoot = FYAssetPathUtility.JoinFilePath(
            BuildPathManager.ProjectRoot,
            "Temp",
            "FYAssetLocalBuildData",
            Guid.NewGuid().ToString("N"));
        string stageRoot = FYAssetPathUtility.JoinFilePath(workRoot, "stage");
        string backupRoot = FYAssetPathUtility.JoinFilePath(workRoot, "backup");
        var backups = new List<BackupEntry>();

        try
        {
            BuildIndexData buildIndexData = CreateBuildIndexData(request);
            StageBuildIndex(stageRoot, buildIndexData);
            StageBaselinePackage(request, stageRoot);
            ValidateStage(request, stageRoot);

            BackupOwnedTargets(backupRoot, backups);
            ApplyStagedData(request, stageRoot);

            AssetDatabase.Refresh();
            Debug.Log("[TaskExportLocalBuildData] 本地启动数据导出完成。");
            Debug.Log($"[TaskExportLocalBuildData] Info - GUID: {buildIndexData.BuildGUID}, Ver: {request.Version.GetReleaseVersionString()}, Backend: {buildIndexData.BackendMode}");
        }
        catch
        {
            RestoreBackups(backups);
            AssetDatabase.Refresh();
            throw;
        }
        finally
        {
            FileHelper.TryDeleteDirectory(workRoot, true);
        }
    }

    private static BuildIndexData CreateBuildIndexData(BuildPackageRequest request)
    {
        string buildTime = request.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss");
        return new BuildIndexData
        {
            BuildGUID = request.PackageName,
            BuildTime = buildTime,
            IsDebug = EditorUserBuildSettings.development,
            Platform = EditorUserBuildSettings.activeBuildTarget.ToString(),
            BackendMode = BackendModeNames.FromBackendMode(request.BackendMode),
            Version = request.Version
        };
    }

    private static void StageBuildIndex(string stageRoot, BuildIndexData buildIndexData)
    {
        string path = FYAssetPathUtility.JoinFilePath(stageRoot, BuildIndexFileName);
        FileHelper.WriteAllTextAtomic(path, SerializationUtility.SerializeToJson(buildIndexData, true));
    }

    private static void StageBaselinePackage(BuildPackageRequest request, string stageRoot)
    {
        if (request.BackendMode == BackendMode.ABManifest)
        {
            Debug.Log("[TaskExportLocalBuildData] 正在暂存 AB baseline package...");
            StagePackageFileIfExists(request.OutputDir, stageRoot, FYAssetSettings.MANIFEST_FILE_NAME);
            StagePackageFileIfExists(request.OutputDir, stageRoot, FYAssetSettings.MANIFEST_FILE_NAME_BIN);
            StagePackageBundles(request, stageRoot);
            return;
        }

        Debug.Log("[TaskExportLocalBuildData] 正在暂存 AA baseline manifest...");
        StagePackageFileIfExists(request.OutputDir, stageRoot, FYAssetSettings.AA_MANIFEST_FILE_NAME);
        StagePackageFileIfExists(request.OutputDir, stageRoot, FYAssetSettings.AA_MANIFEST_FILE_NAME_BIN);
    }

    private static void StagePackageFileIfExists(string sourceDir, string stageRoot, string fileName)
    {
        string sourcePath = FYAssetPathUtility.JoinFilePath(sourceDir, fileName);
        string targetPath = FYAssetPathUtility.JoinFilePath(stageRoot, fileName);
        if (FileHelper.Exists(sourcePath))
            FileHelper.CopyFile(sourcePath, targetPath, true);
    }

    private static void StagePackageBundles(BuildPackageRequest request, string stageRoot)
    {
        if (!FileHelper.DirectoryExists(request.BundlesDir))
            return;

        string targetBundlesDir = FYAssetPathUtility.JoinFilePath(stageRoot, FYAssetSettings.BUNDLES_DIRECTORY_NAME);
        FileHelper.EnsureDirectory(targetBundlesDir);
        string[] bundleFiles = FileHelper.GetFiles(request.BundlesDir, "*", SearchOption.AllDirectories);
        for (int i = 0; i < bundleFiles.Length; i++)
        {
            string relativePath = FYAssetPathUtility.GetRelativeFilePath(request.BundlesDir, bundleFiles[i]);
            string targetPath = FYAssetPathUtility.JoinFilePath(targetBundlesDir, relativePath);
            FileHelper.CopyFile(bundleFiles[i], targetPath, true);
        }
    }

    private static void ValidateStage(BuildPackageRequest request, string stageRoot)
    {
        string buildIndexPath = FYAssetPathUtility.JoinFilePath(stageRoot, BuildIndexFileName);
        if (!FileHelper.Exists(buildIndexPath))
            throw new FileNotFoundException($"Staged BuildIndex missing: {buildIndexPath}", buildIndexPath);

        if (request.BackendMode == BackendMode.ABManifest)
        {
            string json = FYAssetPathUtility.JoinFilePath(stageRoot, FYAssetSettings.MANIFEST_FILE_NAME);
            string bin = FYAssetPathUtility.JoinFilePath(stageRoot, FYAssetSettings.MANIFEST_FILE_NAME_BIN);
            if (!FileHelper.Exists(json) && !FileHelper.Exists(bin))
                throw new FileNotFoundException($"Staged ABManifest missing: {json} or {bin}", json);

            int sourceBundleCount = CountFiles(request.BundlesDir);
            int stageBundleCount = CountFiles(FYAssetPathUtility.JoinFilePath(stageRoot, FYAssetSettings.BUNDLES_DIRECTORY_NAME));
            if (sourceBundleCount > 0 && stageBundleCount != sourceBundleCount)
                throw new IOException($"Staged AB bundles mismatch. Source={sourceBundleCount}, Stage={stageBundleCount}");
            return;
        }

        string aaJson = FYAssetPathUtility.JoinFilePath(stageRoot, FYAssetSettings.AA_MANIFEST_FILE_NAME);
        string aaBin = FYAssetPathUtility.JoinFilePath(stageRoot, FYAssetSettings.AA_MANIFEST_FILE_NAME_BIN);
        if (!FileHelper.Exists(aaJson) && !FileHelper.Exists(aaBin))
            throw new FileNotFoundException($"Staged AAManifest missing: {aaJson} or {aaBin}", aaJson);
    }

    private static void BackupOwnedTargets(string backupRoot, List<BackupEntry> backups)
    {
        BackupFile(BuildIndexStreamingPath, backupRoot, "StreamingAssets/" + BuildIndexFileName, backups);
        BackupFile(FYAssetSettings.Instance.BuildIndexJsonPath, backupRoot, "ProjectBuildIndex/" + BuildIndexFileName, backups);
        BackupFile(FYAssetPathUtility.JoinFilePath(Application.streamingAssetsPath, FYAssetSettings.ADDRESSABLES_CATALOG_FILE_NAME), backupRoot, "StreamingAssets/" + FYAssetSettings.ADDRESSABLES_CATALOG_FILE_NAME, backups);
        BackupFile(FYAssetPathUtility.JoinFilePath(Application.streamingAssetsPath, FYAssetSettings.AA_MANIFEST_FILE_NAME), backupRoot, "StreamingAssets/" + FYAssetSettings.AA_MANIFEST_FILE_NAME, backups);
        BackupFile(FYAssetPathUtility.JoinFilePath(Application.streamingAssetsPath, FYAssetSettings.AA_MANIFEST_FILE_NAME_BIN), backupRoot, "StreamingAssets/" + FYAssetSettings.AA_MANIFEST_FILE_NAME_BIN, backups);
        BackupFile(FYAssetPathUtility.JoinFilePath(Application.streamingAssetsPath, FYAssetSettings.MANIFEST_FILE_NAME), backupRoot, "StreamingAssets/" + FYAssetSettings.MANIFEST_FILE_NAME, backups);
        BackupFile(FYAssetPathUtility.JoinFilePath(Application.streamingAssetsPath, FYAssetSettings.MANIFEST_FILE_NAME_BIN), backupRoot, "StreamingAssets/" + FYAssetSettings.MANIFEST_FILE_NAME_BIN, backups);
        BackupDirectory(FYAssetPathUtility.JoinFilePath(Application.streamingAssetsPath, FYAssetSettings.BUNDLES_DIRECTORY_NAME), backupRoot, "StreamingAssets/" + FYAssetSettings.BUNDLES_DIRECTORY_NAME, backups);
    }

    private static void ApplyStagedData(BuildPackageRequest request, string stageRoot)
    {
        ApplyFileOrDelete(stageRoot, Application.streamingAssetsPath, BuildIndexFileName);

        if (request.BackendMode == BackendMode.ABManifest)
        {
            ApplyFileOrDelete(stageRoot, Application.streamingAssetsPath, FYAssetSettings.MANIFEST_FILE_NAME);
            ApplyFileOrDelete(stageRoot, Application.streamingAssetsPath, FYAssetSettings.MANIFEST_FILE_NAME_BIN);
            DeleteStreamingFile(FYAssetSettings.ADDRESSABLES_CATALOG_FILE_NAME);
            DeleteStreamingFile(FYAssetSettings.AA_MANIFEST_FILE_NAME);
            DeleteStreamingFile(FYAssetSettings.AA_MANIFEST_FILE_NAME_BIN);
            ApplyBundles(stageRoot);
        }
        else
        {
            ApplyFileOrDelete(stageRoot, Application.streamingAssetsPath, FYAssetSettings.AA_MANIFEST_FILE_NAME);
            ApplyFileOrDelete(stageRoot, Application.streamingAssetsPath, FYAssetSettings.AA_MANIFEST_FILE_NAME_BIN);
            DeleteStreamingFile(FYAssetSettings.MANIFEST_FILE_NAME);
            DeleteStreamingFile(FYAssetSettings.MANIFEST_FILE_NAME_BIN);
        }

        string projectPath = FYAssetSettings.Instance.BuildIndexJsonPath;
        FileHelper.CopyFile(FYAssetPathUtility.JoinFilePath(stageRoot, BuildIndexFileName), projectPath, true);
        Debug.Log($"[TaskExportLocalBuildData] BuildIndex 已写入: {BuildIndexStreamingPath}");
        Debug.Log($"[TaskExportLocalBuildData] BuildIndex 副本已写入: {projectPath}");
    }

    private static void ApplyFileOrDelete(string sourceDir, string targetDir, string fileName)
    {
        string sourcePath = FYAssetPathUtility.JoinFilePath(sourceDir, fileName);
        string targetPath = FYAssetPathUtility.JoinFilePath(targetDir, fileName);
        if (FileHelper.Exists(sourcePath))
        {
            FileHelper.CopyFile(sourcePath, targetPath, true);
            return;
        }

        FileHelper.TryDelete(targetPath);
    }

    private static void DeleteStreamingFile(string fileName)
    {
        FileHelper.TryDelete(FYAssetPathUtility.JoinFilePath(Application.streamingAssetsPath, fileName));
    }

    private static void ApplyBundles(string stageRoot)
    {
        string sourceBundlesDir = FYAssetPathUtility.JoinFilePath(stageRoot, FYAssetSettings.BUNDLES_DIRECTORY_NAME);
        string targetBundlesDir = FYAssetPathUtility.JoinFilePath(Application.streamingAssetsPath, FYAssetSettings.BUNDLES_DIRECTORY_NAME);
        FileHelper.TryDeleteDirectory(targetBundlesDir, true);
        if (FileHelper.DirectoryExists(sourceBundlesDir))
            CopyDirectory(sourceBundlesDir, targetBundlesDir);
    }

    private static void BackupFile(string targetPath, string backupRoot, string relativeBackupPath, List<BackupEntry> backups)
    {
        var entry = new BackupEntry
        {
            TargetPath = targetPath,
            BackupPath = FYAssetPathUtility.JoinFilePath(backupRoot, relativeBackupPath),
            IsDirectory = false,
            Existed = FileHelper.Exists(targetPath)
        };
        if (entry.Existed)
            FileHelper.CopyFile(targetPath, entry.BackupPath, true);
        backups.Add(entry);
    }

    private static void BackupDirectory(string targetPath, string backupRoot, string relativeBackupPath, List<BackupEntry> backups)
    {
        var entry = new BackupEntry
        {
            TargetPath = targetPath,
            BackupPath = FYAssetPathUtility.JoinFilePath(backupRoot, relativeBackupPath),
            IsDirectory = true,
            Existed = FileHelper.DirectoryExists(targetPath)
        };
        if (entry.Existed)
            CopyDirectory(targetPath, entry.BackupPath);
        backups.Add(entry);
    }

    private static void RestoreBackups(List<BackupEntry> backups)
    {
        for (int i = backups.Count - 1; i >= 0; i--)
        {
            BackupEntry entry = backups[i];
            try
            {
                if (entry.IsDirectory)
                {
                    FileHelper.TryDeleteDirectory(entry.TargetPath, true);
                    if (entry.Existed)
                        CopyDirectory(entry.BackupPath, entry.TargetPath);
                    continue;
                }

                if (entry.Existed)
                    FileHelper.CopyFile(entry.BackupPath, entry.TargetPath, true);
                else
                    FileHelper.TryDelete(entry.TargetPath);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[TaskExportLocalBuildData] Restore failed: {entry.TargetPath}, {ex.Message}");
            }
        }
    }

    private static void CopyDirectory(string sourceDir, string targetDir)
    {
        FileHelper.EnsureDirectory(targetDir);
        string[] files = FileHelper.GetFiles(sourceDir, "*", SearchOption.AllDirectories);
        for (int i = 0; i < files.Length; i++)
        {
            string relativePath = FYAssetPathUtility.GetRelativeFilePath(sourceDir, files[i]);
            string targetPath = FYAssetPathUtility.JoinFilePath(targetDir, relativePath);
            FileHelper.CopyFile(files[i], targetPath, true);
        }
    }

    private static int CountFiles(string dir)
    {
        return FileHelper.GetFiles(dir, "*", SearchOption.AllDirectories).Length;
    }
}
