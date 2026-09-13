#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

/// <summary>
/// 永久 FYAssetPipeline 测试夹具：资产、AA/AB 分组、地址与 Lua 映射。
/// </summary>
public static class BuildTestFixtures
{
    public static void EnsurePermanentFixtures()
    {
        EnsureFolder(BuildTestConstants.Folder);
        EnsureTextFile(BuildTestConstants.SyncAssetPath, BuildTestConstants.MarkerSyncV1 + "\n");
        EnsureTextFile(BuildTestConstants.RawAssetPath, BuildTestConstants.MarkerRawV1 + "\n");
        EnsureTextFile(BuildTestConstants.LuaModulePath,
            "-- Permanent FYAsset pipeline Lua smoke module.\nreturn {\n    marker = \"" +
            BuildTestConstants.MarkerLua + "\"\n}\n");
        EnsureSmokeAsset();
        EnsureLuaContainer();
        EnsureAAGroup();
        EnsureABGroup();
        EnsureLuaScriptsIndexMapping();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        AssertPreflight();
    }

    public static void AssertPreflight()
    {
        RequireAsset<FYAssetPipelineSmokeAsset>(BuildTestConstants.AsyncAssetPath);
        RequireAsset<TextAsset>(BuildTestConstants.SyncAssetPath);
        RequireAsset<TextAsset>(BuildTestConstants.LuaModulePath);
        RequireAsset<LuaScriptContainer>(BuildTestConstants.LuaContainerPath);

        var raw = AssetDatabase.LoadMainAssetAtPath(BuildTestConstants.RawAssetPath);
        if (raw == null)
            throw new InvalidOperationException("Raw fixture missing: " + BuildTestConstants.RawAssetPath);
        if (raw.GetType() != typeof(DefaultAsset))
            throw new InvalidOperationException(
                "FYAssetPipelineRaw.fyraw must remain DefaultAsset (no importer). Actual=" + raw.GetType().Name);

        AssertAAEntries();
        AssertABEntries();
        AssertLuaMapping();
    }

    public static string GetHotfixFixturePath(BuildTestBackend backend)
    {
        return backend == BuildTestBackend.AB
            ? BuildTestConstants.RawAssetPath
            : BuildTestConstants.SyncAssetPath;
    }

    public static string GetHotfixFixtureV1(BuildTestBackend backend)
    {
        return backend == BuildTestBackend.AB
            ? BuildTestConstants.MarkerRawV1
            : BuildTestConstants.MarkerSyncV1;
    }

    public static string GetHotfixFixtureV2(BuildTestBackend backend)
    {
        return backend == BuildTestBackend.AB
            ? BuildTestConstants.MarkerRawV2
            : BuildTestConstants.MarkerSyncV2;
    }

