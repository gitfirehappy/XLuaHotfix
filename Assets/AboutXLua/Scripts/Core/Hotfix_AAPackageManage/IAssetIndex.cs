using System.Collections.Generic;

public interface IAssetIndex
{
    List<string> GetKeysByLabel(string label);
    List<string> GetKeysByType(string type);
    List<string> GetLabels();
    bool ContainsKey(string key);
}
