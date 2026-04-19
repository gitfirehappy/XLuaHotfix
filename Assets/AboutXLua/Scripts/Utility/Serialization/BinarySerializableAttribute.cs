using System;

/// <summary>
/// 标记类型可参与二进制序列化。
/// 顶层文件类型可指定 Magic / SchemaVersion。
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
public sealed class BinarySerializableAttribute : Attribute
{
    public uint Magic { get; set; }
    public ushort SchemaVersion { get; set; } = 1;
}

/// <summary>
/// 标记字段序列化顺序。
/// </summary>
[AttributeUsage(AttributeTargets.Field)]
public sealed class BinaryFieldAttribute : Attribute
{
    public int Order { get; }

    public BinaryFieldAttribute(int order)
    {
        Order = order;
    }
}
