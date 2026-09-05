using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Project Selection 标签面板的共享展示层。
/// 只负责选择读取、标签规范化和 UI；具体数据写入由调用方拥有。
/// </summary>
public sealed class ProjectSelectionLabelPanelView : IDisposable
{
    public sealed class SelectionRow
    {
        public string AssetPath;
        public string Address;
        public string Labels;
        public bool Writable;
        public string Message;
    }

    private readonly string _title;
    private readonly string _description;
    private readonly Func<string, SelectionRow> _describeSelection;
    private readonly Func<IReadOnlyList<string>, IReadOnlyList<string>, string> _applyLabels;

    private VisualElement _root;
    private ScrollView _selectionList;
    private TextField _labelsField;
    private Label _selectionSummary;
    private Label _statusLabel;
    private Button _applyButton;
    private bool _subscribed;

    public ProjectSelectionLabelPanelView(
        string title,
        string description,
        Func<string, SelectionRow> describeSelection,
        Func<IReadOnlyList<string>, IReadOnlyList<string>, string> applyLabels)
    {
        _title = title;
        _description = description;
        _describeSelection = describeSelection ?? throw new ArgumentNullException(nameof(describeSelection));
        _applyLabels = applyLabels ?? throw new ArgumentNullException(nameof(applyLabels));
    }

    public VisualElement CreateContent()
    {
        _root = new VisualElement();
        _root.style.flexGrow = 1f;
        _root.style.flexDirection = FlexDirection.Column;

        VisualElement header = BuildPipelineUI.Card();
        header.Add(BuildPipelineUI.Header(_title));
        header.Add(BuildPipelineUI.SmallText(_description));
        _root.Add(header);

        VisualElement editor = BuildPipelineUI.Card();
        editor.Add(BuildPipelineUI.Header("Replace Labels"));
        editor.Add(BuildPipelineUI.SmallText(
            "Enter comma, semicolon, or newline separated labels. Empty input clears labels when the concrete authority permits it."));

        _labelsField = new TextField("Labels")
        {
            multiline = true
        };
        _labelsField.style.minHeight = 58f;
        _labelsField.style.marginTop = 6f;
        editor.Add(_labelsField);

        VisualElement actions = new VisualElement();
        actions.style.flexDirection = FlexDirection.Row;
        actions.style.marginTop = 6f;
        actions.Add(new Button(Refresh) { text = "Refresh Selection" });
        _applyButton = new Button(Apply) { text = "Replace Existing Entry Labels" };
        _applyButton.style.marginLeft = 6f;
        actions.Add(_applyButton);
        editor.Add(actions);

        _statusLabel = BuildPipelineUI.SmallText(string.Empty);
        _statusLabel.style.marginTop = 6f;
        editor.Add(_statusLabel);
        _root.Add(editor);

        VisualElement selection = BuildPipelineUI.Card();
        selection.style.flexGrow = 1f;
        selection.style.minHeight = 0f;
        selection.Add(BuildPipelineUI.Header("Project Selection"));
        _selectionSummary = BuildPipelineUI.SmallText(string.Empty);
        selection.Add(_selectionSummary);
        _selectionList = new ScrollView();
        _selectionList.style.flexGrow = 1f;
        _selectionList.style.minHeight = 0f;
        _selectionList.style.marginTop = 6f;
        selection.Add(_selectionList);
        _root.Add(selection);

        Subscribe();
        Refresh();
        return _root;
    }

    public void SetVisible(bool visible)
    {
        if (visible)
            Refresh();
    }

    public void Dispose()
    {
        if (_subscribed)
        {
            Selection.selectionChanged -= Refresh;
            _subscribed = false;
        }

        _root = null;
        _selectionList = null;
        _labelsField = null;
        _selectionSummary = null;
        _statusLabel = null;
        _applyButton = null;
    }

    public static List<string> NormalizeLabels(string rawLabels)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string[] parts = (rawLabels ?? string.Empty).Split(new[] { ',', ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < parts.Length; i++)
        {
            string label = parts[i].Trim();
            if (label.Length > 0 && seen.Add(label))
                result.Add(label);
        }

        return result;
    }

    private void Subscribe()
    {
        if (_subscribed)
            return;

        Selection.selectionChanged += Refresh;
        _subscribed = true;
    }

    private void Refresh()
    {
        if (_selectionList == null)
            return;

        _selectionList.Clear();
        string[] guids = GetSelectedGuids();
        int writableCount = 0;

        for (int i = 0; i < guids.Length; i++)
        {
            SelectionRow row = _describeSelection(guids[i]) ?? new SelectionRow();
            if (row.Writable)
                writableCount++;

            VisualElement card = BuildPipelineUI.Card();
            string assetPath = string.IsNullOrEmpty(row.AssetPath)
                ? AssetDatabase.GUIDToAssetPath(guids[i])
                : row.AssetPath;
            card.Add(BuildPipelineUI.Header(string.IsNullOrEmpty(assetPath) ? guids[i] : assetPath));
            if (!string.IsNullOrEmpty(row.Address))
                card.Add(BuildPipelineUI.SmallText("Address: " + row.Address));
            card.Add(BuildPipelineUI.SmallText("Labels: " + (string.IsNullOrEmpty(row.Labels) ? "(none)" : row.Labels)));
            if (!string.IsNullOrEmpty(row.Message))
                card.Add(BuildPipelineUI.SmallText(row.Message));
            _selectionList.Add(card);
        }

        _selectionSummary.text = guids.Length == 0
            ? "Select assets in the Project window."
            : $"Selected {guids.Length}  |  Existing writable entries {writableCount}";
        _applyButton?.SetEnabled(guids.Length > 0);
    }

    private void Apply()
    {
        string[] guids = GetSelectedGuids();
        List<string> labels = NormalizeLabels(_labelsField?.value);
        if (guids.Length > 0 && labels.Count == 0 && !EditorUtility.DisplayDialog(
                "Clear Existing Entry Labels?",
                "The label input is empty. This will request clearing labels on every selected existing entry. " +
                "Backend safety rules still apply, and the operation can be undone.",
                "Clear Labels",
                "Cancel"))
        {
            _statusLabel.text = "Cancelled. No labels were changed.";
            return;
        }

        _statusLabel.text = _applyLabels(guids, labels) ?? string.Empty;
        Refresh();
    }

    private static string[] GetSelectedGuids()
    {
        string[] selected = Selection.assetGUIDs ?? Array.Empty<string>();
        var result = new List<string>(selected.Length);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < selected.Length; i++)
        {
            string guid = selected[i];
            if (string.IsNullOrEmpty(guid) || !seen.Add(guid))
                continue;

            string assetPath = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(assetPath) || AssetDatabase.IsValidFolder(assetPath))
                continue;

            result.Add(guid);
        }

        return result.ToArray();
    }
}
