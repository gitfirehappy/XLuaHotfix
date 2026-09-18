#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// AB 构建管线：导出构建输出。
/// 1. 计算本次交付的内容集合（Hotfix 为相对作用域最近成功 Full 的变化内容）；
/// 2. 按构建类型把内容文件放进包目录，写出 ABManifest 与构建摘要，清理临时产物；
/// 3. 写模式输出（Full/Standalone 的包内 BuildIndex）。
/// </summary>
/// <remarks>
/// Full/Standalone 输出完整包；Hotfix 以作用域最近成功 Full 为基准，只交付 Added/Modified 内容。
/// 基准解析失败即中止；attempt 根在整体成功后才提升为正式输出。
/// </remarks>
public class ExportABOutputTask : IBuildTask
{
    public string TaskName => "ExportABOutput";

    public BuildTaskResult Execute(BuildRunContext ctx)
    {
        var cfg = ctx.Require<BuildConfig>(BuildContextKeys.BuildConfig);
        var request = ctx.Require<BuildRequest>(BuildContextKeys.BuildRequest);
        var buildType = request.BuildType;
        var manifest = ctx.Require<ABManifest>(ABBuildContextKeys.ABManifest);
        var buildResults = ctx.Require<List<ContentBuildResult>>(ABBuildContextKeys.BundleBuildResults);
        string outputDir = request.TemporaryOutputDir;
        string bundleOutputDir = request.BundlesDir;

        BuildTaskResult planResult = ComputeDeliveryContents(ctx, request, buildType, manifest, out List<ManifestContentEntry> contentsToCopy);
        if (!planResult.Success)
            return planResult;

        var messages = new List<string>(planResult.Warnings ?? new List<string>());

        try
        {
            if (FileHelper.DirectoryExists(outputDir))
                FileHelper.TryDeleteDirectory(outputDir, true);
            FileHelper.EnsureDirectory(outputDir);
            FileHelper.EnsureDirectory(bundleOutputDir);
        }
        catch (Exception ex)
        {
            return BuildTaskResult.Fail(BuildErrorCodes.BuildFailed,
                $"AB 输出目录准备失败: {ex.Message}", true);
        }

        string tempDir = FYAssetPathUtility.JoinFilePath(cfg.OutputRoot, "_temp");

        // Full 与 Standalone 以整包内容为口径，Hotfix 只算本次交付集合
        BuildTaskResult sizeValidation = ValidatePackageSize(
            buildType == BuildType.Hotfix ? contentsToCopy : manifest.ContentEntries);
        if (!sizeValidation.Success)
            return sizeValidation;

        BuildTaskResult copyResult = CopyDeliveryContents(
            ctx, request, buildType, contentsToCopy, tempDir, bundleOutputDir, out List<string> copiedFiles);
        if (!copyResult.Success)
            return copyResult;

        messages.AddRange(copyResult.Warnings ?? new List<string>());

        var manifestWrite = WriteManifest(manifest, outputDir, out string manifestDescription);
        if (!manifestWrite.Success)
            return manifestWrite;

        long totalSize = 0;
        for (int i = 0; i < buildResults.Count; i++)
            totalSize += buildResults[i].Size;

        DateTime startedAt = ctx.Get<DateTime>(BuildContextKeys.PipelineStartedAtUtc);
        if (startedAt == default)
            startedAt = DateTime.UtcNow;

        try
        {
            CompleteBuildSummary summary = BuildExportWriter.CreateSummary(ctx, startedAt);
            FillSummary(summary, manifest, buildResults, totalSize, ctx);

            string buildIndexPath = BuildExportWriter.WritePackageBuildIndex(ctx, outputDir);
            if (!string.IsNullOrEmpty(buildIndexPath))
                messages.Add($"[AB EXPORT] BuildIndex: {buildIndexPath}");
            messages.Add($"[AB EXPORT] Mode: {summary.BuildType}/{summary.RuntimeMode}");

            summary.Duration = DateTime.UtcNow - startedAt;
            summary.FinishedAtUtc = DateTime.UtcNow;
            ctx.Set(BuildContextKeys.BuildSummary, summary);
            messages.Add($"[AB EXPORT] Summary: {summary.BuildId}");
        }
        catch (Exception ex)
        {
            return BuildTaskResult.Fail(BuildErrorCodes.BuildFailed,
                $"AB 构建摘要写入失败: {ex.Message}", true);
        }

        CleanupTempDirectory(tempDir);

        messages.Add($"[ORGANIZE] {copiedFiles.Count}/{manifest.ContentEntries.Count} contents → {bundleOutputDir}");
        messages.Add($"[AB MANIFEST] {manifestDescription}");
        return BuildTaskResult.Ok(messages);
    }

