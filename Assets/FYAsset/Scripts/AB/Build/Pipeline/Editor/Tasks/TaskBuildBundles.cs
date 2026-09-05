using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// AssetBundle 构建 Task —— 按 BundleName 分组，按 PayloadKind 分流，
/// 调用 Unity BuildPipeline.BuildAssetBundles 产出 .bundle 文件。
/// 压缩模式从 BuildPipelineConfig SO 读取（默认 LZ4）。
/// </summary>
public class TaskBuildBundles : IBuildTask
{
    public string TaskName => "TaskBuildBundles";
    /// <summary>
    /// AssetBundle 构建执行。
    /// 流程：按 BundleName 分组 -> 按 PayloadKind 分流（Serialized 走 BuildPipeline / Scene 独立打包 / RawFile 直接拷贝）
    /// -> 调用 Unity BuildPipeline.BuildAssetBundles -> 收集 BundleBuildInfo -> 写入 BuildContext。
    /// </summary>
    public BuildTaskResult Execute(BuildContext ctx)
    {
        var cfg = ctx.Require<BuildConfig>(BuildContextKeys.BuildConfig);
        var assets = ctx.Require<List<CollectedAssetInfo>>(ABBuildContextKeys.CollectedAssets);
        string outputRoot = cfg.OutputRoot;
        var platform = cfg.TargetPlatform;

        // 读取压缩配置
        var config = AssetDatabase.LoadAssetAtPath<BuildPipelineConfig>(
            FYAssetABSettings.Instance.BuildPipelineConfigPath);
        BundleCompression compression = config != null
            ? config.BundleCompression
            : BundleCompression.LZ4;

        var options = compression switch
        {
            BundleCompression.LZMA => BuildAssetBundleOptions.None,
            BundleCompression.Uncompressed => BuildAssetBundleOptions.UncompressedAssetBundle,
            _ => BuildAssetBundleOptions.ChunkBasedCompression
        };

        // 按 BundleName 分组
        var groups = new Dictionary<string, List<CollectedAssetInfo>>(StringComparer.Ordinal);
        for (int i = 0; i < assets.Count; i++)
        {
            string name = assets[i].BundleName;
            if (string.IsNullOrEmpty(name))
                continue;

            if (!groups.TryGetValue(name, out var list))
            {
                list = new List<CollectedAssetInfo>();
                groups[name] = list;
            }
            list.Add(assets[i]);
        }

        // 构建 AssetBundleBuild[] + 收集 RawFile
        var builds = new List<AssetBundleBuild>();
        var rawFileEntries = new List<(string BundleName, string assetPath)>();

        foreach (var kv in groups)
        {
            string bundleName = kv.Key;
            var assetList = kv.Value;

            var validation = ValidateBundleGroup(bundleName, assetList);
            if (!validation.Success)
                return validation;

            var serializedPaths = new List<string>();
            var scenePaths = new List<string>();

            for (int i = 0; i < assetList.Count; i++)
            {
                var a = assetList[i];
                switch (a.Classification.PayloadKind)
                {
                    case EPayloadKind.RawFile:
                        rawFileEntries.Add((bundleName, a.AssetPath));
                        break;
                    case EPayloadKind.Scene:
                        scenePaths.Add(a.AssetPath);
                        break;
                    default:
                        var entryValidation = ValidateSerializedBundleEntry(a.AssetPath);
                        if (!entryValidation.Success)
                            return entryValidation;

                        serializedPaths.Add(a.AssetPath);
                        break;
                }
            }

            if (serializedPaths.Count > 0)
            {
                builds.Add(new AssetBundleBuild
                {
                    assetBundleName = bundleName,
                    assetNames = serializedPaths.ToArray()
                });
            }

            // Scene 必须独立打包（Unity 要求单独一个 AB 入口）
            for (int s = 0; s < scenePaths.Count; s++)
            {
                builds.Add(new AssetBundleBuild
                {
                    assetBundleName = bundleName + "_scene_" + s,
                    assetNames = new[] { scenePaths[s] }
                });
            }
        }

        // 创建临时构建目录
        string tempDir = FYAssetPathUtility.JoinFilePath(outputRoot, "_temp");
        FileHelper.EnsureDirectory(tempDir);

        // 调用 Unity BuildPipeline
        AssetBundleManifest unityManifest;
        if (builds.Count > 0)
        {
            unityManifest = BuildPipeline.BuildAssetBundles(tempDir, builds.ToArray(), options, platform);
            if (unityManifest == null)
                return BuildTaskResult.Fail(BuildErrorCodes.BuildFailed,
                    "BuildPipeline.BuildAssetBundles 返回了 null。", true);
        }
        else
        {
            // 无 Serialized/Scene 资产，跳过 AB 构建直接处理 RawFile
            unityManifest = null;
        }

        // 收集 BundleBuildInfo
        var results = new List<BundleBuildInfo>();
        var processedOutputs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 预计算每个逻辑名的资产路径分类
        var groupSerializedPaths = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var groupScenePaths = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var kv in groups)
        {
            var serialized = new List<string>();
            var scenes = new List<string>();
            for (int i = 0; i < kv.Value.Count; i++)
            {
                var a = kv.Value[i];
                switch (a.Classification.PayloadKind)
                {
                    case EPayloadKind.Scene:
                        scenes.Add(a.AssetPath);
                        break;
                    case EPayloadKind.RawFile:
                        break;
                    default:
                        serialized.Add(a.AssetPath);
                        break;
                }
            }
            groupSerializedPaths[kv.Key] = serialized;
            groupScenePaths[kv.Key] = scenes;
        }

