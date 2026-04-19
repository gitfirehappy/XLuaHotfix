using System;

/// <summary>
/// 标记LuaBridgeType枚举对应的桥接组件类型
/// </summary>
[AttributeUsage(AttributeTargets.Field)]
public class LuaBridgeTypeMappingAttribute : Attribute
{
    public Type BridgeType { get; }

    public LuaBridgeTypeMappingAttribute(Type bridgeType)
    {
        BridgeType = bridgeType;
    }
}