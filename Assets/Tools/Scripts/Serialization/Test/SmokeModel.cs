using System;
using System.Collections.Generic;

/// <summary>
/// Smoke 测试用模型。
/// </summary>
[BinarySerializable(Magic = 0x54455354, SchemaVersion = 1)]
[Serializable]
public class SmokeModel
{
    [BinaryField(0)] public int Id;
    [BinaryField(1)] public string Name;
    [BinaryField(2)] public List<string> Tags = new();
}
