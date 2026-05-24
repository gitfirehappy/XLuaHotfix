#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine;

/// <summary>
/// AA legacy hotfix group 迁移容器。负责把变更资源移入 Hotfix group，并用 undo log 精确还原。
/// TODO: 后续迁移为Task，构建热更包调用
/// </summary>
public static class LegacyAddressableHotfixGroups
{
    private const string UndoLogPath = "Assets/FYAsset/Editor/Generated/HotfixGroupUndoLog.json";

    /// <summary>是否存在尚未 Restore 的 group 迁移记录。</summary>
    public static bool HasPendingMoves => FileHelper.Exists(UndoLogPath) && LoadUndoLog().Entries.Count > 0;

    /// <summary>
    /// 将 Added + Modified 资源移入 Hotfix group。若 undo log 未清理，直接阻断，避免多轮迁移覆盖原始 group。
    /// </summary>
    public static bool Apply(ArtifactDelta delta)
    {
        if (delta == null || (delta.Added.Count == 0 && delta.Modified.Count == 0))
            return true;

        if (HasPendingMoves)
        {
            Debug.LogError("[LegacyAddressableHotfixGroups] Pending hotfix group moves exist. Run ResetGroupsToOriginal before preparing another hotfix.");
            return false;
        }

        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null)
        {
            Debug.LogError("[LegacyAddressableHotfixGroups] AddressableAssetSettings is null.");
            return false;
        }

        var hotfixGroup = GetOrCreateHotfixGroup(settings);
        var undoLog = new HotfixGroupUndoLog();

        MoveArtifacts(delta.Added, settings, hotfixGroup, undoLog);
        MoveArtifacts(delta.Modified, settings, hotfixGroup, undoLog);

        if (undoLog.Entries.Count == 0)
            return true;

        SaveUndoLog(undoLog);
        EditorUtility.SetDirty(settings);
        AssetDatabase.SaveAssets();
        Debug.Log($"[LegacyAddressableHotfixGroups] Moved {undoLog.Entries.Count} asset(s) into {FYAssetSettings.HOTFIX_GROUP_NAME}.");
        return true;
    }

    /// <summary>按 undo log 把资源移回原 group；原 group 缺失时回退到 DefaultGroup。</summary>
    public static void Restore()
    {
        if (!HasPendingMoves)
        {
            Debug.Log("[LegacyAddressableHotfixGroups] No pending hotfix group moves to restore.");
            return;
        }

        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null)
        {
            Debug.LogError("[LegacyAddressableHotfixGroups] AddressableAssetSettings is null.");
            return;
        }

        var undoLog = LoadUndoLog();
        int restored = 0;
        for (int i = 0; i < undoLog.Entries.Count; i++)
        {
            var item = undoLog.Entries[i];
            if (item == null || string.IsNullOrEmpty(item.Guid))
                continue;

            var entry = settings.FindAssetEntry(item.Guid);
            if (entry == null)
            {
                Debug.LogWarning($"[LegacyAddressableHotfixGroups] Asset entry not found while restoring: {item.Guid}");
                continue;
            }

            var targetGroup = !string.IsNullOrEmpty(item.OriginalGroupName)
                ? settings.FindGroup(item.OriginalGroupName)
                : null;

            if (targetGroup == null)
            {
                targetGroup = settings.DefaultGroup;
                Debug.LogWarning($"[LegacyAddressableHotfixGroups] Original group missing for {entry.address}. Restoring to default group.");
            }

            if (targetGroup == null)
            {
                Debug.LogWarning($"[LegacyAddressableHotfixGroups] No default group available for {entry.address}.");
                continue;
            }

            settings.MoveEntry(entry, targetGroup);
            restored++;
        }

        FileHelper.TryDelete(UndoLogPath);
        EditorUtility.SetDirty(settings);
        AssetDatabase.SaveAssets();
        Debug.Log($"[LegacyAddressableHotfixGroups] Restored {restored} asset(s) from hotfix group.");
    }

    private static void MoveArtifacts(List<ArtifactDigest> artifacts, AddressableAssetSettings settings, AddressableAssetGroup hotfixGroup, HotfixGroupUndoLog undoLog)
    {
        for (int i = 0; i < artifacts.Count; i++)
        {
            var artifact = artifacts[i];
            if (artifact == null || string.IsNullOrEmpty(artifact.Name))
                continue;

            var entry = settings.FindAssetEntry(artifact.Name);
            if (entry == null)
            {
                Debug.LogWarning($"[LegacyAddressableHotfixGroups] Addressable entry not found for guid: {artifact.Name}");
                continue;
            }

            var originalGroup = entry.parentGroup != null ? entry.parentGroup.Name : string.Empty;
            if (entry.parentGroup == hotfixGroup)
                continue;

            // 原始 group 必须在 MoveEntry 前记录，作为 Restore 的唯一权威来源。
            settings.MoveEntry(entry, hotfixGroup);
            undoLog.Entries.Add(new HotfixGroupUndoEntry
            {
                Guid = artifact.Name,
                OriginalGroupName = originalGroup
            });
        }
    }

    private static AddressableAssetGroup GetOrCreateHotfixGroup(AddressableAssetSettings settings)
    {
        var group = settings.FindGroup(FYAssetSettings.HOTFIX_GROUP_NAME);
        if (group != null)
            return group;

        group = settings.CreateGroup(FYAssetSettings.HOTFIX_GROUP_NAME, false, false, true, null);
        var schema = group.AddSchema<BundledAssetGroupSchema>();
        schema.BuildPath.SetVariableByName(settings, AddressableAssetSettings.kRemoteBuildPath);
        schema.LoadPath.SetVariableByName(settings, AddressableAssetSettings.kRemoteLoadPath);
        schema.BundleMode = BundledAssetGroupSchema.BundlePackingMode.PackTogetherByLabel;
        return group;
    }

    private static HotfixGroupUndoLog LoadUndoLog()
    {
        if (!FileHelper.Exists(UndoLogPath))
            return new HotfixGroupUndoLog();

        try
        {
            string json = FileHelper.ReadAllText(UndoLogPath);
            var log = JsonUtility.FromJson<HotfixGroupUndoLog>(json);
            return log ?? new HotfixGroupUndoLog();
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[LegacyAddressableHotfixGroups] Failed to read undo log: {ex.Message}");
            return new HotfixGroupUndoLog();
        }
    }

    private static void SaveUndoLog(HotfixGroupUndoLog undoLog)
    {
        string json = JsonUtility.ToJson(undoLog, true);
        FileHelper.WriteAllTextAtomic(UndoLogPath, json, Encoding.UTF8);
    }

    [Serializable]
    private class HotfixGroupUndoLog
    {
        public List<HotfixGroupUndoEntry> Entries = new();
    }

    [Serializable]
    private class HotfixGroupUndoEntry
    {
        public string Guid;
        public string OriginalGroupName;
    }
}
#endif
