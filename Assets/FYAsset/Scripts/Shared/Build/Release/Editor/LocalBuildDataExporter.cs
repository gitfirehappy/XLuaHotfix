using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 本地启动数据导出器 —— 把 BuildIndex 与清单/内容落到 StreamingAssets，供 Player 启动读取。
/// 不是构建 Task：产物提升到正式出口之后，由 BuildProjectRunner 的交付事务调用本类。
/// </summary>
/// <remarks>
/// 生命周期：Full / Standalone 导出，Hotfix 不导出（Hotfix 不覆盖安装包的 BuildIndex）。
/// BeginDelivery 会把被覆盖的目标备份到工作目录，调用方必须在事务边界 Commit 或 Rollback；
/// 只有导出成功且外部事务提交后才释放备份区。
/// </remarks>
public static class LocalBuildDataExporter
{
    private const string LogPrefix = "[LocalBuildDataExporter]";
    private const string BuildIndexFileName = FYAssetSettings.BUILD_INDEX_FILENAME;

    // internal：供同程序集的交付 token 构造参数字段使用；对外不暴露写入入口。
    internal sealed class BackupEntry
    {
        public string TargetPath;
        public string BackupPath;
        public bool IsDirectory;
        public bool Existed;
    }

    private static string BuildIndexStreamingPath => FYAssetPathUtility.JoinFilePath(Application.streamingAssetsPath, BuildIndexFileName);

    /// <summary>
    /// 生成本次构建的 BuildIndex 数据。构建阶段与导出阶段共用同一口径，
    /// 保证包目录内的 BuildIndex 与 StreamingAssets 内的 BuildIndex 完全一致。
    /// </summary>
    /// <remarks>
    /// RuntimeMode 由构建类型推导并写进 BuildIndex，运行时只读该字段判断 Online/Standalone。
    /// </remarks>
    public static BuildIndexData CreateBuildIndexData(BuildRequest request)
    {
        string buildTime = request.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss");
        return new BuildIndexData
        {
            BuildGUID = request.PackageName,
            BuildTime = buildTime,
            IsDebug = EditorUserBuildSettings.development,
            Platform = EditorUserBuildSettings.activeBuildTarget.ToString(),
            BackendMode = request.BackendKey,
            Version = request.Version,
            RuntimeMode = CompleteBuildSummary.ResolveRuntimeMode(request.BuildType)
        };
    }

    /// <summary>
    /// 一次已应用的本地数据导出。rollback 依据与 write 同调用的 backup；
    /// 持有者必须在事务边界调用 Commit 或 Rollback，否则备份永不清除。
    /// </summary>
    public sealed class LocalBuildDataDelivery
    {
        private readonly string _workRoot;
        private readonly List<BackupEntry> _backups;
        private bool _settled;

        // 仅同程序集可构造：备份状态不允许外部组装。
        internal LocalBuildDataDelivery(string workRoot, List<BackupEntry> backups)
        {
            _workRoot = workRoot;
            _backups = backups;
        }

        /// <summary>导出完成：释放备份区，不可逆。</summary>
        public void Commit()
        {
            if (_settled)
                return;
            _settled = true;
            FileHelper.TryDeleteDirectory(_workRoot, true);
        }

        /// <summary>交付补偿：把本地数据恢复回导出前状态并释放备份区。</summary>
        public void Rollback()
        {
            if (_settled)
                return;
            _settled = true;
            RestoreBackups(_backups);
            AssetDatabase.Refresh();
            FileHelper.TryDeleteDirectory(_workRoot, true);
        }
    }

    /// <summary>导出启动期所需的本地构建数据，并立即结算。</summary>
    public static void Publish(BuildRequest request, IBuiltInPackageHandler builtInHandler)
    {
        LocalBuildDataDelivery delivery = BeginDelivery(request, builtInHandler);
        delivery?.Commit();
    }

