using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// ABManifest 生成 Task —— 消费 CollectedAssets + BundleBuildResults，
/// 产出完整的 ABManifest（资产索引 + 内容文件事实 + 内容级依赖下标）。
/// 在 BuildABContent 之后、VerifyABContent 之前执行。
/// </summary>
/// <remarks>
/// 构建输出的文件事实来自磁盘；依赖下标来自 Unity 构建报告或复用摘要回放，预期依赖图只用于诊断。
/// </remarks>
public class GenerateABManifestTask : IBuildTask
{
    public string TaskName => "GenerateABManifest";
    public BuildTaskResult Execute(BuildContext ctx)
    {
        var cfg = ctx.Require<BuildConfig>(BuildContextKeys.BuildConfig);
        var collected = ctx.Require<List<CollectedAssetInfo>>(ABBuildContextKeys.CollectedAssets);
        var buildResults = ctx.Require<List<ContentBuildResult>>(ABBuildContextKeys.BundleBuildResults);
        var depGraph = ctx.Get<BundleDependencyGraph>(ABBuildContextKeys.BundleDependencyGraph);

        var validation = ValidateContentIdentity(buildResults);
        if (!validation.Success)
            return validation;

        string tempDir = FYAssetPathUtility.JoinFilePath(cfg.OutputRoot, "_temp");

        var contentEntries = BuildContentEntries(buildResults, tempDir, out BuildTaskResult contentError);
        if (contentError != null)
            return contentError;

        var dependencyError = ApplyDependencyIndices(buildResults, contentEntries);
        if (dependencyError != null)
            return dependencyError;

        var assetEntries = BuildAssetEntries(collected, buildResults, out BuildTaskResult assetError);
        if (assetError != null)
            return assetError;

        var manifest = new ABManifest
        {
            PackageVersion = cfg.Version,
            AssetEntries = assetEntries,
            ContentEntries = contentEntries
        };

        try
        {
            manifest.Initialize();
        }
        catch (Exception ex)
        {
            return BuildTaskResult.Fail(BuildErrorCodes.ManifestInitFailed,
                $"ABManifest.Initialize() 执行异常: {ex.Message}", true);
        }

        ctx.Set(ABBuildContextKeys.ABManifest, manifest);

        var messages = new List<string>
        {
            $"[MANIFEST] {assetEntries.Count} assets, {contentEntries.Count} contents generated."
        };
        messages.AddRange(CollectDependencyDiagnostics(depGraph, buildResults, contentEntries));

        return BuildTaskResult.Ok(messages);
    }

    /// <summary>按实际输出文件生成内容条目；文件缺失即阻断，Hash/CRC/大小取磁盘事实。</summary>
    private static List<ManifestContentEntry> BuildContentEntries(
        List<ContentBuildResult> buildResults,
        string tempDir,
        out BuildTaskResult error)
    {
        error = null;
        var contentEntries = new List<ManifestContentEntry>(buildResults.Count);

        for (int i = 0; i < buildResults.Count; i++)
        {
            ContentBuildResult build = buildResults[i];
            string fileName = build.FileName ?? build.ContentName;
            string filePath = FYAssetPathUtility.JoinFilePath(tempDir, fileName);
            if (!FileHelper.Exists(filePath))
            {
                error = BuildTaskResult.Fail(BuildErrorCodes.BundleFileNotFound,
                    $"内容输出文件不存在: '{filePath}'（内容 '{build.ContentName}'）。", true);
                return null;
            }

            string fileHash;
            uint fileCRC;
            long fileSize;
            try
            {
                HashGenerator.ComputeFileHashAndCRC(filePath, out fileHash, out fileCRC);
                fileSize = new FileInfo(filePath).Length;
            }
            catch (Exception ex)
            {
                error = BuildTaskResult.Fail(BuildErrorCodes.BundleFileNotFound,
                    $"内容输出文件不可读: '{filePath}'（内容 '{build.ContentName}'）: {ex.Message}", true);
                return null;
            }

            contentEntries.Add(new ManifestContentEntry
            {
                FileName = fileName,
                Hash = fileHash,
                CRC = fileCRC,
                Size = fileSize,
                ContentType = build.ContentType,
                DependencyIndices = Array.Empty<int>()
            });
        }

        return contentEntries;
    }

