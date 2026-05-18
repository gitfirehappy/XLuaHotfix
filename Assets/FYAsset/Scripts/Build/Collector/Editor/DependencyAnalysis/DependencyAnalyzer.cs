using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 依赖分析器 —— 单次 BFS 遍历完成三项工作：
///   第一步： Bundle 依赖边构建 + 隐式依赖发现（refCount 计数）
///   第二步： SharePolicy 决策（共享 vs 复制）
/// 全局 visited set 防止无限展开。
/// </summary>
public static class DependencyAnalyzer
{
    /// <summary>BFS 展开时过滤的非资源文件扩展名</summary>
    private static readonly HashSet<string> FilterExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".meta", ".cs", ".dll", ".asmdef", ".asmref", ".py", ".js", ".shader"
    };

    /// <summary>BFS 展开时过滤的目录段</summary>
    private static readonly string[] FilterDirSegments = { "/Editor/", "\\Editor\\" };

    /// <summary>
    /// 对指定 Package 的已收集资产执行依赖分析。
    /// </summary>
    /// <param name="assets">CollectionScanner 产出的资产列表（可包含多个 Package）</param>
    /// <param name="sharePolicies">Per-Package 共享策略（PackageName → SharePolicyConfig）</param>
    /// <param name="graph">输出：Bundle 依赖图</param>
    /// <param name="messages">输出：错误/警告/信息消息列表</param>
    /// <returns>增强后的资产列表（含隐式依赖条目）</returns>
    public static List<CollectedAssetInfo> Analyze(
        List<CollectedAssetInfo> assets,
        Dictionary<string, SharePolicyConfig> sharePolicies,
        out BundleDependencyGraph graph,
        out List<BuildMessage> messages)
    {
        graph = new BundleDependencyGraph();
        messages = new List<BuildMessage>();
        var result = new List<CollectedAssetInfo>(assets);

        // 按 Package 分组
        var byPackage = new Dictionary<string, List<CollectedAssetInfo>>();
        foreach (var asset in assets)
        {
            string pkg = asset.PackageName ?? string.Empty;
            if (!byPackage.ContainsKey(pkg))
                byPackage[pkg] = new List<CollectedAssetInfo>();
            byPackage[pkg].Add(asset);
        }

        foreach (var kvp in byPackage)
        {
            string packageName = kvp.Key;
            var packageAssets = kvp.Value;
            var policy = sharePolicies != null && sharePolicies.TryGetValue(packageName, out var p)
                ? p : new SharePolicyConfig();

            AnalyzePackage(packageAssets, policy, packageName, graph, messages, result);
        }

        return result;
    }

    private static void AnalyzePackage(
        List<CollectedAssetInfo> packageAssets,
        SharePolicyConfig policy,
        string packageName,
        BundleDependencyGraph graph,
        List<BuildMessage> messages,
        List<CollectedAssetInfo> result)
    {
        // 构建 owned 查找表（GUID → CollectedAssetInfo）
        var ownedGUIDs = new Dictionary<string, CollectedAssetInfo>();
        foreach (var asset in packageAssets)
        {
            if (!string.IsNullOrEmpty(asset.AssetGUID))
                ownedGUIDs[asset.AssetGUID] = asset;
        }

        // 第一阶段：BFS 遍历 — Bundle 依赖边 + 隐式依赖候选发现 + 循环检测
        var implicitCandidates = new Dictionary<string, ImplicitCandidate>();
        var cycleEntries = new List<(string fromPath, string toPath)>();
        BfsTraverseAll(packageAssets, ownedGUIDs, graph, implicitCandidates, cycleEntries);

        // 第二阶段：报告循环依赖诊断消息
        ReportDependencyCycles(cycleEntries, messages, packageName);

        // 第三阶段：SharePolicy 决策（共享 vs 复制）
        ApplySharePolicy(implicitCandidates, policy, packageName, graph, messages, result);
    }

    /// <summary>
    /// 对所有 Package 资产执行 BFS 展开，同时记录 Bundle 依赖边 + 隐式依赖候选 + 循环路径。
    /// </summary>
    private static void BfsTraverseAll(
        List<CollectedAssetInfo> packageAssets,
        Dictionary<string, CollectedAssetInfo> ownedGUIDs,
        BundleDependencyGraph graph,
        Dictionary<string, ImplicitCandidate> implicitCandidates,
        List<(string fromPath, string toPath)> cycleEntries)
    {
        var globalVisited = new HashSet<string>(); // 跨资产共享：所有已展开过的 GUID

        foreach (var asset in packageAssets)
        {
            if (string.IsNullOrEmpty(asset.AssetGUID))
                continue;

            if (globalVisited.Contains(asset.AssetGUID))
                continue;

            var bfsStack = new List<(string guid, string path)>();
            var bfsGuidSet = new HashSet<string>(); // 并行 HashSet，O(1) 循环检测
            var queue = new Queue<string>();
            queue.Enqueue(asset.AssetGUID);
            var localVisited = new HashSet<string>(); // 本次 BFS 已入队的 GUID

            while (queue.Count > 0)
            {
                string guid = queue.Dequeue();
                if (globalVisited.Contains(guid))
                    continue;

                string depPath = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(depPath))
                    continue;

                globalVisited.Add(guid);
                bfsStack.Add((guid, depPath));
                bfsGuidSet.Add(guid);

                string[] deps;
                try
                {
                    deps = AssetDatabase.GetDependencies(depPath, false);
                }
                catch (Exception ex)
                {
                    bfsStack.RemoveAt(bfsStack.Count - 1);
                    bfsGuidSet.Remove(guid);
                    Debug.LogWarning($"[DependencyAnalyzer] GetDependencies failed for '{depPath}': {ex.Message}");
                    continue;
                }

                foreach (var dep in deps)
                {
                    if (ShouldSkip(dep))
                        continue;

                    string depGuid = AssetDatabase.AssetPathToGUID(dep);
                    if (string.IsNullOrEmpty(depGuid))
                        continue;

                    // 循环检测：depGuid 已在当前 BFS 路径中 → 报告并跳过（O(1) fast path）
                    if (bfsGuidSet.Contains(depGuid))
                    {
                        // 从 bfsStack 查找路径用于报告（cycle 是极端情况，线性扫描可接受）
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

                    // 判断归属
                    if (ownedGUIDs.TryGetValue(depGuid, out var ownedAsset))
                    {
                        // 已归属 → 记录 Bundle 边（排除同 Bundle）
                        if (asset.BundleName != ownedAsset.BundleName)
                        {
                            string depType = AssetDatabase.GetMainAssetTypeAtPath(dep)?.Name ?? "Unknown";
                            graph.AddEdge(asset.BundleName, ownedAsset.BundleName, dep);
                        }
                        continue;
                    }

                    // 未归属 → 隐式依赖候选
                    if (!implicitCandidates.TryGetValue(depGuid, out var candidate))
                    {
                        string primaryType = AssetDatabase.GetMainAssetTypeAtPath(dep)?.Name ?? "Unknown";
                        candidate = new ImplicitCandidate
                        {
                            AssetPath = dep,
                            PrimaryType = primaryType,
                            PackageName = string.Empty // 由调用方填充
                        };
                        implicitCandidates[depGuid] = candidate;
                    }

                    if (!candidate.ReferencingBundles.Contains(asset.BundleName))
                        candidate.ReferencingBundles.Add(asset.BundleName);

                    // 展开隐式依赖的子依赖
                    if (!localVisited.Contains(depGuid) && !globalVisited.Contains(depGuid))
                    {
                        localVisited.Add(depGuid);
                        queue.Enqueue(depGuid);
                    }
                }

                bfsStack.RemoveAt(bfsStack.Count - 1);
                bfsGuidSet.Remove(guid);
            }
        }
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
                messages.Add(BuildMessage.Error("CYCLE_DEPENDENCY",
                    $"检测到循环依赖: '{fromPath}' -> ... -> '{toPath}' -> '{fromPath}'。",
                    fromPath));
            }
            cycleCount++;
        }

        if (cycleCount > 0)
        {
            messages.Add(BuildMessage.Warning("CYCLE_COUNT",
                $"Package '{packageName}' 中发现 {cycleCount} 个循环依赖，已上报前 20 个。",
                packageName));
            if (cycleCount > 20)
                messages.Add(BuildMessage.Warning("CYCLE_TRUNCATED",
                    $"另有 {cycleCount - 20} 个循环依赖未显示。", packageName));
        }
    }

    /// <summary>SharePolicy 决策：对每个隐式依赖决定共享还是复制到引用 Bundle</summary>
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
            int refCount = candidate.ReferencingBundles.Count;

            bool forceShare = IsGlobMatch(candidate.AssetPath, policy.ForceSharePatterns);
            bool noShare = IsGlobMatch(candidate.AssetPath, policy.NoSharePatterns);

            // 规则冲突检测：同时匹配 ForceShare 和 NoShare → 配置错误
            if (forceShare && noShare)
            {
                messages.Add(BuildMessage.Error("SHAREPOLICY_CONFLICT",
                    $"Asset '{candidate.AssetPath}' 同时匹配 ForceShare 和 NoShare 规则。请修正 Package '{packageName}' 的 SharePolicyConfig。",
                    candidate.AssetPath));
                continue;
            }

            // MinAssetSizeBytes 检查：小于阈值的资产不参与共享
            bool meetsSizeThreshold = true;
            if (policy.MinAssetSizeBytes > 0)
            {
                long fileSize = GetAssetFileSize(candidate.AssetPath);
                if (fileSize > 0 && fileSize < policy.MinAssetSizeBytes)
                    meetsSizeThreshold = false;
            }

            string bundleName;
            bool isShared;
            bool isDuplicated;

            if (forceShare || (refCount >= policy.MinReferenceCount && meetsSizeThreshold))
            {
                // 共享：打入 "$shared" Bundle
                string packKey = candidate.PrimaryType;
                bundleName = BundleNameBuilder.Build(packageName, "$shared", packKey);
                isShared = true;
                isDuplicated = false;

                var sharedEntry = CreateImplicitEntry(candidate, depGuid, bundleName, isShared, isDuplicated);
                result.Add(sharedEntry);

                // 记录每个引用 Bundle 到共享 Bundle 的依赖边
                foreach (var refBundle in candidate.ReferencingBundles)
                    graph.AddEdge(refBundle, bundleName, candidate.AssetPath);
            }
            else if (noShare)
            {
                // 强制复制：每个引用 Bundle 各一份
                foreach (var refBundle in candidate.ReferencingBundles)
                {
                    var dupEntry = CreateImplicitEntry(candidate, depGuid, refBundle, false, true);
                    result.Add(dupEntry);
                }
            }
            else
            {
                // 引用不足最小阈值 → 复制到每个引用 Bundle
                foreach (var refBundle in candidate.ReferencingBundles)
                {
                    isDuplicated = candidate.ReferencingBundles.Count > 1;
                    var dupEntry = CreateImplicitEntry(candidate, depGuid, refBundle, false, isDuplicated);
                    result.Add(dupEntry);
                }
            }
        }
    }

    private static CollectedAssetInfo CreateImplicitEntry(
        ImplicitCandidate candidate,
        string guid,
        string bundleName,
        bool isShared,
        bool isDuplicated)
    {
        // 共享型 → GroupName = "$shared"
        // 复制型 → GroupName = PackageName（与 "$shared" 明确区分，避免数据模型语义冲突）
        string groupName = isShared ? "$shared" : candidate.PackageName;

        return new CollectedAssetInfo
        {
            AssetPath = candidate.AssetPath,
            AssetGUID = guid,
            Address = AssetAddressGenerator.GenerateShortAddress(candidate.AssetPath, candidate.PrimaryType, true),
            PrimaryType = candidate.PrimaryType,
            Labels = new List<string>(),
            GroupName = groupName,
            PackageName = candidate.PackageName,
            BundleName = bundleName,
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

    private static bool ShouldSkip(string assetPath)
    {
        if (string.IsNullOrEmpty(assetPath))
            return true;

        // 排除已知不可打包的扩展名
        foreach (var ext in FilterExtensions)
        {
            if (assetPath.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        // 排除 Editor 目录
        foreach (var seg in FilterDirSegments)
        {
            if (assetPath.IndexOf(seg, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }

        // 排除不在 Assets/ 下的资源
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

    private static long GetAssetFileSize(string assetPath)
    {
        if (string.IsNullOrEmpty(assetPath))
            return -1;

        try
        {
            var info = new System.IO.FileInfo(assetPath);
            if (info.Exists)
                return info.Length;
        }
        catch
        {
            // 权限不足或路径非法 → 忽略，不影响分析流程
        }
        return -1;
    }

    private class ImplicitCandidate
    {
        public string AssetPath;
        public string PrimaryType;
        public string PackageName;
        public readonly List<string> ReferencingBundles = new();
    }
}
