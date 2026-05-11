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
    public string[] DependsOn => new[] { "TaskAnalyzeDependencies" };
    public string[] ReadKeys => new[]
    {
        BuildContextKeys.BuildConfig,
        BuildContextKeys.CollectedAssets,
        BuildContextKeys.BundleDependencyGraph
    };
    public string[] WriteKeys => new[] { BuildContextKeys.BundleBuildResults };

    public BuildTaskResult Execute(BuildContext ctx)
    {
        var cfg = ctx.Require<BuildConfig>(BuildContextKeys.BuildConfig);
        var assets = ctx.Require<List<CollectedAssetInfo>>(BuildContextKeys.CollectedAssets);
        string outputRoot = cfg.OutputRoot;
        var platform = cfg.TargetPlatform;

        // 读取压缩配置
        var config = AssetDatabase.LoadAssetAtPath<BuildPipelineConfig>(
            "Assets/Build/BuildPipelineConfig.asset");
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
        string tempDir = Path.Combine(outputRoot, "_temp");
        if (!Directory.Exists(tempDir))
            Directory.CreateDirectory(tempDir);

        // 调用 Unity BuildPipeline
        AssetBundleManifest unityManifest;
        if (builds.Count > 0)
        {
            unityManifest = BuildPipeline.BuildAssetBundles(tempDir, builds.ToArray(), options, platform);
            if (unityManifest == null)
                return BuildTaskResult.Fail("BUILD_FAILED",
                    "BuildPipeline.BuildAssetBundles returned null.", true);
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

        // 构建 scene 输出名 → (logicalName, sceneIndex) 索引
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
                string filePath = Path.Combine(tempDir, outputName);
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

            if (processedOutputs.Contains(bundleName))
                continue;

            if (rawBundleFileCount[bundleName] > 1)
                return BuildTaskResult.Fail("RAWFILE_MULTI_ASSET",
                    $"Bundle '{bundleName}' has {rawBundleFileCount[bundleName]} RawFile assets " +
                    "but only one raw file per bundle is supported.", true);

            string destPath = Path.Combine(tempDir, bundleName);
            try
            {
                File.Copy(assetPath, destPath, true);
            }
            catch (IOException ex)
            {
                return BuildTaskResult.Fail("RAWFILE_COPY_FAILED",
                    $"Failed to copy '{assetPath}' → '{destPath}': {ex.Message}", true);
            }

            var destInfo = new FileInfo(destPath);
            results.Add(new BundleBuildInfo
            {
                BundleName = bundleName,
                OutputFileName = bundleName,
                Hash = "",
                Size = destInfo.Exists ? destInfo.Length : 0,
                AssetPaths = new List<string> { assetPath },
                PayloadKind = EPayloadKind.RawFile
            });

            processedOutputs.Add(bundleName);
        }

        ctx.Set(BuildContextKeys.BundleBuildResults, results);
        return BuildTaskResult.Ok(new List<string>
            { $"[BUILD] {results.Count} bundle(s) produced in {tempDir}." });
    }
}
