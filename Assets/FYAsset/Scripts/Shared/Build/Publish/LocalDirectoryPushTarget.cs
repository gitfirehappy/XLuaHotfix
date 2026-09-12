#if UNITY_EDITOR
using System;
using System.IO;
using UnityEngine;

/// <summary>
/// 目录型发布目标：把包发布到 `{service root}/{AA|AB}` 目录镜像。
/// </summary>
/// <remarks>
/// 目录型目标提供完整发布事务所需的事实访问；Push 仅负责搬运已组装负载，不删除其他包目录。
/// </remarks>
public sealed class LocalDirectoryPushTarget : IDirectoryPushTarget
{
    private readonly PushTargetConfig _config;

    public string Id => _config.TargetId;

    public LocalDirectoryPushTarget(PushTargetConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
    }

    public string ResolveBackendRoot(string backendKey)
    {
        try
        {
            return _config.ResolveBackendRoot(backendKey);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[{nameof(LocalDirectoryPushTarget)}] 服务器目录解析失败：{ex.Message}");
            return null;
        }
    }

    public PushReceipt Push(PushPayload payload)
    {
        if (payload == null)
            throw new ArgumentNullException(nameof(payload));
        if (payload.Request == null)
            return Fail("PushPayload 缺少发布请求。", string.Empty);

        string backendKey = payload.Request.BackendKey;
        string backendRoot = ResolveBackendRoot(backendKey);
        if (string.IsNullOrEmpty(backendRoot))
            return Fail("无法解析服务器后端根目录。", string.Empty);

        if (!payload.Request.TryResolveIdentity(out PackageBuildIdentity identity, out string identityError))
            return Fail(identityError, backendRoot);

        if (string.IsNullOrEmpty(payload.StagedPackageDir) || !FileHelper.DirectoryExists(payload.StagedPackageDir))
            return Fail($"待上传包目录不存在: {payload.StagedPackageDir}", backendRoot);

        try
        {
            string packagesRoot = FYAssetPathUtility.JoinFilePath(backendRoot, payload.PackagesFolderName);
            string targetPackageDir = FYAssetPathUtility.JoinFilePath(packagesRoot, identity.PackageName);
            FileHelper.EnsureDirectory(packagesRoot);

            if (FileHelper.DirectoryExists(targetPackageDir))
                FileHelper.TryDeleteDirectory(targetPackageDir, true);
            FileHelper.EnsureDirectory(Path.GetDirectoryName(targetPackageDir));
            CopyDirectory(payload.StagedPackageDir, targetPackageDir);

            // PackageIndex 最后写入：内容就位之后才更新指针。
            if (!string.IsNullOrEmpty(payload.PackageIndexJson))
            {
                FileHelper.WriteAllTextAtomic(
                    FYAssetPathUtility.JoinFilePath(backendRoot, FYAssetSettings.PACKAGE_INDEX_FILE_NAME),
                    payload.PackageIndexJson);
            }

            return new PushReceipt
            {
                Success = true,
                TargetId = Id,
                TargetLocation = targetPackageDir,
                PushedAtUtc = DateTime.UtcNow.ToString("o")
            };
        }
        catch (Exception ex)
        {
            Debug.LogError($"[{nameof(LocalDirectoryPushTarget)}] Push 失败：{ex}");
            return Fail(ex.Message, backendRoot);
        }
    }

    private PushReceipt Fail(string reason, string location)
    {
        return new PushReceipt
        {
            Success = false,
            TargetId = Id,
            TargetLocation = location,
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
            string relativePath = FYAssetPathUtility.GetRelativeFilePath(sourceDir, files[i]);
            FileHelper.CopyFile(files[i], FYAssetPathUtility.JoinFilePath(targetDir, relativePath), true);
        }
    }
}
#endif
