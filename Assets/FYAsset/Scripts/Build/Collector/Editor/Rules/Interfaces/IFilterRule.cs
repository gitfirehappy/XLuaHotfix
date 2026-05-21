/// <summary>
/// 过滤规则接口 —— 决定一个资产路径是否应被采集。
/// 实现类须无参构造，由 RuleResolver 反射实例化并缓存。
/// </summary>
public interface IFilterRule
{
    /// <summary>返回 true 表示该资产可被采集，false 表示跳过</summary>
    bool IsCollectable(FilterRuleContext ctx);
}

/// <summary>
/// 过滤规则的上下文参数。
/// </summary>
public struct FilterRuleContext
{
    /// <summary>资产在项目中的相对路径</summary>
    public string AssetPath;

    /// <summary>资产文件扩展名（含点号，如 .prefab）</summary>
    public string Extension;
    
    /// <summary>采集器的根目录路径</summary>
    public string CollectPath;
}
