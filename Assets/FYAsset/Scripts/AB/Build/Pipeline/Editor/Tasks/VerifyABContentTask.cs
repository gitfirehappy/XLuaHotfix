using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// 构建输出完整性校验 Task —— 以 ABManifest.AssetEntries + ContentEntries 与 BundleBuildResults 为数据源，
/// 校验 Address 唯一性、公共边界、资产到内容的成员关系、内容类型、依赖下标、文件集合与文件摘要。
/// Error → 构建中止；Warning → 继续执行（孤儿文件、异常大小等）。
/// 在 ExportABOutput 之前执行。
/// </summary>
public class VerifyABContentTask : IBuildTask
{
    public string TaskName => "VerifyABContent";
    private const long MinSizeBytes = 1024L;
    private const long MaxSizeBytes = 500_000_000L;

    /// <summary>UnityFS bundle 文件头魔数</summary>
    private static readonly byte[] UnityFSMagic = { 0x55, 0x6E, 0x69, 0x74, 0x79, 0x46, 0x53 }; // "UnityFS"

    public BuildTaskResult Execute(BuildContext ctx)
    {
        var cfg = ctx.Require<BuildConfig>(BuildContextKeys.BuildConfig);
        var manifest = ctx.Require<ABManifest>(ABBuildContextKeys.ABManifest);
        var buildResults = ctx.Require<List<BundleBuildInfo>>(ABBuildContextKeys.BundleBuildResults);
        string tempDir = FYAssetPathUtility.JoinFilePath(cfg.OutputRoot, "_temp");

        var issues = new List<VerificationIssue>();
        int errorCount = 0;
        int warningCount = 0;

        VerifyAddressAndPublicBoundary(manifest, issues, ref errorCount, ref warningCount);
        VerifyAssetContentMembership(manifest, buildResults, issues, ref errorCount, ref warningCount);
        VerifyDependencyIndices(manifest, buildResults, issues, ref errorCount, ref warningCount);

        var knownFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        VerifyContentFiles(manifest, tempDir, knownFiles, issues, ref errorCount, ref warningCount);
        VerifyFileSet(manifest, tempDir, knownFiles, buildResults, issues, ref errorCount, ref warningCount);

        var result = new BuildVerificationResult
        {
            Success = errorCount == 0,
            Issues = issues,
            ErrorCount = errorCount,
            WarningCount = warningCount
        };

        ctx.Set(BuildContextKeys.BuildVerificationResult, result);

        if (errorCount > 0)
            return BuildTaskResult.Fail(BuildErrorCodes.VerificationFailed,
                $"{errorCount} 个错误, {warningCount} 个警告。", true);

        return BuildTaskResult.Ok(new List<string>
        {
            $"[VERIFY] {errorCount} error(s), {warningCount} warning(s)."
        });
    }

