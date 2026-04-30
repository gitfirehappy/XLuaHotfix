using System;
using System.Collections.Generic;
using UnityEditor;

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
        // 构建 owned 查找表
        var ownedGUIDs = new Dictionary<string, CollectedAssetInfo>();
        foreach (var asset in packageAssets)
        {
            if (!string.IsNullOrEmpty(asset.AssetGUID))
                ownedGUIDs[asset.AssetGUID] = asset;
        }

        // 隐式依赖候选：GUID → (refCount, PrimaryType, firstAssetPath, referencingBundles)
        var implicitCandidates = new Dictionary<string, ImplicitCandidate>();

        // 第一步：BFS 遍历，记录 Bundle 依赖边 + 隐式依赖候选
        var globalVisited = new HashSet<string>(); // 所有已展开过的 GUID

        foreach (var asset in packageAssets)
        {
            if (string.IsNullOrEmpty(asset.AssetGUID))
                continue;

            // 如果该资产在其他 Package 已展开过 → 跳过
            if (globalVisited.Contains(asset.AssetGUID))
                continue;

            var bfsStack = new List<string>(); // 当前路径（用于循环报告）
            var queue = new Queue<string>();
            queue.Enqueue(asset.AssetGUID);
            var localVisited = new HashSet<string>(); // 本次 BFS 已入队的 GUID

            while (queue.Count > 0)
            {
                string guid = queue.Dequeue();
                if (globalVisited.Contains(guid))
                    continue;

                globalVisited.Add(guid);
                bfsStack.Add(guid);

                string depPath = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(depPath))
                {
                    bfsStack.RemoveAt(bfsStack.Count - 1);
                    continue;
                }

                string[] deps;
                try
                {
                    deps = AssetDatabase.GetDependencies(depPath, false);
                }
                catch
                {
                    bfsStack.RemoveAt(bfsStack.Count - 1);
                    continue;
                }

                foreach (var dep in deps)
                {
                    if (ShouldSkip(dep))
                        continue;

                    string depGuid = AssetDatabase.AssetPathToGUID(dep);
                    if (string.IsNullOrEmpty(depGuid))
                        continue;

                    // 判断归属
                    if (ownedGUIDs.TryGetValue(depGuid, out var ownedAsset))
                    {
                        // Owned → 记录 Bundle 边
                        if (asset.BundleName != ownedAsset.BundleName)
                        {
                            string depType = AssetDatabase.GetMainAssetTypeAtPath(dep)?.Name ?? "Unknown";
                            graph.AddEdge(asset.BundleName, ownedAsset.BundleName, dep);
                        }
                        // 不展开 owned 资产的子依赖
                        continue;
                    }

                    // Unowned → 隐式依赖候选
                    if (!implicitCandidates.TryGetValue(depGuid, out var candidate))
                    {
                        string primaryType = AssetDatabase.GetMainAssetTypeAtPath(dep)?.Name ?? "Unknown";
                        candidate = new ImplicitCandidate
                        {
                            AssetPath = dep,
                            PrimaryType = primaryType,
                            PackageName = packageName
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
            }
        }

        // 第二步：SharePolicy 决策（共享 vs 复制）
        foreach (var kvp in implicitCandidates)
        {
            string depGuid = kvp.Key;
            var candidate = kvp.Value;
            int refCount = candidate.ReferencingBundles.Count;

            bool forceShare = IsGlobMatch(candidate.AssetPath, policy.ForceSharePatterns);
            bool noShare = IsGlobMatch(candidate.AssetPath, policy.NoSharePatterns);

            if (forceShare && noShare)
            {
                messages.Add(BuildMessage.Error("SHAREPOLICY_CONFLICT",
                    $"Asset '{candidate.AssetPath}' matches both ForceShare and NoShare patterns. Fix SharePolicyConfig for package '{packageName}'.",
                    candidate.AssetPath));
                continue;
            }

            string bundleName;
            bool isShared;
            bool isDuplicated;

            if (forceShare || (refCount >= policy.MinReferenceCount))
            {
                // 共享
                string packKey = candidate.PrimaryType;
                bundleName = BundleNameBuilder.Build(
                    packageName, "$shared", packKey);
                isShared = true;
                isDuplicated = false;

                var sharedEntry = CreateImplicitEntry(candidate, depGuid, bundleName, isShared, isDuplicated);
                result.Add(sharedEntry);

                // 记录共享 Bundle 到每个引用 Bundle 的依赖边
                foreach (var refBundle in candidate.ReferencingBundles)
                    graph.AddEdge(refBundle, bundleName, candidate.AssetPath);
            }
            else if (noShare)
            {
                // 强制复制
                foreach (var refBundle in candidate.ReferencingBundles)
                {
                    bundleName = refBundle;
                    var dupEntry = CreateImplicitEntry(candidate, depGuid, bundleName, false, true);
                    result.Add(dupEntry);
                    // 隐式依赖打入引用 Bundle，不产生新边
                }
            }
            else
            {
                // 引用不足 → 复制到每个引用 Bundle
                foreach (var refBundle in candidate.ReferencingBundles)
                {
                    bundleName = refBundle;
                    isDuplicated = candidate.ReferencingBundles.Count > 1;
                    var dupEntry = CreateImplicitEntry(candidate, depGuid, bundleName, false, isDuplicated);
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
        return new CollectedAssetInfo
        {
            AssetPath = candidate.AssetPath,
            AssetGUID = guid,
            Address = AssetAddressGenerator.GenerateShortAddress(candidate.AssetPath, candidate.PrimaryType, true),
            PrimaryType = candidate.PrimaryType,
            Labels = new List<string>(),
            GroupName = "$shared",
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

    private class ImplicitCandidate
    {
        public string AssetPath;
        public string PrimaryType;
        public string PackageName;
        public readonly List<string> ReferencingBundles = new();
    }
}
