#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// 将构建产物推送到本地发布根。
/// Push 发布根包含 PackageIndex.json 和 Packages/{PackageName}。
/// </summary>
public sealed class LocalDirectoryPushTarget : IPushTarget
{
    private readonly string _id;
    private readonly string _path;

    public string Id => _id;

    public LocalDirectoryPushTarget(PushTargetConfig config)
    {
        if (config == null)
            throw new ArgumentNullException(nameof(config));
        _id = string.IsNullOrEmpty(config.Id) ? "local" : config.Id;
        _path = config.Path;
    }

    public PushReceipt Push(PushPayload payload)
    {
        if (payload == null)
            throw new ArgumentNullException(nameof(payload));
        if (payload.ToCommit == null)
            throw new ArgumentException("ToCommit 不能为空。", nameof(payload));
        if (string.IsNullOrEmpty(payload.ToCommit.PackageRootDir))
            return Fail("PackageRootDir is empty.");
        if (!FileHelper.DirectoryExists(payload.ToCommit.PackageRootDir))
            return Fail($"PackageRootDir missing: {payload.ToCommit.PackageRootDir}");
        string publishRoot = ResolvePublishRoot();
        string packageRoot = FYAssetPathUtility.JoinFilePath(
            publishRoot,
            FYAssetSettings.Instance.BuildPackagesFolderName,
            payload.ToCommit.PackageName);
        string stagingRoot = FYAssetPathUtility.JoinFilePath(
            publishRoot,
            ".fyasset_push_staging",
            payload.ToCommit.PackageName + "_" + Guid.NewGuid().ToString("N").Substring(0, 8));
        string backupRoot = FYAssetPathUtility.JoinFilePath(
            publishRoot,
            ".fyasset_push_backup",
            payload.ToCommit.PackageName + "_" + Guid.NewGuid().ToString("N").Substring(0, 8));
        bool packageAlreadyAtTarget = FYAssetPathUtility.AreSamePath(payload.ToCommit.PackageRootDir, packageRoot);

        try
        {
            FileHelper.EnsureDirectory(publishRoot);
            PublishPackage(payload.ToCommit.PackageRootDir, packageRoot, stagingRoot, backupRoot, payload.ToCommit);
            WritePackageIndex(publishRoot, payload.ToCommit);
            FileHelper.TryDeleteDirectory(stagingRoot, true);
            FileHelper.TryDeleteDirectory(backupRoot, true);

            return new PushReceipt
            {
                Success = true,
                TargetId = Id,
                TargetLocation = publishRoot,
                PushedAtUtc = DateTime.UtcNow.ToString("o")
            };
        }
        catch (Exception ex)
        {
            TryRestorePackage(packageRoot, backupRoot, !packageAlreadyAtTarget);
            FileHelper.TryDeleteDirectory(stagingRoot, true);
            Debug.LogError($"[LocalDirectoryPushTarget] Push failed: {ex}");
            return Fail(ex.Message);
        }
    }

    private PushReceipt Fail(string reason)
    {
        return new PushReceipt
        {
            Success = false,
            TargetId = Id,
            TargetLocation = ResolvePublishRoot(),
            PushedAtUtc = DateTime.UtcNow.ToString("o"),
            FailureReason = reason
        };
    }

    private string ResolvePublishRoot()
    {
        if (string.IsNullOrWhiteSpace(_path))
            return BuildPathManager.OutputRoot;

        return FYAssetPathUtility.ResolveFilePath(BuildPathManager.ProjectRoot, _path);
    }

    private static void PublishPackage(string sourceDir, string targetDir, string stagingRoot, string backupRoot, RepositoryCommit commit)
    {
        if (FYAssetPathUtility.AreSamePath(sourceDir, targetDir))
        {
            ValidatePackage(sourceDir, commit);
            Debug.Log($"[LocalDirectoryPushTarget] Source package already lives at publish target: {targetDir}");
            return;
        }

        string stagedPackage = FYAssetPathUtility.JoinFilePath(stagingRoot, commit.PackageName);
        CopyDirectory(sourceDir, stagedPackage);
        ValidatePackage(stagedPackage, commit);

        if (FileHelper.DirectoryExists(targetDir))
        {
            string backupPackage = FYAssetPathUtility.JoinFilePath(backupRoot, commit.PackageName);
            MoveDirectory(targetDir, backupPackage);
        }

        MoveDirectory(stagedPackage, targetDir);
    }

    private static void WritePackageIndex(string publishRoot, RepositoryCommit commit)
    {
        var packageIndex = new PackageIndex
        {
            LatestPackage = commit.PackageName,
            LatestVersion = commit.Version,
            BackendMode = commit.BackendMode
        };

        string path = FYAssetPathUtility.JoinFilePath(publishRoot, FYAssetSettings.PACKAGE_INDEX_FILE_NAME);
        FileHelper.WriteAllTextAtomic(path, SerializationUtility.SerializeToJson(packageIndex, true));
    }

    private static void ValidatePackage(string packageDir, RepositoryCommit commit)
    {
        if (!FileHelper.DirectoryExists(packageDir))
            throw new DirectoryNotFoundException($"Package directory missing: {packageDir}");

        bool isAB = commit != null && string.Equals(commit.BackendMode, BackendModeNames.AB, StringComparison.OrdinalIgnoreCase);
        string jsonName = isAB ? FYAssetSettings.MANIFEST_FILE_NAME : FYAssetSettings.AA_MANIFEST_FILE_NAME;
        string binName = isAB ? FYAssetSettings.MANIFEST_FILE_NAME_BIN : FYAssetSettings.AA_MANIFEST_FILE_NAME_BIN;
        string jsonPath = FYAssetPathUtility.JoinFilePath(packageDir, jsonName);
        string binPath = FYAssetPathUtility.JoinFilePath(packageDir, binName);
        if (!FileHelper.Exists(jsonPath) && !FileHelper.Exists(binPath))
            throw new FileNotFoundException($"Package manifest missing: {jsonName} or {binName}", jsonPath);
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

    private static void MoveDirectory(string sourceDir, string targetDir)
    {
        if (!FileHelper.DirectoryExists(sourceDir))
            return;

        string parent = Path.GetDirectoryName(targetDir);
        FileHelper.EnsureDirectory(parent);
        if (FileHelper.DirectoryExists(targetDir))
            FileHelper.TryDeleteDirectory(targetDir, true);
        Directory.Move(sourceDir, targetDir);
    }

    private static void TryRestorePackage(string targetDir, string backupRoot, bool allowDeleteWithoutBackup)
    {
        string[] backups = FileHelper.GetDirectories(backupRoot);
        if (backups.Length == 0)
        {
            if (allowDeleteWithoutBackup)
                FileHelper.TryDeleteDirectory(targetDir, true);
            return;
        }

        try
        {
            FileHelper.TryDeleteDirectory(targetDir, true);
            MoveDirectory(backups[0], targetDir);
            FileHelper.TryDeleteDirectory(backupRoot, true);
        }
        catch (Exception restoreEx)
        {
            Debug.LogWarning($"[LocalDirectoryPushTarget] Restore failed: {restoreEx.Message}");
        }
    }
}
#endif
