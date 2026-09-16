using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// AssetBundle 构建 Task —— 按 ContentName 分组，按 AssetContentType 分流，
/// 调用 Unity BuildPipeline.BuildAssetBundles 产出内容文件；命中历史制品的内容直接复用旧产物，不再交给 Unity 构建。
/// 压缩模式从 BuildPipelineConfig SO 读取（默认 LZ4）。
/// </summary>
/// <remarks>
/// 复用只依赖两处事实：正式 Summary（复用索引）与历史 Build_* 包（唯一物理字节来源）；不扫描目录、不推断内容身份。
/// 候选必须满足：同后端/平台/构建配方、Summary 可读、内容身份与输入指纹一致、制品存在且 Hash/CRC/Size 与记录一致。
/// 复制后的制品重新校验 Hash/CRC/Size，任一不符即重建。
/// Summary、索引或历史包缺失/损坏只损失优化，不阻断本可完成的构建，全部退化为该内容的全量构建并给出 Warning。
/// 本 Task 不写任何构建缓存：历史包由正式交付流程产生，本次产物由导出阶段负责落地。
/// </remarks>
public class BuildABContentTask : IBuildTask
{
    /// <summary>Scene 独立打包时附加在内容名后的后缀；属于既有物理命名规则，改动会改变全部 Scene 产物文件名。</summary>

    public string TaskName => "BuildABContent";

    public BuildTaskResult Execute(BuildContext ctx)
    {
        var cfg = ctx.Require<BuildConfig>(BuildContextKeys.BuildConfig);
        var assets = ctx.Require<List<CollectedAssetInfo>>(ABBuildContextKeys.CollectedAssets);
        string outputRoot = cfg.OutputRoot;
        var platform = cfg.TargetPlatform;

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

        var groups = new Dictionary<string, List<CollectedAssetInfo>>(StringComparer.Ordinal);
        for (int i = 0; i < assets.Count; i++)
        {
            string name = assets[i].ContentName;
            if (string.IsNullOrEmpty(name))
                continue;

            if (!groups.TryGetValue(name, out var list))
            {
                list = new List<CollectedAssetInfo>();
                groups[name] = list;
            }
            list.Add(assets[i]);
        }

        var plans = new List<ContentBuildItem>(groups.Count);
        foreach (var kv in groups)
        {
            var validation = ValidateBundleGroup(kv.Key, kv.Value);
            if (!validation.Success)
                return validation;

            if (!TryCreatePlan(kv.Key, kv.Value, out ContentBuildItem plan, out BuildTaskResult planError))
                return planError;

            plans.Add(plan);
        }

        BuildTaskResult uniqueness = ValidatePhysicalNameUniqueness(plans);
        if (!uniqueness.Success)
            return uniqueness;

        string tempDir = FYAssetPathUtility.JoinFilePath(outputRoot, "_temp");
        FileHelper.EnsureDirectory(tempDir);

        var warnings = new List<string>();

        var plannedFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < plans.Count; i++)
            plannedFileNames.Add(plans[i].PhysicalName);

        // 复用来源只有两处事实：正式 Summary（复用索引）与历史 Build_* 包（唯一物理字节来源）。
        // 存储不可用只损失优化，不得阻断构建。
        BuildSummaryStore summaryStore = null;
        try
        {
            summaryStore = BuildSummaryStore.CreateDefault();
        }
        catch (Exception ex)
        {
            AddWarning(warnings, $"构建事实存储不可用，本次不做历史制品复用: {ex.Message}");
        }

        string platformToken = ABBuildContentFingerprint.ResolvePlatformToken(cfg.TargetPlatform);
        string compressionToken = ABBuildContentFingerprint.ResolveCompressionToken(compression);
        string buildFormatToken = ABBuildContentFingerprint.ResolveBuildFormatToken(cfg.TargetPlatform);
        string recipeFingerprint = ABBuildContentFingerprint.ComputeRecipeFingerprint(cfg, compression);

        // 导出阶段写 Summary 时取同一配方，保证「本次记录的配方」与「复用判定用的配方」逐字符一致。
        ctx.Set(ABBuildContextKeys.BuildRecipeFingerprint, recipeFingerprint);

