using System;
using System.Collections.Generic;

/// <summary>
/// ABManifest 映射校验 Task：只校验 Manifest 与 ContentBuildResult 的字段映射、
/// 资产成员归属、ContentIndex 和 DependencyIndices。
/// </summary>
public class VerifyABContentTask : IBuildTask
{
    public string TaskName => "VerifyABContent";

    public BuildTaskResult Execute(BuildRunContext ctx)
    {
        var manifest = ctx.Require<ABManifest>(ABBuildContextKeys.ABManifest);
        var buildResults = ctx.Require<List<ContentBuildResult>>(ABBuildContextKeys.BundleBuildResults);
        var issues = new List<VerificationIssue>();
        int errorCount = 0;
        int warningCount = 0;

        VerifyContentMapping(manifest, buildResults, issues, ref errorCount, ref warningCount);
        VerifyAssetContentMembership(manifest, buildResults, issues, ref errorCount, ref warningCount);
        VerifyDependencyIndices(manifest, buildResults, issues, ref errorCount, ref warningCount);

        var result = new BuildVerificationResult
        {
            Success = errorCount == 0,
            Issues = issues,
            ErrorCount = errorCount,
            WarningCount = warningCount
        };
        ctx.Set(BuildContextKeys.BuildVerificationResult, result);

        if (errorCount > 0)
        {
            return BuildTaskResult.Fail(BuildErrorCodes.VerificationFailed,
                $"{errorCount} 个错误, {warningCount} 个警告。", true);
        }

        return BuildTaskResult.Ok(new List<string>
        {
            $"[VERIFY] {errorCount} error(s), {warningCount} warning(s)."
        });
    }

    /// <summary>Manifest 的物理文件事实必须逐项等于 Build 阶段产出的唯一事实。</summary>
    internal static void VerifyContentMapping(
        ABManifest manifest,
        List<ContentBuildResult> buildResults,
        List<VerificationIssue> issues,
        ref int errorCount,
        ref int warningCount)
    {
        int manifestCount = manifest.ContentEntries != null ? manifest.ContentEntries.Count : 0;
        int buildCount = buildResults != null ? buildResults.Count : 0;
        if (manifestCount != buildCount)
        {
            AddIssue(issues, BuildVerificationIssueCodes.CountCrossCheck, IssueLevel.Error, null,
                $"内容数量不一致: manifest={manifestCount}, buildResult={buildCount}",
                ref errorCount, ref warningCount);
        }

        int count = Math.Min(manifestCount, buildCount);
        for (int i = 0; i < count; i++)
        {
            ManifestContentEntry content = manifest.ContentEntries[i];
            ContentBuildResult build = buildResults[i];
            if (content == null || build == null)
            {
                AddIssue(issues, BuildVerificationIssueCodes.CountCrossCheck, IssueLevel.Error, null,
                    $"内容下标 {i} 的 Manifest 或 ContentBuildResult 条目为空。",
                    ref errorCount, ref warningCount);
                continue;
            }

            if (!string.Equals(content.FileName, build.FileName, StringComparison.Ordinal))
            {
                AddIssue(issues, BuildVerificationIssueCodes.FileExistence, IssueLevel.Error, content.FileName,
                    $"FileName 映射不一致: manifest='{content.FileName}', buildResult='{build.FileName}'。",
                    ref errorCount, ref warningCount);
            }
            if (!string.Equals(content.Hash, build.Hash, StringComparison.Ordinal))
            {
                AddIssue(issues, BuildVerificationIssueCodes.HashReVerify, IssueLevel.Error, content.FileName,
                    $"Hash 映射不一致: manifest={content.Hash}, buildResult={build.Hash}。",
                    ref errorCount, ref warningCount);
            }
            if (content.CRC != build.CRC)
            {
                AddIssue(issues, BuildVerificationIssueCodes.CrcVerify, IssueLevel.Error, content.FileName,
                    $"CRC 映射不一致: manifest={content.CRC}, buildResult={build.CRC}。",
                    ref errorCount, ref warningCount);
            }
            if (content.Size != build.Size)
            {
                AddIssue(issues, BuildVerificationIssueCodes.SizeVerify, IssueLevel.Error, content.FileName,
                    $"Size 映射不一致: manifest={content.Size}, buildResult={build.Size}。",
                    ref errorCount, ref warningCount);
            }
            if (content.ContentType != build.ContentType)
            {
                AddIssue(issues, BuildVerificationIssueCodes.ContentTypeCheck, IssueLevel.Error, content.FileName,
                    $"ContentType 映射不一致: manifest={content.ContentType}, buildResult={build.ContentType}。",
                    ref errorCount, ref warningCount);
            }
        }
    }