    public static void MutateHotfixFixture(BuildTestBackend backend)
    {
        string path = GetHotfixFixturePath(backend);
        string abs = ToAbsolute(path);
        string expected = GetHotfixFixtureV1(backend);
        string current = File.ReadAllText(abs, Encoding.UTF8).TrimEnd('\r', '\n');
        if (!string.Equals(current, expected, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"Fixture not at Full delivery state before mutation. Path={path}, Expected={expected}, Actual={current}");
        File.WriteAllText(abs, GetHotfixFixtureV2(backend) + "\n", new UTF8Encoding(false));
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
    }

    public static void RestoreHotfixFixture(BuildTestBackend backend)
    {
        string path = GetHotfixFixturePath(backend);
        File.WriteAllText(ToAbsolute(path), GetHotfixFixtureV1(backend) + "\n", new UTF8Encoding(false));
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
    }

    public static string ReadFixtureHash(string assetPath)
    {
        return HashGenerator.GenerateFileHash(ToAbsolute(assetPath));
    }

    private static void EnsureSmokeAsset()
    {
        var asset = AssetDatabase.LoadAssetAtPath<FYAssetPipelineSmokeAsset>(BuildTestConstants.AsyncAssetPath);
        if (asset == null)
        {
            asset = ScriptableObject.CreateInstance<FYAssetPipelineSmokeAsset>();
            asset.Marker = BuildTestConstants.MarkerAsync;
            AssetDatabase.CreateAsset(asset, BuildTestConstants.AsyncAssetPath);
        }
        else if (!string.Equals(asset.Marker, BuildTestConstants.MarkerAsync, StringComparison.Ordinal))
        {
            asset.Marker = BuildTestConstants.MarkerAsync;
            EditorUtility.SetDirty(asset);
        }
    }

    private static void EnsureLuaContainer()
    {
        var container = AssetDatabase.LoadAssetAtPath<LuaScriptContainer>(BuildTestConstants.LuaContainerPath);
        if (container == null)
        {
            container = ScriptableObject.CreateInstance<LuaScriptContainer>();
            AssetDatabase.CreateAsset(container, BuildTestConstants.LuaContainerPath);
        }

        var lua = AssetDatabase.LoadAssetAtPath<TextAsset>(BuildTestConstants.LuaModulePath);
        if (lua == null)
            throw new InvalidOperationException("Lua module missing: " + BuildTestConstants.LuaModulePath);

        container.luaAssets ??= new List<TextAsset>();
        if (container.luaAssets.Count != 1 || container.luaAssets[0] != lua)
        {
            container.luaAssets.Clear();
            container.luaAssets.Add(lua);
            EditorUtility.SetDirty(container);
        }
    }

    private static void EnsureAAGroup()
    {
        AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null)
            throw new InvalidOperationException("AddressableAssetSettings missing.");

        AddressableAssetGroup group = settings.FindGroup(BuildTestConstants.GroupName)
                                      ?? settings.CreateGroup(BuildTestConstants.GroupName, false, false, true, null);

        settings.AddLabel(BuildTestConstants.LabelGroup, false);
        settings.AddLabel(BuildTestConstants.LabelSmokeAsset, false);
        settings.AddLabel(BuildTestConstants.LabelTextAsset, false);
        settings.AddLabel(BuildTestConstants.LabelLuaContainer, false);

        EnsureAAEntry(settings, group, BuildTestConstants.AsyncAssetPath, BuildTestConstants.AddressAsync,
            BuildTestConstants.LabelSmokeAsset);
        EnsureAAEntry(settings, group, BuildTestConstants.SyncAssetPath, BuildTestConstants.AddressSync,
            BuildTestConstants.LabelTextAsset);
        EnsureAAEntry(settings, group, BuildTestConstants.LuaContainerPath, BuildTestConstants.AddressLua,
            BuildTestConstants.LabelLuaContainer);

        // Raw 仅 AB 使用，不得出现在 AA 条目中。
        string rawGuid = AssetDatabase.AssetPathToGUID(BuildTestConstants.RawAssetPath);
        AddressableAssetEntry rawEntry = settings.FindAssetEntry(rawGuid);
        if (rawEntry != null)
            settings.RemoveAssetEntry(rawGuid);

        EditorUtility.SetDirty(settings);
    }

    private static void EnsureAAEntry(
        AddressableAssetSettings settings,
        AddressableAssetGroup group,
        string path,
        string address,
        string firstLabel)
    {
        string guid = AssetDatabase.AssetPathToGUID(path);
        if (string.IsNullOrEmpty(guid))
            throw new InvalidOperationException("Missing GUID for " + path);

        AddressableAssetEntry entry = settings.CreateOrMoveEntry(guid, group, false, false);
        entry.address = address;
        // AA Type = 首个 label。保持 type label 在前、group label 在后（与生产 Lua 容器一致）。
        entry.labels.Clear();
        if (!string.IsNullOrEmpty(firstLabel))
        {
            settings.AddLabel(firstLabel, false);
            entry.SetLabel(firstLabel, true, true);
        }
        settings.AddLabel(BuildTestConstants.LabelGroup, false);
        entry.SetLabel(BuildTestConstants.LabelGroup, true, true);
    }

