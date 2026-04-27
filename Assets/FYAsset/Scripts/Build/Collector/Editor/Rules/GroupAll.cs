/// <summary>
/// 默认分组规则 —— 所有资产归属到 Collector 所在的父 Group。
/// 行为与前 GroupRule 机制（Collector 直接映射到父 Group）完全一致。
/// </summary>
public sealed class GroupAll : IGroupRule
{
    #region Public Methods

    public string GetTargetGroup(GroupRuleContext ctx)
    {
        return ctx.ParentGroupName;
    }

    #endregion
}