    /// <summary>
    /// 计算本次交付内容集合：
    /// Full/Standalone 交付整包；Hotfix 交付“相对最近成功 Full 的新增与修改内容”。
    /// </summary>
    /// <remarks>
    /// 基准是 Summary Index 作用域指向的成功 Full（构建事实），不依赖前一个 Hotfix 包；
    /// 完整目标 Manifest 始终随包交付，包内只放变化内容，未变化内容由客户端从当前包复用。
    /// </remarks>
    private static BuildTaskResult ComputeDeliveryContents(
        BuildRunContext ctx,
        BuildRequest request,
        BuildType buildType,
        ABManifest manifest,
        out List<ManifestContentEntry> deliveryContents)
    {
        if (buildType != BuildType.Hotfix)
        {
            ctx.Set(ABBuildContextKeys.ABDeliveryContents, new List<ManifestContentEntry>());
            deliveryContents = manifest.ContentEntries ?? new List<ManifestContentEntry>();
            UnityEngine.Debug.Log($"[{nameof(ExportABOutputTask)}] {buildType} build 交付整包内容: {deliveryContents.Count}");
            return BuildTaskResult.Ok(new List<string>
            {
                $"[AB DIFF] {buildType} delivery=all({deliveryContents.Count})"
            });
        }

        try
        {
            var cfg = ctx.Require<BuildConfig>(BuildContextKeys.BuildConfig);
            string platform = cfg.TargetPlatform.ToString();
            string channel = request.Version.Channel ?? string.Empty;

            if (!HotfixBaselineResolver.TryResolve(request.BackendKey, platform, channel,
                    out string baseFullDir, out string baseFullBuildId, out string baselineError))
            {
                deliveryContents = new List<ManifestContentEntry>();
                return BuildTaskResult.Fail(BuildErrorCodes.BuildFailed,
                    $"Hotfix 基准 Full 解析失败: {baselineError}", true);
            }

            if (!ABPackageManifestReader.Instance.TryReadContentDigests(
                    baseFullDir, out IReadOnlyList<FileHelper.FileDigest> baseContents, out string readError))
            {
                deliveryContents = new List<ManifestContentEntry>();
                return BuildTaskResult.Fail(BuildErrorCodes.BuildFailed,
                    $"基准 Full Manifest 不可读: {readError}", true);
            }

            List<FileHelper.FileDigest> currentContents = ScanManifestContents(manifest);
            FileHelper.ComputeDiff(baseContents, currentContents,
                out List<FileHelper.FileDigest> added,
                out List<FileHelper.FileDigest> modified,
                out List<FileHelper.FileDigest> unchanged,
                out List<string> removed);
            deliveryContents = MapChangedContents(manifest, added, modified);

            ctx.Set(ABBuildContextKeys.ABDeliveryContents, deliveryContents);
            UnityEngine.Debug.Log($"[{nameof(ExportABOutputTask)}] AB Hotfix 相对 Full 差异完成: Base={baseFullBuildId}, "
                                  + $"Added={added.Count}, Modified={modified.Count}, "
                                  + $"Unchanged={unchanged.Count}, Delivery={deliveryContents.Count}");
            return BuildTaskResult.Ok(new List<string>
            {
                $"[AB DIFF] base={baseFullBuildId} added={added.Count} modified={modified.Count} "
                + $"unchanged={unchanged.Count} removed={removed.Count} delivery={deliveryContents.Count}"
            });
        }
        catch (Exception ex)
        {
            deliveryContents = new List<ManifestContentEntry>();
            UnityEngine.Debug.LogError($"[{nameof(ExportABOutputTask)}] AB Hotfix 差异计算失败: {ex}");
            return BuildTaskResult.Fail(BuildErrorCodes.BuildFailed,
                $"AB Hotfix 差异计算失败: {ex.Message}", true);
        }
    }

