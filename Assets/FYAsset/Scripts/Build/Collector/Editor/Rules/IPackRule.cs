using System.Collections.Generic;

/// <summary>
/// 打包规则接口 —— 为资产生成分组键（PackKey），框架据此组装最终的逻辑 Bundle 名称。
/// 实现类须无参构造，由 RuleResolver 反射实例化并缓存。
/// </summary>
public interface IPackRule
{
    /// <summary>根据上下文生成资产的打包分组键</summary>
    string GetPackKey(PackRuleContext ctx);
}

/// <summary>
/// 打包规则的上下文参数。
/// </summary>
public struct PackRuleContext
{
    /// <summary>资产在项目中的相对路径</summary>
    public string AssetPath;

    /// <summary>所属 Group 名称</summary>
    public string GroupName;

    /// <summary>采集器的根目录路径</summary>
    public string CollectPath;

    /// <summary>所属 Package 名称</summary>
    public string PackageName;

    /// <summary>资产的分类结果（角色 + 载荷类型）</summary>
    public AssetClassification Classification;
    
    /// <summary>合并后的标签列表（Group.Tags ∪ Collector.Tags），供 PackByLabel 使用</summary>
    public IReadOnlyList<string> Labels;
}
