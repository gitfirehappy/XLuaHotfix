using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine;

/// <summary>
/// AA hotfix group move Task。把 diff 中 Added/Modified 资源移入 Hotfix group，并保留手动 Restore 能力。
/// </summary>
public class TaskMoveAddressableHotfixGroups : IBuildTask
{
    private const string UndoLogPath = "Assets/FYAsset/Editor/Generated/HotfixGroupUndoLog.json";
    private static string _undoLogPathOverrideForSelfCheck;

    public string TaskName => "TaskMoveAddressableHotfixGroups";
    /// <summary>是否存在尚未 Restore 的 group 迁移记录。</summary>
    public static bool HasPendingMoves => LoadUndoLog().Entries.Count > 0;

    /// <summary>读取当前 AA 热更分组还原状态，供 Editor UI 展示。</summary>
    public static HotfixGroupRestoreStatus GetRestoreStatus()
    {
        HotfixGroupUndoLog undoLog = LoadUndoLog();
        var status = new HotfixGroupRestoreStatus
        {
            PendingCount = undoLog.Entries.Count
        };

        if (status.PendingCount == 0)
            return status;

        AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null)
        {
            status.SettingsAvailable = false;
            status.UnrestorableCount = status.PendingCount;
            status.ErrorMessage = "AddressableAssetSettings is unavailable.";
            return status;
        }

        status.SettingsAvailable = true;
        for (int i = 0; i < undoLog.Entries.Count; i++)
        {
            if (TryResolveRestoreTarget(settings, undoLog.Entries[i], out _, out _, out bool fallsBackToDefault, out _))
            {
                status.RestorableCount++;
                if (fallsBackToDefault)
                    status.DefaultGroupFallbackCount++;
            }
            else
            {
                status.UnrestorableCount++;
            }
        }