    private static bool TryBuildFileIndex(
        IReadOnlyList<string> orderedFileNames,
        out Dictionary<string, int> indexByFileName,
        out string failureReason)
    {
        indexByFileName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        failureReason = null;
        if (orderedFileNames == null)
        {
            failureReason = "内容文件名列表为空";
            return false;
        }

        for (int i = 0; i < orderedFileNames.Count; i++)
        {
            string fileName = orderedFileNames[i];
            if (string.IsNullOrEmpty(fileName))
            {
                failureReason = $"内容下标 {i} 的输出文件名为空";
                return false;
            }
            if (indexByFileName.ContainsKey(fileName))
            {
                failureReason = $"输出文件名重复: '{fileName}'";
                return false;
            }
            indexByFileName[fileName] = i;
        }
        return true;
    }

    private static bool TryResolveDependencyIndices(
        string contentName,
        IReadOnlyList<string> dependencyFileNames,
        IReadOnlyDictionary<string, int> indexByFileName,
        out int[] indices,
        out string failureReason)
    {
        indices = Array.Empty<int>();
        failureReason = null;
        if (dependencyFileNames == null || dependencyFileNames.Count == 0)
            return true;
        if (indexByFileName == null)
        {
            failureReason = $"内容 '{contentName}' 缺少输出文件名索引";
            return false;
        }

        var resolved = new List<int>(dependencyFileNames.Count);
        for (int i = 0; i < dependencyFileNames.Count; i++)
        {
            string dependency = dependencyFileNames[i];
            if (string.IsNullOrEmpty(dependency)
                || string.Equals(dependency, contentName, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!indexByFileName.TryGetValue(dependency, out int dependencyIndex))
            {
                failureReason = $"内容 '{contentName}' 依赖的输出文件 '{dependency}' 不在本次构建内容集合中";
                return false;
            }
            if (!resolved.Contains(dependencyIndex))
                resolved.Add(dependencyIndex);
        }
        resolved.Sort();
        indices = resolved.ToArray();
        return true;
    }

    /// <summary>把内容级依赖文件名换算成 ContentEntries 下标；依赖名无法解析即阻断构建。</summary>
    private static BuildTaskResult ApplyDependencyIndices(
        List<ContentBuildResult> buildResults,
        List<ManifestContentEntry> contentEntries)
    {
        var fileNames = new List<string>(contentEntries.Count);
        for (int i = 0; i < contentEntries.Count; i++)
            fileNames.Add(contentEntries[i].FileName);

        if (!TryBuildFileIndex(fileNames, out Dictionary<string, int> indexByFileName, out string indexReason))
        {
            return BuildTaskResult.Fail(BuildErrorCodes.ManifestDependencyConflict,
                $"无法建立内容文件名索引: {indexReason}", true);
        }

        for (int i = 0; i < buildResults.Count; i++)
        {
            ContentBuildResult build = buildResults[i];
            if (!TryResolveDependencyIndices(
                    contentEntries[i].FileName, build.DependencyFileNames, indexByFileName,
                    out int[] indices, out string failureReason))
            {
                return BuildTaskResult.Fail(BuildErrorCodes.ManifestDependencyConflict,
                    $"内容 '{build.ContentName}' 的依赖下标无法换算: {failureReason}", true);
            }

            contentEntries[i].DependencyIndices = indices;
        }

        return null;
    }

    /// <summary>
    /// 按实际构建结果生成公共资源条目。隐式依赖只保留在 ContentBuildResult.AssetPaths 中，
    /// 不生成运行时 ManifestAssetEntry。
    /// </summary>
    private static List<ManifestAssetEntry> BuildAssetEntries(
        List<CollectedAssetInfo> collected,
        List<ContentBuildResult> buildResults,
        out BuildTaskResult error)
    {
        error = null;

        // 归属事实来自构建结果的实际成员列表，预期归属只用于诊断。
        var membership = new Dictionary<BuildMembershipKey, int>(BuildMembershipKey.Comparer);
        for (int contentIndex = 0; contentIndex < buildResults.Count; contentIndex++)
        {
            ContentBuildResult build = buildResults[contentIndex];
            if (build.AssetPaths == null)
                continue;

            for (int p = 0; p < build.AssetPaths.Count; p++)
            {
                var key = new BuildMembershipKey(build.AssetPaths[p], build.ContentName);
                if (membership.ContainsKey(key))
                {
                    error = BuildTaskResult.Fail(BuildErrorCodes.DuplicateManifestMembership,
                        $"Asset '{build.AssetPaths[p]}' 在实际构建结果中重复归属于内容 '{build.ContentName}'。", true);
                    return null;
                }

                membership[key] = contentIndex;
            }
        }

        var assetEntries = new List<ManifestAssetEntry>(collected.Count);
        for (int i = 0; i < collected.Count; i++)
        {
            CollectedAssetInfo asset = collected[i];
            if (string.IsNullOrEmpty(asset.ContentName))
                continue;

            if (!asset.IsPublic)
                continue;

            var membershipKey = new BuildMembershipKey(asset.AssetPath, asset.ContentName);
            if (!membership.TryGetValue(membershipKey, out int contentIndex))
            {
                error = BuildTaskResult.Fail(BuildErrorCodes.ManifestMembershipMissing,
                    $"Asset '{asset.AssetPath}' 无法在 ContentBuildResult.AssetPaths 中找到实际归属内容 '{asset.ContentName}'。", true);
                return null;
            }

            ContentBuildResult actualBuild = buildResults[contentIndex];
            if (actualBuild.ContentType != asset.ContentType)
            {
                error = BuildTaskResult.Fail(BuildErrorCodes.ManifestPayloadMismatch,
                    $"Asset '{asset.AssetPath}' 的采集 ContentType={asset.ContentType}，" +
                    $"实际构建内容 '{actualBuild.ContentName}' ContentType={actualBuild.ContentType}。", true);
                return null;
            }

            assetEntries.Add(new ManifestAssetEntry
            {
                Address = asset.Address ?? "",
                AssetType = asset.AssetType ?? "",
                Labels = asset.Labels != null ? new List<string>(asset.Labels) : new List<string>(),
                AssetPath = asset.AssetPath ?? "",
                ContentIndex = contentIndex
            });
        }

        return assetEntries;
    }

    /// <summary>
    /// 比对预期依赖与 Unity 实际依赖，只输出诊断，不改写依赖下标。
    /// </summary>
    private static List<string> CollectDependencyDiagnostics(
        BundleDependencyGraph depGraph,
        List<ContentBuildResult> buildResults,
        List<ManifestContentEntry> contentEntries)
    {
        var diagnostics = new List<string>();
        if (depGraph == null)
            return diagnostics;

        Dictionary<string, HashSet<string>> planned = depGraph.GetDependencyMap();
        if (planned == null || planned.Count == 0)
            return diagnostics;

        for (int i = 0; i < contentEntries.Count; i++)
        {
            string contentName = buildResults[i].ContentName;
            var actual = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int[] indices = contentEntries[i].DependencyIndices;
            for (int d = 0; d < indices.Length; d++)
            {
                int dependencyIndex = indices[d];
                if (dependencyIndex >= 0 && dependencyIndex < contentEntries.Count)
                    actual.Add(buildResults[dependencyIndex].ContentName);
            }

            if (!planned.TryGetValue(contentName, out HashSet<string> expected) || expected == null)
            {
                if (actual.Count > 0)
                {
                    diagnostics.Add(
                        $"[DEPENDENCY] 内容 '{contentName}' 的实际依赖未出现在预期依赖图中: [{string.Join(", ", actual)}]");
                }

                continue;
            }

            var missing = new List<string>();
            foreach (string dependency in expected)
            {
                if (!string.IsNullOrEmpty(dependency) && !actual.Contains(dependency))
                    missing.Add(dependency);
            }

            var unexpected = new List<string>();
            foreach (string dependency in actual)
            {
                if (!expected.Contains(dependency))
                    unexpected.Add(dependency);
            }

            if (missing.Count > 0 || unexpected.Count > 0)
            {
                diagnostics.Add(
                    $"[DEPENDENCY] 内容 '{contentName}' 的实际依赖与预期依赖图不一致。" +
                    $"预期依赖图缺少实际依赖: [{string.Join(", ", unexpected)}]；实际缺少预期依赖: [{string.Join(", ", missing)}]。" +
                    "依赖下标以 Unity AssetBundleManifest 为准，本条只是诊断。");
            }
        }

        return diagnostics;
    }

    /// <summary>构建结果的内容身份校验：逻辑名与输出文件名都必须唯一且非空。</summary>
    private static BuildTaskResult ValidateContentIdentity(List<ContentBuildResult> buildResults)
    {
        var logicalToOutput = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var physicalNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < buildResults.Count; i++)
        {
            var result = buildResults[i];
            string logicalName = result != null ? result.ContentName : string.Empty;
            string outputName = result != null ? (result.FileName ?? result.ContentName) : string.Empty;

            if (string.IsNullOrEmpty(logicalName))
                return BuildTaskResult.Fail(BuildErrorCodes.DuplicateBundleName,
                    $"BundleBuildResults[{i}] 的逻辑 ContentName 为空。", true);
            if (string.IsNullOrEmpty(outputName))
                return BuildTaskResult.Fail(BuildErrorCodes.DuplicateBundleName,
                    $"BundleBuildResults[{i}] 的输出 ContentName 为空: Logical={logicalName}", true);

            if (logicalToOutput.TryGetValue(logicalName, out string existingOutput))
            {
                if (!string.Equals(existingOutput, outputName, StringComparison.OrdinalIgnoreCase))
                {
                    return BuildTaskResult.Fail(BuildErrorCodes.DuplicateBundleName,
                        $"逻辑 Bundle '{logicalName}' 映射到多个输出文件: '{existingOutput}' / '{outputName}'。Scene 必须保持 PackSeparately + short GUID 唯一 BundleKey。", true);
                }

                return BuildTaskResult.Fail(BuildErrorCodes.DuplicateBundleName,
                    $"BundleBuildResults 中存在重复逻辑 ContentName: '{logicalName}'。", true);
            }
            logicalToOutput[logicalName] = outputName;

            if (!physicalNames.Add(outputName))
            {
                return BuildTaskResult.Fail(BuildErrorCodes.DuplicateBundleName,
                    $"BundleBuildResults 中存在重复输出 ContentName: '{outputName}'。", true);
            }
        }

        return BuildTaskResult.Ok();
    }

