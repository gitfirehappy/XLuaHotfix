using System;
using System.Collections.Generic;

/// <summary>
/// 序列化的 AA key、Type 与 Labels。
/// </summary>
[Serializable]
[BinarySerializable]
public class PackageEntry
{
    [BinaryField(0)]
    public string key;

    [BinaryField(1)]
    public string Type; // AA 索引构建时取第一个 Label；无 Label 时为 Untyped。

    [BinaryField(2)]
    public List<string> Labels;
}
