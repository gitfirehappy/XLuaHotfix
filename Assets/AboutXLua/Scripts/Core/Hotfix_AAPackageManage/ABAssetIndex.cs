using System;
using System.Collections.Generic;

public class ABAssetIndex : IAssetIndex
{
    public List<string> GetKeysByLabel(string label) => throw new NotImplementedException("ABAssetIndex: B4 阶段实现");
    public List<string> GetKeysByType(string type) => throw new NotImplementedException("ABAssetIndex: B4 阶段实现");
    public List<string> GetLabels() => throw new NotImplementedException("ABAssetIndex: B4 阶段实现");
    public bool ContainsKey(string key) => throw new NotImplementedException("ABAssetIndex: B4 阶段实现");
}