        for (int i = 0; i < plans.Count; i++)
        {
            ContentBuildItem plan = plans[i];
            if (!ABBuildContentFingerprint.TryCompute(
                    plan.ContentName, plan.AssetPaths, platformToken, compressionToken, buildFormatToken,
                    out string fingerprint))
            {
                AddWarning(warnings,
                    $"内容 '{plan.ContentName}' 的成员无法计算输入指纹，本次重新构建且不参与历史制品复用。");
                continue;
            }

            plan.InputFingerprint = fingerprint;

            if (!BuildArtifactReuseService.TryReuse(
                    summaryStore, cfg.BackendKey, platformToken, recipeFingerprint,
                    plan.ContentName, fingerprint, tempDir,
                    out _, out FileHelper.FileDigest reusedDigest, out List<string> reusedDependencies,
                    out string reuseReason))
            {
                AddWarning(warnings, $"内容 '{plan.ContentName}' 未复用历史制品: {reuseReason}");
                continue;
            }

            plan.ReusedOutput = reusedDigest;
            plan.DependencyFileNames = reusedDependencies;
        }

        // 复用内容必须带可解析的依赖事实，否则依赖下标会与全量构建不一致，此时退回重建。
        for (int i = 0; i < plans.Count; i++)
        {
            ContentBuildItem plan = plans[i];
            if (!plan.ReusedOutput.HasValue)
                continue;

            if (!AreDependenciesResolvable(
                    plan.ContentName, plan.DependencyFileNames, plannedFileNames, out string dependencyReason))
            {
                AddWarning(warnings,
                    $"内容 '{plan.ContentName}' 的复用候选缺少可解析的依赖事实，本次重新构建: {dependencyReason}");
                InvalidateReuse(plan, tempDir);
            }
        }

        // 依赖闭合裁剪：被重建内容依赖到的复用内容必须一起重建。
        // 制品在判定时已复制进本次输出，因此被撤销的候选要把产物一并删除，避免留下孤儿文件。
        DropReuseViolatingDependencyClosure(plans, ctx, warnings);
        for (int i = 0; i < plans.Count; i++)
            DropReusedArtifactIfNotCandidate(plans[i], tempDir);

        int reusedCount = 0;
        for (int i = 0; i < plans.Count; i++)
        {
            if (plans[i].ReusedOutput.HasValue)
                reusedCount++;
        }

        // 只有未复用的内容交给 Unity 构建；复用内容不产生 Unity 构建调用
        var builds = new List<AssetBundleBuild>();
        for (int i = 0; i < plans.Count; i++)
            AppendUnityBuild(plans[i], builds);

        AssetBundleManifest unityManifest = null;
        if (builds.Count > 0)
        {
            unityManifest = BuildPipeline.BuildAssetBundles(tempDir, builds.ToArray(), options, platform);
            if (unityManifest == null)
            {
                return BuildTaskResult.Fail(BuildErrorCodes.BuildFailed,
                    "BuildPipeline.BuildAssetBundles 返回了 null。", true);
            }
        }

        if (!TryCollectUnityOutputs(unityManifest, tempDir, out Dictionary<string, FileHelper.FileDigest> unityOutputs, out BuildTaskResult outputError))
            return outputError;

        // 依赖事实分两路收集后合并：本次交给 Unity 的内容由 AssetBundleManifest 报告，
        // 复用缓存的内容由缓存条目回放。两者合并后才与全量构建等价。
        var dependencyMergeError = TryApplyDependencyFacts(plans, unityManifest);
        if (dependencyMergeError != null)
            return dependencyMergeError;

        var results = new List<ContentBuildResult>(plans.Count);
        var processedOutputs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int builtCount = 0;

