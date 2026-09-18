#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;

/// <summary>把完整内容解析结果写入目标服务器，负责暂存、幂等、回滚和最后写入 PackageIndex。</summary>
internal static class PackageUploader
{
    public static PublishResult Upload(
        PublishRequest request,
        string backendRoot,
        string publicUrl,
        PackageContentResolution resolution)
    {
        string identityError = string.Empty;
        if (request == null || resolution == null || !request.TryResolveIdentity(out PackageBuildIdentity identity, out identityError))
            return Fail(identityError ?? "发布输入无效。", backendRoot, resolution);
        if (!PublishPathGuard.IsSafeSegment(request.PackagesFolderName))
            return Fail($"包集合名不是合法单段目录名: '{request.PackagesFolderName}'", backendRoot, resolution);

        string root = FYAssetPathUtility.NormalizePath(Path.GetFullPath(backendRoot ?? string.Empty));
        string packagesRoot = FYAssetPathUtility.JoinFilePath(root, request.PackagesFolderName);
        string targetDir = FYAssetPathUtility.JoinFilePath(packagesRoot, identity.PackageName);
        string indexPath = FYAssetPathUtility.JoinFilePath(root, FYAssetSettings.PACKAGE_INDEX_FILE_NAME);
        string workRoot = FYAssetPathUtility.JoinFilePath(root, "_temp", identity.PackageName);
        string stagedDir = FYAssetPathUtility.JoinFilePath(workRoot, "staged");
        string backupDir = FYAssetPathUtility.JoinFilePath(workRoot, "backup");
        string backupIndex = FYAssetPathUtility.JoinFilePath(workRoot, "PackageIndex.previous.json");

        string reparseError = string.Empty;
        if (!PublishPathGuard.IsContainedIn(root, packagesRoot)
            || !PublishPathGuard.IsContainedIn(root, targetDir)
            || !PublishPathGuard.IsContainedIn(root, workRoot)
            || !PublishPathGuard.HasNoReparsePoint(root, out reparseError))
            return Fail(string.IsNullOrEmpty(reparseError) ? $"发布路径越出服务器根: {root}" : reparseError, root, resolution);
        if (FileHelper.DirectoryExists(workRoot))
            return Fail($"发布临时目录已存在，拒绝覆盖: {workRoot}", root, resolution);

        bool targetExisted = FileHelper.DirectoryExists(targetDir);
        bool targetBackedUp = false;
        bool targetReplaced = false;
        bool indexExisted = FileHelper.Exists(indexPath);
        bool indexWritten = false;
        bool indexBackedUp = false;
        try
        {
            FileHelper.EnsureDirectory(stagedDir);
            for (int i = 0; i < resolution.Files.Count; i++)
            {
                FileHelper.FileDigest expected = resolution.Files[i];
                if (!resolution.Sources.TryGetValue(expected.Name, out string source) || string.IsNullOrEmpty(source))
                    throw new InvalidOperationException($"来源不足: 目标文件没有可用字节来源: {expected.Name}");
                string destination = FYAssetPathUtility.JoinFilePath(stagedDir, expected.Name);
                FileHelper.CopyFile(source, destination, true);
                if (!FileHelper.TryCreateDigest(destination, expected.Name, out FileHelper.FileDigest copied)
                    || !copied.Matches(expected))
                    throw new IOException($"隔离目录文件摘要与目标包声明不一致: {expected.Name}（来源={source}）");
            }

            VerifyStaged(request, stagedDir, resolution.Files);
            if (targetExisted)
            {
                PackageIndex current = TryReadIndex(indexPath, out _);
                if (current != null && string.Equals(current.LatestPackage, identity.PackageName, StringComparison.Ordinal))
                {
                    if (ContentMatches(targetDir, resolution.Files))
                    {
                        return Success(publicUrl, targetDir, resolution, "Idempotent");
                    }
                    throw new InvalidOperationException(
                        $"拒绝覆盖当前 PackageIndex 指向的包目录（已发布内容不可变）: {targetDir}");
                }

                FileHelper.EnsureDirectory(backupDir);
                MoveDirectory(targetDir, FYAssetPathUtility.JoinFilePath(backupDir, identity.PackageName));
                targetBackedUp = true;
            }

            MoveDirectory(stagedDir, targetDir);
            targetReplaced = true;

            if (indexExisted)
            {
                FileHelper.CopyFile(indexPath, backupIndex, true);
                indexBackedUp = true;
            }
            var index = new PackageIndex
            {
                LatestPackage = identity.PackageName,
                LatestVersion = identity.Version,
                BackendMode = string.IsNullOrEmpty(identity.BackendId) ? request.BackendKey : identity.BackendId
            };
            FileHelper.WriteAllTextAtomic(indexPath, SerializationUtility.SerializeToJson(index, true));
            indexWritten = true;
            return Success(publicUrl, targetDir, resolution, resolution.IsFullUpload ? "Full" : "Incremental");
        }
        catch (Exception ex)
        {
            try
            {
                if (indexWritten)
                {
                    if (indexBackedUp && FileHelper.Exists(backupIndex))
                        FileHelper.CopyFile(backupIndex, indexPath, true);
                    else if (!indexExisted)
                        FileHelper.TryDelete(indexPath);
                }
                if (targetReplaced && FileHelper.DirectoryExists(targetDir))
                    FileHelper.TryDeleteDirectory(targetDir, true);
                if (targetBackedUp)
                {
                    string oldTarget = FYAssetPathUtility.JoinFilePath(backupDir, identity.PackageName);
                    if (FileHelper.DirectoryExists(oldTarget))
                        MoveDirectory(oldTarget, targetDir);
                }
            }
            catch (Exception rollbackError)
            {
                ex = new InvalidOperationException($"{ex.Message}; 回滚失败: {rollbackError.Message}", ex);
            }
            return Fail($"{ex.GetType().Name}: {ex.Message}", root, resolution);
        }
        finally
        {
            FileHelper.TryDeleteDirectory(workRoot, true);
        }
    }