    /// <summary>把差异集合映射回本次 Manifest 的内容条目；新增/修改名称必须能在本次 Manifest 中找到。</summary>
    private static List<ManifestContentEntry> MapChangedContents(
        ABManifest manifest,
        IReadOnlyList<FileHelper.FileDigest> added,
        IReadOnlyList<FileHelper.FileDigest> modified)
    {
        var byRelative = new Dictionary<string, ManifestContentEntry>(StringComparer.Ordinal);
        List<ManifestContentEntry> entries = manifest.ContentEntries;
        if (entries != null)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                ManifestContentEntry entry = entries[i];
                if (entry == null || string.IsNullOrEmpty(entry.FileName))
                    continue;

                string relative = ToContentRelativeName(entry.FileName);
                if (!byRelative.ContainsKey(relative))
                    byRelative.Add(relative, entry);
            }
        }

        var result = new List<ManifestContentEntry>(added.Count + modified.Count);
        AppendChanged(added, byRelative, result);
        AppendChanged(modified, byRelative, result);
        return result;
    }

    private static void AppendChanged(
        IReadOnlyList<FileHelper.FileDigest> files,
        Dictionary<string, ManifestContentEntry> byRelative,
        List<ManifestContentEntry> result)
    {
        for (int i = 0; i < files.Count; i++)
        {
            FileHelper.FileDigest file = files[i];
            if (byRelative.TryGetValue(file.Name, out ManifestContentEntry entry))
            {
                result.Add(entry);
                continue;
            }

            throw new InvalidOperationException(
                $"差异内容在本次 Manifest 中不存在，无法交付: {file.Name}");
        }
    }

    /// <summary>内容文件名 → 包根相对路径。</summary>
    private static string ToContentRelativeName(string fileName) =>
        string.Concat(FYAssetSettings.BUNDLES_DIRECTORY_NAME, "/", fileName);

    /// <summary>包根相对路径 → 内容文件名；不在内容目录内时返回 null。</summary>
    private static string ToContentFileName(string relativeName)
    {
        string prefix = FYAssetSettings.BUNDLES_DIRECTORY_NAME + "/";
        return !string.IsNullOrEmpty(relativeName) && relativeName.StartsWith(prefix, StringComparison.Ordinal)
            ? relativeName.Substring(prefix.Length)
            : null;
    }

    /// <summary>把交付内容放进 bundle 输出目录：整包模式全量复制，Hotfix 模式按目标集合落地。</summary>
    private static BuildTaskResult CopyDeliveryContents(
        BuildRunContext ctx,
        BuildRequest request,
        BuildType buildType,
        List<ManifestContentEntry> contentsToCopy,
        string tempDir,
        string bundleOutputDir,
        out List<string> copiedFiles)
    {
        copiedFiles = new List<string>();

        for (int i = 0; i < contentsToCopy.Count; i++)
        {
            ManifestContentEntry content = contentsToCopy[i];
            if (content == null)
                continue;

            string fileName = content.FileName;
            string srcPath = FYAssetPathUtility.JoinFilePath(tempDir, fileName);
            string destPath = FYAssetPathUtility.JoinFilePath(bundleOutputDir, fileName);
            if (!FileHelper.Exists(srcPath))
            {
                return BuildTaskResult.Fail(BuildErrorCodes.BundleFileNotFound,
                    $"AB 最终输出缺少内容文件: '{srcPath}'。", true);
            }

            FileHelper.CopyFile(srcPath, destPath, true);
            copiedFiles.Add(fileName);
        }

        return BuildTaskResult.Ok();
    }

    /// <summary>本次 Manifest 声明的内容集合（名称为包根相对路径）。</summary>
    private static List<FileHelper.FileDigest> ScanManifestContents(ABManifest manifest)
    {
        var result = new List<FileHelper.FileDigest>();
        if (manifest?.ContentEntries == null)
            return result;

        for (int i = 0; i < manifest.ContentEntries.Count; i++)
        {
            ManifestContentEntry entry = manifest.ContentEntries[i];
            if (entry == null || string.IsNullOrEmpty(entry.FileName))
                continue;

            result.Add(new FileHelper.FileDigest(
                ToContentRelativeName(entry.FileName),
                entry.Hash,
                entry.CRC,
                entry.Size));
        }

        return result;
    }

    /// <summary>摘要事实只来自清单与实际构建结果，不重新计算内容文件摘要。</summary>
    private static void FillSummary(
        CompleteBuildSummary summary,
        ABManifest manifest,
        List<ContentBuildResult> buildResults,
        long totalSize,
        BuildRunContext ctx)
    {
        summary.Statistics.AssetCount = manifest.AssetEntries != null ? manifest.AssetEntries.Count : 0;
        summary.Statistics.ContentCount = manifest.ContentEntries != null ? manifest.ContentEntries.Count : 0;

        // 构建配方与复用判定同源：由 BuildABContentTask 计算并写入 Context，缺失时留空（仅损失复用优化）。
        summary.BuildRecipeFingerprint = ctx.Get<string>(ABBuildContextKeys.BuildRecipeFingerprint) ?? string.Empty;

        var facts = new List<BuildExportWriter.ContentFileDigest>();
        if (manifest.ContentEntries != null)
        {
            for (int i = 0; i < manifest.ContentEntries.Count; i++)
            {
                ManifestContentEntry content = manifest.ContentEntries[i];
                if (content == null || string.IsNullOrEmpty(content.FileName))
                    continue;

                facts.Add(new BuildExportWriter.ContentFileDigest
                {
                    FileName = content.FileName,
                    Hash = content.Hash,
                    CRC = content.CRC,
                    Size = content.Size
                });
            }
        }

        BuildExportWriter.AddContentFileDigests(summary, facts);
        BuildExportWriter.AddVerificationMessages(summary, ctx);

        // 内容复用事实：后续构建靠它按内容身份与输入指纹定位历史制品，并回放依赖输出文件名。
        summary.ContentReuseRecords = new List<ContentReuseRecord>(buildResults.Count);
        for (int i = 0; i < buildResults.Count; i++)
        {
            ContentBuildResult bundle = buildResults[i];
            if (bundle == null || string.IsNullOrEmpty(bundle.FileName))
                continue;

            summary.ContentReuseRecords.Add(new ContentReuseRecord
            {
                ContentName = bundle.ContentName ?? string.Empty,
                InputFingerprint = bundle.InputFingerprint ?? string.Empty,
                FileName = bundle.FileName,
                Hash = bundle.Hash ?? string.Empty,
                CRC = bundle.CRC,
                Size = bundle.Size,
                DependencyFileNames = bundle.DependencyFileNames != null
                    ? new List<string>(bundle.DependencyFileNames)
                    : new List<string>()
            });
        }

        summary.AddMessage(BuildMessage.Warning(
            BuildVerificationIssueCodes.CountCrossCheck,
            $"内容文件总数 {buildResults.Count}，合计 {totalSize} 字节。",
            nameof(ExportABOutputTask)));
    }

    /// <summary>
    /// 热更包大小校验。口径由调用方按构建类型给出，
    /// 与删除前的独立写包 Task 保持同一语义。
    /// </summary>
    private static BuildTaskResult ValidatePackageSize(List<ManifestContentEntry> sizeScope)
    {
        long packageSize = 0;
        if (sizeScope != null)
        {
            for (int i = 0; i < sizeScope.Count; i++)
                packageSize += sizeScope[i] != null ? sizeScope[i].Size : 0;
        }

        if (!FileHelper.IsWithinSizeLimit(packageSize, FYAssetABSettings.Instance.MaxHotfixSizeBytes))
        {
            string message = $"AB 热更包大小超过阈值: {FileHelper.FormatBytes(packageSize)} >= {FileHelper.FormatBytes(FYAssetABSettings.Instance.MaxHotfixSizeBytes)}";
            Debug.LogWarning($"[{nameof(ExportABOutputTask)}] {message}");
            if (Application.isBatchMode)
                throw new InvalidOperationException(message);
            EditorUtility.DisplayDialog("AB 热更包过大", message, "OK");
            return BuildTaskResult.Fail(BuildErrorCodes.VerificationFailed,
                "AB 热更包大小超过阈值，Manifest 发布已中止。", true);
        }

        return BuildTaskResult.Ok();
    }

    /// <summary>
    /// 按 ManifestOutputFormat 把清单写入包目录。
    /// 未选择的格式会删除同名旧文件，避免上一次构建的清单残留在包内。
    /// </summary>
    private static BuildTaskResult WriteManifest(ABManifest manifest, string outputDir, out string description)
    {
        ManifestOutputFormat outputFormat = FYAssetABSettings.Instance.ManifestOutputFormat;
        string manifestPath = FYAssetPathUtility.JoinFilePath(outputDir, FYAssetSettings.MANIFEST_FILE_NAME);
        string manifestBinPath = FYAssetPathUtility.JoinFilePath(outputDir, FYAssetSettings.MANIFEST_FILE_NAME_BIN);

        FileHelper.EnsureDirectory(outputDir);

        if (outputFormat != ManifestOutputFormat.BinaryOnly)
        {
            FileHelper.WriteAllTextAtomic(manifestPath, manifest.SerializeToJson());
        }
        else
        {
            FileHelper.TryDelete(manifestPath);
        }

        if (outputFormat != ManifestOutputFormat.JsonOnly)
            SerializationUtility.WriteToFile(manifestBinPath, manifest, "binary", false);
        else
            FileHelper.TryDelete(manifestBinPath);

        description = $"JSON: {outputFormat != ManifestOutputFormat.BinaryOnly}, Binary: {outputFormat != ManifestOutputFormat.JsonOnly}";
        return BuildTaskResult.Ok();
    }

    private static void CleanupTempDirectory(string tempDir)
    {
        if (!FileHelper.DirectoryExists(tempDir))
            return;

        try
        {
            FileHelper.TryDeleteDirectory(tempDir, true);
        }
        catch (IOException)
        {
            // 尽力清理，失败忽略：_temp 不是交付物。
        }
    }

    private static long SumContentSize(IList<ManifestContentEntry> contents)
    {
        long total = 0;
        if (contents == null)
            return total;
        for (int i = 0; i < contents.Count; i++)
            total += contents[i] != null ? contents[i].Size : 0;
        return total;
    }
}
#endif
