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

        try
        {
            FileHelper.EnsureDirectory(publishRoot);
            PublishPackage(payload.ToCommit.PackageRootDir, packageRoot);
            WritePackageIndex(publishRoot, payload.ToCommit);

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

    private static void PublishPackage(string sourceDir, string targetDir)
    {
        if (FYAssetPathUtility.AreSamePath(sourceDir, targetDir))
        {
            Debug.Log($"[LocalDirectoryPushTarget] Source package already lives at publish target: {targetDir}");
            return;
        }

        if (FileHelper.DirectoryExists(targetDir))
            FileHelper.TryDeleteDirectory(targetDir, true);
        CopyDirectory(sourceDir, targetDir);
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
        SerializationUtility.WriteToFile(path, packageIndex);
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
#endif
