/// <summary>
/// 分组规则接口 —— 决定采集到的资产路由到哪个 Group。
/// 一个 Collector 可通过 GroupRule 将不同资产分配到不同 Group。
/// 实现类须无参构造，由 RuleResolver 反射实例化并缓存。
/// </summary>
public interface IGroupRule
{
    /// <summary>返回该资产应归属的目标 Group 名称</summary>
    string GetTargetGroup(GroupRuleContext ctx);
}

/// <summary>
/// 分组规则的上下文参数。
/// </summary>
public struct GroupRuleContext
{
    /// <summary>资产在项目中的相对路径</summary>
    public string AssetPath;

    /// <summary>Classifier 的分类结果（角色 + 载荷类型）</summary>
    public AssetClassification Classification;

    /// <summary>采集器的根目录路径</summary>
    public string CollectPath;

    /// <summary>采集器所属的 Package 名称</summary>
    public string PackageName;

    /// <summary>采集器所属的父 Group 名称，供 GroupAll 等回退规则使用</summary>
    public string ParentGroupName;
}
