using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 依赖分析器 —— 通过 BFS 遍历完成内容依赖边构建、隐式依赖发现与 SharePolicy 决策。
/// 单资产 visited set 防止无限展开，并缓存 AssetDatabase 依赖查询结果。
/// </summary>
public static class DependencyAnalyzer
{
    /// <summary>BFS 展开时默认过滤的框架级非资源文件扩展名</summary>
    private static readonly string[] DefaultFilterExtensions =
    {
        ".meta", ".cs", ".dll", ".asmdef", ".asmref", ".gitignore",
        ".cginc", ".hlsl", ".hlslinc"
    };

    /// <summary>BFS 展开时过滤的目录段</summary>
    private static readonly string[] FilterDirSegments = { "/Editor/", "\\Editor\\" };

    /// <summary>
    /// 对已收集资产执行依赖分析。
    /// </summary>
    /// <param name="assets">CollectionScanner 产出的资产列表</param>
    /// <param name="sharePolicy">项目级共享策略；null 时按默认策略处理</param>
    /// <param name="rawFileRules">项目级 RawFile 白名单；隐式依赖的分类必须与显式采集使用同一份规则</param>
    /// <param name="extraFilterExtensions">BFS 展开时追加过滤的项目级扩展名</param>
    /// <param name="graph">输出：内容依赖图</param>
    /// <param name="messages">输出：错误/警告/信息消息列表</param>
    /// <returns>增强后的资产列表（含提取为共享内容的隐式依赖条目；单引用隐式依赖随行打包，不产生条目）</returns>
    public static List<CollectedAssetInfo> Analyze(
        List<CollectedAssetInfo> assets,
        SharePolicyConfig sharePolicy,
        RawFileRules rawFileRules,
        IEnumerable<string> extraFilterExtensions,
        out BundleDependencyGraph graph,
        out List<BuildMessage> messages,
        IEnumerable<string> ignorePatterns = null)
    {
        graph = new BundleDependencyGraph();
        messages = new List<BuildMessage>();
        var result = new List<CollectedAssetInfo>(assets);
        HashSet<string> filterExtensions = BuildFilterExtensions(extraFilterExtensions);

        // 忽略路径资产不进入共享决策：单引用时随引用方物理带入，多引用时由 Unity 各自带入引用方内容，
        // 两种情况都不产生 manifest 条目。
        List<string> effectiveIgnorePatterns = ignorePatterns != null
            ? new List<string>(ignorePatterns)
            : null;

        AnalyzeAssets(
            assets,
            sharePolicy ?? new SharePolicyConfig(),
            rawFileRules,
            filterExtensions,
            effectiveIgnorePatterns,
            graph,
            messages,
            result);

        return result;
    }

    private static void AnalyzeAssets(
        List<CollectedAssetInfo> assets,
        SharePolicyConfig policy,
        RawFileRules rawFileRules,
        HashSet<string> filterExtensions,
        IList<string> ignorePatterns,
        BundleDependencyGraph graph,
        List<BuildMessage> messages,
        List<CollectedAssetInfo> result)
    {
        var ownedGUIDs = new Dictionary<string, CollectedAssetInfo>();
        foreach (var asset in assets)
        {
            if (!string.IsNullOrEmpty(asset.AssetGUID))
                ownedGUIDs[asset.AssetGUID] = asset;
        }

        var implicitCandidates = new Dictionary<string, ImplicitCandidate>();
        var cycleEntries = new List<(string fromPath, string toPath)>();
        BfsTraverseAll(assets, ownedGUIDs, filterExtensions, graph, implicitCandidates, cycleEntries);

        ReportDependencyCycles(cycleEntries, messages);

        // 忽略路径隐式化：只允许随引用方物理带入，不产生 manifest 条目、不建立 Bundle 边。
        if (ignorePatterns != null && implicitCandidates.Count > 0)
        {
            var implicitOnly = new List<string>();
            foreach (var kvp in implicitCandidates)
            {
                if (IsIgnoredPath(kvp.Value.AssetPath, ignorePatterns))
                    implicitOnly.Add(kvp.Key);
            }
            foreach (var guid in implicitOnly)
            {
                messages.Add(BuildMessage.Warning(
                    "IMPLICIT_IGNORED_PATH_DEP",
                    $"Asset '{implicitCandidates[guid].AssetPath}' 位于忽略路径，作为引用方物理随行内容打包（不生成 manifest 条目 / 独立内容）。",
                    implicitCandidates[guid].AssetPath));
                implicitCandidates.Remove(guid);
            }
        }

        // 共享决策：单引用随引用方打包，多引用提取共享内容，Force/NoShare 覆盖默认判定。
        ApplySharePolicy(implicitCandidates, policy, rawFileRules, graph, messages, result);
    }

