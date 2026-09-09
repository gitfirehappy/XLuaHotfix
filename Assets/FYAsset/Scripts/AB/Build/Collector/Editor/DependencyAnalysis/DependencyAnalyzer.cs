using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 依赖分析器 —— 通过 BFS 遍历完成 Bundle 依赖边构建、隐式依赖发现与 SharePolicy 决策。
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
    /// 对指定 Package 的已收集资产执行依赖分析。
    /// </summary>
    /// <param name="assets">CollectionScanner 产出的资产列表（可包含多个 Package）</param>
    /// <param name="sharePolicies">Package 级共享策略（PackageName → SharePolicyConfig）</param>
    /// <param name="extraFilterExtensions">BFS 展开时追加过滤的项目级扩展名</param>
    /// <param name="graph">输出：Bundle 依赖图</param>
    /// <param name="messages">输出：错误/警告/信息消息列表</param>
    /// <returns>增强后的资产列表（含隐式依赖条目）</returns>
    public static List<CollectedAssetInfo> Analyze(
        List<CollectedAssetInfo> assets,
        Dictionary<string, SharePolicyConfig> sharePolicies,
        IEnumerable<string> extraFilterExtensions,
        out BundleDependencyGraph graph,
        out List<BuildMessage> messages,
        IEnumerable<string> ignorePatterns = null)
    {
        graph = new BundleDependencyGraph();
        messages = new List<BuildMessage>();
        var result = new List<CollectedAssetInfo>(assets);
        HashSet<string> filterExtensions = BuildFilterExtensions(extraFilterExtensions);

        var byPackage = new Dictionary<string, List<CollectedAssetInfo>>();
        foreach (var asset in assets)
        {
            string pkg = asset.PackageName ?? string.Empty;
            if (!byPackage.ContainsKey(pkg))
                byPackage[pkg] = new List<CollectedAssetInfo>();
            byPackage[pkg].Add(asset);
        }

        // 忽略路径资产成为隐式随行打包内容（不产生 manifest 条目、不生成独立 Bundle）；
        // 非忽略但未收集资产一律按自身类型独立成桶。
        HashSet<string> effectiveIgnorePatterns = ignorePatterns != null
            ? new HashSet<string>(ignorePatterns, StringComparer.OrdinalIgnoreCase)
            : null;

        foreach (var kvp in byPackage)
        {
            string packageName = kvp.Key;
            var packageAssets = kvp.Value;
            var policy = sharePolicies != null && sharePolicies.TryGetValue(packageName, out var p)
                ? p : new SharePolicyConfig();

            AnalyzePackage(packageAssets, policy, packageName, filterExtensions, effectiveIgnorePatterns, graph, messages, result);
        }

        return result;
    }

    private static void AnalyzePackage(
        List<CollectedAssetInfo> packageAssets,
        SharePolicyConfig policy,
        string packageName,
        HashSet<string> filterExtensions,
        HashSet<string> ignorePatterns,
        BundleDependencyGraph graph,
        List<BuildMessage> messages,
        List<CollectedAssetInfo> result)
    {
        var ownedGUIDs = new Dictionary<string, CollectedAssetInfo>();
        foreach (var asset in packageAssets)
        {
            if (!string.IsNullOrEmpty(asset.AssetGUID))
                ownedGUIDs[asset.AssetGUID] = asset;
        }

        var implicitCandidates = new Dictionary<string, ImplicitCandidate>();
        var cycleEntries = new List<(string fromPath, string toPath)>();
        BfsTraverseAll(packageAssets, ownedGUIDs, filterExtensions, graph, implicitCandidates, cycleEntries);

        ReportDependencyCycles(cycleEntries, messages, packageName);

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
                    $"Asset '{implicitCandidates[guid].AssetPath}' 位于忽略路径，作为引用方物理随行内容打包（不生成 manifest 条目 / 独立 Bundle）。",
                    implicitCandidates[guid].AssetPath));
                implicitCandidates.Remove(guid);
            }
        }

        // 隐式依赖一律按自身类型独立成桶。
        ApplySharePolicy(implicitCandidates, policy, packageName, graph, messages, result);
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
                        if (asset.BundleName != ownedAsset.BundleName)
                            graph.AddEdge(asset.BundleName, ownedAsset.BundleName, dep);
                        continue;
                    }

                    if (!implicitCandidates.TryGetValue(depGuid, out var candidate))
                    {
                        string primaryType = AssetDatabase.GetMainAssetTypeAtPath(dep)?.Name ?? "Unknown";
                        candidate = new ImplicitCandidate
                        {
                            AssetPath = dep,
                            PrimaryType = primaryType,
                            PackageName = asset.PackageName ?? string.Empty
                        };
                        implicitCandidates[depGuid] = candidate;
                    }

                    if (!candidate.ReferencingBundles.Contains(asset.BundleName))
                        candidate.ReferencingBundles.Add(asset.BundleName);

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
        List<BuildMessage> messages,
        string packageName)
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
                $"Package '{packageName}' 中发现 {cycleCount} 个循环依赖，已上报前 20 个。",
                packageName));
            if (cycleCount > 20)
                messages.Add(BuildMessage.Warning(BuildErrorCodes.CycleTruncated,
                    $"另有 {cycleCount - 20} 个循环依赖未显示。", packageName));
        }
    }

    /// <summary>SharePolicy 决策：对每个隐式依赖做规则冲突校验并分配到共享 Bundle</summary>
    private static void ApplySharePolicy(
        Dictionary<string, ImplicitCandidate> implicitCandidates,
        SharePolicyConfig policy,
        string packageName,
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
                    $"Asset '{candidate.AssetPath}' 同时匹配 ForceShare 和 NoShare 规则。请修正 Package '{packageName}' 的 SharePolicyConfig。",
                    candidate.AssetPath));
                continue;
            }

            // 一律按依赖自身（payload + 精确类型）形成独立 Bundle，保证分桶唯一性。
            string bundleName = BundleNameBuilder.BuildShared(
                packageName,
                candidate.PrimaryType,
                EPayloadKind.Serialized,
                candidate.PrimaryType);

            var sharedEntry = CreateImplicitEntry(candidate, depGuid, bundleName, isShared: true, isDuplicated: false);
            result.Add(sharedEntry);

            foreach (var refBundle in candidate.ReferencingBundles)
                graph.AddEdge(refBundle, bundleName, candidate.AssetPath);
        }
    }

    private static bool IsIgnoredPath(string assetPath, HashSet<string> ignorePatterns)
    {
        if (string.IsNullOrEmpty(assetPath) || ignorePatterns == null || ignorePatterns.Count == 0)
            return false;
        foreach (var pattern in ignorePatterns)
        {
            if (GlobMatcher.IsMatch(assetPath, pattern))
                return true;
        }
        return false;
    }

    private static CollectedAssetInfo CreateImplicitEntry(
        ImplicitCandidate candidate,
        string guid,
        string bundleName,
        bool isShared,
        bool isDuplicated)
    {
        // 共享条目 GroupName = "$shared"，否则 = PackageName，避免数据模型语义冲突
        string groupName = isShared ? SystemIdentifiers.SharedGroupName : candidate.PackageName;

        return new CollectedAssetInfo
        {
            AssetPath = candidate.AssetPath,
            AssetGUID = guid,
            Address = AssetAddressGenerator.GenerateAddress(candidate.AssetPath, candidate.PrimaryType, AssetAddressStyle.ShortName),
            PrimaryType = candidate.PrimaryType,
            Labels = new List<string>(),
            GroupLabels = new List<string>(),
            AssetLabels = new List<string>(),
            GroupName = groupName,
            PackageName = candidate.PackageName,
            BundleName = bundleName,
            BundlePackingMode = BundlePackingMode.PackSeparately,
            Classification = new AssetClassification
            {
                Role = EAssetRole.ImplicitDependency,
                PayloadKind = EPayloadKind.Serialized
            },
            CollectorType = ECollectorType.Implicit,
            IsInSharedBundle = isShared,
            IsDuplicated = isDuplicated
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
        if (patterns == null || patterns.Count == 0)
            return false;

        foreach (var pattern in patterns)
        {
            if (GlobMatcher.IsMatch(assetPath, pattern))
                return true;
        }
        return false;
    }

    private class ImplicitCandidate
    {
        public string AssetPath;
        public string PrimaryType;
        public string PackageName;
        public readonly List<string> ReferencingBundles = new();
    }
}
