#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

/// <summary>
/// 历史包内容复用：Summary 是复用索引，历史 Build_* 包是唯一物理字节来源。
/// </summary>
/// <remarks>
/// 复用候选必须同时满足：同后端/平台/构建配方；Summary 可读且制品存在；内容身份与输入指纹一致；
/// 复制后 Hash/CRC/Size 与记录一致。任一不满足即退化为重建，且不阻断构建。
/// 本类不扫描目录、不推断身份，只按 Summary 记录定位制品。
/// </remarks>
public static class BuildArtifactReuseService
{
    /// <summary>
    /// 尝试从历史包复用单个内容的制品。
    /// </summary>
    /// <param name="store">构建事实存储；根目录由调用方注入。</param>
    /// <param name="backendKey">后端标识（AA/AB）；决定只在同后端的历史 Summary 中查找。</param>
    /// <param name="platform">目标平台标识；与 Summary 记录的平台必须逐字符一致。</param>
    /// <param name="buildRecipeFingerprint">构建配方指纹；与 Summary 记录的配方必须逐字符一致。</param>
    /// <param name="contentIdentity">内容逻辑名；与 Summary 记录的 ContentIdentity 对应。</param>
    /// <param name="inputFingerprint">本次构建计算的输入指纹；覆盖所有影响产物的事实。</param>
    /// <param name="targetDirectory">本次构建的产物目录（attempt 布局下为 `_temp`）。</param>
    /// <param name="fileName">命中时输出制品物理文件名（沿用历史包内的名称）。</param>
    /// <param name="digest">命中时输出复制后重新校验通过的制品摘要。</param>
    /// <param name="dependencyFileNames">命中时输出 Summary 记录的内容级依赖输出文件名，供依赖下标回放。</param>
    /// <param name="reason">未命中原因，用于 Warning；命中时为 null。</param>
    /// <returns>命中并已把制品复制到目标目录时返回 true。</returns>
    public static bool TryReuse(
        BuildSummaryStore store,
        string backendKey,
        string platform,
        string buildRecipeFingerprint,
        string contentIdentity,
        string inputFingerprint,
        string targetDirectory,
        out string fileName,
        out FileHelper.FileDigest digest,
        out List<string> dependencyFileNames,
        out string reason)
    {
        fileName = null;
        digest = default;
        dependencyFileNames = null;
        reason = null;

        if (store == null)
        {
            reason = "Summary 存储不可用";
            return false;
        }

        if (string.IsNullOrEmpty(backendKey))
        {
            reason = "后端标识为空";
            return false;
        }

        if (string.IsNullOrEmpty(platform) || string.IsNullOrEmpty(buildRecipeFingerprint))
        {
            reason = "平台或构建配方指纹为空";
            return false;
        }

        if (string.IsNullOrEmpty(contentIdentity))
        {
            reason = "内容身份为空";
            return false;
        }

        if (string.IsNullOrEmpty(inputFingerprint))
        {
            reason = $"内容 '{contentIdentity}' 缺少输入指纹";
            return false;
        }

        if (string.IsNullOrEmpty(targetDirectory))
        {
            reason = "复用目标目录为空";
            return false;
        }

        List<CompleteBuildSummary.SummaryDocument> documents;
        try
        {
            documents = store.ReadSummaries(backendKey);
        }
        catch (Exception ex)
        {
            reason = $"读取历史 Summary 失败: {ex.Message}";
            return false;
        }

        if (documents == null || documents.Count == 0)
        {
            reason = $"没有可读的历史 Summary（后端 {backendKey}）";
            return false;
        }

        // 同配方的新构造成品更可能仍被保留，按构建时间倒序取第一个可用候选。
        documents.Sort((left, right) => ParseStartedAtUtc(right).CompareTo(ParseStartedAtUtc(left)));

        string missReason = null;
        for (int i = 0; i < documents.Count; i++)
        {
            CompleteBuildSummary.SummaryDocument document = documents[i];
            if (document == null || !document.Success)
                continue;

            if (!string.Equals(document.Platform, platform, StringComparison.Ordinal)
                || !string.Equals(document.BuildRecipeFingerprint, buildRecipeFingerprint, StringComparison.Ordinal))
            {
                continue;
            }

            if (document.Contents == null || document.Contents.Count == 0)
                continue;

            SummaryContentFact fact = null;
            bool identitySeen = false;
            bool multipleArtifacts = false;
            for (int c = 0; c < document.Contents.Count; c++)
            {
                SummaryContentFact candidate = document.Contents[c];
                if (candidate == null
                    || !string.Equals(candidate.ContentIdentity, contentIdentity, StringComparison.Ordinal))
                {
                    continue;
                }

                identitySeen = true;
                if (!string.Equals(candidate.InputFingerprint, inputFingerprint, StringComparison.Ordinal))
                    continue;

                if (fact == null)
                {
                    fact = candidate;
                    continue;
                }

                // 同一内容在同一 Summary 中只能有一条制品记录；多条说明索引不可信，必须重建。
                if (!string.Equals(fact.FileName, candidate.FileName, StringComparison.Ordinal))
                    multipleArtifacts = true;
            }

            if (!identitySeen)
                continue;

            if (fact == null)
            {
                missReason = $"内容 '{contentIdentity}' 的输入指纹与历史记录不一致（{document.BuildId}）";
                continue;
            }

            if (multipleArtifacts)
            {
                missReason = $"内容 '{contentIdentity}' 在历史 Summary 中有多条同身份制品记录（{document.BuildId}）";
                continue;
            }

            string packageDirectory = ResolvePackageDirectory(document.ArtifactRelativePath);
            if (string.IsNullOrEmpty(packageDirectory))
            {
                missReason = $"历史 Summary 缺少可用的制品路径（{document.BuildId}）";
                continue;
            }

            var recorded = new FileHelper.FileDigest(fact.FileName, fact.FileHash, fact.FileCRC, fact.FileSize);
            if (!recorded.IsComplete)
            {
                missReason = $"历史 Summary 记录的制品摘要不完整（{document.BuildId}）";
                continue;
            }

            // 缺少依赖事实的历史记录无法回放依赖下标，按不可信处理；空集合是合法事实（叶子内容）。
            if (fact.DependencyFileNames == null)
            {
                missReason = $"历史 Summary 缺少依赖事实（{document.BuildId}）";
                continue;
            }

            string sourcePath = FYAssetPathUtility.JoinFilePath(
                FYAssetPathUtility.JoinFilePath(packageDirectory, FYAssetSettings.BUNDLES_DIRECTORY_NAME),
                fact.FileName);

            if (!FileHelper.TryCreateDigest(sourcePath, fact.FileName, out FileHelper.FileDigest sourceDigest))
            {
                missReason = $"历史制品缺失或不可读: {sourcePath}";
                continue;
            }

            if (!sourceDigest.Matches(recorded))
            {
                missReason = $"历史制品与 Summary 记录的 Hash/CRC/Size 不一致: {sourcePath}";
                continue;
            }

            string targetPath = FYAssetPathUtility.JoinFilePath(targetDirectory, fact.FileName);
            try
            {
                Directory.CreateDirectory(targetDirectory);
                File.Copy(sourcePath, targetPath, true);
            }
            catch (Exception ex)
            {
                missReason = $"历史制品复制失败: {ex.Message}";
                continue;
            }

            // 复制是唯一可能引入损坏的环节，必须对落盘文件重新校验，不能沿用源文件摘要。
            if (!FileHelper.TryCreateDigest(targetPath, fact.FileName, out FileHelper.FileDigest copied) || !copied.Matches(recorded))
            {
                TryDelete(targetPath);
                missReason = $"复制后的制品与 Summary 记录不一致: {targetPath}";
                continue;
            }

            fileName = fact.FileName;
            digest = copied;
            dependencyFileNames = new List<string>(fact.DependencyFileNames);
            reason = null;
            return true;
        }

        reason = missReason ?? $"内容 '{contentIdentity}' 没有可用的历史复用候选（后端 {backendKey}）";
        return false;
    }

    /// <summary>ArtifactRelativePath → 项目根下的绝对包目录；缺失或无法定位时返回 null。</summary>
    private static string ResolvePackageDirectory(string artifactRelativePath)
    {
        if (string.IsNullOrWhiteSpace(artifactRelativePath))
            return null;

        try
        {
            string projectRoot = BuildPathManager.ProjectRoot;
            if (string.IsNullOrWhiteSpace(projectRoot))
                return null;

            return FYAssetPathUtility.ResolveFilePath(projectRoot, artifactRelativePath);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>摘要时间戳解析；缺失或格式非法时按最早处理，只影响候选顺序。</summary>
    private static DateTime ParseStartedAtUtc(CompleteBuildSummary.SummaryDocument document)
    {
        if (document == null)
            return DateTime.MinValue;

        return DateTime.TryParse(
            document.StartedAtUtc,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out DateTime parsed)
            ? parsed
            : DateTime.MinValue;
    }

    private static void TryDelete(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
                File.Delete(filePath);
        }
        catch (Exception)
        {
            // 尽力清理：残留文件位于本次构建的临时产物目录，不影响交付内容。
        }
    }
}
#endif
