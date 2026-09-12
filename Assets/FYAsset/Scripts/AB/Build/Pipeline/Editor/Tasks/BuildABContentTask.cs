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

        var plans = new List<ContentPlan>(groups.Count);
        foreach (var kv in groups)
        {
            var validation = ValidateBundleGroup(kv.Key, kv.Value);
            if (!validation.Success)
                return validation;

            if (!TryCreatePlan(kv.Key, kv.Value, out ContentPlan plan, out BuildTaskResult planError))
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
        {
            for (int o = 0; o < plans[i].OutputNames.Count; o++)
                plannedFileNames.Add(plans[i].OutputNames[o]);
        }

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
            ContentPlan plan = plans[i];
            if (plan.OutputNames.Count != 1)
            {
                AddWarning(warnings,
                    $"内容 '{plan.ContentName}' 产出 {plan.OutputNames.Count} 个物理文件，不参与历史制品复用。");
                continue;
            }

            if (!ABBuildContentFingerprint.TryCompute(
                    plan.ContentName, plan.Members, platformToken, compressionToken, buildFormatToken,
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
            plan.CachedDependencyFileNames = reusedDependencies;
            plan.CacheCandidate = true;
        }

        // 复用内容必须带可解析的依赖事实，否则依赖下标会与全量构建不一致，此时退回重建。
        for (int i = 0; i < plans.Count; i++)
        {
            ContentPlan plan = plans[i];
            if (!plan.CacheCandidate)
                continue;

            if (!ContentDependencyIndexResolver.AreDependenciesResolvable(
                    plan.ContentName, plan.CachedDependencyFileNames, plannedFileNames, out string dependencyReason))
            {
                AddWarning(warnings,
                    $"内容 '{plan.ContentName}' 的复用候选缺少可解析的依赖事实，本次重新构建: {dependencyReason}");
                plan.CacheCandidate = false;
                DropReusedArtifactIfNotCandidate(plan, tempDir);
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

        var results = new List<BundleBuildInfo>(plans.Count);
        var processedOutputs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int builtCount = 0;

        for (int i = 0; i < plans.Count; i++)
        {
            ContentPlan plan = plans[i];

            if (plan.ReusedOutput.HasValue)
            {
                results.Add(CreateBuildInfo(plan, plan.ReusedOutput.Value));
                processedOutputs.Add(plan.ReusedOutput.Value.Name);
                continue;
            }

            if (plan.ContentType == AssetContentType.RawFile)
            {
                string rawAssetPath = plan.Members[0].AssetPath;
                if (unityOutputs.ContainsKey(plan.ContentName) || processedOutputs.Contains(plan.ContentName))
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

            var produced = new List<FileHelper.FileDigest>(plan.OutputNames.Count);
            for (int o = 0; o < plan.OutputNames.Count; o++)
            {
                string outputName = plan.OutputNames[o];
                if (!TryTakeUnityOutput(unityOutputs, outputName, processedOutputs, out FileHelper.FileDigest digest))
                {
                    return BuildTaskResult.Fail(BuildErrorCodes.BundleFileNotFound,
                        $"内容 '{plan.ContentName}' 的 Unity 构建产物缺失: 期望 '{outputName}'，实际产出 '{string.Join(", ", unityOutputs.Keys)}'。", true);
                }

                produced.Add(digest);
                results.Add(CreateBuildInfo(plan, digest));
                processedOutputs.Add(digest.Name);
            }

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

    /// <summary>
    /// 把已失去复用资格的内容退回重建：删除已复制到本次输出目录的制品，避免留下孤儿文件。
    /// </summary>
    private static void DropReusedArtifactIfNotCandidate(ContentPlan plan, string tempDir)
    {
        if (plan.CacheCandidate || !plan.ReusedOutput.HasValue)
            return;

        FileHelper.TryDelete(FYAssetPathUtility.JoinFilePath(tempDir, plan.ReusedOutput.Value.Name));
        plan.ReusedOutput = null;
        plan.CachedDependencyFileNames = null;
    }

    /// <summary>
    /// 收集并合并内容级依赖事实：本次 Unity 构建的内容取 AssetBundleManifest 的直接依赖，
    /// 复用历史制品的内容取 Summary 回放的依赖。合并失败即阻断，避免写出与全量构建不同的依赖下标。
    /// </summary>
    private static BuildTaskResult TryApplyDependencyFacts(
        List<ContentPlan> plans,
        AssetBundleManifest unityManifest)
    {
        var rebuiltDependencies = new Dictionary<string, IList<string>>(StringComparer.Ordinal);
        var reusedDependencies = new Dictionary<string, IList<string>>(StringComparer.Ordinal);

        for (int i = 0; i < plans.Count; i++)
        {
            ContentPlan plan = plans[i];
            if (plan.ReusedOutput.HasValue)
            {
                reusedDependencies[plan.ContentName] =
                    new List<string>(plan.CachedDependencyFileNames ?? new List<string>(0));
                continue;
            }

            // RawFile 是普通物理文件，不进入 AssetBundle 依赖体系。
            if (plan.ContentType == AssetContentType.RawFile)
            {
                rebuiltDependencies[plan.ContentName] = new List<string>(0);
                continue;
            }

            var dependencies = new List<string>();
            for (int o = 0; o < plan.OutputNames.Count; o++)
            {
                string outputName = plan.OutputNames[o];
                string[] direct = unityManifest != null
                    ? unityManifest.GetDirectDependencies(outputName)
                    : null;
                if (direct == null)
                    continue;

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

        if (!ContentDependencyIndexResolver.TryMergeDependencyNames(
                rebuiltDependencies, reusedDependencies, out Dictionary<string, List<string>> merged, out List<string> problems))
        {
            return BuildTaskResult.Fail(BuildErrorCodes.ManifestDependencyConflict,
                $"内容依赖事实合并失败，无法保证与全量构建一致: {string.Join(" | ", problems)}", true);
        }

        for (int i = 0; i < plans.Count; i++)
        {
            ContentPlan plan = plans[i];
            if (!merged.TryGetValue(plan.ContentName, out List<string> dependencies))
            {
                return BuildTaskResult.Fail(BuildErrorCodes.ManifestDependencyConflict,
                    $"内容 '{plan.ContentName}' 缺少依赖事实，无法生成 Manifest 依赖下标。", true);
            }

            plan.DependencyFileNames = dependencies;
        }

        return null;
    }

    /// <summary>
    /// 按依赖闭合撤销不安全的复用，返回被撤销的内容数。
    /// Unity 只把显式列入本次构建的资产写入内容，未被显式分配的依赖资产会被复制进引用它的每个内容，
    /// 因此被重建内容依赖到的复用内容必须一起重建，否则重建产物与全量构建不一致。
    /// </summary>
    private static int DropReuseViolatingDependencyClosure(List<ContentPlan> plans, BuildContext ctx, List<string> warnings)
    {
        var reusables = new HashSet<string>(StringComparer.Ordinal);
        var rebuilt = new List<string>();
        for (int i = 0; i < plans.Count; i++)
        {
            ContentPlan plan = plans[i];
            if (plan.CacheCandidate)
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
                if (!plans[i].CacheCandidate)
                    continue;
                plans[i].CacheCandidate = false;
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

        List<string> dropped = ContentDependencyIndexResolver.DropReuseViolatingDependencyClosure(
            dependenciesByContent, reusables, rebuilt);

        for (int i = 0; i < dropped.Count; i++)
        {
            AddWarning(warnings,
                $"内容 '{dropped[i]}' 被本次重建内容依赖，撤销历史制品复用以免 Unity 把其资产复制进重建内容。");
        }

        for (int i = 0; i < plans.Count; i++)
        {
            ContentPlan plan = plans[i];
            if (plan.CacheCandidate && !reusables.Contains(plan.ContentName))
                plan.CacheCandidate = false;
        }

        return dropped.Count;
    }

    /// <summary>把未复用内容的构建请求追加到 Unity 构建列表；RawFile 与复用内容不进入列表。</summary>
    private static void AppendUnityBuild(ContentPlan plan, List<AssetBundleBuild> builds)
    {
        if (plan.ReusedOutput.HasValue || plan.ContentType == AssetContentType.RawFile)
            return;

        if (plan.ContentType == AssetContentType.Scene)
        {
            // Scene 必须独立打包（Unity 要求单独一个 AB 入口）
            for (int s = 0; s < plan.ScenePaths.Count; s++)
            {
                builds.Add(new AssetBundleBuild
                {
                    assetBundleName = plan.OutputNames[s],
                    assetNames = new[] { plan.ScenePaths[s] }
                });
            }
            return;
        }

        if (plan.SerializedPaths.Count > 0)
        {
            builds.Add(new AssetBundleBuild
            {
                assetBundleName = plan.PhysicalName,
                assetNames = plan.SerializedPaths.ToArray()
            });
        }
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

    private static BundleBuildInfo CreateBuildInfo(ContentPlan plan, in FileHelper.FileDigest digest)
    {
        return new BundleBuildInfo
        {
            BundleName = plan.ContentName,
            OutputFileName = digest.Name,
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
            primaryTypes.Add(asset.PrimaryType ?? string.Empty);
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
                    .Append(" [PrimaryType=").Append(assets[i].PrimaryType ?? "")
                    .Append(", Address=").Append(assets[i].Address ?? "").Append(']');
            }
            return BuildTaskResult.Fail(BuildErrorCodes.MixedPrimaryTypeBundle,
                $"Bundle '{bundleName}' 混入了多种 PrimaryType。每个物理 Bundle 必须按精确主类型分桶。成员:{members}", true);
        }

        AssetContentType contentType = assets[0].ContentType;
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
        out ContentPlan plan,
        out BuildTaskResult error)
    {
        plan = null;
        error = null;

        var created = new ContentPlan
        {
            ContentName = contentName,
            PhysicalName = BundleNameBuilder.BuildPhysicalName(contentName, ResolveReadableName(members)),
            Members = members,
            ContentType = members[0].ContentType
        };

        for (int i = 0; i < members.Count; i++)
        {
            CollectedAssetInfo member = members[i];
            created.AssetPaths.Add(member.AssetPath);

            switch (member.ContentType)
            {
                case AssetContentType.RawFile:
                    created.OutputNames.Add(created.PhysicalName);
                    break;

                case AssetContentType.Scene:
                    // Scene 强制独立打包；每个场景一个物理文件，可读段使用场景短名。
                    created.OutputNames.Add(
                        BundleNameBuilder.BuildPhysicalName(contentName, ShortAssetName(member.AssetPath)));
                    created.ScenePaths.Add(member.AssetPath);
                    break;

                default:
                    var entryValidation = ValidateSerializedBundleEntry(member.AssetPath);
                    if (!entryValidation.Success)
                    {
                        error = entryValidation;
                        return false;
                    }

                    created.SerializedPaths.Add(member.AssetPath);
                    break;
            }
        }

        if (created.SerializedPaths.Count > 0)
            created.OutputNames.Insert(0, created.PhysicalName);

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
    private static BuildTaskResult ValidatePhysicalNameUniqueness(List<ContentPlan> plans)
    {
        var seen = new Dictionary<string, ContentPlan>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < plans.Count; i++)
        {
            ContentPlan plan = plans[i];
            for (int o = 0; o < plan.OutputNames.Count; o++)
            {
                string name = plan.OutputNames[o];
                if (string.IsNullOrEmpty(name))
                    continue;
                if (seen.TryGetValue(name, out ContentPlan other))
                {
                    return BuildTaskResult.Fail(BuildErrorCodes.DuplicateBundleName,
                        $"物理文件名冲突（大小写不敏感）: '{name}' 同时来自 '{other.ContentName}' 与 '{plan.ContentName}'。",
                        true);
                }

                seen.Add(name, plan);
            }
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

    /// <summary>单个内容的构建输入、路线和产物事实。</summary>
    private sealed class ContentPlan
    {
        public string ContentName;
        public List<CollectedAssetInfo> Members;

        /// <summary>内容类型；同一内容的成员类型在 ValidateBundleGroup 中已保证一致</summary>
        public AssetContentType ContentType;

        /// <summary>内容包含的全部成员资产路径，供 Manifest 归属使用</summary>
        public List<string> AssetPaths = new List<string>();

        /// <summary>走 Unity 序列化路线的成员路径</summary>
        public List<string> SerializedPaths = new List<string>();

        /// <summary>Scene 成员路径</summary>
        public List<string> ScenePaths = new List<string>();

        /// <summary>预期物理输出文件名，按生成顺序</summary>
        public List<string> OutputNames = new List<string>();

        /// <summary>
        /// 物理内容文件名（由逻辑内容名派生的短哈希）。
        /// 逻辑名可能上百字符，部署路径较深时完整路径会超过 Windows MAX_PATH，运行时读不到文件。
        /// </summary>
        public string PhysicalName;

        /// <summary>输入指纹；null 表示该内容不参与历史制品复用</summary>
        public string InputFingerprint;

        /// <summary>已通过复用判定、制品已复制进本次输出的候选</summary>
        public bool CacheCandidate;

        /// <summary>复用命中的历史制品摘要（名称与 Hash/CRC/Size 均来自复制后的重新校验）</summary>
        public FileHelper.FileDigest? ReusedOutput;

        /// <summary>Summary 回放的依赖输出文件名；仅复用候选阶段有效</summary>
        public List<string> CachedDependencyFileNames;

        /// <summary>
        /// 内容级直接依赖的输出文件名集合（Manifest 依赖下标的事实来源）；
        /// 复用内容取 Summary 回放，重建内容取 Unity AssetBundleManifest，合并后统一填充。
        /// </summary>
        public List<string> DependencyFileNames;
    }
}
