using System.Collections.Generic;

/// <summary>Analyze 阶段唯一输出；不持有 Collect 快照，只承载分析后的资产和依赖图。</summary>
public sealed class ABDependencyAnalysisResult
{
    public List<CollectedAssetInfo> Assets { get; }
    public BundleDependencyGraph DependencyGraph { get; }
    public string InputFingerprint { get; }

    public ABDependencyAnalysisResult(
        IReadOnlyList<CollectedAssetInfo> assets,
        BundleDependencyGraph dependencyGraph,
        string inputFingerprint = null)
    {
        Assets = new List<CollectedAssetInfo>(assets ?? new List<CollectedAssetInfo>());
        DependencyGraph = dependencyGraph;
        InputFingerprint = inputFingerprint ?? string.Empty;
    }
}