    private static void EnsureABGroup()
    {
        var setting = AssetDatabase.LoadAssetAtPath<AssetCollectionSetting>(
            FYAssetABSettings.Instance.AssetCollectionSettingPath);
        if (setting == null)
            throw new InvalidOperationException("AB AssetCollectionSetting missing.");

        setting.Groups ??= new List<AssetCollectionGroup>();

        AssetCollectionGroup group = null;
        for (int i = 0; i < setting.Groups.Count; i++)
        {
            if (setting.Groups[i] != null
                && string.Equals(setting.Groups[i].GroupName, BuildTestConstants.GroupName, StringComparison.Ordinal))
            {
                group = setting.Groups[i];
                break;
            }
        }

        if (group == null)
        {
            group = new AssetCollectionGroup
            {
                GroupName = BuildTestConstants.GroupName,
                Enabled = true,
                BundlePackingMode = BundlePackingMode.PackSeparately,
                Collectors = new List<Collector>()
            };
            setting.Groups.Add(group);
        }

        group.Enabled = true;
        group.BundlePackingMode = BundlePackingMode.PackSeparately;
        group.Collectors = new List<Collector>
        {
            new Collector
            {
                CollectPath = BuildTestConstants.Folder,
                CollectPathType = ECollectPathType.Folder
            }
        };

        EnsureABAddress(setting, BuildTestConstants.AsyncAssetPath, BuildTestConstants.AddressAsync,
            BuildTestConstants.LabelSmokeAsset);
        EnsureABAddress(setting, BuildTestConstants.SyncAssetPath, BuildTestConstants.AddressSync,
            BuildTestConstants.LabelTextAsset);
        EnsureABAddress(setting, BuildTestConstants.LuaContainerPath, BuildTestConstants.AddressLua,
            BuildTestConstants.LabelLuaContainer);
        EnsureABAddress(setting, BuildTestConstants.RawAssetPath, BuildTestConstants.AddressRaw, null);

        EditorUtility.SetDirty(setting);
    }

    private static void EnsureABAddress(
        AssetCollectionSetting setting,
        string assetPath,
        string address,
        string firstLabel)
    {
        string guid = AssetDatabase.AssetPathToGUID(assetPath);
        if (string.IsNullOrEmpty(guid))
            throw new InvalidOperationException("Missing GUID for " + assetPath);

        AssetAddressEntry entry = setting.FindAssetAddressEntry(guid);
        if (entry == null)
        {
            entry = new AssetAddressEntry { AssetGUID = guid };
            setting.AssetAddressEntries ??= new List<AssetAddressEntry>();
            setting.AssetAddressEntries.Add(entry);
        }

        entry.Address = address;
        entry.Labels ??= new List<string>();
        // 保持 type/first label 在共享 group label 之前。
        entry.Labels.Remove(BuildTestConstants.LabelGroup);
        if (!string.IsNullOrEmpty(firstLabel))
        {
            entry.Labels.Remove(firstLabel);
            entry.Labels.Insert(0, firstLabel);
        }
        if (!entry.Labels.Contains(BuildTestConstants.LabelGroup))
            entry.Labels.Add(BuildTestConstants.LabelGroup);
    }

    private static void EnsureLuaScriptsIndexMapping()
    {
        var index = AssetDatabase.LoadAssetAtPath<LuaScriptsIndex>(LuaScriptsIndex.EditorAssetPath);
        if (index == null)
            throw new InvalidOperationException("LuaScriptsIndex missing at " + LuaScriptsIndex.EditorAssetPath);

        index.data ??= new List<LuaScriptsIndex.ContainerEntry>();
        LuaScriptsIndex.ContainerEntry found = null;
        for (int i = 0; i < index.data.Count; i++)
        {
            if (index.data[i] != null
                && string.Equals(index.data[i].containerAddress, BuildTestConstants.AddressLua, StringComparison.Ordinal))
            {
                found = index.data[i];
                break;
            }
        }

        if (found == null)
        {
            found = new LuaScriptsIndex.ContainerEntry
            {
                containerAddress = BuildTestConstants.AddressLua,
                scriptNames = new List<string>()
            };
            index.data.Add(found);
        }

        found.scriptNames ??= new List<string>();
        if (!found.scriptNames.Contains(BuildTestConstants.LuaModuleName))
            found.scriptNames.Add(BuildTestConstants.LuaModuleName);

        // 保证 smoke 模块的反向映射唯一。
        for (int i = 0; i < index.data.Count; i++)
        {
            var entry = index.data[i];
            if (entry == null || entry.scriptNames == null)
                continue;
            if (string.Equals(entry.containerAddress, BuildTestConstants.AddressLua, StringComparison.Ordinal))
                continue;
            entry.scriptNames.RemoveAll(n =>
                string.Equals(n, BuildTestConstants.LuaModuleName, StringComparison.Ordinal));
        }

        EditorUtility.SetDirty(index);
    }

