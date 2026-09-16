#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

/// <summary>Editor 预览用 Manifest：只建立公共 Address 到工程路径的内存映射。</summary>
public static class EditorVirtualManifestBuilder
{
    public static ABManifest Build(ScanResult scan, string settingPath)
    {
        var manifest = new ABManifest
        {
            PackageVersion = new VersionNumber { Major = 0, Minor = 0, Patch = 0 },
            AssetEntries = new List<ManifestAssetEntry>(),
            ContentEntries = new List<ManifestContentEntry>()
        };
        var contentIndices = new Dictionary<AssetContentType, int>();

        for (int i = 0; i < scan.Assets.Count; i++)
        {
            CollectedAssetInfo info = scan.Assets[i];
            if (info == null || !info.IsPublic || string.IsNullOrEmpty(info.Address))
                continue;

            if (!contentIndices.TryGetValue(info.ContentType, out int contentIndex))
            {
                contentIndex = manifest.ContentEntries.Count;
                contentIndices.Add(info.ContentType, contentIndex);
                manifest.ContentEntries.Add(new ManifestContentEntry
                {
                    FileName = $"editor_virtual_{info.ContentType}.bundle",
                    Hash = string.Empty,
                    CRC = 0,
                    Size = 0,
                    ContentType = info.ContentType,
                    DependencyIndices = System.Array.Empty<int>()
                });
            }

            manifest.AssetEntries.Add(new ManifestAssetEntry
            {
                Address = info.Address,
                AssetType = string.IsNullOrEmpty(info.AssetType) ? AssetTypeKey.FromType(typeof(UnityEngine.Object)) : info.AssetType,
                Labels = info.Labels != null ? new List<string>(info.Labels) : new List<string>(),
                AssetPath = info.AssetPath,
                ContentIndex = contentIndex
            });
        }

        manifest.Initialize();
        Debug.Log($"[EditorVirtualManifestBuilder] Editor 索引已构建。Assets={manifest.AssetEntries.Count}, Setting={settingPath}");
        return manifest;
    }
}
#endif
