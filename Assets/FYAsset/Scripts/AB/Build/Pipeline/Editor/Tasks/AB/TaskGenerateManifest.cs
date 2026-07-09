using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// ABManifest 生成 Task —— 消费 CollectedAssets + BundleBuildResults，
/// 产出完整的 ABManifest（资产索引 + Bundle 元数据 + 依赖关系 + 分类推断）。
/// 在 TaskBuildBundles 之后、TaskVerifyBuildResult 之前执行。
/// </summary>
public class TaskGenerateManifest : IBuildTask
{
    public string TaskName => "TaskGenerateManifest";
    public string[] DependsOn => new[] { "TaskBuildBundles" };
    public string[] ReadKeys => new[]
    {
        BuildContextKeys.BuildConfig,
        BuildContextKeys.CollectedAssets,
        BuildContextKeys.BundleBuildResults,
        BuildContextKeys.BundleDependencyGraph
    };
    public string[] WriteKeys => new[] { BuildContextKeys.ABManifest };

    public BuildTaskResult Execute(BuildContext ctx)
    {
        var cfg = ctx.Require<BuildConfig>(BuildContextKeys.BuildConfig);
        var collected = ctx.Require<List<CollectedAssetInfo>>(BuildContextKeys.CollectedAssets);
        var buildResults = ctx.Require<List<BundleBuildInfo>>(BuildContextKeys.BundleBuildResults);
        var depGraph = ctx.Get<BundleDependencyGraph>(BuildContextKeys.BundleDependencyGraph);

        var validation = ValidateBundleIdentity(buildResults);
        if (!validation.Success)
            return validation;

        // ③ bundleNameToIndex（依赖图仍以逻辑 BundleName 表达）
        var bundleNameToIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < buildResults.Count; i++)
            bundleNameToIndex[buildResults[i].BundleName] = i;

        var membership = new Dictionary<BuildMembershipKey, int>(BuildMembershipKey.Comparer);
        for (int bundleIndex = 0; bundleIndex < buildResults.Count; bundleIndex++)
        {
            BundleBuildInfo bundleInfo = buildResults[bundleIndex];
            if (bundleInfo.AssetPaths == null)
                continue;

            for (int p = 0; p < bundleInfo.AssetPaths.Count; p++)
            {
                var key = new BuildMembershipKey(bundleInfo.AssetPaths[p], bundleInfo.BundleName);
                if (membership.ContainsKey(key))
                {
                    return BuildTaskResult.Fail(BuildErrorCodes.DuplicateManifestMembership,
                        $"Asset '{bundleInfo.AssetPaths[p]}' 在实际构建结果中重复归属于 Bundle '{bundleInfo.BundleName}'。", true);
                }

                membership[key] = bundleIndex;
            }
        }

        // ④ BundleBuildInfo → ManifestBundleEntry（基础字段，BundleType/DependBundleIndices/Tags 待填）
        var bundleEntries = new List<ManifestBundleEntry>(buildResults.Count);
        for (int i = 0; i < buildResults.Count; i++)
        {
            var b = buildResults[i];
            string outputDir = cfg.OutputRoot;
            string fileName = b.OutputFileName ?? b.BundleName;
            string filePath = FYAssetPathUtility.JoinFilePath(outputDir, "_temp", fileName);
            if (!FileHelper.Exists(filePath))
                return BuildTaskResult.Fail(BuildErrorCodes.BundleFileNotFound,
                    $"Bundle 输出文件不存在: '{filePath}'。", true);

            uint crc = HashGenerator.GenerateFileCRC(filePath);

            bundleEntries.Add(new ManifestBundleEntry
            {
                BundleName = b.OutputFileName ?? b.BundleName,
                FileHash = b.Hash,
                FileCRC = crc,
                FileSize = b.Size,
                Encrypted = false,
                BundleType = "",
                Tags = new List<string>(),
                DependBundleIndices = new int[0]
            });
        }

        // ⑤ CollectedAssetInfo → ManifestAssetEntry
        var assetEntries = new List<ManifestAssetEntry>(collected.Count);
        for (int i = 0; i < collected.Count; i++)
        {
            var a = collected[i];
            if (string.IsNullOrEmpty(a.BundleName))
                continue;

            var membershipKey = new BuildMembershipKey(a.AssetPath, a.BundleName);
            if (!membership.TryGetValue(membershipKey, out int bundleIndex))
                return BuildTaskResult.Fail(BuildErrorCodes.ManifestMembershipMissing,
                    $"Asset '{a.AssetPath}' 无法在 BundleBuildInfo.AssetPaths 中找到实际归属 Bundle '{a.BundleName}'。", true);

            BundleBuildInfo actualBundle = buildResults[bundleIndex];
            if (actualBundle.PayloadKind != a.Classification.PayloadKind)
                return BuildTaskResult.Fail(BuildErrorCodes.ManifestPayloadMismatch,
                    $"Asset '{a.AssetPath}' 的采集 PayloadKind={a.Classification.PayloadKind}，" +
                    $"实际构建 Bundle '{actualBundle.BundleName}' PayloadKind={actualBundle.PayloadKind}。", true);

            assetEntries.Add(new ManifestAssetEntry
            {
                EntryId = a.AssetGUID,
                Address = a.Address ?? "",
                PrimaryType = a.PrimaryType ?? "",
                Labels = a.Labels != null ? new List<string>(a.Labels) : new List<string>(),
                SourcePath = a.AssetPath ?? "",
                Group = a.GroupName ?? "",
                AutoAddress = true,
                BundleIndex = bundleIndex,
                PayloadKind = a.Classification.PayloadKind
            });
        }

