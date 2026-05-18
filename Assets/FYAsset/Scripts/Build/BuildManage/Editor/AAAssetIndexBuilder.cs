#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor.AddressableAssets.Settings;

public static class AAAssetIndexBuilder
{
    public static AAAssetIndexData Build(AddressableAssetSettings settings)
    {
        var data = new AAAssetIndexData();
        if (settings == null)
            return data;

        var typeDict = new Dictionary<string, List<string>>();
        var labelDict = new Dictionary<string, List<string>>();

        foreach (var group in settings.groups)
        {
            if (group == null)
                continue;

            foreach (var entry in group.entries)
            {
                if (entry.IsFolder || string.IsNullOrEmpty(entry.address))
                    continue;

                string key = entry.address;
                List<string> labels = entry.labels.ToList();
                string entryType = labels.Count > 0 ? labels[0] : "Untyped";

                data.AssetEntries.Add(new PackageEntry
                {
                    key = key,
                    Type = entryType,
                    Labels = labels
                });

                AddToDict(typeDict, entryType, key);

                if (labels.Count == 0)
                {
                    AddToDict(labelDict, "Untyped", key);
                }
                else
                {
                    foreach (var label in labels)
                        AddToDict(labelDict, label, key);
                }
            }
        }

        foreach (var pair in typeDict)
            data.KeysByType.Add(new TypeToKeys { Type = pair.Key, Keys = pair.Value });

        foreach (var pair in labelDict)
            data.KeysByLabel.Add(new LabelToKeys { Label = pair.Key, Keys = pair.Value });

        return data;
    }

    private static void AddToDict(Dictionary<string, List<string>> dict, string name, string key)
    {
        if (!dict.TryGetValue(name, out var keys))
        {
            keys = new List<string>();
            dict[name] = keys;
        }

        keys.Add(key);
    }
}

public class AAAssetIndexData
{
    public List<PackageEntry> AssetEntries = new();
    public List<TypeToKeys> KeysByType = new();
    public List<LabelToKeys> KeysByLabel = new();
}
#endif
