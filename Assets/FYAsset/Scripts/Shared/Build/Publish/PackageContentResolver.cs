#if UNITY_EDITOR
using System;
using System.Collections.Generic;

internal sealed class PackageContentResolution
{
    public readonly List<FileHelper.FileDigest> Files = new List<FileHelper.FileDigest>();
    public readonly Dictionary<string, string> Sources = new Dictionary<string, string>(StringComparer.Ordinal);
    public readonly List<FileHelper.FileDigest> ReusedByHash = new List<FileHelper.FileDigest>();
    public readonly List<string> Messages = new List<string>();
    public bool IsFullUpload;
    public string DegradeReason = string.Empty;
    public int UploadCount;
    public int ReuseCount;
}

/// <summary>将本地包、远端事实和基准 Full 合并为完整目标内容及其字节来源。</summary>
internal static class PackageContentResolver
{
    public static bool TryResolve(
        PublishRequest request,
        IReadOnlyList<FileHelper.FileDigest> localFiles,
        RemotePackageFacts remote,
        out PackageContentResolution resolution,
        out string error)
    {
        resolution = new PackageContentResolution();
        error = string.Empty;
        if (request == null || localFiles == null)
        {
            error = "发布内容输入为空。";
            return false;
        }

        var localByName = FileHelper.IndexByName(localFiles);
        for (int i = 0; i < localFiles.Count; i++)
        {
            FileHelper.FileDigest file = localFiles[i];
            if (string.IsNullOrEmpty(file.Name))
                continue;
            resolution.Files.Add(file);
            resolution.Sources[file.Name] = FYAssetPathUtility.JoinFilePath(request.SourcePackageDir, file.Name);
        }

        if (!IsHotfix(request.Identity))
        {
            resolution.Messages.Add($"目标包文件集合取自本地包目录: 文件={resolution.Files.Count}");
            return true;
        }
        if (request.ManifestReader == null)
        {
            error = "来源不足: 缺少后端清单读取器，无法确定 Hotfix 的目标内容集合。";
            return false;
        }
        if (!request.ManifestReader.TryReadContentDigests(request.SourcePackageDir,
                out IReadOnlyList<FileHelper.FileDigest> declared, out string manifestError))
        {
            error = $"来源不足: 本地 Hotfix 包清单不可读，无法确定目标内容集合 — {manifestError}";
            return false;
        }

        var remoteByName = FileHelper.IndexByName(remote?.Files);
        var remoteByHash = FileHelper.IndexByHash(remote?.Files);
        string baselineDir = string.Empty;
        string baselineError = string.Empty;
        string baselineFileError = string.Empty;
        bool baselineResolved = false;
        int declaredCount = 0;
        int localCount = resolution.Files.Count;
        int remoteCount = 0;
        int baselineCount = 0;
        if (declared != null)
        {
            for (int i = 0; i < declared.Count; i++)
            {
                FileHelper.FileDigest content = declared[i];
                if (string.IsNullOrEmpty(content.Name))
                    continue;
                declaredCount++;
                if (!content.IsComplete)
                {
                    error = $"来源不足: Hotfix 清单声明的内容摘要不完整: '{content.Name}'";
                    return false;
                }
                if (localByName.ContainsKey(content.Name))
                    continue;
                if (TryFindRemoteSource(content, remoteByName, remoteByHash,
                        remote?.PackageDir, out string remotePath))
                {
                    resolution.Files.Add(content);
                    resolution.Sources[content.Name] = remotePath;
                    remoteCount++;
                    continue;
                }

                if (!baselineResolved)
                {
                    baselineResolved = true;
                    baselineDir = ResolveBaselineDir(request, out baselineError);
                }
                if (!string.IsNullOrEmpty(baselineDir)
                    && TryFindBaselineSource(baselineDir, content, out string baselinePath, out baselineFileError))
                {
                    resolution.Files.Add(content);
                    resolution.Sources[content.Name] = baselinePath;
                    baselineCount++;
                    continue;
                }

                string baselineFact = !string.IsNullOrEmpty(baselineDir)
                    ? $"基准 Full 包内该内容不可用: {baselineFileError}（基准={baselineDir}）"
                    : $"基准 Full 包不可用: {baselineError}";
                error = "来源不足: Hotfix 清单声明的内容在本地包目录缺失，且无法从服务器当前包或基准 Full 补齐: "
                        + $"{content.Name} — {baselineFact}";
                return false;
            }
        }

        resolution.IsFullUpload = remote == null || !remote.IsUsable;
        resolution.DegradeReason = resolution.IsFullUpload
            ? $"服务器 PackageIndex 不可用: {remote?.Error ?? "无远端事实"}"
            : string.Empty;
        resolution.Messages.Add($"Hotfix 目标内容集合: 清单声明={declaredCount}, 本地包目录={localCount}, 服务器复用={remoteCount}, 基准 Full 补齐={baselineCount}");

        var remoteByNameForDiff = FileHelper.IndexByName(remote?.Files);
        int unchanged = 0;
        int changed = 0;
        for (int i = 0; i < resolution.Files.Count; i++)
        {
            FileHelper.FileDigest file = resolution.Files[i];
            if (remoteByNameForDiff.TryGetValue(file.Name, out FileHelper.FileDigest old)
                && old.Matches(file))
                unchanged++;
            else
                changed++;
        }
        resolution.ReuseCount = unchanged + resolution.ReusedByHash.Count;
        resolution.UploadCount = changed;
        return true;
    }

