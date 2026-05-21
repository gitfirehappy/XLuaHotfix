/// <summary>
/// 地址规则接口 —— 为采集到的资产生成运行时寻址地址（Address）。
/// 实现类须无参构造，由 RuleResolver 反射实例化并缓存。
/// </summary>
public interface IAddressRule
{
    /// <summary>根据上下文生成资产的运行时地址</summary>
    string GetAddress(AddressRuleContext ctx);
}

/// <summary>
/// 地址规则的上下文参数。
/// </summary>
public struct AddressRuleContext
{
    /// <summary>资产在项目中的相对路径</summary>
    public string AssetPath;

    /// <summary>所属 Group 名称</summary>
    public string GroupName;

    /// <summary>采集器的根目录路径</summary>
    public string CollectPath;
    
    /// <summary>资产主类型名称（如 Texture2D / GameObject）</summary>
    public string PrimaryType;
}
