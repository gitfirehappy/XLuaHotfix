#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.AddressableAssets.Settings;

/// <summary>AA 资源索引构建器；Type 来源为资产主类型，Labels 只保留业务分类。</summary>
public static class AAAssetIndexBuilder
{
    public static AAAssetIndexData Build(AddressableAssetSettings settings)
    {
        var data = new AAAssetIndexData();
        if (settings == null)
            return data;

        var typeDict = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var labelDict = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in settings.groups)
        {
            if (group == null)
                continue;
            foreach (var entry in group.entries)
            {
                if (entry == null || entry.IsFolder || string.IsNullOrEmpty(entry.address))
                    continue;

                string assetPath = AssetDatabase.GUIDToAssetPath(entry.guid);
                Type mainType = AssetDatabase.GetMainAssetTypeAtPath(assetPath);
                string assetType = AssetTypeKey.FromType(mainType);
                if (string.IsNullOrEmpty(assetType))
                    continue;

                List<string> labels = new List<string>(entry.labels);
                data.AssetEntries.Add(new PackageEntry
                {
                    key = entry.address,
                    Type = assetType,
                    Labels = labels
                });
                AddToDict(typeDict, assetType, entry.address);
                for (int i = 0; i < labels.Count; i++)
                    AddToDict(labelDict, labels[i], entry.address);
            }
        }

        foreach (var pair in typeDict)
            data.KeysByType.Add(new TypeToKeys { Type = pair.Key, Keys = pair.Value });
        foreach (var pair in labelDict)
            data.KeysByLabel.Add(new LabelToKeys { Label = pair.Key, Keys = pair.Value });
        return data;
    }

    private static void AddToDict(Dictionary<string, List<string>> dict, string key, string address)
    {
        if (string.IsNullOrWhiteSpace(key))
            return;
        if (!dict.TryGetValue(key, out List<string> addresses))
        {
            addresses = new List<string>();
            dict.Add(key, addresses);
        }
        if (!addresses.Contains(address))
            addresses.Add(address);
    }
}

public class AAAssetIndexData
{
    public List<PackageEntry> AssetEntries = new();
    public List<TypeToKeys> KeysByType = new();
    public List<LabelToKeys> KeysByLabel = new();
}
#endif