    /// <summary>校验公共资源的 ContentIndex 和实际 Content 成员关系。</summary>
    private static void VerifyAssetContentMembership(
        ABManifest manifest,
        List<ContentBuildResult> buildResults,
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

            string assetPath = string.IsNullOrEmpty(entry.AssetPath) ? entry.Address : entry.AssetPath;
            int contentIndex = entry.ContentIndex;
            if (contentIndex < 0 || contentIndex >= contentCount || contentIndex >= buildResults.Count)
            {
                AddIssue(issues, BuildVerificationIssueCodes.AssetContentMembership, IssueLevel.Error, assetPath,
                    $"资产条目的 ContentIndex 越界：ContentIndex={contentIndex}, ContentEntries={contentCount}, BuildResults={buildResults.Count}。",
                    ref errorCount, ref warningCount);
                continue;
            }

            if (!string.IsNullOrEmpty(entry.AssetPath))
            {
                if (assetOwners.TryGetValue(entry.AssetPath, out string ownerContent))
                {
                    AddIssue(issues, BuildVerificationIssueCodes.AssetContentMembership, IssueLevel.Error, assetPath,
                        $"资产同时归属多个内容：'{entry.AssetPath}' 已在 '{ownerContent}'，又出现在 '{manifest.ContentEntries[contentIndex].FileName}'。",
                        ref errorCount, ref warningCount);
                }
                else
                {
                    assetOwners[entry.AssetPath] = manifest.ContentEntries[contentIndex].FileName;
                }
            }

            if (!AssetBelongsToBuildResult(buildResults[contentIndex], entry.AssetPath))
            {
                AddIssue(issues, BuildVerificationIssueCodes.AssetContentMembership, IssueLevel.Error, assetPath,
                    $"资产不在其所属内容的实际构建成员中：Content='{manifest.ContentEntries[contentIndex].FileName}', AssetPath='{entry.AssetPath}'。",
                    ref errorCount, ref warningCount);
            }
        }
    }

    private static bool AssetBelongsToBuildResult(ContentBuildResult buildResult, string assetPath)
    {
        if (buildResult?.AssetPaths == null || string.IsNullOrEmpty(assetPath))
            return false;

        for (int i = 0; i < buildResult.AssetPaths.Count; i++)
        {
            if (string.Equals(buildResult.AssetPaths[i], assetPath, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>依赖下标必须完整有效，并与 Build 阶段记录的直接依赖文件名一致。</summary>
    private static void VerifyDependencyIndices(
        ABManifest manifest,
        List<ContentBuildResult> buildResults,
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
            var actual = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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
                actual.Add(manifest.ContentEntries[dependencyIndex].FileName);
            }

            if (i >= buildResults.Count)
                continue;

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
                    $"依赖下标与 Build 依赖事实不一致：Manifest=[{string.Join(", ", actual)}], BuildResult=[{string.Join(", ", expected)}]。",
                    ref errorCount, ref warningCount);
            }
        }
    }

    private static void AddIssue(
        List<VerificationIssue> issues,
        string checkName,
        IssueLevel level,
        string bundleName,
        string message,
        ref int errorCount,
        ref int warningCount)
    {
        issues.Add(new VerificationIssue
        {
            CheckName = checkName,
            Level = level,
            BundleName = bundleName,
            Message = message
        });
        if (level == IssueLevel.Error)
            errorCount++;
        else
            warningCount++;
    }
}
