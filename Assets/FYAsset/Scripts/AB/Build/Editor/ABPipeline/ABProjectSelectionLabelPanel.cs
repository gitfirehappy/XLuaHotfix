using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// AB Project Selection 标签面板。只修改已收集资产的 AssetEntry 标签。
/// </summary>
public sealed class ABProjectSelectionLabelPanel : BuildPipelineUIToolkitPanel
{
    private readonly AssetsCollectionPanel _assetsCollectionPanel;
    private ProjectSelectionLabelPanelView _view;

    public ABProjectSelectionLabelPanel(AssetsCollectionPanel assetsCollectionPanel)
    {
        _assetsCollectionPanel = assetsCollectionPanel ?? throw new ArgumentNullException(nameof(assetsCollectionPanel));
    }

    public override string PanelName => "Project Labels";

    protected override void BuildContent(VisualElement root)
    {
        _view = new ProjectSelectionLabelPanelView(
            "AB Project Labels",
            "Replaces labels only on existing collected non-folder AssetEntries. Missing entries reject the entire batch, and unsaved AssetsCollection Curate state blocks writes.",
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
        AssetCollectionSetting setting = CollectorMutationUtility.LoadSetting();
        AssetEntry entry = setting?.FindAssetEntry(guid);
        string assetPath = AssetDatabase.GUIDToAssetPath(guid);
        if (entry == null)
        {
            return new ProjectSelectionLabelPanelView.SelectionRow
            {
                AssetPath = assetPath,
                Writable = false,
                Message = "No existing collected AssetEntry. This panel will not create one."
            };
        }

        return new ProjectSelectionLabelPanelView.SelectionRow
        {
            AssetPath = assetPath,
            Address = entry.Address,
            Labels = entry.Labels == null ? string.Empty : string.Join(", ", entry.Labels),
            Writable = true
        };
    }

    private string ApplyLabels(IReadOnlyList<string> guids, IReadOnlyList<string> labels)
    {
        if (_assetsCollectionPanel.HasUnsavedChanges)
            return "Rejected with zero writes. Save or cancel the current AssetsCollection Curate changes first.";
        if (guids == null || guids.Count == 0)
            return "Rejected: Project Selection is empty.";

        AssetCollectionSetting setting = CollectorMutationUtility.LoadSetting();
        if (setting == null)
            return "Rejected: AssetCollectionSetting is missing.";

        var entries = new List<AssetEntry>(guids.Count);
        var missing = new List<string>();
        for (int i = 0; i < guids.Count; i++)
        {
            AssetEntry entry = setting.FindAssetEntry(guids[i]);
            if (entry == null)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                missing.Add(string.IsNullOrEmpty(path) ? guids[i] : path);
                continue;
            }

            entries.Add(entry);
        }

        if (missing.Count > 0)
            return "Rejected with zero writes. Missing collected AssetEntries: " + string.Join(", ", missing);

        Undo.RecordObject(setting, "Replace AB Project Labels");
        bool changed = false;
        for (int i = 0; i < entries.Count; i++)
        {
            AssetEntry entry = entries[i];
            if (LabelsEqual(entry.Labels, labels))
                continue;

            entry.Labels = new List<string>(labels ?? Array.Empty<string>());
            changed = true;
        }

        if (!changed)
            return $"No changes. {entries.Count} existing AB entries already match.";

        EditorUtility.SetDirty(setting);
        AssetDatabase.SaveAssets();
        CollectorMutationUtility.NotifyChanged();
        return $"Updated {entries.Count} existing AB entries. No AssetEntry, collector, group, or package was created.";
    }

    private static bool LabelsEqual(IReadOnlyList<string> current, IReadOnlyList<string> requested)
    {
        int currentCount = current?.Count ?? 0;
        int requestedCount = requested?.Count ?? 0;
        if (currentCount != requestedCount)
            return false;

        for (int i = 0; i < currentCount; i++)
        {
            if (!string.Equals(current[i], requested[i], StringComparison.Ordinal))
                return false;
        }

        return true;
    }
}
