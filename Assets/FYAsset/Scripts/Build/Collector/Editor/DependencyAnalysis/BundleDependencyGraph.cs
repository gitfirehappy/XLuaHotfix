using System.Collections.Generic;

/// <summary>
/// Bundle 依赖关系图，DependencyAnalyzer 的输出。
/// 扁平边列表，支持 ViaAssets 追溯到具体触发资产。
/// </summary>
public class BundleDependencyGraph
{
    /// <summary>所有有向边：FromBundle 依赖 ToBundle</summary>
    public List<BundleDependencyEdge> Edges = new();

    private Dictionary<string, HashSet<string>> _dependencyMap;

    /// <summary>
    /// 按 Bundle 按需构建的依赖索引。
    /// Key = FromBundle，Value = 直接依赖的 ToBundle 集合。
    /// O(1) 查找：某个 Bundle 依赖哪些其他 Bundle。
    /// </summary>
    public Dictionary<string, HashSet<string>> GetDependencyMap()
    {
        if (_dependencyMap != null)
            return _dependencyMap;

        _dependencyMap = new Dictionary<string, HashSet<string>>();
        foreach (var edge in Edges)
        {
            if (!_dependencyMap.ContainsKey(edge.FromBundle))
                _dependencyMap[edge.FromBundle] = new HashSet<string>();
            _dependencyMap[edge.FromBundle].Add(edge.ToBundle);
        }
        return _dependencyMap;
    }

    /// <summary>添加一条依赖边，自动去重（相同 From+To 仅追加 ViaAssets）</summary>
    public void AddEdge(string fromBundle, string toBundle, string viaAsset)
    {
        // 排除自引用边
        if (fromBundle == toBundle)
            return;

        foreach (var edge in Edges)
        {
            if (edge.FromBundle == fromBundle && edge.ToBundle == toBundle)
            {
                if (!edge.ViaAssets.Contains(viaAsset))
                    edge.ViaAssets.Add(viaAsset);
                return;
            }
        }

        Edges.Add(new BundleDependencyEdge
        {
            FromBundle = fromBundle,
            ToBundle = toBundle,
            ViaAssets = new List<string> { viaAsset }
        });

        // Edges 变更后使懒构建缓存失效，防止 GetDependencyMap() 返回陈旧数据
        _dependencyMap = null;
    }
}

/// <summary>
/// Bundle 依赖关系图中的单条有向边。
/// FromBundle 的某个资产引用了 ToBundle 中的资产。
/// </summary>
public class BundleDependencyEdge
{
    /// <summary>引用方 Bundle 名称</summary>
    public string FromBundle;

    /// <summary>被引用方 Bundle 名称</summary>
    public string ToBundle;

    /// <summary>触发此依赖边的具体资产路径列表</summary>
    public List<string> ViaAssets;
}
