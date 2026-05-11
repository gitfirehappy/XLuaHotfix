using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// 构建输出完整性校验 Task — 6 项检查（文件存在/完整性/孤儿/Hash 重验/大小异常/数量交叉）。
/// Error → 构建中止；Warning → 继续执行。
/// 以 Manifest.BundleEntries + BundleBuildResults 为数据源，不依赖文件扩展名。
/// 在 TaskOrganizeOutput 之前执行。
/// </summary>
public class TaskVerifyBuildResult : IBuildTask
{
    public string TaskName => "TaskVerifyBuildResult";
    public string[] DependsOn => new[] { "TaskGenerateManifest" };
    public string[] ReadKeys => new[]
    {
        BuildContextKeys.BuildConfig,
        BuildContextKeys.ABManifest,
        BuildContextKeys.BundleBuildResults
    };
    public string[] WriteKeys => new[] { BuildContextKeys.BuildVerificationResult };

    private const long MinSizeBytes = 1024L;
    private const long MaxSizeBytes = 500_000_000L;

    /// <summary>UnityFS bundle 文件头魔数</summary>
    private static readonly byte[] UnityFSMagic = { 0x55, 0x6E, 0x69, 0x74, 0x79, 0x46, 0x53 }; // "UnityFS"

    public BuildTaskResult Execute(BuildContext ctx)
    {
        var cfg = ctx.Require<BuildConfig>(BuildContextKeys.BuildConfig);
        var manifest = ctx.Require<ABManifest>(BuildContextKeys.ABManifest);
        var buildResults = ctx.Require<List<BundleBuildInfo>>(BuildContextKeys.BundleBuildResults);
        string outputRoot = cfg.OutputRoot;
        string tempDir = Path.Combine(outputRoot, "_temp");

        // 构建 bundleName → PayloadKind 索引
        var payloadKindByBundle = new Dictionary<string, EPayloadKind>(StringComparer.OrdinalIgnoreCase);
        foreach (var b in buildResults)
            payloadKindByBundle[b.BundleName] = b.PayloadKind;

        var issues = new List<VerificationIssue>();
        int errorCount = 0;
        int warningCount = 0;

        // 收集 manifest entry 对应的文件名用于孤儿检查
        var knownFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // ① FILE EXISTENCE + ② FILE INTEGRITY + ④ HASH RE-VERIFY + ⑤ SIZE ANOMALY
        foreach (var bundle in manifest.BundleEntries)
        {
            string bundlePath = Path.Combine(tempDir, bundle.BundleName);
            knownFiles.Add(bundle.BundleName);

            // ①
            if (!File.Exists(bundlePath))
            {
                AddIssue(issues, "FILE_EXISTENCE", IssueLevel.Error, bundle.BundleName,
                    $"Bundle file not found: {bundlePath}", ref errorCount, ref warningCount);
                continue;
            }

            var fileInfo = new FileInfo(bundlePath);

            // ② — 大小 > 0；对非 RawFile 检查 UnityFS header
            if (fileInfo.Length == 0)
            {
                AddIssue(issues, "FILE_INTEGRITY", IssueLevel.Error, bundle.BundleName,
                    "Bundle file size is 0.", ref errorCount, ref warningCount);
            }
            else if (NeedsUnityHeaderCheck(bundle.BundleName, payloadKindByBundle))
            {
                try
                {
                    using var fs = File.OpenRead(bundlePath);
                    var header = new byte[UnityFSMagic.Length];
                    if (fs.Read(header, 0, header.Length) < header.Length)
                        AddIssue(issues, "FILE_INTEGRITY", IssueLevel.Error, bundle.BundleName,
                            "Bundle file too small to contain UnityFS header.", ref errorCount, ref warningCount);
                    else
                    {
                        bool validHeader = true;
                        for (int h = 0; h < header.Length; h++)
                        {
                            if (header[h] != UnityFSMagic[h]) { validHeader = false; break; }
                        }
                        if (!validHeader)
                            AddIssue(issues, "FILE_INTEGRITY", IssueLevel.Error, bundle.BundleName,
                                "Bundle file missing UnityFS header magic.", ref errorCount, ref warningCount);
                    }
                }
                catch (IOException ex)
                {
                    AddIssue(issues, "FILE_INTEGRITY", IssueLevel.Error, bundle.BundleName,
                        $"Failed to read bundle header: {ex.Message}", ref errorCount, ref warningCount);
                }
            }

            // ④
            string recomputedHash = HashGenerator.GenerateFileHash(bundlePath);
            if (!string.Equals(recomputedHash, bundle.FileHash, StringComparison.Ordinal))
            {
                AddIssue(issues, "HASH_RE_VERIFY", IssueLevel.Error, bundle.BundleName,
                    $"Hash mismatch: manifest={bundle.FileHash}, actual={recomputedHash}", ref errorCount, ref warningCount);
            }

            // ⑤
            if (fileInfo.Length < MinSizeBytes)
            {
                AddIssue(issues, "SIZE_ANOMALY", IssueLevel.Warning, bundle.BundleName,
                    $"Bundle size {fileInfo.Length} bytes below minimum ({MinSizeBytes} bytes).", ref errorCount, ref warningCount);
            }
            if (fileInfo.Length > MaxSizeBytes)
            {
                AddIssue(issues, "SIZE_ANOMALY", IssueLevel.Warning, bundle.BundleName,
                    $"Bundle size {fileInfo.Length} bytes exceeds maximum ({MaxSizeBytes} bytes).", ref errorCount, ref warningCount);
            }
        }

        // ③ ORPHAN CHECK — 扫描 temp 目录所有文件，发现不在 knownFiles 中的报 Warning
        if (Directory.Exists(tempDir))
        {
            foreach (var filePath in Directory.GetFiles(tempDir))
            {
                string fileName = Path.GetFileName(filePath);
                if (!knownFiles.Contains(fileName))
                {
                    AddIssue(issues, "ORPHAN_CHECK", IssueLevel.Warning, fileName,
                        $"Orphan file in output dir with no matching manifest entry: {fileName}", ref errorCount, ref warningCount);
                }
            }
        }

        // ⑥ COUNT CROSS-CHECK — 以 manifest 文件数为基准与实际文件数比对
        int manifestCount = manifest.BundleEntries.Count;
        int actualCount = Directory.Exists(tempDir)
            ? Directory.GetFiles(tempDir).Length
            : 0;

        if (actualCount != manifestCount || manifest.BundleEntries.Count != buildResults.Count)
        {
            AddIssue(issues, "COUNT_CROSS_CHECK", IssueLevel.Error, null,
                $"Count mismatch: actualFiles={actualCount}, manifest={manifestCount}, buildInfo={buildResults.Count}",
                ref errorCount, ref warningCount);
        }

        var result = new BuildVerificationResult
        {
            Success = errorCount == 0,
            Issues = issues,
            ErrorCount = errorCount,
            WarningCount = warningCount
        };

        ctx.Set(BuildContextKeys.BuildVerificationResult, result);

        if (errorCount > 0)
            return BuildTaskResult.Fail("VERIFICATION_FAILED",
                $"{errorCount} error(s), {warningCount} warning(s).", true);

        return BuildTaskResult.Ok(new List<string>
        {
            $"[VERIFY] {errorCount} error(s), {warningCount} warning(s)."
        });
    }

    private static bool NeedsUnityHeaderCheck(string bundleName, Dictionary<string, EPayloadKind> payloadKindByBundle)
    {
        if (bundleName == null) return true;
        // 精确匹配或按 bundleName 查找
        if (payloadKindByBundle.TryGetValue(bundleName, out var pk))
            return pk != EPayloadKind.RawFile;
        return true; // 未知 → 默认检查
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
