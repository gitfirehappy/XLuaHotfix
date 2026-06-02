#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// 将 AB 产物推送到本地目录。
/// Push 只发布已经构建完成的包体目录，不重新解释包内 PackageIndex。
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

        try
        {
            if (FileHelper.DirectoryExists(packageDir))
                FileHelper.TryDeleteDirectory(packageDir, true);
            CopyDirectory(payload.ToCommit.PackageRootDir, packageDir);

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

    private static void CopyDirectory(string sourceDir, string targetDir)
    {
        FileHelper.EnsureDirectory(targetDir);

        string[] files = FileHelper.GetFiles(sourceDir, "*", SearchOption.AllDirectories);
        for (int i = 0; i < files.Length; i++)
        {
            string relativePath = files[i].Substring(sourceDir.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string targetPath = Path.Combine(targetDir, relativePath);
            FileHelper.CopyFile(files[i], targetPath, true);
        }
    }
}
#endif
