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
            PackageVersion = new VersionNumber { Major = 0, Minor = 0, Patch = 0, Build = 0 },
            AssetEntries = new List<ManifestAssetEntry>(),
            ContentEntries = new List<ManifestContentEntry>()
        };

        // Editor 预览不产出物理文件，用一个占位内容承载全部条目，文件事实留空
        manifest.ContentEntries.Add(new ManifestContentEntry
        {
            FileName = "editor_virtual.bundle",
            FileHash = string.Empty,
            FileCRC = 0,
            FileSize = 0,
            ContentType = AssetContentType.SerializedObject,
            DependencyIndices = new int[0]
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
                IsPublic = true,
                ContentType = info.ContentType,
                ContentIndex = 0
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
