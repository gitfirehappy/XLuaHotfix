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
        BuildContextKeys.CollectedAssets,
        BuildContextKeys.BundleDependencyGraph,
        BuildContextKeys.OutputRoot,
        BuildContextKeys.TargetPlatform
    };
    public string[] WriteKeys => new[] { BuildContextKeys.BundleBuildResults };

    public BuildTaskResult Execute(BuildContext ctx)
    {
        var assets = ctx.Require<List<CollectedAssetInfo>>(BuildContextKeys.CollectedAssets);
        string outputRoot = ctx.Require<string>(BuildContextKeys.OutputRoot);
        var platform = ctx.Require<BuildTarget>(BuildContextKeys.TargetPlatform);

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
        var rawFileEntries = new List<(string bundleName, string assetPath)>();

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
        var processedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (unityManifest != null)
        {
            string[] allBundles = unityManifest.GetAllAssetBundles();
            for (int i = 0; i < allBundles.Length; i++)
            {
                string fileName = allBundles[i];
                string filePath = Path.Combine(tempDir, fileName);
                var info = new FileInfo(filePath);
                string hash = unityManifest.GetAssetBundleHash(fileName).ToString();

                // 尝试从逻辑名反查资产路径列表
                var assetPaths = new List<string>();
                string logicalName = GetLogicalBundleName(fileName, groups.Keys);
                if (logicalName != null && groups.TryGetValue(logicalName, out var logicalAssets))
                {
                    for (int a = 0; a < logicalAssets.Count; a++)
                        assetPaths.Add(logicalAssets[a].AssetPath);
                }

                results.Add(new BundleBuildInfo
                {
                    BundleName = logicalName ?? fileName,
                    OutputFileName = fileName,
                    Hash = hash,
                    Size = info.Exists ? info.Length : 0,
                    AssetPaths = assetPaths,
                    PayloadKind = EPayloadKind.Serialized
                });

                processedNames.Add(fileName);
            }
        }

        // RawFile 直接文件拷贝
        for (int r = 0; r < rawFileEntries.Count; r++)
        {
            var (bundleName, assetPath) = rawFileEntries[r];
            if (processedNames.Contains(bundleName))
                continue;

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

            var info = new FileInfo(destPath);
            results.Add(new BundleBuildInfo
            {
                BundleName = bundleName,
                OutputFileName = bundleName,
                Hash = "",
                Size = info.Exists ? info.Length : 0,
                AssetPaths = new List<string> { assetPath },
                PayloadKind = EPayloadKind.RawFile
            });

            processedNames.Add(bundleName);
        }

        ctx.Set(BuildContextKeys.BundleBuildResults, results);
        return BuildTaskResult.Ok(new List<string>
            { $"[BUILD] {results.Count} bundle(s) produced in {tempDir}." });
    }

    /// <summary>
    /// 尝试从 Unity 产出的文件名匹配回逻辑 BundleName。
    /// Unity 产出的文件名形如 "hotfix_ui_abc123_md5hash.bundle"，
    /// 需要匹配回 logicName = "hotfix_ui_abc123"。
    /// </summary>
    private static string GetLogicalBundleName(string fileName, IEnumerable<string> logicalNames)
    {
        foreach (var name in logicalNames)
        {
            if (fileName.StartsWith(name, StringComparison.OrdinalIgnoreCase))
                return name;
        }
        return null;
    }
}