        // 构建 scene 输出名 -> (logicalName, sceneIndex) 索引
        var sceneOutputIndex = new Dictionary<string, (string logicalName, int sceneIndex)>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in groupScenePaths)
        {
            for (int s = 0; s < kv.Value.Count; s++)
                sceneOutputIndex[kv.Key + "_scene_" + s] = (kv.Key, s);
        }

        if (unityManifest != null)
        {
            string[] allBundles = unityManifest.GetAllAssetBundles();
            foreach (var outputName in allBundles)
            {
                string filePath = FYAssetPathUtility.JoinFilePath(tempDir, outputName);
                var info = new FileInfo(filePath);
                string hash = HashGenerator.GenerateFileHash(filePath);
                long size = info.Exists ? info.Length : 0;

                if (sceneOutputIndex.TryGetValue(outputName, out var sceneInfo))
                {
                    var scenePaths = groupScenePaths[sceneInfo.logicalName];
                    results.Add(new BundleBuildInfo
                    {
                        BundleName = sceneInfo.logicalName,
                        OutputFileName = outputName,
                        Hash = hash,
                        Size = size,
                        AssetPaths = new List<string> { scenePaths[sceneInfo.sceneIndex] },
                        PayloadKind = EPayloadKind.Scene
                    });
                }
                else
                {
                    // 序列化 bundle — 按前缀匹配回逻辑名
                    string matchedLogical = null;
                    foreach (var logicalName in groups.Keys)
                    {
                        if (outputName.StartsWith(logicalName, StringComparison.OrdinalIgnoreCase))
                        {
                            matchedLogical = logicalName;
                            break;
                        }
                    }

                    if (matchedLogical != null)
                    {
                        results.Add(new BundleBuildInfo
                        {
                            BundleName = matchedLogical,
                            OutputFileName = outputName,
                            Hash = hash,
                            Size = size,
                            AssetPaths = new List<string>(groupSerializedPaths[matchedLogical]),
                            PayloadKind = EPayloadKind.Serialized
                        });
                    }
                }

                processedOutputs.Add(outputName);
            }
        }

        // RawFile 直接文件拷贝（检测多文件冲突）
        var rawBundleFileCount = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int r = 0; r < rawFileEntries.Count; r++)
        {
            string bundleName = rawFileEntries[r].BundleName;
            if (!rawBundleFileCount.ContainsKey(bundleName))
                rawBundleFileCount[bundleName] = 0;
            rawBundleFileCount[bundleName]++;
        }

        for (int r = 0; r < rawFileEntries.Count; r++)
        {
            var (bundleName, assetPath) = rawFileEntries[r];

            if (rawBundleFileCount[bundleName] > 1)
                return BuildTaskResult.Fail(BuildErrorCodes.RawfileMultiAsset,
                    $"Bundle '{bundleName}' 包含 {rawBundleFileCount[bundleName]} 个 RawFile，" +
                    $"每个 Bundle 仅支持一个 RawFile。示例 Asset: '{assetPath}'。", true);

            if (processedOutputs.Contains(bundleName))
            {
                return BuildTaskResult.Fail(BuildErrorCodes.RawfilePayloadConflict,
                    $"Bundle '{bundleName}' 同时包含 RawFile 与 Serialized/Scene 输出路线。RawFile Asset '{assetPath}' " +
                    "不会被 Unity AssetBundle 构建流程写入已存在的同名输出；请调整分组、BundlePackingMode 或 PayloadKind 配置。", true);
            }

            string destPath = FYAssetPathUtility.JoinFilePath(tempDir, bundleName);
            try
            {
                FileHelper.CopyFile(assetPath, destPath, true);
            }
            catch (Exception ex)
            {
                return BuildTaskResult.Fail(BuildErrorCodes.RawfileCopyFailed,
                    $"文件拷贝失败 '{assetPath}' -> '{destPath}': {ex.Message}", true);
            }

            var destInfo = new FileInfo(destPath);
            string hash = HashGenerator.GenerateFileHash(destPath);
            results.Add(new BundleBuildInfo
            {
                BundleName = bundleName,
                OutputFileName = bundleName,
                Hash = hash,
                Size = destInfo.Exists ? destInfo.Length : 0,
                AssetPaths = new List<string> { assetPath },
                PayloadKind = EPayloadKind.RawFile
            });

            processedOutputs.Add(bundleName);
        }

        ctx.Set(ABBuildContextKeys.BundleBuildResults, results);
        return BuildTaskResult.Ok(new List<string>
            { $"[BUILD] {results.Count} bundle(s) produced in {tempDir}." });
    }

    private static BuildTaskResult ValidateBundleGroup(string bundleName, List<CollectedAssetInfo> assets)
    {
        if (assets == null || assets.Count == 0)
            return BuildTaskResult.Ok();

        var payloads = new HashSet<EPayloadKind>();
        var primaryTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < assets.Count; i++)
        {
            var asset = assets[i];
            payloads.Add(asset.Classification.PayloadKind);
            primaryTypes.Add(asset.PrimaryType ?? string.Empty);
        }

        if (payloads.Count != 1)
        {
            return BuildTaskResult.Fail(BuildErrorCodes.MixedPayloadBundle,
                $"Bundle '{bundleName}' 混入了多种 PayloadKind。每个物理 Bundle 只能有一种载荷路线。", true);
        }

        if (primaryTypes.Count != 1)
        {
            return BuildTaskResult.Fail(BuildErrorCodes.MixedPrimaryTypeBundle,
                $"Bundle '{bundleName}' 混入了多种 PrimaryType。每个物理 Bundle 必须按精确主类型分桶。", true);
        }

        EPayloadKind payload = assets[0].Classification.PayloadKind;
        if (payload == EPayloadKind.RawFile && assets.Count != 1)
        {
            return BuildTaskResult.Fail(BuildErrorCodes.RawfileMultiAsset,
                $"Bundle '{bundleName}' 包含 {assets.Count} 个 RawFile，每个 RawFile 必须独立输出。示例 Asset: '{assets[0].AssetPath}'。", true);
        }

        return BuildTaskResult.Ok();
    }

    private static BuildTaskResult ValidateSerializedBundleEntry(string assetPath)
    {
        if (!AssetClassifier.CanUseAsSerializedBundleEntry(assetPath, out string reason))
        {
            return BuildTaskResult.Fail(BuildErrorCodes.InvalidBundleEntryAsset,
                $"Asset '{assetPath}' 不能作为 AssetBundle Serialized 入口资产: {reason}", true);
        }

        return BuildTaskResult.Ok();
    }
}