    private readonly struct BuildMembershipKey : IEquatable<BuildMembershipKey>
    {
        public static readonly IEqualityComparer<BuildMembershipKey> Comparer = new KeyComparer();

        public readonly string AssetPath;
        public readonly string LogicalBundleName;

        public BuildMembershipKey(string assetPath, string logicalBundleName)
        {
            AssetPath = assetPath ?? string.Empty;
            LogicalBundleName = logicalBundleName ?? string.Empty;
        }

        public bool Equals(BuildMembershipKey other)
        {
            return string.Equals(AssetPath, other.AssetPath, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(LogicalBundleName, other.LogicalBundleName, StringComparison.OrdinalIgnoreCase);
        }

        public override bool Equals(object obj)
        {
            return obj is BuildMembershipKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = StringComparer.OrdinalIgnoreCase.GetHashCode(AssetPath);
                hash = (hash * 397) ^ StringComparer.OrdinalIgnoreCase.GetHashCode(LogicalBundleName);
                return hash;
            }
        }

        private sealed class KeyComparer : IEqualityComparer<BuildMembershipKey>
        {
            public bool Equals(BuildMembershipKey x, BuildMembershipKey y)
            {
                return x.Equals(y);
            }

            public int GetHashCode(BuildMembershipKey obj)
            {
                return obj.GetHashCode();
            }
        }
    }
}