        for (int i = 0; i < plans.Count; i++)
        {
            ContentBuildItem plan = plans[i];

            if (plan.ReusedOutput.HasValue)
            {
                results.Add(CreateBuildInfo(plan, plan.ReusedOutput.Value));
                processedOutputs.Add(plan.ReusedOutput.Value.Name);
                continue;
            }

            if (plan.ContentType == AssetContentType.RawFile)
            {
                string rawAssetPath = plan.AssetPaths[0];
                if (unityOutputs.ContainsKey(plan.PhysicalName) || processedOutputs.Contains(plan.PhysicalName))
                {
                    return BuildTaskResult.Fail(BuildErrorCodes.RawfilePayloadConflict,
                        $"Bundle '{plan.ContentName}' 同时包含 RawFile 与 Serialized/Scene 输出路线。RawFile Asset '{rawAssetPath}' " +
                        "不会被 Unity AssetBundle 构建流程写入已存在的同名输出；请调整分组、BundlePackingMode 或内容类型配置。", true);
                }

                string destPath = FYAssetPathUtility.JoinFilePath(tempDir, plan.PhysicalName);
                try
                {
                    FileHelper.CopyFile(rawAssetPath, destPath, true);
                }
                catch (Exception ex)
                {
                    return BuildTaskResult.Fail(BuildErrorCodes.RawfileCopyFailed,
                        $"文件拷贝失败 '{rawAssetPath}' -> '{destPath}': {ex.Message}", true);
                }

                if (!FileHelper.TryCreateDigest(destPath, plan.PhysicalName, out FileHelper.FileDigest rawDigest))
                {
                    return BuildTaskResult.Fail(BuildErrorCodes.RawfileCopyFailed,
                        $"RawFile 产物不可读: '{destPath}'。", true);
                }

                results.Add(CreateBuildInfo(plan, rawDigest));
                processedOutputs.Add(plan.PhysicalName);
                builtCount++;
                continue;
            }

            var produced = new List<FileHelper.FileDigest>(1);
            string outputName = plan.PhysicalName;
            if (!TryTakeUnityOutput(unityOutputs, outputName, processedOutputs, out FileHelper.FileDigest digest))
            {
                return BuildTaskResult.Fail(BuildErrorCodes.BundleFileNotFound,
                    $"内容 '{plan.ContentName}' 的 Unity 构建产物缺失: 期望 '{outputName}'，实际产出 '{string.Join(", ", unityOutputs.Keys)}'。", true);
            }

            produced.Add(digest);
            results.Add(CreateBuildInfo(plan, digest));
            processedOutputs.Add(digest.Name);
            builtCount++;
        }

        ctx.Set(ABBuildContextKeys.BundleBuildResults, results);

