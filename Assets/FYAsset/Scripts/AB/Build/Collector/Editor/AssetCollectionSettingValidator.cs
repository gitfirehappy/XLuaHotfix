using System;
using System.Collections.Generic;
using UnityEditor;

/// <summary>
/// AssetCollectionSetting 保存时校验器。
/// </summary>
public static class AssetCollectionSettingValidator
{
    public static List<BuildMessage> Validate(AssetCollectionSetting setting)
    {
        var messages = new List<BuildMessage>();

        if (setting == null)
        {
            messages.Add(BuildMessage.SettingNull("Setting"));
            return messages;
        }

        if (setting.Groups == null || setting.Groups.Count == 0)
        {
            messages.Add(BuildMessage.NoGroups("Setting"));
            return messages;
        }

        ValidateGroups(setting, messages);
        ValidateAssetAddressEntries(setting, messages);
        ValidateStringList(setting.IgnorePatterns, "Setting.IgnorePatterns", messages);
        ValidateRawFileRules(setting.RawFileRules, messages);
        ValidateSharePolicy(setting.SharePolicy, messages);
        return messages;
    }

    private static void ValidateGroups(AssetCollectionSetting setting, List<BuildMessage> messages)
    {
        HashSet<string> seenGroupNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        List<(string path, string src)> collectPaths = new List<(string, string)>();

        for (int gi = 0; gi < setting.Groups.Count; gi++)
        {
            AssetCollectionGroup group = setting.Groups[gi];
            if (group == null)
                continue;

            string groupSrc = string.Concat("Group[", gi, "]");
            if (string.IsNullOrEmpty(group.GroupName))
            {
                messages.Add(BuildMessage.EmptyGroupName(groupSrc));
            }
            else
            {
                string segmentError = BundleNameBuilder.ValidateSegment(group.GroupName);
                if (segmentError != null)
                    messages.Add(BuildMessage.InvalidBundleNameSegment(segmentError, groupSrc));

                if (!seenGroupNames.Add(group.GroupName))
                    messages.Add(BuildMessage.DuplicateGroupName(group.GroupName, groupSrc));
            }

            if (group.Collectors == null)
                continue;

            for (int ci = 0; ci < group.Collectors.Count; ci++)
            {
                Collector collector = group.Collectors[ci];
                if (collector == null)
                    continue;

                string collectorSrc = string.Concat(groupSrc, ".Collector[", ci, "]");
                if (string.IsNullOrEmpty(collector.CollectPath))
                {
                    messages.Add(BuildMessage.EmptyCollectPath(collectorSrc));
                    continue;
                }

                collectPaths.Add((CollectorPathUtility.NormalizePath(collector.CollectPath), collectorSrc));
                if (!CollectPathExists(collector))
                    messages.Add(BuildMessage.PathNotFound(collector.CollectPath, collectorSrc));
            }
        }

        CheckSamePathConflicts(collectPaths, messages);
    }

    private static void ValidateAssetAddressEntries(AssetCollectionSetting setting, List<BuildMessage> messages)
    {
        if (setting.AssetAddressEntries == null)
            return;

        HashSet<string> seenGuids = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < setting.AssetAddressEntries.Count; i++)
        {
            AssetAddressEntry entry = setting.AssetAddressEntries[i];
            if (entry == null)
                continue;

            string src = string.Concat("AssetAddressEntries[", i, "]");
            if (string.IsNullOrEmpty(entry.AssetGUID)
                || string.IsNullOrEmpty(AssetDatabase.GUIDToAssetPath(entry.AssetGUID)))
            {
                messages.Add(BuildMessage.InvalidAssetAddressEntryGuid(entry.AssetGUID, src));
                continue;
            }

            if (!seenGuids.Add(entry.AssetGUID))
                messages.Add(BuildMessage.DuplicateGuid(entry.AssetGUID, src));

            ValidateLabels(entry.Labels, src, messages);
        }
    }

    private static void ValidateRawFileRules(RawFileRules rules, List<BuildMessage> messages)
    {
        if (rules != null)
            ValidateStringList(rules.Patterns, "Setting.RawFileRules.Patterns", messages);
    }

    private static void ValidateSharePolicy(SharePolicyConfig policy, List<BuildMessage> messages)
    {
        if (policy == null)
            return;

        ValidateStringList(policy.ForceSharePatterns, "Setting.SharePolicy.ForceSharePatterns", messages);
        ValidateStringList(policy.NoSharePatterns, "Setting.SharePolicy.NoSharePatterns", messages);
    }

    private static void ValidateStringList(List<string> values, string fieldPath, List<BuildMessage> messages)
    {
        if (values == null)
            return;

        for (int i = 0; i < values.Count; i++)
        {
            string value = values[i];
            if (string.IsNullOrEmpty(value) || !string.Equals(value, value.Trim(), StringComparison.Ordinal))
                messages.Add(BuildMessage.BlankConfigEntry(fieldPath, value, string.Concat(fieldPath, "[", i, "]")));
        }
    }

    private static void ValidateLabels(List<string> labels, string source, List<BuildMessage> messages)
    {
        if (labels == null)
            return;

        if (!AssetLabelValidator.TryValidate(labels, out string invalidLabel, out string duplicateLabel))
        {
            if (duplicateLabel != null)
                messages.Add(BuildMessage.Error(BuildErrorCodes.DuplicateLabel,
                    $"Labels 包含大小写不敏感重复值: '{duplicateLabel}'。", source));
            else
                messages.Add(BuildMessage.BlankConfigEntry("Labels", invalidLabel, source));
            return;
        }

        for (int i = 0; i < labels.Count; i++)
        {
            string error = BundleNameBuilder.ValidateSegment(labels[i]);
            if (error != null)
                messages.Add(BuildMessage.InvalidLabel(error, source));
        }
    }

    private static void CheckSamePathConflicts(List<(string path, string src)> paths, List<BuildMessage> messages)
    {
        for (int i = 0; i < paths.Count; i++)
        {
            for (int j = i + 1; j < paths.Count; j++)
            {
                if (string.Equals(paths[i].path, paths[j].path, StringComparison.OrdinalIgnoreCase))
                    messages.Add(BuildMessage.SamePathConflict(paths[i].path, paths[i].src));
            }
        }
    }

    private static bool CollectPathExists(Collector collector)
    {
        if (collector == null || string.IsNullOrEmpty(collector.CollectPath))
            return false;

        if (collector.CollectPathType == ECollectPathType.File)
        {
            return !AssetDatabase.IsValidFolder(collector.CollectPath) &&
                   !string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(collector.CollectPath));
        }

        return AssetDatabase.IsValidFolder(collector.CollectPath);
    }
}
