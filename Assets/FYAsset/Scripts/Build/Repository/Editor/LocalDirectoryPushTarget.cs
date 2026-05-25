#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// 将 AB 产物推送到本地目录。
/// 只写 delta bundles、ABManifest.json 和 PackageIndex.json。
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
        if (string.IsNullOrEmpty(_path))
            return Fail("Push target path is empty.");

        string packageDir = Path.Combine(_path, payload.ToCommit.PackageName);
        string bundleDir = Path.Combine(packageDir, "bundles");
        if (FileHelper.DirectoryExists(packageDir))
            FileHelper.TryDeleteDirectory(packageDir, true);
        FileHelper.EnsureDirectory(bundleDir);

        try
        {
            for (int i = 0; i < payload.DeltaBundleFiles.Count; i++)
            {
                string src = payload.DeltaBundleFiles[i];
                string dest = Path.Combine(bundleDir, Path.GetFileName(src));
                if (!FileHelper.Exists(src))
                    return Fail($"Delta bundle missing: {src}");
                FileHelper.CopyFile(src, dest, true);
            }

            if (!string.IsNullOrEmpty(payload.AbManifestPath))
            {
                string manifestDest = Path.Combine(packageDir, FYAssetSettings.MANIFEST_FILE_NAME);
                FileHelper.CopyFile(payload.AbManifestPath, manifestDest, true);
            }

            if (!string.IsNullOrEmpty(payload.PackageIndexPath) && FileHelper.Exists(payload.PackageIndexPath))
                FileHelper.CopyFile(payload.PackageIndexPath, Path.Combine(_path, FYAssetSettings.PACKAGE_INDEX_FILE_NAME), true);
            else if (!string.IsNullOrEmpty(payload.PackageIndexPath))
                return Fail($"PackageIndexPath missing: {payload.PackageIndexPath}");

            return new PushReceipt
            {
                Success = true,
                TargetId = Id,
                TargetLocation = _path,
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
            TargetLocation = _path ?? string.Empty,
            PushedAtUtc = DateTime.UtcNow.ToString("o"),
            FailureReason = reason
        };
    }
}
#endif