        var messages = new List<string>
        {
            $"[BUILD] {results.Count} content(s) produced in {tempDir} (reused={reusedCount}, built={builtCount})."
        };
        messages.AddRange(warnings);
        return BuildTaskResult.Ok(messages);
    }

    private static void DropReusedArtifactIfNotCandidate(ContentBuildItem plan, string tempDir)
    {
        if (plan == null || plan.ReusedOutput.HasValue)
            return;
        FileHelper.TryDelete(FYAssetPathUtility.JoinFilePath(tempDir, plan.PhysicalName));
    }

    private static void InvalidateReuse(ContentBuildItem plan, string tempDir)
    {
        if (plan == null || !plan.ReusedOutput.HasValue)
            return;

        FileHelper.TryDelete(FYAssetPathUtility.JoinFilePath(tempDir, plan.ReusedOutput.Value.Name));
        plan.ReusedOutput = null;
        plan.DependencyFileNames = null;
    }

    private static void InvalidateReuseIfDropped(ContentBuildItem plan, string tempDir)
    {
        if (plan == null || plan.ReusedOutput.HasValue)
            return;

        plan.DependencyFileNames = null;
    }

    /// <summary>
    /// 收集并合并内容级依赖事实：本次 Unity 构建的内容取 AssetBundleManifest 的直接依赖，
    /// 复用历史制品的内容取 Summary 回放的依赖。合并失败即阻断，避免写出与全量构建不同的依赖下标。
    /// </summary>
    private static BuildTaskResult TryApplyDependencyFacts(
        List<ContentBuildItem> plans,
        AssetBundleManifest unityManifest)
    {
        var rebuiltDependencies = new Dictionary<string, IList<string>>(StringComparer.Ordinal);
        var reusedDependencies = new Dictionary<string, IList<string>>(StringComparer.Ordinal);

        for (int i = 0; i < plans.Count; i++)
        {
            ContentBuildItem plan = plans[i];
            if (plan.ReusedOutput.HasValue)
            {
                reusedDependencies[plan.ContentName] =
                    new List<string>(plan.DependencyFileNames ?? new List<string>(0));
                continue;
            }

            // RawFile 是普通物理文件，不进入 AssetBundle 依赖体系。
            if (plan.ContentType == AssetContentType.RawFile)
            {
                rebuiltDependencies[plan.ContentName] = new List<string>(0);
                continue;
            }

            var dependencies = new List<string>();
            string[] direct = unityManifest != null
                ? unityManifest.GetDirectDependencies(plan.PhysicalName)
                : null;
            if (direct != null)
            {
                for (int d = 0; d < direct.Length; d++)
                {
                    string dependencyName = direct[d];
                    if (string.IsNullOrEmpty(dependencyName) || dependencies.Contains(dependencyName))
                        continue;
                    dependencies.Add(dependencyName);
                }
            }

            rebuiltDependencies[plan.ContentName] = dependencies;
        }

        if (!TryMergeDependencyNames(
                rebuiltDependencies, reusedDependencies, out Dictionary<string, List<string>> merged, out List<string> problems))
        {
            return BuildTaskResult.Fail(BuildErrorCodes.ManifestDependencyConflict,
                $"内容依赖事实合并失败，无法保证与全量构建一致: {string.Join(" | ", problems)}", true);
        }

        for (int i = 0; i < plans.Count; i++)
        {
            ContentBuildItem plan = plans[i];
            if (!merged.TryGetValue(plan.ContentName, out List<string> dependencies))
            {
                return BuildTaskResult.Fail(BuildErrorCodes.ManifestDependencyConflict,
                    $"内容 '{plan.ContentName}' 缺少依赖事实，无法生成 Manifest 依赖下标。", true);
            }

            plan.DependencyFileNames = dependencies;
        }

        return null;
    }

    private static bool AreDependenciesResolvable(
        string contentName,
        IReadOnlyList<string> dependencyFileNames,
        IReadOnlyCollection<string> knownFileNames,
        out string failureReason)
    {
        failureReason = null;
        if (dependencyFileNames == null)
        {
            failureReason = $"内容 '{contentName}' 缺少依赖事实";
            return false;
        }

        if (dependencyFileNames.Count == 0 || knownFileNames == null)
            return true;

        var known = new HashSet<string>(knownFileNames, StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < dependencyFileNames.Count; i++)
        {
            string dependency = dependencyFileNames[i];
            if (string.IsNullOrEmpty(dependency)
                || string.Equals(dependency, contentName, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!known.Contains(dependency))
            {
                failureReason = $"内容 '{contentName}' 的缓存依赖 '{dependency}' 不在本次构建内容集合中";
                return false;
            }
        }

        return true;
    }

    private static bool TryMergeDependencyNames(
        IReadOnlyDictionary<string, IList<string>> rebuiltDependencies,
        IReadOnlyDictionary<string, IList<string>> reusedDependencies,
        out Dictionary<string, List<string>> merged,
        out List<string> problems)
    {
        merged = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        problems = new List<string>();
        AppendDependencies(merged, problems, reusedDependencies, "缓存复用");
        AppendDependencies(merged, problems, rebuiltDependencies, "本次重建");
        return problems.Count == 0;
    }

    private static void AppendDependencies(
        Dictionary<string, List<string>> merged,
        List<string> problems,
        IReadOnlyDictionary<string, IList<string>> source,
        string sourceLabel)
    {
        if (source == null)
            return;

        foreach (var pair in source)
        {
            if (string.IsNullOrEmpty(pair.Key))
                continue;
            if (pair.Value == null)
            {
                problems.Add($"内容 '{pair.Key}' 在{sourceLabel}来源中缺少依赖事实");
                continue;
            }

            List<string> normalized = NormalizeDependencies(pair.Key, pair.Value);
            if (merged.TryGetValue(pair.Key, out List<string> existing))
            {
                if (!SequenceEqual(existing, normalized))
                    problems.Add($"内容 '{pair.Key}' 的两份依赖事实不一致: [{string.Join(", ", existing)}] vs [{string.Join(", ", normalized)}]");
                continue;
            }

            merged[pair.Key] = normalized;
        }
    }

    private static List<string> NormalizeDependencies(string contentName, IList<string> dependencies)
    {
        var result = new List<string>(dependencies.Count);
        for (int i = 0; i < dependencies.Count; i++)
        {
            string dependency = dependencies[i];
            if (string.IsNullOrEmpty(dependency)
                || string.Equals(dependency, contentName, StringComparison.OrdinalIgnoreCase)
                || result.Contains(dependency))
                continue;
            result.Add(dependency);
        }
        result.Sort(StringComparer.Ordinal);
        return result;
    }

    private static bool SequenceEqual(List<string> left, List<string> right)
    {
        if (left.Count != right.Count)
            return false;
        for (int i = 0; i < left.Count; i++)
        {
            if (!string.Equals(left[i], right[i], StringComparison.Ordinal))
                return false;
        }
        return true;
    }

    private static List<string> DropReuseViolatingDependencyClosure(
        IReadOnlyDictionary<string, IList<string>> dependenciesByContent,
        ISet<string> reusableContents,
        IEnumerable<string> rebuiltContents)
    {
        var dropped = new List<string>();
        if (reusableContents == null || reusableContents.Count == 0 || rebuiltContents == null)
            return dropped;

        var requiresBuild = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<string>();
        foreach (string name in rebuiltContents)
        {
            if (!string.IsNullOrEmpty(name) && requiresBuild.Add(name))
                queue.Enqueue(name);
        }

        while (queue.Count > 0)
        {
            string contentName = queue.Dequeue();
            if (dependenciesByContent == null
                || !dependenciesByContent.TryGetValue(contentName, out IList<string> dependencies)
                || dependencies == null)
                continue;

            for (int i = 0; i < dependencies.Count; i++)
            {
                string dependency = dependencies[i];
                if (string.IsNullOrEmpty(dependency) || !reusableContents.Remove(dependency))
                    continue;
                dropped.Add(dependency);
                if (requiresBuild.Add(dependency))
                    queue.Enqueue(dependency);
            }
        }

        return dropped;
    }

    /// <summary>按依赖闭合撤销不安全的复用，返回被撤销的内容数。</summary>
    private static int DropReuseViolatingDependencyClosure(List<ContentBuildItem> plans, BuildContext ctx, List<string> warnings)
    {
        var reusables = new HashSet<string>(StringComparer.Ordinal);
        var rebuilt = new List<string>();
        for (int i = 0; i < plans.Count; i++)
        {
            ContentBuildItem plan = plans[i];
            if (plan.ReusedOutput.HasValue)
                reusables.Add(plan.ContentName);
            else if (plan.ContentType != AssetContentType.RawFile)
                rebuilt.Add(plan.ContentName);
        }

        if (reusables.Count == 0 || rebuilt.Count == 0)
            return 0;

        BundleDependencyGraph graph = ctx.Get<BundleDependencyGraph>(ABBuildContextKeys.BundleDependencyGraph);
        if (graph == null)
        {
            // 没有依赖图就无法判断安全性，宁可不复用。
            AddWarning(warnings, "缺少 BundleDependencyGraph，本次不做历史制品复用。");
            int disabled = 0;
            for (int i = 0; i < plans.Count; i++)
            {
                if (!plans[i].ReusedOutput.HasValue)
                    continue;
                InvalidateReuse(plans[i], FYAssetPathUtility.JoinFilePath(
                    ctx.Require<BuildConfig>(BuildContextKeys.BuildConfig).OutputRoot, "_temp"));
                disabled++;
            }
            return disabled;
        }

        Dictionary<string, HashSet<string>> dependencyMap = graph.GetDependencyMap();
        var dependenciesByContent = new Dictionary<string, IList<string>>(StringComparer.Ordinal);
        foreach (var kv in dependencyMap)
        {
            if (kv.Key == null || kv.Value == null || kv.Value.Count == 0)
                continue;

            var list = new List<string>(kv.Value.Count);
            foreach (string dependency in kv.Value)
                list.Add(dependency);
            dependenciesByContent[kv.Key] = list;
        }

        List<string> dropped = DropReuseViolatingDependencyClosure(
            dependenciesByContent, reusables, rebuilt);

        for (int i = 0; i < dropped.Count; i++)
        {
            AddWarning(warnings,
                $"内容 '{dropped[i]}' 被本次重建内容依赖，撤销历史制品复用以免 Unity 把其资产复制进重建内容。");
        }

        for (int i = 0; i < plans.Count; i++)
        {
            ContentBuildItem plan = plans[i];
            if (plan.ReusedOutput.HasValue && !reusables.Contains(plan.ContentName))
                InvalidateReuse(plan, FYAssetPathUtility.JoinFilePath(
                    ctx.Require<BuildConfig>(BuildContextKeys.BuildConfig).OutputRoot, "_temp"));
        }

        return dropped.Count;
    }

    /// <summary>把未复用内容的构建请求追加到 Unity 构建列表；RawFile 与复用内容不进入列表。</summary>
    private static void AppendUnityBuild(ContentBuildItem plan, List<AssetBundleBuild> builds)
    {
        if (plan.ReusedOutput.HasValue || plan.ContentType == AssetContentType.RawFile)
            return;

        builds.Add(new AssetBundleBuild
        {
            assetBundleName = plan.PhysicalName,
            assetNames = plan.AssetPaths.ToArray()
        });
    }

    /// <summary>收集本次 Unity 实际产出的文件摘要；无 Unity 构建时返回空集合。</summary>
    private static bool TryCollectUnityOutputs(
        AssetBundleManifest unityManifest,
        string tempDir,
        out Dictionary<string, FileHelper.FileDigest> outputs,
        out BuildTaskResult error)
    {
        outputs = new Dictionary<string, FileHelper.FileDigest>(StringComparer.OrdinalIgnoreCase);
        error = null;
        if (unityManifest == null)
            return true;

        string[] allBundles = unityManifest.GetAllAssetBundles();
        for (int i = 0; i < allBundles.Length; i++)
        {
            string outputName = allBundles[i];
            string filePath = FYAssetPathUtility.JoinFilePath(tempDir, outputName);
            if (!FileHelper.TryCreateDigest(filePath, outputName, out FileHelper.FileDigest digest))
            {
                error = BuildTaskResult.Fail(BuildErrorCodes.BundleFileNotFound,
                    $"Unity 构建产物不可读: '{filePath}'。", true);
                return false;
            }

            outputs[outputName] = digest;
        }

        return true;
    }

    /// <summary>按预期输出名取本次构建产物；名称不匹配即视为内容缺失，避免把别的产物错配给该内容。</summary>
    private static bool TryTakeUnityOutput(
        Dictionary<string, FileHelper.FileDigest> unityOutputs,
        string expectedName,
        HashSet<string> usedOutputs,
        out FileHelper.FileDigest digest)
    {
        digest = default;
        if (!unityOutputs.TryGetValue(expectedName, out FileHelper.FileDigest found))
            return false;

        if (usedOutputs.Contains(found.Name))
            return false;

        digest = found;
        return true;
    }

    private static ContentBuildResult CreateBuildInfo(ContentBuildItem plan, in FileHelper.FileDigest digest)
    {
        return new ContentBuildResult
        {
            ContentName = plan.ContentName,
            FileName = digest.Name,
            Hash = digest.Hash,
            CRC = digest.CRC,
            Size = digest.Size,
            InputFingerprint = plan.InputFingerprint,
            AssetPaths = new List<string>(plan.AssetPaths),
            ContentType = plan.ContentType,
            DependencyFileNames = new List<string>(plan.DependencyFileNames ?? new List<string>(0))
        };
    }

    /// <summary>复用退化等异常进入 Task Warning 列表，同时写入 Console，保证无人值守构建可见。</summary>
    private static void AddWarning(List<string> warnings, string message)
    {
        string text = $"[REUSE] {message}";
        warnings.Add(text);
        Debug.LogWarning($"[{nameof(BuildABContentTask)}] {text}");
    }

    private static BuildTaskResult ValidateBundleGroup(string bundleName, List<CollectedAssetInfo> assets)
    {
        if (assets == null || assets.Count == 0)
            return BuildTaskResult.Ok();

        var contentTypes = new HashSet<AssetContentType>();
        var primaryTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < assets.Count; i++)
        {
            var asset = assets[i];
            contentTypes.Add(asset.ContentType);
            primaryTypes.Add(asset.AssetType ?? string.Empty);
        }

        if (contentTypes.Count != 1)
        {
            return BuildTaskResult.Fail(BuildErrorCodes.MixedPayloadBundle,
                $"Bundle '{bundleName}' 混入了多种内容类型（AssetContentType）。每个物理 Bundle 只能有一种构建路线。", true);
        }

        if (primaryTypes.Count != 1)
        {
            var members = new System.Text.StringBuilder();
            for (int i = 0; i < assets.Count; i++)
            {
                members.Append("\n  - ").Append(assets[i].AssetPath)
                    .Append(" [AssetType=").Append(assets[i].AssetType ?? "")
                    .Append(", Address=").Append(assets[i].Address ?? "").Append(']');
            }
            return BuildTaskResult.Fail(BuildErrorCodes.MixedAssetTypeBundle,
                $"Bundle '{bundleName}' 混入了多种 AssetType。每个物理 Bundle 必须按精确主类型分桶。成员:{members}", true);
        }

        AssetContentType contentType = assets[0].ContentType;
        if (contentType == AssetContentType.Scene && assets.Count != 1)
        {
            return BuildTaskResult.Fail(BuildErrorCodes.MixedPayloadBundle,
                $"Bundle '{bundleName}' 包含 {assets.Count} 个 Scene；每个 Scene 必须对应一个独立 Content 文件。", true);
        }

        if (contentType == AssetContentType.RawFile && assets.Count != 1)
        {
            return BuildTaskResult.Fail(BuildErrorCodes.RawfileMultiAsset,
                $"Bundle '{bundleName}' 包含 {assets.Count} 个 RawFile，每个 RawFile 必须独立输出。示例 Asset: '{assets[0].AssetPath}'。", true);
        }

        return BuildTaskResult.Ok();
    }

    /// <summary>归类内容成员并得出预期物理输出名；Serialized 入口资产在此校验。</summary>
    private static bool TryCreatePlan(
        string contentName,
        List<CollectedAssetInfo> members,
        out ContentBuildItem plan,
        out BuildTaskResult error)
    {
        plan = null;
        error = null;

        var created = new ContentBuildItem
        {
            ContentName = contentName,
            PhysicalName = members[0].ContentType == AssetContentType.Scene
                ? BundleNameBuilder.BuildPhysicalName(contentName, ShortAssetName(members[0].AssetPath))
                : BundleNameBuilder.BuildPhysicalName(contentName, ResolveReadableName(members)),
            ContentType = members[0].ContentType
        };

        for (int i = 0; i < members.Count; i++)
        {
            CollectedAssetInfo member = members[i];
            created.AssetPaths.Add(member.AssetPath);
            if (member.ContentType == AssetContentType.SerializedObject)
            {
                var entryValidation = ValidateSerializedBundleEntry(member.AssetPath);
                if (!entryValidation.Success)
                {
                    error = entryValidation;
                    return false;
                }
            }
        }

        plan = created;
        return true;
    }

    /// <summary>取成员资源的短名作为物理名可读段；组/标签模式由打包模式段决定，不使用它。</summary>
    private static string ResolveReadableName(List<CollectedAssetInfo> members)
    {
        return members != null && members.Count > 0 ? ShortAssetName(members[0].AssetPath) : null;
    }

    private static string ShortAssetName(string assetPath)
    {
        return string.IsNullOrEmpty(assetPath)
            ? null
            : System.IO.Path.GetFileNameWithoutExtension(assetPath);
    }

    /// <summary>物理文件名必须大小写不敏感唯一；冲突时报告两个内容桶的完整身份并阻断。</summary>
    private static BuildTaskResult ValidatePhysicalNameUniqueness(List<ContentBuildItem> plans)
    {
        var seen = new Dictionary<string, ContentBuildItem>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < plans.Count; i++)
        {
            ContentBuildItem plan = plans[i];
            string name = plan.PhysicalName;
            if (string.IsNullOrEmpty(name))
                continue;
            if (seen.TryGetValue(name, out ContentBuildItem other))
            {
                return BuildTaskResult.Fail(BuildErrorCodes.DuplicateBundleName,
                    $"物理文件名冲突（大小写不敏感）: '{name}' 同时来自 '{other.ContentName}' 与 '{plan.ContentName}'。",
                    true);
            }

            seen.Add(name, plan);
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

    /// <summary>单个内容的短生命周期构建工作记录；只保留平坦资产路径和必要处理状态。</summary>
    private sealed class ContentBuildItem
    {
        public string ContentName;
        public AssetContentType ContentType;
        public List<string> AssetPaths = new List<string>();
        public string PhysicalName;
        public string InputFingerprint;
        public FileHelper.FileDigest? ReusedOutput;
        public List<string> DependencyFileNames;
    }
}
