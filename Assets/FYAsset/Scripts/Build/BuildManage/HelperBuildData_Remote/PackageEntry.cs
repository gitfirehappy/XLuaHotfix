using System;
using System.Collections.Generic;

/// <summary>
/// AA包索引
/// </summary>
[Serializable]
public class PackageEntry
{
    public string key;
    public string Type;         // 默认第一个 Label 作为 Type
    public List<string> Labels;
}