    private static void AssertAAEntries()
    {
        AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
        AssertAAEntry(settings, BuildTestConstants.AsyncAssetPath, BuildTestConstants.AddressAsync);
        AssertAAEntry(settings, BuildTestConstants.SyncAssetPath, BuildTestConstants.AddressSync);
        AssertAAEntry(settings, BuildTestConstants.LuaContainerPath, BuildTestConstants.AddressLua);
    }

    private static void AssertAAEntry(AddressableAssetSettings settings, string path, string address)
    {
        string guid = AssetDatabase.AssetPathToGUID(path);
        AddressableAssetEntry entry = settings.FindAssetEntry(guid);
        if (entry == null)
            throw new InvalidOperationException("AA entry missing: " + path);
        if (!string.Equals(entry.address, address, StringComparison.Ordinal))
            throw new InvalidOperationException($"AA address mismatch for {path}: {entry.address} != {address}");
        if (entry.parentGroup == null
            || !string.Equals(entry.parentGroup.Name, BuildTestConstants.GroupName, StringComparison.Ordinal))
            throw new InvalidOperationException("AA entry not in permanent group: " + path);
    }

    private static void AssertABEntries()
    {
        var setting = AssetDatabase.LoadAssetAtPath<AssetCollectionSetting>(
            FYAssetABSettings.Instance.AssetCollectionSettingPath);
        AssertABAddress(setting, BuildTestConstants.AsyncAssetPath, BuildTestConstants.AddressAsync);
        AssertABAddress(setting, BuildTestConstants.SyncAssetPath, BuildTestConstants.AddressSync);
        AssertABAddress(setting, BuildTestConstants.LuaContainerPath, BuildTestConstants.AddressLua);
        AssertABAddress(setting, BuildTestConstants.RawAssetPath, BuildTestConstants.AddressRaw);

        bool groupOk = false;
        for (int g = 0; setting.Groups != null && g < setting.Groups.Count; g++)
        {
            if (setting.Groups[g] != null
                && string.Equals(setting.Groups[g].GroupName, BuildTestConstants.GroupName, StringComparison.Ordinal)
                && setting.Groups[g].Enabled
                && setting.Groups[g].BundlePackingMode == BundlePackingMode.PackSeparately)
            {
                groupOk = true;
            }
        }

        if (!groupOk)
            throw new InvalidOperationException("AB permanent group missing or misconfigured.");
    }

    private static void AssertABAddress(AssetCollectionSetting setting, string path, string address)
    {
        string guid = AssetDatabase.AssetPathToGUID(path);
        AssetAddressEntry entry = setting.FindAssetAddressEntry(guid);
        if (entry == null || !string.Equals(entry.Address, address, StringComparison.Ordinal))
            throw new InvalidOperationException($"AB fixed address missing for {path} -> {address}");
    }

    private static void AssertLuaMapping()
    {
        var index = AssetDatabase.LoadAssetAtPath<LuaScriptsIndex>(LuaScriptsIndex.EditorAssetPath);
        index.BuildRuntimeDics();
        if (!index.ScriptToContainer.TryGetValue(BuildTestConstants.LuaModuleName, out string container)
            || !string.Equals(container, BuildTestConstants.AddressLua, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "LuaScriptsIndex mapping missing: " + BuildTestConstants.LuaModuleName + " -> " +
                BuildTestConstants.AddressLua);
        }
    }

    private static void EnsureFolder(string assetFolder)
    {
        if (AssetDatabase.IsValidFolder(assetFolder))
            return;
        string[] parts = assetFolder.Split('/');
        string current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }
    }

    private static void EnsureTextFile(string assetPath, string content)
    {
        string abs = ToAbsolute(assetPath);
        FileHelper.EnsureDirectory(Path.GetDirectoryName(abs));
        if (!File.Exists(abs))
            File.WriteAllText(abs, content, new UTF8Encoding(false));
    }

    private static T RequireAsset<T>(string path) where T : UnityEngine.Object
    {
        T asset = AssetDatabase.LoadAssetAtPath<T>(path);
        if (asset == null)
            throw new InvalidOperationException($"Required asset missing: {path} ({typeof(T).Name})");
        return asset;
    }

    private static string ToAbsolute(string assetPath)
    {
        return FYAssetPathUtility.ResolveFilePath(BuildPathManager.ProjectRoot, assetPath);
    }
}
#endif
