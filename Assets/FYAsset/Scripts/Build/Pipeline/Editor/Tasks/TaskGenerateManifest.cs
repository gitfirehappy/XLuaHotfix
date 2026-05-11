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

        // ③ bundleNameToIndex
        var bundleNameToIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < buildResults.Count; i++)
            bundleNameToIndex[buildResults[i].BundleName] = i;

        // ④ BundleBuildInfo → ManifestBundleEntry（基础字段，BundleType/DependBundleIndices/Tags 待填）
        var bundleEntries = new List<ManifestBundleEntry>(buildResults.Count);
        for (int i = 0; i < buildResults.Count; i++)
        {
            var b = buildResults[i];
            string outputDir = cfg.OutputRoot;
            string fileName = b.OutputFileName ?? b.BundleName;
            string filePath = Path.Combine(outputDir, "_temp", fileName);
            if (!File.Exists(filePath))
                return BuildTaskResult.Fail(BuildErrorCodes.BundleFileNotFound,
                    $"Bundle output file not found: '{filePath}'.", true);

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

            if (!bundleNameToIndex.TryGetValue(a.BundleName, out int bundleIndex))
                return BuildTaskResult.Fail(BuildErrorCodes.BundleNotFoundBuild,
                    $"Asset '{a.AssetPath}' references bundle '{a.BundleName}' " +
                    "which is not in BundleBuildResults.", true);

            assetEntries.Add(new ManifestAssetEntry
            {
                EntryId = a.AssetGUID,
                Address = a.Address ?? "",
                PrimaryType = a.PrimaryType ?? "",
                Labels = a.Labels != null ? new List<string>(a.Labels) : new List<string>(),
                SourcePath = a.AssetPath ?? "",
                Group = a.GroupName ?? "",
                AutoAddress = true,
                BundleIndex = bundleIndex
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
                $"ABManifest.Initialize() threw: {ex.Message}", true);
        }

        // ⑩ 写入 Context
        ctx.Set(BuildContextKeys.ABManifest, manifest);

        return BuildTaskResult.Ok(new List<string>
        {
            $"[MANIFEST] {assetEntries.Count} assets, {bundleEntries.Count} bundles generated."
        });
    }

}
