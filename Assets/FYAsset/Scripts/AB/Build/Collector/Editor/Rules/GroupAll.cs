/// <summary>
/// 默认分组规则 —— 所有资产归属到 Collector 所在的父 Group。
/// </summary>
public sealed class GroupAll : IGroupRule
{
    public string GetTargetGroup(GroupRuleContext ctx)
    {
        return ctx.ParentGroupName;
    }
}