    /// <summary>
    /// 公共 Address 校验：大小写不敏感唯一，显式采集条目必须带 Address，隐式依赖条目必须不带 Address。
    /// 冲突消息带文件路径与冲突 Address，便于直接定位到清单条目。
    /// </summary>
    private static void VerifyAddressAndPublicBoundary(
        ABManifest manifest,
        List<VerificationIssue> issues,
        ref int errorCount,
        ref int warningCount)
    {
        if (manifest.AssetEntries == null)
            return;

        var seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < manifest.AssetEntries.Count; i++)
        {
            ManifestAssetEntry entry = manifest.AssetEntries[i];
            if (entry == null)
                continue;

            string sourcePath = string.IsNullOrEmpty(entry.SourcePath) ? entry.EntryId : entry.SourcePath;
            if (entry.IsPublic)
            {
                if (string.IsNullOrEmpty(entry.Address))
                {
                    AddIssue(issues, BuildVerificationIssueCodes.PublicBoundary, IssueLevel.Error, sourcePath,
                        $"公共条目缺少 Address: EntryId={entry.EntryId}, SourcePath={entry.SourcePath}",
                        ref errorCount, ref warningCount);
                    continue;
                }

                if (seen.TryGetValue(entry.Address, out string owner))
                {
                    AddIssue(issues, BuildVerificationIssueCodes.AddressUniqueness, IssueLevel.Error, sourcePath,
                        $"公共 Address 冲突：'{entry.Address}' 同时被 '{owner}' 与 '{entry.SourcePath}' 使用（大小写不敏感唯一）。",
                        ref errorCount, ref warningCount);
                    continue;
                }

                seen[entry.Address] = sourcePath;
                continue;
            }

            if (!string.IsNullOrEmpty(entry.Address))
            {
                AddIssue(issues, BuildVerificationIssueCodes.PublicBoundary, IssueLevel.Error, sourcePath,
                    $"隐式依赖条目不得带 Address：EntryId={entry.EntryId}, Address='{entry.Address}'。",
                    ref errorCount, ref warningCount);
            }
        }
    }

    /// <summary>
    /// 成员关系校验：ContentIndex 有效，内容确实包含该资产，内容类型与采集类型一致，
    /// 且同一资产不会在两个内容里重复出现。
    /// </summary>
    private static void VerifyAssetContentMembership(
        ABManifest manifest,
        List<BundleBuildInfo> buildResults,
        List<VerificationIssue> issues,
        ref int errorCount,
        ref int warningCount)
    {
        if (manifest.AssetEntries == null)
            return;

        int contentCount = manifest.ContentEntries != null ? manifest.ContentEntries.Count : 0;
        var assetOwners = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < manifest.AssetEntries.Count; i++)
        {
            ManifestAssetEntry entry = manifest.AssetEntries[i];
            if (entry == null)
                continue;

            string sourcePath = string.IsNullOrEmpty(entry.SourcePath) ? entry.EntryId : entry.SourcePath;
            int contentIndex = entry.ContentIndex;
            if (contentIndex < 0 || contentIndex >= contentCount)
            {
                AddIssue(issues, BuildVerificationIssueCodes.AssetContentMembership, IssueLevel.Error, sourcePath,
                    $"资产条目的 ContentIndex 越界：ContentIndex={contentIndex}, ContentEntries={contentCount}。",
                    ref errorCount, ref warningCount);
                continue;
            }

            if (!string.IsNullOrEmpty(entry.SourcePath))
            {
                if (assetOwners.TryGetValue(entry.SourcePath, out string ownerContent))
                {
                    AddIssue(issues, BuildVerificationIssueCodes.AssetContentMembership, IssueLevel.Error, sourcePath,
                        $"资产同时归属多个内容：'{entry.SourcePath}' 已在 '{ownerContent}'，又出现在 '{manifest.ContentEntries[contentIndex].FileName}'。",
                        ref errorCount, ref warningCount);
                }
                else
                {
                    assetOwners[entry.SourcePath] = manifest.ContentEntries[contentIndex].FileName;
                }
            }

            if (!AssetBelongsToBuildResult(buildResults, contentIndex, entry.SourcePath))
            {
                AddIssue(issues, BuildVerificationIssueCodes.AssetContentMembership, IssueLevel.Error, sourcePath,
                    $"资产不在其所属内容的实际构建成员中：Content='{manifest.ContentEntries[contentIndex].FileName}', SourcePath='{entry.SourcePath}'。",
                    ref errorCount, ref warningCount);
            }

            if (entry.ContentType != manifest.ContentEntries[contentIndex].ContentType)
            {
                AddIssue(issues, BuildVerificationIssueCodes.ContentTypeCheck, IssueLevel.Error, sourcePath,
                    $"资产与内容的内容类型不一致：Asset.ContentType={entry.ContentType}, Content.ContentType={manifest.ContentEntries[contentIndex].ContentType}。",
                    ref errorCount, ref warningCount);
            }
        }
    }

    private static bool AssetBelongsToBuildResult(List<BundleBuildInfo> buildResults, int contentIndex, string sourcePath)
    {
        if (contentIndex < 0 || contentIndex >= buildResults.Count)
            return false;

        List<string> assetPaths = buildResults[contentIndex].AssetPaths;
        if (assetPaths == null || string.IsNullOrEmpty(sourcePath))
            return false;

        for (int i = 0; i < assetPaths.Count; i++)
        {
            if (string.Equals(assetPaths[i], sourcePath, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>
    /// 依赖下标校验：必须与本次构建结果记录的内容级依赖完全一致，且指向存在的内容、不含自依赖与重复项。
    /// </summary>
    private static void VerifyDependencyIndices(
        ABManifest manifest,
        List<BundleBuildInfo> buildResults,
        List<VerificationIssue> issues,
        ref int errorCount,
        ref int warningCount)
    {
        if (manifest.ContentEntries == null)
            return;

        for (int i = 0; i < manifest.ContentEntries.Count; i++)
        {
            ManifestContentEntry content = manifest.ContentEntries[i];
            if (content == null)
                continue;

            int[] indices = content.DependencyIndices ?? Array.Empty<int>();
            var seen = new HashSet<int>();
            for (int d = 0; d < indices.Length; d++)
            {
                int dependencyIndex = indices[d];
                if (dependencyIndex < 0 || dependencyIndex >= manifest.ContentEntries.Count)
                {
                    AddIssue(issues, BuildVerificationIssueCodes.DependencyIndices, IssueLevel.Error, content.FileName,
                        $"依赖下标指向不存在的内容：DependencyIndices[{d}]={dependencyIndex}, ContentEntries={manifest.ContentEntries.Count}。",
                        ref errorCount, ref warningCount);
                    continue;
                }

                if (dependencyIndex == i)
                {
                    AddIssue(issues, BuildVerificationIssueCodes.DependencyIndices, IssueLevel.Error, content.FileName,
                        "依赖下标包含自依赖。", ref errorCount, ref warningCount);
                }

                if (!seen.Add(dependencyIndex))
                {
                    AddIssue(issues, BuildVerificationIssueCodes.DependencyIndices, IssueLevel.Error, content.FileName,
                        $"依赖下标重复：{dependencyIndex}。", ref errorCount, ref warningCount);
                }
            }

            if (i >= buildResults.Count)
                continue;

            var actual = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int d = 0; d < indices.Length; d++)
            {
                int dependencyIndex = indices[d];
                if (dependencyIndex >= 0 && dependencyIndex < manifest.ContentEntries.Count)
                    actual.Add(manifest.ContentEntries[dependencyIndex].FileName);
            }

            List<string> expectedNames = buildResults[i].DependencyFileNames ?? new List<string>();
            var expected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int e = 0; e < expectedNames.Count; e++)
            {
                if (!string.IsNullOrEmpty(expectedNames[e]))
                    expected.Add(expectedNames[e]);
            }

            if (!actual.SetEquals(expected))
            {
                AddIssue(issues, BuildVerificationIssueCodes.DependencyIndices, IssueLevel.Error, content.FileName,
                    $"依赖下标与本次构建记录的依赖事实不一致：Manifest=[{string.Join(", ", actual)}], BuildInfo=[{string.Join(", ", expected)}]。",
                    ref errorCount, ref warningCount);
            }
        }
    }

    /// <summary>
    /// 内容文件校验：文件存在、内容非空、非 RawFile 检查 UnityFS 头、Hash/CRC/大小与磁盘一致，
    /// 并给出异常大小的 Warning。
    /// </summary>
    private static void VerifyContentFiles(
        ABManifest manifest,
        string tempDir,
        HashSet<string> knownFiles,
        List<VerificationIssue> issues,
        ref int errorCount,
        ref int warningCount)
    {
        if (manifest.ContentEntries == null)
            return;

        for (int i = 0; i < manifest.ContentEntries.Count; i++)
        {
            ManifestContentEntry content = manifest.ContentEntries[i];
            if (content == null || string.IsNullOrEmpty(content.FileName))
                continue;

            knownFiles.Add(content.FileName);
            string filePath = FYAssetPathUtility.JoinFilePath(tempDir, content.FileName);
            if (!FileHelper.Exists(filePath))
            {
                AddIssue(issues, BuildVerificationIssueCodes.FileExistence, IssueLevel.Error, content.FileName,
                    $"内容文件不存在: {filePath}", ref errorCount, ref warningCount);
                continue;
            }

            var fileInfo = new FileInfo(filePath);

            // 完整性：大小 > 0；非 RawFile 检查 UnityFS header
            if (fileInfo.Length == 0)
            {
                AddIssue(issues, BuildVerificationIssueCodes.FileIntegrity, IssueLevel.Error, content.FileName,
                    "内容文件大小为 0。", ref errorCount, ref warningCount);
            }
            else if (content.ContentType != AssetContentType.RawFile)
            {
                VerifyUnityHeader(filePath, content.FileName, issues, ref errorCount, ref warningCount);
            }

            string recomputedHash;
            uint recomputedCRC;
            try
            {
                HashGenerator.ComputeFileHashAndCRC(filePath, out recomputedHash, out recomputedCRC);
            }
            catch (Exception ex)
            {
                AddIssue(issues, BuildVerificationIssueCodes.FileIntegrity, IssueLevel.Error, content.FileName,
                    $"内容文件读取失败: {ex.Message}", ref errorCount, ref warningCount);
                continue;
            }

            if (!string.Equals(recomputedHash, content.FileHash, StringComparison.Ordinal))
            {
                AddIssue(issues, BuildVerificationIssueCodes.HashReVerify, IssueLevel.Error, content.FileName,
                    $"Hash 不一致: manifest={content.FileHash}, actual={recomputedHash}", ref errorCount, ref warningCount);
            }

            if (recomputedCRC != content.FileCRC)
            {
                AddIssue(issues, BuildVerificationIssueCodes.CrcVerify, IssueLevel.Error, content.FileName,
                    $"CRC 不一致: manifest={content.FileCRC}, actual={recomputedCRC}", ref errorCount, ref warningCount);
            }

            if (fileInfo.Length != content.FileSize)
            {
                AddIssue(issues, BuildVerificationIssueCodes.SizeVerify, IssueLevel.Error, content.FileName,
                    $"文件大小不一致: manifest={content.FileSize}, actual={fileInfo.Length}", ref errorCount, ref warningCount);
            }

            if (fileInfo.Length < MinSizeBytes)
            {
                AddIssue(issues, BuildVerificationIssueCodes.SizeAnomaly, IssueLevel.Warning, content.FileName,
                    $"内容文件 {fileInfo.Length} 字节低于最小值 ({MinSizeBytes} 字节)。", ref errorCount, ref warningCount);
            }
            if (fileInfo.Length > MaxSizeBytes)
            {
                AddIssue(issues, BuildVerificationIssueCodes.SizeAnomaly, IssueLevel.Warning, content.FileName,
                    $"内容文件 {fileInfo.Length} 字节超过最大值 ({MaxSizeBytes} 字节)。", ref errorCount, ref warningCount);
            }
        }
    }

    private static void VerifyUnityHeader(
        string filePath,
        string fileName,
        List<VerificationIssue> issues,
        ref int errorCount,
        ref int warningCount)
    {
        try
        {
            using var fs = File.OpenRead(filePath);
            var header = new byte[UnityFSMagic.Length];
            if (fs.Read(header, 0, header.Length) < header.Length)
            {
                AddIssue(issues, BuildVerificationIssueCodes.FileIntegrity, IssueLevel.Error, fileName,
                    "内容文件过小，无法包含 UnityFS 文件头。", ref errorCount, ref warningCount);
                return;
            }

            for (int h = 0; h < header.Length; h++)
            {
                if (header[h] == UnityFSMagic[h])
                    continue;

                AddIssue(issues, BuildVerificationIssueCodes.FileIntegrity, IssueLevel.Error, fileName,
                    "内容文件缺少 UnityFS 文件头魔数。", ref errorCount, ref warningCount);
                return;
            }
        }
        catch (IOException ex)
        {
            AddIssue(issues, BuildVerificationIssueCodes.FileIntegrity, IssueLevel.Error, fileName,
                $"读取内容文件头失败: {ex.Message}", ref errorCount, ref warningCount);
        }
    }

    /// <summary>
    /// 文件集合校验：实际输出目录的可交付文件必须与 Manifest 内容集合一致，
    /// 孤儿文件（不在 Manifest 内）只报 Warning，缺口报 Error。
    /// </summary>
    private static void VerifyFileSet(
        ABManifest manifest,
        string tempDir,
        HashSet<string> knownFiles,
        List<BundleBuildInfo> buildResults,
        List<VerificationIssue> issues,
        ref int errorCount,
        ref int warningCount)
    {
        int manifestCount = manifest.ContentEntries != null ? manifest.ContentEntries.Count : 0;
        if (manifestCount != buildResults.Count)
        {
            AddIssue(issues, BuildVerificationIssueCodes.CountCrossCheck, IssueLevel.Error, null,
                $"内容数量不一致: manifest={manifestCount}, buildInfo={buildResults.Count}",
                ref errorCount, ref warningCount);
        }

        if (!FileHelper.DirectoryExists(tempDir))
        {
            AddIssue(issues, BuildVerificationIssueCodes.FileSetMatch, IssueLevel.Error, null,
                $"构建输出目录不存在: {tempDir}", ref errorCount, ref warningCount);
            return;
        }

        var actualFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var filePath in FileHelper.GetFiles(tempDir))
        {
            string fileName = Path.GetFileName(filePath);
            if (IsUnitySidecarFile(fileName, tempDir))
                continue;

            actualFiles.Add(fileName);
            if (!knownFiles.Contains(fileName))
            {
                AddIssue(issues, BuildVerificationIssueCodes.OrphanCheck, IssueLevel.Warning, fileName,
                    $"输出目录存在未列入 Manifest 的孤儿文件: {fileName}", ref errorCount, ref warningCount);
            }
        }

        foreach (string expected in knownFiles)
        {
            if (actualFiles.Contains(expected))
                continue;

            AddIssue(issues, BuildVerificationIssueCodes.FileSetMatch, IssueLevel.Error, expected,
                $"实际输出文件集合缺少 Manifest 内容: {expected}", ref errorCount, ref warningCount);
        }
    }

    private static bool IsUnitySidecarFile(string fileName, string tempDir)
    {
        if (string.IsNullOrEmpty(fileName))
            return false;

        if (fileName.EndsWith(".manifest", StringComparison.OrdinalIgnoreCase))
            return true;

        string rootManifestName = Path.GetFileName(tempDir);
        return string.Equals(fileName, rootManifestName, StringComparison.OrdinalIgnoreCase);
    }

    private static void AddIssue(List<VerificationIssue> issues, string checkName, IssueLevel level,
        string bundleName, string message, ref int errorCount, ref int warningCount)
    {
        issues.Add(new VerificationIssue
        {
            CheckName = checkName,
            Level = level,
            BundleName = bundleName,
            Message = message
        });
        if (level == IssueLevel.Error) errorCount++;
        else warningCount++;
    }
}
