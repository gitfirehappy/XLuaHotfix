/// <summary>
/// 默认分组规则 —— 所有资产归属到 Collector 所在的父 Group。
/// 行为与前 GroupRule 机制（Collector 直接映射到父 Group）完全一致。
/// </summary>
public sealed class GroupAll : IGroupRule
{
    #region 公共方法

    /// <summary>始终返回父 Group 名称，保持资源归属 Collector 所在 Group</summary>
    public string GetTargetGroup(GroupRuleContext ctx)
    {
        return ctx.ParentGroupName;
    }

    #endregion
}