        return status;
    }

    public BuildTaskResult Execute(BuildContext ctx)
    {
        var buildType = ctx.Require<BuildType>(BuildContextKeys.BuildType);
        if (buildType != BuildType.Hotfix)
            return BuildTaskResult.Ok(new List<string> { "[AA HOTFIX GROUP] Full build skipped" });

        var delta = ctx.Require<ArtifactDelta>(BuildContextKeys.ArtifactDelta);
        if (delta == null || delta.IsEmpty || (delta.Added.Count == 0 && delta.Modified.Count == 0))
            return BuildTaskResult.Ok(new List<string> { "[AA HOTFIX GROUP] No changed assets to move" });

        return Apply(delta)
            ? BuildTaskResult.Ok(new List<string> { $"[AA HOTFIX GROUP] Moved={delta.Added.Count + delta.Modified.Count}" })
            : BuildTaskResult.Fail(BuildErrorCodes.BuildFailed, "AA hotfix group move failed.", true);
    }

    /// <summary>
    /// 将 Added + Modified 资源移入 Hotfix group。若 undo log 未清理，直接阻断，避免多轮迁移覆盖原始 group。
    /// </summary>
    private static bool Apply(ArtifactDelta delta)
    {
        if (delta == null || (delta.Added.Count == 0 && delta.Modified.Count == 0))
            return true;

        if (HasPendingMoves)
        {
            Debug.LogError("[TaskMoveAddressableHotfixGroups] Pending hotfix group moves exist. Run ResetGroupsToOriginal before preparing another hotfix.");
            return false;
        }

        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null)
        {
            Debug.LogError("[TaskMoveAddressableHotfixGroups] AddressableAssetSettings is null.");
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
        Debug.Log($"[TaskMoveAddressableHotfixGroups] Moved {undoLog.Entries.Count} asset(s) into {FYAssetSettings.HOTFIX_GROUP_NAME}.");
        return true;
    }

    /// <summary>按 undo log 把资源移回原 group；原 group 缺失时回退到 DefaultGroup。</summary>
    public static HotfixGroupRestoreResult Restore()
    {
        HotfixGroupUndoLog undoLog = LoadUndoLog();
        var result = new HotfixGroupRestoreResult
        {
            InitialPendingCount = undoLog.Entries.Count
        };

        if (undoLog.Entries.Count == 0)
        {
            Debug.Log("[TaskMoveAddressableHotfixGroups] No pending hotfix group moves to restore.");
            result.Message = "No pending hotfix group moves to restore.";
            return result;
        }

        AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null)
        {
            Debug.LogError("[TaskMoveAddressableHotfixGroups] AddressableAssetSettings is null.");
            result.RemainingCount = undoLog.Entries.Count;
            result.Message = "AddressableAssetSettings is unavailable.";
            return result;
        }

        var remainingEntries = new List<HotfixGroupUndoEntry>();
        for (int i = 0; i < undoLog.Entries.Count; i++)
        {
            HotfixGroupUndoEntry item = undoLog.Entries[i];
            if (!TryResolveRestoreTarget(settings, item, out AddressableAssetEntry entry, out AddressableAssetGroup targetGroup,
                    out bool fallsBackToDefault, out string reason))
            {
                remainingEntries.Add(item);
                result.UnrestorableCount++;
                Debug.LogWarning($"[TaskMoveAddressableHotfixGroups] Restore deferred: {reason}");
                continue;
            }

            settings.MoveEntry(entry, targetGroup);
            result.RestoredCount++;
            if (fallsBackToDefault)
                result.DefaultGroupFallbackCount++;
        }

        if (result.RestoredCount > 0)
        {
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
        }

        if (!PersistUndoLog(remainingEntries, out string persistenceError))
        {
            result.RemainingCount = undoLog.Entries.Count;
            result.Message = persistenceError;
            Debug.LogError($"[TaskMoveAddressableHotfixGroups] {persistenceError}");
            return result;
        }

        result.RemainingCount = remainingEntries.Count;
        result.Message = $"Restored {result.RestoredCount} asset(s); {result.RemainingCount} record(s) remain.";
        Debug.Log($"[TaskMoveAddressableHotfixGroups] {result.Message}");
        return result;
    }

    /// <summary>仅移除当前无法恢复的撤销记录，不修改任何 Addressables entry。</summary>
    public static HotfixGroupRestoreResult DiscardUnrestorableRecords()
    {
        HotfixGroupUndoLog undoLog = LoadUndoLog();
        var result = new HotfixGroupRestoreResult
        {
            InitialPendingCount = undoLog.Entries.Count
        };

        if (undoLog.Entries.Count == 0)
        {
            result.Message = "No pending hotfix group records to discard.";
            return result;
        }

        AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null)
        {
            result.RemainingCount = undoLog.Entries.Count;
            result.Message = "AddressableAssetSettings is unavailable; records were not discarded.";
            return result;
        }

        List<HotfixGroupUndoEntry> remainingEntries = FilterEntriesForDiscard(
            undoLog.Entries,
            item => !TryResolveRestoreTarget(settings, item, out _, out _, out _, out _),
            out int discardedCount);

        if (!PersistUndoLog(remainingEntries, out string persistenceError))
        {
            result.RemainingCount = undoLog.Entries.Count;
            result.Message = persistenceError;
            Debug.LogError($"[TaskMoveAddressableHotfixGroups] {persistenceError}");
            return result;
        }

        result.DiscardedCount = discardedCount;
        result.RemainingCount = remainingEntries.Count;
        result.Message = $"Discarded {discardedCount} unrestorable record(s); {result.RemainingCount} record(s) remain.";
        Debug.Log($"[TaskMoveAddressableHotfixGroups] {result.Message}");
        return result;
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
                Debug.LogWarning($"[TaskMoveAddressableHotfixGroups] Addressable entry not found for guid: {artifact.Name}");
                continue;
            }

            var originalGroup = entry.parentGroup != null ? entry.parentGroup.Name : string.Empty;
            if (entry.parentGroup == hotfixGroup)
                continue;

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

    private static bool TryResolveRestoreTarget(
        AddressableAssetSettings settings,
        HotfixGroupUndoEntry item,
        out AddressableAssetEntry entry,
        out AddressableAssetGroup targetGroup,
        out bool fallsBackToDefault,
        out string reason)
    {
        entry = null;
        targetGroup = null;
        fallsBackToDefault = false;
        reason = string.Empty;

        if (item == null || string.IsNullOrEmpty(item.Guid))
        {
            reason = "Undo record has no GUID.";
            return false;
        }

        entry = settings.FindAssetEntry(item.Guid);
        if (entry == null)
        {
            reason = $"Addressable entry not found: {item.Guid}";
            return false;
        }

        targetGroup = !string.IsNullOrEmpty(item.OriginalGroupName)
            ? settings.FindGroup(item.OriginalGroupName)
            : null;
        if (targetGroup != null)
            return true;

        targetGroup = settings.DefaultGroup;
        fallsBackToDefault = true;
        if (targetGroup != null)
            return true;

        reason = $"Original group and DefaultGroup are unavailable for {entry.address}.";
        return false;
    }

    private static List<HotfixGroupUndoEntry> FilterEntriesForDiscard(
        List<HotfixGroupUndoEntry> entries,
        Func<HotfixGroupUndoEntry, bool> shouldDiscard,
        out int discardedCount)
    {
        var remainingEntries = new List<HotfixGroupUndoEntry>();
        discardedCount = 0;
        if (entries == null)
            return remainingEntries;

        for (int i = 0; i < entries.Count; i++)
        {
            HotfixGroupUndoEntry item = entries[i];
            if (shouldDiscard != null && shouldDiscard(item))
            {
                discardedCount++;
                continue;
            }

            remainingEntries.Add(item);
        }

        return remainingEntries;
    }

    private static HotfixGroupUndoLog LoadUndoLog()
    {
        string undoLogPath = GetUndoLogPath();
        if (!FileHelper.Exists(undoLogPath))
            return new HotfixGroupUndoLog();

        try
        {
            string json = FileHelper.ReadAllText(undoLogPath);
            var log = JsonUtility.FromJson<HotfixGroupUndoLog>(json);
            return log ?? new HotfixGroupUndoLog();
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[TaskMoveAddressableHotfixGroups] Failed to read undo log: {ex.Message}");
            return new HotfixGroupUndoLog();
        }
    }

    private static void SaveUndoLog(HotfixGroupUndoLog undoLog)
    {
        string json = JsonUtility.ToJson(undoLog, true);
        FileHelper.WriteAllTextAtomic(GetUndoLogPath(), json, Encoding.UTF8);
    }

    private static bool PersistUndoLog(List<HotfixGroupUndoEntry> remainingEntries, out string error)
    {
        error = string.Empty;
        try
        {
            if (remainingEntries == null || remainingEntries.Count == 0)
            {
                if (!FileHelper.TryDelete(GetUndoLogPath()))
                {
                    error = "Failed to delete the completed hotfix group undo log.";
                    return false;
                }

                return true;
            }

            SaveUndoLog(new HotfixGroupUndoLog
            {
                Entries = remainingEntries
            });
            return true;
        }
        catch (Exception ex)
        {
            error = $"Failed to persist hotfix group undo log: {ex.Message}";
            return false;
        }
    }

    private static string GetUndoLogPath()
    {
        return string.IsNullOrEmpty(_undoLogPathOverrideForSelfCheck)
            ? UndoLogPath
            : _undoLogPathOverrideForSelfCheck;
    }

    internal static void RunUndoLogPersistenceSelfCheck(string root)
    {
        string previousOverride = _undoLogPathOverrideForSelfCheck;
        _undoLogPathOverrideForSelfCheck = Path.Combine(root, "HotfixGroupUndoLog.json");
        try
        {
            FileHelper.EnsureDirectory(root);
            SaveUndoLog(CreateUndoLog("restorable", "unrestorable"));
            Require(PersistUndoLog(new List<HotfixGroupUndoEntry>(), out string allRestoredError), allRestoredError);
            Require(!FileHelper.Exists(GetUndoLogPath()), "Completed restore did not delete the undo log.");

            SaveUndoLog(CreateUndoLog("restorable", "unrestorable"));
            var partialRemaining = new List<HotfixGroupUndoEntry>
            {
                new HotfixGroupUndoEntry { Guid = "unrestorable", OriginalGroupName = "Original" }
            };
            Require(PersistUndoLog(partialRemaining, out string partialError), partialError);
            Require(LoadUndoLog().Entries.Count == 1 && LoadUndoLog().Entries[0].Guid == "unrestorable",
                "Partial restore did not preserve only the unresolved record.");

            SaveUndoLog(CreateUndoLog("restorable", "unrestorable"));
            List<HotfixGroupUndoEntry> afterDiscard = FilterEntriesForDiscard(
                LoadUndoLog().Entries,
                item => string.Equals(item?.Guid, "unrestorable", StringComparison.Ordinal),
                out int discardedCount);
            Require(discardedCount == 1, "Discard did not select the unresolved record.");
            Require(PersistUndoLog(afterDiscard, out string discardError), discardError);
            Require(LoadUndoLog().Entries.Count == 1 && LoadUndoLog().Entries[0].Guid == "restorable",
                "Discard removed a restorable record or retained the unresolved record.");
        }
        finally
        {
            _undoLogPathOverrideForSelfCheck = previousOverride;
        }
    }

    [MenuItem("FYAsset/Tests/AA Hotfix Group Restore Self Check")]
    public static void RunSelfCheck()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            nameof(TaskMoveAddressableHotfixGroups) + "_" + Guid.NewGuid().ToString("N"));
        try
        {
            RunUndoLogPersistenceSelfCheck(root);
            Debug.Log("[TaskMoveAddressableHotfixGroups] PASS - undo-log persistence verified.");
        }
        finally
        {
            FileHelper.TryDeleteDirectory(root, true);
        }
    }

    private static HotfixGroupUndoLog CreateUndoLog(params string[] guids)
    {
        var log = new HotfixGroupUndoLog();
        for (int i = 0; i < guids.Length; i++)
        {
            log.Entries.Add(new HotfixGroupUndoEntry
            {
                Guid = guids[i],
                OriginalGroupName = "Original"
            });
        }

        return log;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
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

/// <summary>AA Hotfix group restore state used by editor tooling.</summary>
public sealed class HotfixGroupRestoreStatus
{
    public int PendingCount { get; internal set; }
    public int RestorableCount { get; internal set; }
    public int DefaultGroupFallbackCount { get; internal set; }
    public int UnrestorableCount { get; internal set; }
    public bool SettingsAvailable { get; internal set; } = true;
    public string ErrorMessage { get; internal set; }

    public bool CanDiscardUnrestorableRecords => SettingsAvailable && UnrestorableCount > 0;
}

/// <summary>Result of restoring or discarding AA Hotfix group undo records.</summary>
public sealed class HotfixGroupRestoreResult
{
    public int InitialPendingCount { get; internal set; }
    public int RestoredCount { get; internal set; }
    public int DefaultGroupFallbackCount { get; internal set; }
    public int UnrestorableCount { get; internal set; }
    public int DiscardedCount { get; internal set; }
    public int RemainingCount { get; internal set; }
    public bool Cancelled { get; internal set; }
    public string Message { get; internal set; }
}
