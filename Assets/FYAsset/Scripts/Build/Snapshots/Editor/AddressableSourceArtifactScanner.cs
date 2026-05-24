#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine;

/// <summary>
/// AA 源侧 scanner。用于 build 前扫描 Addressables entry，产出 asset GUID 粒度的 ArtifactDigest。
/// </summary>
public class AddressableSourceArtifactScanner : IArtifactScanner
{
    private const string BuiltInDataGroupName = "Built In Data";

    private readonly AddressableAssetSettings _settings;

    public AddressableSourceArtifactScanner(AddressableAssetSettings settings)
    {
        _settings = settings;
    }

    public List<ArtifactDigest> Scan()
    {
        var result = new List<ArtifactDigest>();
        if (_settings == null)
        {
            Debug.LogError("[AddressableSourceArtifactScanner] AddressableAssetSettings is null.");
            return result;
        }

        foreach (var group in _settings.groups)
        {
            if (group == null)
                continue;
            if (group.Name == BuiltInDataGroupName || group.HasSchema<PlayerDataGroupSchema>())
                continue;

            foreach (var entry in group.entries)
            {
                if (entry == null || string.IsNullOrEmpty(entry.guid))
                    continue;

                string assetPath = AssetDatabase.GUIDToAssetPath(entry.guid);
                if (string.IsNullOrEmpty(assetPath) || !FileHelper.Exists(assetPath))
                {
                    Debug.LogWarning($"[AddressableSourceArtifactScanner] Asset file missing for guid {entry.guid}: {assetPath}");
                    continue;
                }

                // 主资源文件 + .meta 共同决定 AA 源侧内容身份，避免只改 import 设置却漏判。
                string metaPath = assetPath + ".meta";
                long size = GetFileSize(assetPath) + GetFileSize(metaPath);
                result.Add(new ArtifactDigest
                {
                    Name = entry.guid,
                    Hash = HashGenerator.GenerateCompositeFileHash(assetPath, metaPath),
                    CRC = HashGenerator.GenerateCompositeFileCRC(assetPath, metaPath),
                    Size = size
                });
            }
        }

        return result;
    }

    private static long GetFileSize(string path)
    {
        if (string.IsNullOrEmpty(path) || !FileHelper.Exists(path))
            return 0;
        return new FileInfo(path).Length;
    }
}
#endif