    private static PublishResult Success(string publicUrl, string targetDir, PackageContentResolution resolution, string mode)
    {
        return new PublishResult
        {
            Success = true,
            TargetLocation = string.IsNullOrEmpty(publicUrl) ? targetDir : publicUrl,
            TransferMode = mode,
            UploadedCount = resolution.UploadCount,
            ReusedCount = resolution.ReuseCount
        };
    }

    private static PublishResult Fail(string error, string location, PackageContentResolution resolution)
    {
        return new PublishResult
        {
            Success = false,
            Error = error,
            TargetLocation = location ?? string.Empty,
            TransferMode = resolution != null && resolution.IsFullUpload ? "Full" : string.Empty
        };
    }

    private static void VerifyStaged(PublishRequest request, string stagedDir, IReadOnlyList<FileHelper.FileDigest> expectedFiles)
    {
        if (!PackageFileScanner.TryScan(stagedDir, out List<FileHelper.FileDigest> actual, out string scanError))
            throw new IOException($"隔离目录扫描失败: {scanError}");
        var expectedByName = FileHelper.IndexByName(expectedFiles);
        if (actual.Count != expectedByName.Count)
            throw new InvalidOperationException($"隔离目录文件数与目标内容不一致: 实际={actual.Count}, 目标={expectedByName.Count}");
        for (int i = 0; i < actual.Count; i++)
        {
            if (!expectedByName.TryGetValue(actual[i].Name, out FileHelper.FileDigest expected)
                || !actual[i].Matches(expected))
                throw new InvalidOperationException($"隔离目录文件内容与目标不一致: {actual[i].Name}");
        }
        if (!request.ManifestReader.TryReadContentDigests(stagedDir,
                out IReadOnlyList<FileHelper.FileDigest> declared, out string manifestError))
            throw new InvalidOperationException($"隔离目录清单不可解析: {manifestError}");
        string prefix = string.IsNullOrEmpty(request.ManifestReader.ContentDirectoryName)
            ? string.Empty
            : request.ManifestReader.ContentDirectoryName.Replace('\\', '/').TrimEnd('/') + "/";
        var declaredByName = FileHelper.IndexByName(declared);
        for (int i = 0; i < actual.Count; i++)
        {
            FileHelper.FileDigest file = actual[i];
            if (prefix.Length == 0 || !file.Name.StartsWith(prefix, StringComparison.Ordinal))
                continue;
            if (!declaredByName.TryGetValue(file.Name, out FileHelper.FileDigest declaredDigest)
                || !declaredDigest.Matches(file))
                throw new InvalidOperationException($"包内容文件与清单摘要不一致: {file.Name}");
        }
    }

    private static bool ContentMatches(string directory, IReadOnlyList<FileHelper.FileDigest> expected)
    {
        if (!PackageFileScanner.TryScan(directory, out List<FileHelper.FileDigest> actual, out _))
            return false;
        var byName = FileHelper.IndexByName(expected);
        if (actual.Count != byName.Count)
            return false;
        for (int i = 0; i < actual.Count; i++)
        {
            if (!byName.TryGetValue(actual[i].Name, out FileHelper.FileDigest digest) || !actual[i].Matches(digest))
                return false;
        }
        return true;
    }

    private static PackageIndex TryReadIndex(string path, out string error)
    {
        error = string.Empty;
        if (!FileHelper.Exists(path))
            return null;
        try
        {
            string json = FileHelper.ReadAllText(path);
            PackageIndex index = SerializationUtility.DeserializeJson<PackageIndex>(json);
            if (index == null || !VersionNumber.JsonHasObjectField(json, nameof(PackageIndex.LatestVersion)))
            {
                error = "PackageIndex 无效";
                return null;
            }
            return index;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return null;
        }
    }

    private static void MoveDirectory(string source, string target)
    {
        if (!FileHelper.DirectoryExists(source))
            throw new DirectoryNotFoundException($"待移动目录不存在: {source}");
        FileHelper.EnsureDirectory(Path.GetDirectoryName(target));
        if (FileHelper.DirectoryExists(target))
            FileHelper.TryDeleteDirectory(target, true);
        Directory.Move(source, target);
    }
}
#endif
