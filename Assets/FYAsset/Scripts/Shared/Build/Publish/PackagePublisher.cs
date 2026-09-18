#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>统一发布入口：目标解析 → 本地扫描 → 远端读取 → 内容解析 → 上传。</summary>
public static class PackagePublisher
{
    private const string LogPrefix = "[PackagePublisher]";

    public static PublishResult Publish(PublishRequest request, IPublishTarget target)
    {
        if (target == null)
            return Fail("发布目标为空。", string.Empty);
        if (request == null)
            return Fail("发布请求为空。", string.Empty);
        if (!request.Validate(out string validationError))
            return Fail(validationError, string.Empty);
        if (!request.TryResolveIdentity(out PackageBuildIdentity identity, out string identityError))
            return Fail(identityError, string.Empty);

        string backendRoot;
        try
        {
            backendRoot = target.ResolveBackendRoot(request.BackendKey);
        }
        catch (Exception ex)
        {
            return Fail($"服务器目录解析失败: {ex.Message}", string.Empty);
        }
        if (string.IsNullOrWhiteSpace(backendRoot))
            return Fail("无法解析服务器后端根目录。", string.Empty);
        if (!target.TryGetHotfixUrl(request.BackendKey, out string publicUrl, out string urlError))
            return Fail($"公开地址解析失败: {urlError}", backendRoot);

        if (!PackageFileScanner.TryScan(request.SourcePackageDir,
                out List<FileHelper.FileDigest> localFiles, out string scanError))
            return Fail($"本地包目录扫描失败: {scanError}", backendRoot);
        if (!EnsureRequiredFiles(request, localFiles, out string requiredError))
            return Fail(requiredError, backendRoot);

        RemotePackageFacts remote = PackageRemoteReader.Read(request, backendRoot);
        if (!PackageContentResolver.TryResolve(request, localFiles, remote,
                out PackageContentResolution resolution, out string resolutionError))
            return Fail(resolutionError, backendRoot, remote == null || !remote.IsUsable ? "Full" : string.Empty);

        resolution.IsFullUpload = remote == null || !remote.IsUsable;
        resolution.DegradeReason = resolution.IsFullUpload ? remote?.Error ?? "服务器事实不可用" : string.Empty;
        if (!resolution.IsFullUpload)
            ComputeTransferCounts(resolution, remote.Files);
        else
        {
            resolution.UploadCount = resolution.Files.Count;
            resolution.ReuseCount = 0;
        }

        PublishResult result = PackageUploader.Upload(request, backendRoot, publicUrl, resolution);
        if (result.Success)
            Debug.Log($"{LogPrefix} 发布完成: Package={identity.PackageName}, Target={result.TargetLocation}, 上传={result.UploadedCount}, 复用={result.ReusedCount}, 模式={result.TransferMode}");
        else
            Debug.LogError($"{LogPrefix} 发布失败: {result.Error}");
        return result;
    }

    private static bool EnsureRequiredFiles(PublishRequest request, IReadOnlyList<FileHelper.FileDigest> files, out string error)
    {
        error = string.Empty;
        IReadOnlyList<string> required = request.ManifestReader.RequiredPackageFileNames;
        if (required == null || required.Count == 0)
        {
            error = "后端未声明包清单文件名，无法校验包头完整性。";
            return false;
        }
        var names = FileHelper.IndexByName(files);
        for (int i = 0; i < required.Count; i++)
        {
            string name = required[i];
            if (!string.IsNullOrEmpty(name) && !names.ContainsKey(name.Replace('\\', '/')))
            {
                error = $"本地包目录缺少清单文件: {name}";
                return false;
            }
        }
        return true;
    }

    private static void ComputeTransferCounts(PackageContentResolution resolution, IReadOnlyList<FileHelper.FileDigest> remoteFiles)
    {
        var remoteByName = FileHelper.IndexByName(remoteFiles);
        var remoteByHash = FileHelper.IndexByHash(remoteFiles);
        resolution.UploadCount = 0;
        resolution.ReuseCount = 0;
        resolution.ReusedByHash.Clear();
        for (int i = 0; i < resolution.Files.Count; i++)
        {
            FileHelper.FileDigest file = resolution.Files[i];
            if (remoteByName.TryGetValue(file.Name, out FileHelper.FileDigest sameName) && sameName.Matches(file))
            {
                resolution.ReuseCount++;
                continue;
            }
            if (remoteByHash.TryGetValue(FileHelper.HashKey(file.Hash, file.Size), out FileHelper.FileDigest sameHash)
                && sameHash.Matches(file))
            {
                resolution.ReuseCount++;
                resolution.ReusedByHash.Add(file);
                continue;
            }
            resolution.UploadCount++;
        }
    }

    private static PublishResult Fail(string error, string location, string mode = null)
    {
        Debug.LogError($"{LogPrefix} {error}");
        return new PublishResult
        {
            Success = false,
            Error = error,
            TargetLocation = location ?? string.Empty,
            TransferMode = mode ?? string.Empty
        };
    }
}
#endif