    private static bool IsHotfix(PackageBuildIdentity identity) =>
        identity != null && string.Equals(identity.BuildType, "Hotfix", StringComparison.OrdinalIgnoreCase);

    private static bool SameContent(in FileHelper.FileDigest left, in FileHelper.FileDigest right) =>
        string.Equals(left.Hash, right.Hash, StringComparison.Ordinal)
        && left.CRC == right.CRC && left.Size == right.Size;

    private static bool TryFindRemoteSource(
        in FileHelper.FileDigest content,
        Dictionary<string, FileHelper.FileDigest> byName,
        Dictionary<string, FileHelper.FileDigest> byHash,
        string packageDir,
        out string source)
    {
        source = string.Empty;
        if (string.IsNullOrEmpty(packageDir) || !FileHelper.DirectoryExists(packageDir))
            return false;
        if (byName.TryGetValue(content.Name, out FileHelper.FileDigest sameName) && SameContent(sameName, content))
        {
            string candidate = FYAssetPathUtility.JoinFilePath(packageDir, sameName.Name);
            if (FileHelper.TryCreateDigest(candidate, content.Name, out FileHelper.FileDigest digest) && SameContent(digest, content))
            {
                source = candidate;
                return true;
            }
        }
        if (byHash.TryGetValue(FileHelper.HashKey(content.Hash, content.Size), out FileHelper.FileDigest sameHash))
        {
            string candidate = FYAssetPathUtility.JoinFilePath(packageDir, sameHash.Name);
            if (FileHelper.TryCreateDigest(candidate, sameHash.Name, out FileHelper.FileDigest digest) && SameContent(digest, content))
            {
                source = candidate;
                return true;
            }
        }
        return false;
    }

    private static bool TryFindBaselineSource(string baselineDir, in FileHelper.FileDigest content, out string source, out string error)
    {
        source = string.Empty;
        error = string.Empty;
        string candidate = FYAssetPathUtility.JoinFilePath(baselineDir, content.Name);
        if (!FileHelper.Exists(candidate))
        {
            error = "包内没有同名文件";
            return false;
        }
        if (!FileHelper.TryCreateDigest(candidate, content.Name, out FileHelper.FileDigest digest))
        {
            error = "文件不可读";
            return false;
        }
        if (!SameContent(digest, content))
        {
            error = "文件与清单声明的 Hash/CRC/Size 不一致";
            return false;
        }
        source = candidate;
        return true;
    }

    private static string ResolveBaselineDir(PublishRequest request, out string error)
    {
        error = string.Empty;
        IFullPackageBaselineSource source = request.FullPackageBaselineSource;
        if (source == null)
        {
            error = "本地没有可用的基准 Full 解析入口（Summary 事实不可读）";
            return string.Empty;
        }
        if (!source.TryResolveBaselinePackageDir(request, out string packageDir, out string resolveError))
        {
            error = string.IsNullOrEmpty(resolveError) ? "基准 Full 解析失败" : resolveError;
            return string.Empty;
        }
        if (string.IsNullOrEmpty(packageDir) || !FileHelper.DirectoryExists(packageDir))
        {
            error = $"基准 Full 包目录不存在: {packageDir}";
            return string.Empty;
        }
        return packageDir;
    }
}
#endif
