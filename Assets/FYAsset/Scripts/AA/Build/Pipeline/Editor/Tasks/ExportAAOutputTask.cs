using System;
using System.Collections.Generic;
using System.IO;

/// <summary>
/// AA 构建管线：导出构建输出。
/// 形成完整构建结果（CompleteBuildSummary）、写模式输出（Full/Standalone 的包内 BuildIndex）与源快照。
/// </summary>
/// <remarks>
/// AA 完整矩阵本轮延期：Hotfix 暂时交付本次 Addressables 构建的完整内容，
/// “相对成功 Full 裁剪变化内容”的 AA 适配随 AA 矩阵批次实现。
/// StreamingAssets 的本地启动数据由 BuildProjectRunner 在产物提升之后导出。
/// </remarks>
public class ExportAAOutputTask : IBuildTask
{
    public string TaskName => "ExportAAOutput";

    public BuildTaskResult Execute(BuildContext ctx)
    {
        var request = ctx.Require<BuildRequest>(BuildContextKeys.BuildRequest);
        var buildType = ctx.Require<BuildType>(BuildContextKeys.BuildType);
        string outputPath = ctx.Require<string>(BuildContextKeys.OutputPath);
        if (!string.Equals(outputPath, request.OutputDir, StringComparison.Ordinal))
            return BuildTaskResult.Fail(BuildErrorCodes.BuildFailed,
                $"AA 导出目录必须来自 BuildRequest。Expected: {request.OutputDir}, Actual: {outputPath}", true);

        var manifest = ctx.Get<AAManifest>(AABuildContextKeys.AAManifest);
        DateTime startedAt = ctx.Get<DateTime>(BuildContextKeys.BuildStartedAtUtc);
        if (startedAt == default)
            startedAt = DateTime.UtcNow;

        var messages = new List<string>();
        try
        {
            // 源快照随包交付：它是下一次 Hotfix 计算变化资源的“上次成功构建事实”。
            WriteSourceScan(ctx, request.OutputDir);
            messages.Add($"[AA EXPORT] SourceScan: {AASourceScanFile.FileName}");


            CompleteBuildSummary summary = BuildExportWriter.CreateSummary(ctx, startedAt);
            FillSummary(summary, manifest, ctx);

            string buildIndexPath = BuildExportWriter.WritePackageBuildIndex(ctx, request.OutputDir);
            if (!string.IsNullOrEmpty(buildIndexPath))
                messages.Add($"[AA EXPORT] BuildIndex: {buildIndexPath}");
            messages.Add($"[AA EXPORT] Mode: {summary.BuildType}/{summary.RuntimeMode}");

            summary.Duration = DateTime.UtcNow - startedAt;
            summary.FinishedAtUtc = DateTime.UtcNow;
            ctx.Set(BuildContextKeys.BuildSummary, summary);
            messages.Add($"[AA EXPORT] Summary: {summary.BuildId}");
        }
        catch (Exception ex)
        {
            return BuildTaskResult.Fail(BuildErrorCodes.BuildFailed,
                $"AA 导出失败: {ex.Message}", true);
        }

        return BuildTaskResult.Ok(messages);
    }

    /// <summary>写出本次构建的 Addressables 源快照（来自 PrepareAAInput 阶段的扫描结果）。</summary>
    private static void WriteSourceScan(BuildContext ctx, string outputDir)
    {
        List<FileHelper.FileDigest> scan = ctx.Get<List<FileHelper.FileDigest>>(AABuildContextKeys.AASourceScan);
        if (scan == null)
            throw new InvalidOperationException("PrepareAAInput 未写入源快照，无法导出本次构建事实。");

        AASourceScanFile.Write(outputDir, scan);
    }

    /// <summary>本次 Manifest 声明的内容集合（名称为包根相对路径）。</summary>
    private static List<FileHelper.FileDigest> ScanManifestContents(AAManifest manifest)
    {
        var result = new List<FileHelper.FileDigest>();
        if (manifest?.Bundles == null)
            return result;

        for (int i = 0; i < manifest.Bundles.Count; i++)
        {
            BundleInfo bundle = manifest.Bundles[i];
            if (bundle == null || string.IsNullOrEmpty(bundle.BundleName))
                continue;

            result.Add(new FileHelper.FileDigest(
                string.Concat("bundles/", bundle.BundleName),
                bundle.FileHash,
                bundle.FileCRC,
                bundle.FileSize));
        }

        return result;
    }

    /// <summary>摘要事实只来自清单与实际输出，不重新计算内容文件摘要。</summary>
    private static void FillSummary(CompleteBuildSummary summary, AAManifest manifest, BuildContext ctx)
    {
        if (manifest != null)
        {
            summary.Statistics.AssetCount = manifest.AssetEntries != null ? manifest.AssetEntries.Count : 0;
            summary.Statistics.ContentCount = manifest.Bundles != null ? manifest.Bundles.Count : 0;

            var facts = new List<BuildExportWriter.ContentFileDigest>();
            if (manifest.Bundles != null)
            {
                for (int i = 0; i < manifest.Bundles.Count; i++)
                {
                    BundleInfo bundle = manifest.Bundles[i];
                    if (bundle == null || string.IsNullOrEmpty(bundle.BundleName))
                        continue;

                    facts.Add(new BuildExportWriter.ContentFileDigest
                    {
                        FileName = bundle.BundleName,
                        Hash = bundle.FileHash,
                        CRC = bundle.FileCRC,
                        Size = bundle.FileSize
                    });
                }
            }

            BuildExportWriter.AddContentFileDigests(summary, facts);
        }

        BuildExportWriter.AddVerificationMessages(summary, ctx);
    }
}