    /// <summary>
    /// 对所有 Package 资产执行 BFS 展开，同时记录 Bundle 依赖边 + 隐式依赖候选 + 循环路径。
    /// </summary>
    private static void BfsTraverseAll(
        List<CollectedAssetInfo> packageAssets,
        Dictionary<string, CollectedAssetInfo> ownedGUIDs,
        HashSet<string> filterExtensions,
        BundleDependencyGraph graph,
        Dictionary<string, ImplicitCandidate> implicitCandidates,
        List<(string fromPath, string toPath)> cycleEntries)
    {
        var dependencyCache = new Dictionary<string, string[]>(StringComparer.Ordinal);

        foreach (var asset in packageAssets)
        {
            if (string.IsNullOrEmpty(asset.AssetGUID))
                continue;

            var bfsStack = new List<(string guid, string path)>();
            var bfsGuidSet = new HashSet<string>();
            var queue = new Queue<string>();
            queue.Enqueue(asset.AssetGUID);
            var localVisited = new HashSet<string>();
            localVisited.Add(asset.AssetGUID);

            while (queue.Count > 0)
            {
                string guid = queue.Dequeue();

                string depPath = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(depPath))
                    continue;

                bfsStack.Add((guid, depPath));
                bfsGuidSet.Add(guid);

                if (!TryGetDependencies(guid, depPath, dependencyCache, out string[] deps))
                {
                    PopBfsFrame(bfsStack, bfsGuidSet, guid);
                    continue;
                }

                foreach (var dep in deps)
                {
                    if (ShouldSkip(dep, filterExtensions))
                        continue;

                    string depGuid = AssetDatabase.AssetPathToGUID(dep);
                    if (string.IsNullOrEmpty(depGuid))
                        continue;

                    // 循环检测：depGuid 已在当前 BFS 路径中则报告并跳过
                    if (bfsGuidSet.Contains(depGuid))
                    {
                        // 从 bfsStack 查找路径用于报告（循环依赖是极端情况，线性扫描可接受）
                        for (int si = 0; si < bfsStack.Count; si++)
                        {
                            if (bfsStack[si].guid == depGuid)
                            {
                                cycleEntries.Add((bfsStack[si].path, dep));
                                break;
                            }
                        }
                        continue;
                    }

                    if (ownedGUIDs.TryGetValue(depGuid, out var ownedAsset))
                    {
                        if (asset.ContentName != ownedAsset.ContentName)
                            graph.AddEdge(asset.ContentName, ownedAsset.ContentName, dep);
                        continue;
                    }

                    if (!implicitCandidates.TryGetValue(depGuid, out var candidate))
                    {
                        string primaryType = AssetDatabase.GetMainAssetTypeAtPath(dep)?.Name ?? "Unknown";
                        candidate = new ImplicitCandidate
                        {
                            AssetPath = dep,
                            AssetType = primaryType
                        };
                        implicitCandidates[depGuid] = candidate;
                    }

                    if (!candidate.ReferencingContents.Contains(asset.ContentName))
                        candidate.ReferencingContents.Add(asset.ContentName);

                    if (!localVisited.Contains(depGuid))
                    {
                        localVisited.Add(depGuid);
                        queue.Enqueue(depGuid);
                    }
                }

                PopBfsFrame(bfsStack, bfsGuidSet, guid);
            }
        }
    }

    private static bool TryGetDependencies(
        string guid,
        string assetPath,
        Dictionary<string, string[]> dependencyCache,
        out string[] deps)
    {
        if (dependencyCache.TryGetValue(guid, out deps))
            return true;

        try
        {
            deps = AssetDatabase.GetDependencies(assetPath, false);
            dependencyCache[guid] = deps;
            return true;
        }
        catch (Exception ex)
        {
            deps = Array.Empty<string>();
                Debug.LogWarning($"[DependencyAnalyzer] 对 '{assetPath}' 执行 GetDependencies 失败：{ex.Message}");
            return false;
        }
    }

    private static void PopBfsFrame(List<(string guid, string path)> bfsStack, HashSet<string> bfsGuidSet, string guid)
    {
        if (bfsStack.Count > 0)
            bfsStack.RemoveAt(bfsStack.Count - 1);
        bfsGuidSet.Remove(guid);
    }

    /// <summary>报告 BFS 阶段发现的循环依赖（限制前 20 条，避免日志爆炸）</summary>
    private static void ReportDependencyCycles(
        List<(string fromPath, string toPath)> cycleEntries,
        List<BuildMessage> messages)
    {
        int cycleCount = 0;
        foreach (var (fromPath, toPath) in cycleEntries)
        {
            if (cycleCount < 20)
            {
                messages.Add(BuildMessage.Error(BuildErrorCodes.CycleDependency,
                    $"检测到循环依赖: '{fromPath}' -> ... -> '{toPath}' -> '{fromPath}'。",
                    fromPath));
            }
            cycleCount++;
        }

        if (cycleCount > 0)
        {
            messages.Add(BuildMessage.Warning(BuildErrorCodes.CycleCount,
                $"依赖分析中发现 {cycleCount} 个循环依赖，已上报前 20 个。",
                string.Empty));
            if (cycleCount > 20)
                messages.Add(BuildMessage.Warning(BuildErrorCodes.CycleTruncated,
                    $"另有 {cycleCount - 20} 个循环依赖未显示。", string.Empty));
        }
    }

    /// <summary>
    /// SharePolicy 决策：按引用方数量决定隐式依赖是随引用方打包还是提取共享内容。
    /// </summary>
    /// <remarks>
    /// 单引用随引用方打包：该依赖不生成独立内容条目，由 Unity 在构建引用方内容时一并写入，
    /// 也不进入公共索引（隐式条目一律 IsPublic=false）。多引用提取共享内容，ContentName 由
    /// BundleNameBuilder.BuildShared 生成。ForceShare 强制提取，NoShare 禁止提取；
    /// 多引用同时命中 NoShare 属于无法同时满足的配置矛盾，必须阻断构建。
    /// </remarks>
    private static void ApplySharePolicy(
        Dictionary<string, ImplicitCandidate> implicitCandidates,
        SharePolicyConfig policy,
        RawFileRules rawFileRules,
        BundleDependencyGraph graph,
        List<BuildMessage> messages,
        List<CollectedAssetInfo> result)
    {
        foreach (var kvp in implicitCandidates)
        {
            string depGuid = kvp.Key;
            var candidate = kvp.Value;

            bool forceShare = IsGlobMatch(candidate.AssetPath, policy.ForceSharePatterns);
            bool noShare = IsGlobMatch(candidate.AssetPath, policy.NoSharePatterns);

            // 同时匹配 ForceShare 和 NoShare 判为配置错误
            if (forceShare && noShare)
            {
                messages.Add(BuildMessage.Error(BuildErrorCodes.SharePolicyConflict,
                    $"Asset '{candidate.AssetPath}' 同时匹配 ForceShare 和 NoShare 规则。请修正 AssetCollectionSetting.SharePolicy。",
                    candidate.AssetPath));
                continue;
            }

            int referencingCount = candidate.ReferencingContents.Count;

            // NoShare 只允许“随引用方打包”。被多个内容引用时，同一份资产无法既保持唯一物理归属
            // 又被禁止共享，属于配置矛盾：静默选择任一侧都会产出错误内容集合，因此阻断构建。
            if (noShare && referencingCount > 1)
            {
                messages.Add(BuildMessage.Error(BuildErrorCodes.SharePolicyConflict,
                    $"Asset '{candidate.AssetPath}' 被多个内容引用但被 NoShare 命中，请改为 ForceShare 或收敛引用方。" +
                    $"引用方内容: {string.Join(", ", candidate.ReferencingContents)}。",
                    candidate.AssetPath));
                continue;
            }

            // 单引用且未强制共享：随引用方打包。该依赖由 Unity 在构建引用方内容时一并写入，
            // 因此不生成独立内容条目，也不建立 Bundle 依赖边。
            if (referencingCount == 1 && !forceShare)
                continue;

            AssetContentType contentType = AssetClassifier.ClassifyContentType(candidate.AssetPath, rawFileRules);
            string contentName = BundleNameBuilder.BuildShared(
                candidate.AssetType,
                contentType,
                candidate.AssetType);

            result.Add(CreateImplicitEntry(candidate, depGuid, contentName, contentType));

            foreach (var refContent in candidate.ReferencingContents)
                graph.AddEdge(refContent, contentName, candidate.AssetPath);
        }
    }

    private static bool IsIgnoredPath(string assetPath, IList<string> ignorePatterns)
    {
        return GitIgnoreMatcher.Evaluate(assetPath, ignorePatterns);
    }

    private static CollectedAssetInfo CreateImplicitEntry(
        ImplicitCandidate candidate,
        string guid,
        string contentName,
        AssetContentType contentType)
    {
        return new CollectedAssetInfo
        {
            AssetPath = candidate.AssetPath,
            AssetGUID = guid,
            Address = AssetAddressGenerator.GenerateAddress(candidate.AssetPath, candidate.AssetType, AssetAddressStyle.ShortName),
            AssetType = candidate.AssetType,
            Labels = new List<string>(),
            // 提取为共享内容时归入系统保留 Group；隐式依赖不是公共资源，只作为内容依赖存在。
            GroupName = SystemIdentifiers.SharedGroupName,
            ContentName = contentName,
            BundlePackingMode = BundlePackingMode.PackSeparately,
            ContentType = contentType,
            DependencyOrigin = AssetDependencyOrigin.Implicit,
            IsPublic = false
        };
    }

    private static HashSet<string> BuildFilterExtensions(IEnumerable<string> extraFilterExtensions)
    {
        var result = new HashSet<string>(DefaultFilterExtensions, StringComparer.OrdinalIgnoreCase);
        if (extraFilterExtensions == null)
            return result;

        foreach (string extension in extraFilterExtensions)
        {
            string normalized = NormalizeExtension(extension);
            if (!string.IsNullOrEmpty(normalized))
                result.Add(normalized);
        }

        return result;
    }

    private static string NormalizeExtension(string extension)
    {
        string normalized = extension?.Trim();
        if (string.IsNullOrEmpty(normalized))
            return string.Empty;

        return normalized[0] == '.' ? normalized : "." + normalized;
    }

    private static bool ShouldSkip(string assetPath, HashSet<string> filterExtensions)
    {
        if (string.IsNullOrEmpty(assetPath))
            return true;

        foreach (var ext in filterExtensions)
        {
            if (assetPath.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        if (AssetClassifier.IsUnsupportedAssetBundleEntry(assetPath, out _))
            return true;

        foreach (var seg in FilterDirSegments)
        {
            if (assetPath.IndexOf(seg, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }

        if (!assetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    private static bool IsGlobMatch(string assetPath, List<string> patterns)
    {
        return GitIgnoreMatcher.Evaluate(assetPath, patterns);
    }

    private class ImplicitCandidate
    {
        public string AssetPath;
        public string AssetType;

        /// <summary>引用该隐式依赖的显式内容名称集合，决定随行打包还是提取共享内容。</summary>
        public readonly List<string> ReferencingContents = new();
    }
}
