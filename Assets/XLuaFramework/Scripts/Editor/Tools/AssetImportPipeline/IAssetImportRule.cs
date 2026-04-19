using UnityEditor;

/// <summary>
/// 资源导入规则接口 — 每种特殊资源处理实现此接口。
/// </summary>
public interface IAssetImportRule
{
    /// <summary>
    /// 规则名称，用于日志输出。
    /// </summary>
    string RuleName { get; }

    /// <summary>
    /// 是否匹配该资源路径（返回 true 则执行此规则）。
    /// </summary>
    bool Match(string assetPath);

    /// <summary>
    /// 在 Unity 导入资源前执行（可修改 Importer 设置）。
    /// </summary>
    void OnPreprocess(AssetImporter importer);

    /// <summary>
    /// 在 Unity 导入资源后执行。
    /// </summary>
    void OnPostprocess(string assetPath);
}

/// <summary>
/// 可选规则基类：只需实现 Match 和 OnPreprocess，OnPostprocess 默认空实现。
/// </summary>
public abstract class AssetImportRuleBase : IAssetImportRule
{
    public abstract string RuleName { get; }

    public abstract bool Match(string assetPath);

    public abstract void OnPreprocess(AssetImporter importer);

    public virtual void OnPostprocess(string assetPath)
    {
    }
}
