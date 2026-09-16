using System;
using System.Collections.Generic;
using System.IO;

/// <summary>
/// AA 构建管线：校验 catalog、AAManifest 与内容文件集合。
/// 校验口径与 AABuildBackend.RequiredManifestFileNames 一致：catalog 与按设置选定的清单格式。
/// Error → 构建中止；Warning → 继续执行（孤儿文件、异常大小等）。
/// </summary>
/// <remarks>
/// 清单里的 Hash / CRC / Size 必须与磁盘事实一致，否则热更下载与校验会在客户端失败；
/// 文件集合必须双向对齐：清单条目缺文件报 Error，目录里未列入清单的 .bundle 报 Warning。
/// </remarks>
public class VerifyAAContentTask : IBuildTask
{
    public string TaskName => "VerifyAAContent";

    private const long MinSizeBytes = 1024L;
    private const long MaxSizeBytes = 500_000_000L;

    public BuildTaskResult Execute(BuildContext ctx)
    {
        var request = ctx.Require<BuildRequest>(BuildContextKeys.BuildRequest);
        var manifest = ctx.Get<AAManifest>(AABuildContextKeys.AAManifest);

        var issues = new List<VerificationIssue>();
        int errorCount = 0;
        int warningCount = 0;

        VerifyCatalog(request, issues, ref errorCount, ref warningCount);
        VerifyManifestFiles(request, issues, ref errorCount, ref warningCount);
        VerifyBundles(request, manifest, issues, ref errorCount, ref warningCount);

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
            $"[AA VERIFY] {errorCount} error(s), {warningCount} warning(s)."
        });
    }

    /// <summary>catalog.json 必须存在且非空：没有 catalog 的包在运行时无法定位任何内容。</summary>
    private static void VerifyCatalog(
        BuildRequest request,
        List<VerificationIssue> issues,
        ref int errorCount,
        ref int warningCount)
    {
        string catalogPath = FYAssetPathUtility.JoinFilePath(request.OutputDir, FYAssetSettings.ADDRESSABLES_CATALOG_FILE_NAME);
        if (!FileHelper.Exists(catalogPath))
        {
            AddIssue(issues, BuildVerificationIssueCodes.FileExistence, IssueLevel.Error,
                FYAssetSettings.ADDRESSABLES_CATALOG_FILE_NAME,
                $"Addressables catalog 不存在: {catalogPath}", ref errorCount, ref warningCount);
            return;
        }

        if (new FileInfo(catalogPath).Length == 0)
        {
            AddIssue(issues, BuildVerificationIssueCodes.FileIntegrity, IssueLevel.Error,
                FYAssetSettings.ADDRESSABLES_CATALOG_FILE_NAME,
                "Addressables catalog 文件大小为 0。", ref errorCount, ref warningCount);
        }
    }

    /// <summary>按 ManifestOutputFormat 校验清单文件存在性；未选中的格式不得残留旧文件。</summary>
    private static void VerifyManifestFiles(
        BuildRequest request,
        List<VerificationIssue> issues,
        ref int errorCount,
        ref int warningCount)
    {
        ManifestOutputFormat outputFormat = FYAssetAASettings.Instance.ManifestOutputFormat;
        string jsonPath = FYAssetPathUtility.JoinFilePath(request.OutputDir, FYAssetSettings.AA_MANIFEST_FILE_NAME);
        string binPath = FYAssetPathUtility.JoinFilePath(request.OutputDir, FYAssetSettings.AA_MANIFEST_FILE_NAME_BIN);

        if (outputFormat != ManifestOutputFormat.BinaryOnly)
            RequireNonEmptyFile(jsonPath, FYAssetSettings.AA_MANIFEST_FILE_NAME, issues, ref errorCount, ref warningCount);
        else if (FileHelper.Exists(jsonPath))
            AddIssue(issues, BuildVerificationIssueCodes.FileSetMatch, IssueLevel.Error, FYAssetSettings.AA_MANIFEST_FILE_NAME,
                "ManifestOutputFormat 为 BinaryOnly，但包目录仍残留 JSON 清单。", ref errorCount, ref warningCount);

        if (outputFormat != ManifestOutputFormat.JsonOnly)
            RequireNonEmptyFile(binPath, FYAssetSettings.AA_MANIFEST_FILE_NAME_BIN, issues, ref errorCount, ref warningCount);
        else if (FileHelper.Exists(binPath))
            AddIssue(issues, BuildVerificationIssueCodes.FileSetMatch, IssueLevel.Error, FYAssetSettings.AA_MANIFEST_FILE_NAME_BIN,
                "ManifestOutputFormat 为 JsonOnly，但包目录仍残留二进制清单。", ref errorCount, ref warningCount);
    }

    /// <summary>清单条目与实际 bundle 文件双向对齐，并逐项复核 Hash / CRC / 大小。</summary>
    private static void VerifyBundles(
        BuildRequest request,
        AAManifest manifest,
        List<VerificationIssue> issues,
        ref int errorCount,
        ref int warningCount)
    {
        if (manifest == null)
        {
            AddIssue(issues, BuildVerificationIssueCodes.CountCrossCheck, IssueLevel.Error, null,
                "Context 中缺少 AAManifest，无法校验内容文件集合。", ref errorCount, ref warningCount);
            return;
        }

        string bundlesDir = request.BundlesDir;
        var knownFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        List<BundleInfo> bundles = manifest.Bundles ?? new List<BundleInfo>();

        for (int i = 0; i < bundles.Count; i++)
        {
            BundleInfo bundle = bundles[i];
            if (bundle == null || string.IsNullOrEmpty(bundle.BundleName))
                continue;

            knownFiles.Add(bundle.BundleName);
            string filePath = FYAssetPathUtility.JoinFilePath(bundlesDir, bundle.BundleName);
            if (!FileHelper.Exists(filePath))
            {
                AddIssue(issues, BuildVerificationIssueCodes.FileExistence, IssueLevel.Error, bundle.BundleName,
                    $"清单条目缺少实际文件: {filePath}", ref errorCount, ref warningCount);
                continue;
            }

            var fileInfo = new FileInfo(filePath);
            if (fileInfo.Length == 0)
            {
                AddIssue(issues, BuildVerificationIssueCodes.FileIntegrity, IssueLevel.Error, bundle.BundleName,
                    "内容文件大小为 0。", ref errorCount, ref warningCount);
            }

            string recomputedHash;
            uint recomputedCRC;
            try
            {
                HashGenerator.ComputeFileHashAndCRC(filePath, out recomputedHash, out recomputedCRC);
            }
            catch (Exception ex)
            {
                AddIssue(issues, BuildVerificationIssueCodes.FileIntegrity, IssueLevel.Error, bundle.BundleName,
                    $"内容文件读取失败: {ex.Message}", ref errorCount, ref warningCount);
                continue;
            }

            if (!string.Equals(recomputedHash, bundle.FileHash, StringComparison.Ordinal))
            {
                AddIssue(issues, BuildVerificationIssueCodes.HashReVerify, IssueLevel.Error, bundle.BundleName,
                    $"Hash 不一致: manifest={bundle.FileHash}, actual={recomputedHash}", ref errorCount, ref warningCount);
            }

            if (recomputedCRC != bundle.FileCRC)
            {
                AddIssue(issues, BuildVerificationIssueCodes.CrcVerify, IssueLevel.Error, bundle.BundleName,
                    $"CRC 不一致: manifest={bundle.FileCRC}, actual={recomputedCRC}", ref errorCount, ref warningCount);
            }

            if (fileInfo.Length != bundle.FileSize)
            {
                AddIssue(issues, BuildVerificationIssueCodes.SizeVerify, IssueLevel.Error, bundle.BundleName,
                    $"文件大小不一致: manifest={bundle.FileSize}, actual={fileInfo.Length}", ref errorCount, ref warningCount);
            }

            if (fileInfo.Length < MinSizeBytes)
            {
                AddIssue(issues, BuildVerificationIssueCodes.SizeAnomaly, IssueLevel.Warning, bundle.BundleName,
                    $"内容文件 {fileInfo.Length} 字节低于最小值 ({MinSizeBytes} 字节)。", ref errorCount, ref warningCount);
            }
            if (fileInfo.Length > MaxSizeBytes)
            {
                AddIssue(issues, BuildVerificationIssueCodes.SizeAnomaly, IssueLevel.Warning, bundle.BundleName,
                    $"内容文件 {fileInfo.Length} 字节超过最大值 ({MaxSizeBytes} 字节)。", ref errorCount, ref warningCount);
            }
        }

        if (!FileHelper.DirectoryExists(bundlesDir))
        {
            AddIssue(issues, BuildVerificationIssueCodes.FileSetMatch, IssueLevel.Error, null,
                $"AA bundles 目录不存在: {bundlesDir}", ref errorCount, ref warningCount);
            return;
        }

        string[] actualFiles = FileHelper.GetFiles(bundlesDir, "*", SearchOption.TopDirectoryOnly);
        for (int i = 0; i < actualFiles.Length; i++)
        {
            string fileName = Path.GetFileName(actualFiles[i]);
            if (string.IsNullOrEmpty(fileName) || fileName.EndsWith(".manifest", StringComparison.OrdinalIgnoreCase))
                continue;
            if (knownFiles.Contains(fileName))
                continue;

            AddIssue(issues, BuildVerificationIssueCodes.OrphanCheck, IssueLevel.Warning, fileName,
                $"输出目录存在未列入清单的孤儿文件: {fileName}", ref errorCount, ref warningCount);
        }
    }

    private static void RequireNonEmptyFile(
        string filePath,
        string fileName,
        List<VerificationIssue> issues,
        ref int errorCount,
        ref int warningCount)
    {
        if (!FileHelper.Exists(filePath))
        {
            AddIssue(issues, BuildVerificationIssueCodes.FileExistence, IssueLevel.Error, fileName,
                $"清单文件不存在: {filePath}", ref errorCount, ref warningCount);
            return;
        }

        if (new FileInfo(filePath).Length == 0)
        {
            AddIssue(issues, BuildVerificationIssueCodes.FileIntegrity, IssueLevel.Error, fileName,
                "清单文件大小为 0。", ref errorCount, ref warningCount);
        }
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
