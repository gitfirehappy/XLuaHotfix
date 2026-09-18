#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;

public sealed class PublishSourceCandidate
{
    public string SummaryId;
    public string SourcePackageDir;
    public CompleteBuildSummary.SummaryDocument Document;
    public bool IsValid;
    public string InvalidReason;

    public string Label => IsValid ? SummaryId : $"{SummaryId} (invalid)";
}

/// <summary>
/// 从正式 Summary 枚举发布源，并验证 Summary 声明的制品可用于发布。
/// </summary>
public static class PublishSourceCatalog
{
    public static List<PublishSourceCandidate> Read(
        string backendKey,
        IPackageManifestReader manifestReader)
    {
        var result = new List<PublishSourceCandidate>();
        BuildSummaryStore store = BuildSummaryStore.CreateDefault();
        string backendDir = store.GetBackendDir(backendKey);
        if (!Directory.Exists(backendDir))
            return result;

        string[] paths = Directory.GetFiles(backendDir, "*.json", SearchOption.TopDirectoryOnly);
        Array.Sort(paths, StringComparer.Ordinal);
        for (int i = 0; i < paths.Length; i++)
            result.Add(ReadCandidate(paths[i], backendKey, manifestReader));
        return result;
    }

    private static PublishSourceCandidate ReadCandidate(
        string summaryPath,
        string backendKey,
        IPackageManifestReader manifestReader)
    {
        var candidate = new PublishSourceCandidate
        {
            SummaryId = Path.GetFileNameWithoutExtension(summaryPath),
            InvalidReason = string.Empty
        };

        try
        {
            candidate.Document = SerializationUtility.ReadFromFile<CompleteBuildSummary.SummaryDocument>(summaryPath);
        }
        catch (Exception ex)
        {
            return Invalid(candidate, "Summary 解析失败：" + ex.Message);
        }

        CompleteBuildSummary.SummaryDocument document = candidate.Document;
        if (document == null)
            return Invalid(candidate, "Summary 内容为空。");
        if (document.SummarySchemaVersion != CompleteBuildSummary.CurrentSchemaVersion)
            return Invalid(candidate, $"Summary 版本不兼容：{document.SummarySchemaVersion}");
        if (!document.Success)
            return Invalid(candidate, "Summary 记录的构建未成功。");
        if (!string.Equals(document.BuildId, candidate.SummaryId, StringComparison.Ordinal))
            return Invalid(candidate, "Summary 文件名与 BuildId 不一致。");
        if (!string.Equals(document.BackendId, backendKey, StringComparison.OrdinalIgnoreCase))
            return Invalid(candidate, $"Summary 后端不匹配：{document.BackendId}");
        if (!VersionNumber.TryParse(document.Version, out _))
            return Invalid(candidate, $"Summary 版本无法解析：{document.Version}");
        if (string.IsNullOrWhiteSpace(document.ArtifactRelativePath))
            return Invalid(candidate, "Summary 缺少制品路径。");

        string projectRoot = FYAssetPathUtility.NormalizePath(BuildPathManager.ProjectRoot);
        string sourceDir;
        try
        {
            sourceDir = FYAssetPathUtility.NormalizePath(Path.Combine(
                projectRoot,
                document.ArtifactRelativePath.Replace('/', Path.DirectorySeparatorChar)));
        }
        catch (Exception ex)
        {
            return Invalid(candidate, "制品路径无效：" + ex.Message);
        }

        candidate.SourcePackageDir = sourceDir;
        if (!PublishPathGuard.IsContainedIn(projectRoot, sourceDir))
            return Invalid(candidate, "制品路径不在项目根目录内。");
        if (!Directory.Exists(sourceDir))
            return Invalid(candidate, "Summary 声明的制品目录不存在。");
        if (!string.Equals(Path.GetFileName(sourceDir), document.BuildId, StringComparison.Ordinal))
            return Invalid(candidate, "制品目录名与 BuildId 不一致。");
        if (manifestReader == null)
            return Invalid(candidate, "缺少包清单读取器。");

        IReadOnlyList<string> requiredFiles = manifestReader.RequiredPackageFileNames;
        for (int i = 0; requiredFiles != null && i < requiredFiles.Count; i++)
        {
            string required = FYAssetPathUtility.JoinFilePath(sourceDir, requiredFiles[i]);
            if (!FileHelper.Exists(required))
                return Invalid(candidate, $"制品缺少必需文件：{requiredFiles[i]}");
        }

        if (!manifestReader.TryReadContentDigests(sourceDir, out IReadOnlyList<FileHelper.FileDigest> declared, out string manifestError))
            return Invalid(candidate, "包清单不可用：" + manifestError);

        string contentDir = FYAssetPathUtility.JoinFilePath(sourceDir, manifestReader.ContentDirectoryName);
        if (!TryScanContentDirectory(sourceDir, contentDir, out List<FileHelper.FileDigest> physical, out string scanError))
            return Invalid(candidate, "包内容扫描失败：" + scanError);

        Dictionary<string, FileHelper.FileDigest> declaredByName = FileHelper.IndexByName(declared);
        Dictionary<string, FileHelper.FileDigest> physicalByName = FileHelper.IndexByName(physical);
        if (declaredByName.Count != physicalByName.Count)
            return Invalid(candidate, $"清单内容数与实际文件数不一致：{declaredByName.Count}/{physicalByName.Count}");

        foreach (KeyValuePair<string, FileHelper.FileDigest> pair in declaredByName)
        {
            if (!physicalByName.TryGetValue(pair.Key, out FileHelper.FileDigest actual) || !actual.Matches(pair.Value))
                return Invalid(candidate, "清单内容与实际文件不一致：" + pair.Key);
        }

        candidate.IsValid = true;
        return candidate;
    }

    private static bool TryScanContentDirectory(
        string packageRoot,
        string contentDirectory,
        out List<FileHelper.FileDigest> files,
        out string error)
    {
        if (string.IsNullOrEmpty(contentDirectory) || !FileHelper.DirectoryExists(contentDirectory))
        {
            files = new List<FileHelper.FileDigest>();
            error = string.Empty;
            return true;
        }

        return FileHelper.TryScanFiles(
            contentDirectory,
            path => FYAssetPathUtility.GetRelativeFilePath(packageRoot, path).Replace('\\', '/'),
            out files,
            out error);
    }

    private static PublishSourceCandidate Invalid(PublishSourceCandidate candidate, string reason)
    {
        candidate.IsValid = false;
        candidate.InvalidReason = reason;
        return candidate;
    }
}
#endif