    /// <summary>
    /// 供交付事务使用：导出本地数据但保留备份，直到事务整体 Commit 或 Rollback。
    /// Hotfix / 非 Full·Standalone 请求返回 null；调用方不可省略 Release。
    /// </summary>
    public static LocalBuildDataDelivery BeginDelivery(BuildRequest request, IBuiltInPackageHandler builtInHandler)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));
        if (request.BuildType != BuildType.Full && request.BuildType != BuildType.Standalone)
        {
            Debug.Log($"{LogPrefix} Hotfix build 不导出本地启动数据，BeginDelivery 返回 null。");
            return null;
        }
        if (request.BuildType == BuildType.Full && builtInHandler == null)
            throw new ArgumentNullException(nameof(builtInHandler), "Full 构建导出本地启动数据需要后端注入 IBuiltInPackageHandler。");
        if (!FileHelper.DirectoryExists(request.TemporaryOutputDir))
            throw new DirectoryNotFoundException($"本地构建数据导出前最终输出目录不存在: {request.TemporaryOutputDir}");

        return ExportData(request, builtInHandler);
    }

    private static LocalBuildDataDelivery ExportData(BuildRequest request, IBuiltInPackageHandler builtInHandler)
    {
        Debug.Log($"{LogPrefix} 开始导出本地启动数据到 StreamingAssets...");

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

            // Standalone 包已由构建 Task 直接写入最终目录，这里只写 BuildIndex。
            if (request.BuildType == BuildType.Standalone)
            {
                BackupFile(BuildIndexStreamingPath, backupRoot, "StreamingAssets/" + BuildIndexFileName, backups);
                BackupFile(FYAssetSettings.Instance.BuildIndexJsonPath, backupRoot, "ProjectBuildIndex/" + BuildIndexFileName, backups);
                ApplyFileOrDelete(stageRoot, Application.streamingAssetsPath, BuildIndexFileName);
                string projectPath = FYAssetSettings.Instance.BuildIndexJsonPath;
                FileHelper.CopyFile(FYAssetPathUtility.JoinFilePath(stageRoot, BuildIndexFileName), projectPath, true);
                AssetDatabase.Refresh();
                Debug.Log($"{LogPrefix} Standalone BuildIndex 已写入: {BuildIndexStreamingPath}");
                LogBuildIndexInfo(buildIndexData, request);
                return new LocalBuildDataDelivery(workRoot, backups);
            }

            builtInHandler.StageBuiltInFiles(request, stageRoot);
            StagePackageBundles(request, stageRoot);
            ValidateStage(builtInHandler, stageRoot);

            BackupOwnedTargets(backupRoot, backups);
            ApplyStagedData(builtInHandler, stageRoot);

            AssetDatabase.Refresh();
            Debug.Log($"{LogPrefix} 本地启动数据导出完成。");
            LogBuildIndexInfo(buildIndexData, request);
            return new LocalBuildDataDelivery(workRoot, backups);
        }
        catch
        {
            // 本层自己出错 = 尚未交付，立即回滚并释放备份区，不需要等外部 rollback。
            RestoreBackups(backups);
            AssetDatabase.Refresh();
            FileHelper.TryDeleteDirectory(workRoot, true);
            throw;
        }
    }

    private static void LogBuildIndexInfo(BuildIndexData buildIndexData, BuildRequest request)
    {
        Debug.Log($"{LogPrefix} 信息 - GUID：{buildIndexData.BuildGUID}，Version：{request.Version.GetReleaseVersionString()}，Backend：{buildIndexData.BackendMode}");
    }

    private static void StageBuildIndex(string stageRoot, BuildIndexData buildIndexData)
    {
        string path = FYAssetPathUtility.JoinFilePath(stageRoot, BuildIndexFileName);
        FileHelper.WriteAllTextAtomic(path, SerializationUtility.SerializeToJson(buildIndexData, true));
    }

    private static void StagePackageBundles(BuildRequest request, string stageRoot)
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

    private static void ValidateStage(IBuiltInPackageHandler handler, string stageRoot)
    {
        string buildIndexPath = FYAssetPathUtility.JoinFilePath(stageRoot, BuildIndexFileName);
        if (!FileHelper.Exists(buildIndexPath))
            throw new FileNotFoundException($"Staged BuildIndex missing: {buildIndexPath}", buildIndexPath);

        IReadOnlyList<BundleDownloadItem> bundles = handler.LoadStagedBundles(stageRoot);
        ValidateStagedBundles(stageRoot, bundles);
    }

    private static void ValidateStagedBundles(string stageRoot, IReadOnlyList<BundleDownloadItem> bundles)
    {
        string bundleRoot = FYAssetPathUtility.JoinFilePath(stageRoot, FYAssetSettings.BUNDLES_DIRECTORY_NAME);
        if (!HotfixPackageValidator.TryValidateBundleFiles(bundleRoot, bundles, out string error))
            throw new IOException($"Staged bundles are invalid: {error}");
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

    private static void ApplyStagedData(IBuiltInPackageHandler handler, string stageRoot)
    {
        ApplyFileOrDelete(stageRoot, Application.streamingAssetsPath, BuildIndexFileName);

        handler.ApplyStagedBuiltIn(stageRoot);
        ApplyBundles(stageRoot);

        string projectPath = FYAssetSettings.Instance.BuildIndexJsonPath;
        FileHelper.CopyFile(FYAssetPathUtility.JoinFilePath(stageRoot, BuildIndexFileName), projectPath, true);
        Debug.Log($"{LogPrefix} BuildIndex 已写入: {BuildIndexStreamingPath}");
        Debug.Log($"{LogPrefix} BuildIndex 副本已写入: {projectPath}");
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
                Debug.LogWarning($"{LogPrefix} 恢复失败：{entry.TargetPath}, {ex.Message}");
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
}