        // ⑥ DependBundleIndices 解析
        if (depGraph != null)
        {
            for (int i = 0; i < bundleEntries.Count; i++)
            {
                string logicalName = buildResults[i].BundleName;
                var indices = new List<int>();
                for (int e = 0; e < depGraph.Edges.Count; e++)
                {
                    var edge = depGraph.Edges[e];
                    if (!string.Equals(edge.FromBundle, logicalName, StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (bundleNameToIndex.TryGetValue(edge.ToBundle, out int toIdx))
                        indices.Add(toIdx);
                }
                if (indices.Count > 0)
                    bundleEntries[i].DependBundleIndices = indices.ToArray();
            }
        }

        // ⑦ BundleType 推断（>80% 阈值）
        for (int bi = 0; bi < bundleEntries.Count; bi++)
        {
            string logicalName = buildResults[bi].BundleName;
            var typeCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            int total = 0;
            for (int ai = 0; ai < assetEntries.Count; ai++)
            {
                if (assetEntries[ai].BundleIndex != bi)
                    continue;
                string pt = assetEntries[ai].PrimaryType;
                if (string.IsNullOrEmpty(pt))
                    continue;
                total++;
                typeCounts.TryGetValue(pt, out int cnt);
                typeCounts[pt] = cnt + 1;
            }

            if (total == 0)
            {
                bundleEntries[bi].BundleType = "Mixed";
                continue;
            }

            string dominantType = null;
            int maxCount = 0;
            foreach (var kv in typeCounts)
            {
                if (kv.Value > maxCount)
                {
                    maxCount = kv.Value;
                    dominantType = kv.Key;
                }
            }

            double ratio = (double)maxCount / total;
            bundleEntries[bi].BundleType = ratio > 0.8 ? dominantType : "Mixed";
        }

        // ⑧ 组装 ABManifest（Tags 保留为下载策略标签，不从 asset Labels 自动聚合）
        var manifest = new ABManifest
        {
            PackageName = "MainPackage",
            PackageVersion = cfg.Version,
            BuildTimestamp = DateTime.UtcNow.ToString("o"),
            AssetEntries = assetEntries,
            BundleEntries = bundleEntries
        };

        // ⑨ Initialize() 校验
        try
        {
            manifest.Initialize();
        }
        catch (Exception ex)
        {
            return BuildTaskResult.Fail(BuildErrorCodes.ManifestInitFailed,
                $"ABManifest.Initialize() 执行异常: {ex.Message}", true);
        }

        // ⑩ 写入 Context
        ctx.Set(BuildContextKeys.ABManifest, manifest);

        return BuildTaskResult.Ok(new List<string>
        {
            $"[MANIFEST] {assetEntries.Count} assets, {bundleEntries.Count} bundles generated."
        });
    }

    private static BuildTaskResult ValidateBundleIdentity(List<BundleBuildInfo> buildResults)
    {
        var logicalToOutput = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var physicalNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < buildResults.Count; i++)
        {
            var result = buildResults[i];
            string logicalName = result != null ? result.BundleName : string.Empty;
            string outputName = result != null ? (result.OutputFileName ?? result.BundleName) : string.Empty;

            if (string.IsNullOrEmpty(logicalName))
                return BuildTaskResult.Fail(BuildErrorCodes.DuplicateBundleName,
                    $"BundleBuildResults[{i}] 的逻辑 BundleName 为空。", true);
            if (string.IsNullOrEmpty(outputName))
                return BuildTaskResult.Fail(BuildErrorCodes.DuplicateBundleName,
                    $"BundleBuildResults[{i}] 的输出 BundleName 为空: Logical={logicalName}", true);

            if (logicalToOutput.TryGetValue(logicalName, out string existingOutput))
            {
                if (!string.Equals(existingOutput, outputName, StringComparison.OrdinalIgnoreCase))
                {
                    return BuildTaskResult.Fail(BuildErrorCodes.DuplicateBundleName,
                        $"逻辑 Bundle '{logicalName}' 映射到多个输出文件: '{existingOutput}' / '{outputName}'。Scene 必须保持 PackSeparately + short GUID 唯一 BundleKey。", true);
                }

                return BuildTaskResult.Fail(BuildErrorCodes.DuplicateBundleName,
                    $"BundleBuildResults 中存在重复逻辑 BundleName: '{logicalName}'。", true);
            }
            logicalToOutput[logicalName] = outputName;

            if (!physicalNames.Add(outputName))
            {
                return BuildTaskResult.Fail(BuildErrorCodes.DuplicateBundleName,
                    $"BundleBuildResults 中存在重复输出 BundleName: '{outputName}'。", true);
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
