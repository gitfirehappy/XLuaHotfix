#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 从 Collector 配置扫出内存 ABManifest，供 Editor PlayMode 使用。
/// 不跑完整构建管线，不写磁盘。
/// </summary>
[InitializeOnLoad]
public static class EditorVirtualManifestBuilder
{
    static EditorVirtualManifestBuilder()
    {
        ABPackageManager.RegisterEditorManifestBuilder(Build);
    }

    public static ABManifest Build()
    {
        string settingPath = FYAssetABSettings.Instance.AssetCollectionSettingPath;
        var setting = AssetDatabase.LoadAssetAtPath<AssetCollectionSetting>(settingPath);
        if (setting == null)
        {
            Debug.LogError($"[EditorVirtualManifestBuilder] 未找到 AssetCollectionSetting: {settingPath}");
            return null;
        }

        ScanResult scan = CollectionScanner.Scan(setting);
        if (scan == null || scan.Assets == null || scan.Assets.Count == 0)
        {
            Debug.LogWarning("[EditorVirtualManifestBuilder] Collector 扫描结果为空。");
            return null;
        }

        var manifest = new ABManifest
        {
            PackageName = "EditorPlayMode",
            PackageVersion = new VersionNumber { Major = 0, Minor = 0, Patch = 0, Build = 0 },
            BuildTimestamp = DateTime.UtcNow.ToString("o"),
            FileHash = string.Empty,
            AssetEntries = new List<ManifestAssetEntry>(),
            BundleEntries = new List<ManifestBundleEntry>(),
            DeliveryBundles = new List<ManifestBundleEntry>()
        };

        manifest.BundleEntries.Add(new ManifestBundleEntry
        {
            BundleName = "editor_virtual.bundle",
            FileHash = string.Empty,
            FileCRC = 1,
            FileSize = 1
        });

        for (int i = 0; i < scan.Assets.Count; i++)
        {
            CollectedAssetInfo info = scan.Assets[i];
            if (info == null || string.IsNullOrEmpty(info.Address) || string.IsNullOrEmpty(info.AssetGUID))
                continue;

            var entry = new ManifestAssetEntry
            {
                EntryId = info.AssetGUID,
                Address = info.Address,
                PrimaryType = string.IsNullOrEmpty(info.PrimaryType) ? "Object" : info.PrimaryType,
                Labels = info.Labels != null ? new List<string>(info.Labels) : new List<string>(),
                SourcePath = info.AssetPath,
                Group = info.GroupName ?? string.Empty,
                AutoAddress = true,
                BundleIndex = 0,
                PayloadKind = info.Classification.PayloadKind
            };
            manifest.AssetEntries.Add(entry);
        }

        manifest.Initialize();
        Debug.Log(
            $"[EditorVirtualManifestBuilder] Editor 索引已构建。Assets={manifest.AssetEntries.Count}, " +
            $"Setting={settingPath}");
        return manifest;
    }
}
#endif
