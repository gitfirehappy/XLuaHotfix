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
public class TaskMoveAAHotfixGroups : IBuildTask
{
    private const string UndoLogPath = "Assets/FYAsset/Editor/Generated/HotfixGroupUndoLog.json";
    private static string _undoLogPathOverrideForSelfCheck;

    public string TaskName => "TaskMoveAAHotfixGroups";
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
            status.ErrorMessage = "AddressableAssetSettings 不可用。";
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
            return BuildTaskResult.Ok(new List<string> { "[AA HOTFIX GROUP] 已跳过 Full build" });

        var delta = ctx.Require<ArtifactDelta>(BuildContextKeys.ArtifactDelta);
        if (delta == null || delta.IsEmpty || (delta.Added.Count == 0 && delta.Modified.Count == 0))
            return BuildTaskResult.Ok(new List<string> { "[AA HOTFIX GROUP] 没有需要移动的已变更 asset" });

        return Apply(delta)
            ? BuildTaskResult.Ok(new List<string> { $"[AA HOTFIX GROUP] Moved={delta.Added.Count + delta.Modified.Count}" })
            : BuildTaskResult.Fail(BuildErrorCodes.BuildFailed, "AA Hotfix Group 移动失败。", true);
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
            Debug.LogError("[TaskMoveAAHotfixGroups] 存在待处理的 Hotfix Group 移动记录。准备下一次 Hotfix 前请运行 ResetGroupsToOriginal。");
            return false;
        }

        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null)
        {
            Debug.LogError("[TaskMoveAAHotfixGroups] AddressableAssetSettings 为 null。");
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
        Debug.Log($"[TaskMoveAAHotfixGroups] 已将 {undoLog.Entries.Count} 个 asset 移入 {FYAssetSettings.HOTFIX_GROUP_NAME}。");
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
            Debug.Log("[TaskMoveAAHotfixGroups] 没有待恢复的 Hotfix Group 移动记录。");
            result.Message = "没有待恢复的 Hotfix Group 移动记录。";
            return result;
        }

        AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null)
        {
            Debug.LogError("[TaskMoveAAHotfixGroups] AddressableAssetSettings 为 null。");
            result.RemainingCount = undoLog.Entries.Count;
            result.Message = "AddressableAssetSettings 不可用。";
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
                Debug.LogWarning($"[TaskMoveAAHotfixGroups] 恢复已暂缓：{reason}");
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
            Debug.LogError($"[TaskMoveAAHotfixGroups] {persistenceError}");
            return result;
        }

        result.RemainingCount = remainingEntries.Count;
        result.Message = $"已恢复 {result.RestoredCount} 个 asset；剩余 {result.RemainingCount} 条记录。";
        Debug.Log($"[TaskMoveAAHotfixGroups] {result.Message}");
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
            result.Message = "没有待丢弃的 Hotfix Group 记录。";
            return result;
        }

        AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null)
        {
            result.RemainingCount = undoLog.Entries.Count;
            result.Message = "AddressableAssetSettings 不可用；记录未丢弃。";
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
            Debug.LogError($"[TaskMoveAAHotfixGroups] {persistenceError}");
            return result;
        }

        result.DiscardedCount = discardedCount;
        result.RemainingCount = remainingEntries.Count;
        result.Message = $"已丢弃 {discardedCount} 条无法恢复的记录；剩余 {result.RemainingCount} 条记录。";
        Debug.Log($"[TaskMoveAAHotfixGroups] {result.Message}");
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
                Debug.LogWarning($"[TaskMoveAAHotfixGroups] 未找到 guid 对应的 Addressable entry：{artifact.Name}");
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
            reason = "undo 记录缺少 GUID。";
            return false;
        }

        entry = settings.FindAssetEntry(item.Guid);
        if (entry == null)
        {
            reason = $"未找到 Addressable entry：{item.Guid}";
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

        reason = $"{entry.address} 的 original group 和 DefaultGroup 均不可用。";
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
            Debug.LogWarning($"[TaskMoveAAHotfixGroups] 读取 undo log 失败：{ex.Message}");
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
                    error = "删除已完成的 Hotfix Group undo log 失败。";
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
            error = $"持久化 Hotfix Group undo log 失败：{ex.Message}";
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

    public static void RunSelfCheck()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            nameof(TaskMoveAAHotfixGroups) + "_" + Guid.NewGuid().ToString("N"));
        try
        {
            RunUndoLogPersistenceSelfCheck(root);
            Debug.Log("[TaskMoveAAHotfixGroups] PASS - undo log 持久化验证通过。");
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

/// <summary>Editor 工具使用的 AA Hotfix Group 恢复状态。</summary>
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

/// <summary>恢复或丢弃 AA Hotfix Group undo 记录的结果。</summary>
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
