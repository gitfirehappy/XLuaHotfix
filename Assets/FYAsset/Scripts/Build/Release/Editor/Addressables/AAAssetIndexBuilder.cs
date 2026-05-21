#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor.AddressableAssets.Settings;

/// <summary>
/// AA 资源索引构建器 — 从 AddressableAssetSettings 提取全部 Entry，
/// 按 Type（首标签）和 Label 分组构建 KeysByType / KeysByLabel 索引。
///
/// 用于 AA 构建链路中填充 AAManifest 的索引字段。
/// </summary>
public static class AAAssetIndexBuilder
{
    /// <summary>
    /// 遍历 AddressableAssetSettings 全部 Group 的 Entry，构建 AAAssetIndexData。
    /// Type 取自 Entry 的首个 Label，无 Label 时默认为 "Untyped"。
    /// </summary>
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
                // Type = 首标签，无标签时默认 "Untyped"
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

        // 字典展平为 List<TypeToKeys> / List<LabelToKeys>，适配序列化
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

/// <summary>
/// AA 资源索引数据，写入 AAManifest.AssetEntries / KeysByType / KeysByLabel。
/// </summary>
public class AAAssetIndexData
{
    public List<PackageEntry> AssetEntries = new();
    public List<TypeToKeys> KeysByType = new();
    public List<LabelToKeys> KeysByLabel = new();
}
#endif
