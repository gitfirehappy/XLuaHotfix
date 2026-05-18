using System;
using System.Collections.Generic;

/// <summary>
/// AA包索引
/// </summary>
[Serializable]
[BinarySerializable]
public class PackageEntry
{
    [BinaryField(0)]
    public string key;

    [BinaryField(1)]
    public string Type;         // 默认第一个 Label 作为 Type

    [BinaryField(2)]
    public List<string> Labels;
}
