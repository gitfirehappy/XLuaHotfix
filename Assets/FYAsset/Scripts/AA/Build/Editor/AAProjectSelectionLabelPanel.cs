using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets;
using UnityEngine.UIElements;

/// <summary>
/// AA Project Selection 标签面板。只修改已经发布的 Addressables entries。
/// </summary>
public sealed class AAProjectSelectionLabelPanel : BuildPipelineUIToolkitPanel
{
    private ProjectSelectionLabelPanelView _view;

    public override string PanelName => "Project Labels";

    protected override void BuildContent(VisualElement root)
    {
        _view = new ProjectSelectionLabelPanelView(
            "AA Project Labels",
            "Replaces labels only on existing non-folder Addressables entries. Missing or folder entries reject the entire batch. The current first label is protected because AA still derives Type from it.",
            DescribeSelection,
            ApplyLabels);
        root.Add(_view.CreateContent());
    }

    public override void SetVisible(bool visible)
    {
        _view?.SetVisible(visible);
    }

    public override void OnDisable()
    {
        _view?.Dispose();
        _view = null;
        base.OnDisable();
    }

    private static ProjectSelectionLabelPanelView.SelectionRow DescribeSelection(string guid)
    {
        AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
        AddressableAssetEntry entry = settings?.FindAssetEntry(guid);
        string assetPath = AssetDatabase.GUIDToAssetPath(guid);
        if (entry == null || entry.IsFolder)
        {
            return new ProjectSelectionLabelPanelView.SelectionRow
            {
                AssetPath = assetPath,
                Writable = false,
                Message = entry == null
                    ? "No existing AA entry. This panel will not create one."
                    : "AA folder entries are not writable because the runtime manifest excludes folders."
            };
        }

        string firstLabel = entry.labels.FirstOrDefault();
        return new ProjectSelectionLabelPanelView.SelectionRow
        {
            AssetPath = assetPath,
            Address = entry.address,
            Labels = string.Join(", ", entry.labels),
            Writable = true,
            Message = string.IsNullOrEmpty(firstLabel) ? string.Empty : "Protected first label: " + firstLabel
        };
    }

    private static string ApplyLabels(IReadOnlyList<string> guids, IReadOnlyList<string> labels)
    {
        AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null)
            return "Rejected: Addressables settings are missing.";
        if (guids == null || guids.Count == 0)
            return "Rejected: Project Selection is empty.";

        var entries = new List<AddressableAssetEntry>(guids.Count);
        var missing = new List<string>();
        var protectedLabels = new List<string>();
        var requested = new HashSet<string>(labels ?? Array.Empty<string>(), StringComparer.Ordinal);

        for (int i = 0; i < guids.Count; i++)
        {
            AddressableAssetEntry entry = settings.FindAssetEntry(guids[i]);
            if (entry == null || entry.IsFolder)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                missing.Add(string.IsNullOrEmpty(path) ? guids[i] : path);
                continue;
            }

            string firstLabel = entry.labels.FirstOrDefault();
            if (!string.IsNullOrEmpty(firstLabel) && !requested.Contains(firstLabel))
                protectedLabels.Add($"{entry.address}: {firstLabel}");
            entries.Add(entry);
        }

        if (missing.Count > 0)
            return "Rejected with zero writes. Missing or unsupported AA entries: " + string.Join(", ", missing);
        if (protectedLabels.Count > 0)
            return "Rejected with zero writes. Keep each current first label to preserve AA Type: " +
                   string.Join(", ", protectedLabels);

        Undo.RecordObject(settings, "Replace AA Project Labels");
        var recordedGroups = new HashSet<AddressableAssetGroup>();
        for (int i = 0; i < entries.Count; i++)
        {
            AddressableAssetGroup group = entries[i].parentGroup;
            if (group != null && recordedGroups.Add(group))
                Undo.RecordObject(group, "Replace AA Project Labels");
        }

        bool changed = false;
        for (int i = 0; i < entries.Count; i++)
        {
            AddressableAssetEntry entry = entries[i];
            List<string> current = entry.labels.ToList();
            for (int c = 0; c < current.Count; c++)
            {
                if (!requested.Contains(current[c]))
                    changed |= entry.SetLabel(current[c], false, false, false);
            }

            for (int l = 0; l < labels.Count; l++)
                changed |= entry.SetLabel(labels[l], true, true, false);
        }

        if (!changed)
            return $"No changes. {entries.Count} existing AA entries already match.";

        settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryModified, entries, true, true);
        EditorUtility.SetDirty(settings);
        AssetDatabase.SaveAssets();
        return $"Updated {entries.Count} existing AA entries. No entry or group was created.";
    }
}
