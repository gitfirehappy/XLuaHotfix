#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// AB 构建差异面板：当前 output 与 Latest baseline 的 Changes，以及与 Full baseline 的 Delivery 预览。
/// 预览只在用户点击时运行，不修改 baseline 或 PackageIndex。
/// </summary>
public sealed class ABBuildDiffPanel : IBuildPipelinePanel, IBuildPipelinePanelVisibility
{
    private enum DiffKind
    {
        Added,
        Modified,
        Removed
    }

    private sealed class DiffItem
    {
        public DiffKind Kind;
        public string Name;
        public BuildDiffEntry OldArtifact;
        public BuildDiffEntry NewArtifact;
    }

    private VisualElement _root;
    private Label _statusBadge;
    private Label _messageLabel;
    private Label _channelLabel;
    private Label _headLabel;
    private Label _versionLabel;
    private Label _packageLabel;
    private VisualElement _summaryRow;
    private VisualElement _deliverySummary;
    private VisualElement _artifactList;
    private VisualElement _detailContent;
    private Label _detailTitle;

    private BuildPackageRequest _request;
    private ArtifactDelta _delta;
    private ABRepositoryPreviewResult _deliveryPreview;
    private bool _hasDelta;
    private string _selectedArtifactKey;
    private readonly List<DiffItem> _items = new();

    public string PanelName => "Diff";

    public void OnEnable(EditorWindow window)
    {
    }

    public VisualElement CreateContent()
    {
        _root = new VisualElement();
        _root.style.flexGrow = 1f;
        _root.style.flexDirection = FlexDirection.Column;
        Rebuild();
        return _root;
    }

    public void OnDisable()
    {
        _root = null;
        _items.Clear();
        _delta = null;
        _deliveryPreview = null;
        _hasDelta = false;
        _selectedArtifactKey = null;
    }

    public void SetVisible(bool visible)
    {
        if (visible)
            RefreshBaseline();
    }

    private void Rebuild()
    {
        if (_root == null)
            return;

        _root.Clear();
        ClearPreviewState();

        _root.Add(CreateHeader());

        var split = new TwoPaneSplitView(0, 320f, TwoPaneSplitViewOrientation.Horizontal);
        split.style.flexGrow = 1f;
        split.style.minWidth = 0f;
        split.Add(CreateListPane());
        split.Add(CreateDetailPane());
        _root.Add(split);

        RefreshBaseline();
    }

    private VisualElement CreateHeader()
    {
        var header = BuildPipelineUI.Card();

        var top = new VisualElement();
        top.style.flexDirection = FlexDirection.Row;
        top.style.alignItems = Align.Center;

        var titleBox = new VisualElement();
        titleBox.style.flexGrow = 1f;
        titleBox.style.minWidth = 0f;
        var title = BuildPipelineUI.Header("Changes / Delivery");
        title.style.fontSize = 14f;
        titleBox.Add(title);
        _channelLabel = BuildPipelineUI.SmallText("Channel: -");
        titleBox.Add(_channelLabel);
        top.Add(titleBox);
        top.Add(BuildPipelineUI.ToolbarButton("Refresh", Rebuild, 70f));
        top.Add(BuildPipelineUI.ToolbarButton("Refresh Changes", RunRefreshChanges, 118f));
        top.Add(BuildPipelineUI.ToolbarButton("Preview Delivery", RunPreviewDelivery, 118f));
        header.Add(top);

        var statusRow = new VisualElement();
        statusRow.style.flexDirection = FlexDirection.Row;
        statusRow.style.alignItems = Align.Center;
        statusRow.style.marginTop = 6f;
        _statusBadge = PublishTargetPanel.CreateBadge("No Baseline", new Color(0.42f, 0.42f, 0.42f));
        statusRow.Add(_statusBadge);
        _messageLabel = BuildPipelineUI.SmallText(string.Empty);
        _messageLabel.style.marginLeft = 8f;
        _messageLabel.style.flexGrow = 1f;
        statusRow.Add(_messageLabel);
        header.Add(statusRow);

        var stats = new VisualElement();
        stats.style.flexDirection = FlexDirection.Row;
        stats.style.marginTop = 8f;
        _headLabel = AddStat(stats, "Latest", "-");
        _versionLabel = AddStat(stats, "Version", "-");
        _packageLabel = AddStat(stats, "Package", "-");
        header.Add(stats);

        _summaryRow = new VisualElement();
        _summaryRow.style.flexDirection = FlexDirection.Row;
        _summaryRow.style.flexWrap = Wrap.Wrap;
        _summaryRow.style.marginTop = 6f;
        header.Add(_summaryRow);

        _deliverySummary = new VisualElement();
        header.Add(_deliverySummary);

        return header;
    }

    private VisualElement CreateListPane()
    {
        var pane = BuildPipelineUI.Card();
        pane.style.minWidth = 220f;
        pane.style.marginBottom = 0f;
        var scroll = new ScrollView();
        scroll.style.flexGrow = 1f;
        scroll.style.minHeight = 0f;
        _artifactList = new VisualElement();
        scroll.Add(_artifactList);
        pane.Add(scroll);
        return pane;
    }

    private VisualElement CreateDetailPane()
    {
        var pane = BuildPipelineUI.Card();
        pane.style.minWidth = 320f;
        pane.style.marginBottom = 0f;
        _detailTitle = BuildPipelineUI.Header("Artifact Detail");
        pane.Add(_detailTitle);
        var scroll = new ScrollView();
        scroll.style.flexGrow = 1f;
        _detailContent = new VisualElement();
        scroll.Add(_detailContent);
        pane.Add(scroll);
        return pane;
    }

    private void RefreshBaseline()
    {
        if (_root == null)
            return;

        VersionRecord versionDB = AssetDatabase.LoadAssetAtPath<VersionRecord>(FYAssetSettings.Instance.VersionRecordPath);
        VersionNumber version = versionDB != null
            ? versionDB.CurrentVersion
            : new VersionNumber { Major = 0, Minor = 0, Patch = 0 };
        _request = BuildPackageRequest.Create(version, BuildType.Full, BackendModeNames.AB);
        string channelKey = BuildBaselineStore.GetChannelKey(version, BackendModeNames.AB);

        BuildBaselineState state;
        try
        {
            state = BuildBaselineStore.Load(channelKey);
        }
        catch (BuildBaselineException ex)
        {
            SetBadge("Baseline Error", new Color(0.65f, 0.20f, 0.16f));
            _messageLabel.text = ex.Message;
            _channelLabel.text = $"Channel: {channelKey}    Backend: AB";
            _headLabel.text = "-";
            _versionLabel.text = version.GetReleaseVersionString();
            _packageLabel.text = "-";
            RenderContent();
            return;
        }

        BuildBaseline latest = state?.Latest;
        _channelLabel.text = $"Channel: {channelKey}    Backend: AB";
        _headLabel.text = latest != null ? latest.Version.GetReleaseVersionString() : "-";
        _versionLabel.text = version.GetReleaseVersionString();
        _packageLabel.text = latest?.PackageName ?? "-";

        if (latest == null)
        {
            SetBadge("No Baseline", new Color(0.42f, 0.42f, 0.42f));
            _messageLabel.text = "完成一次成功交付（构建+发布）后生成 baseline。";
        }
        else
        {
            SetBadge("Baseline OK", new Color(0.18f, 0.48f, 0.28f));
            string latestFull = state.LatestFull != null ? state.LatestFull.Version.GetReleaseVersionString() : "-";
            _messageLabel.text = $"Latest={latest.Version.GetReleaseVersionString()} | LatestFull={latestFull} | {latest.PackageName}";
        }

        RenderContent();
    }

    private void RunRefreshChanges()
    {
        try
        {
            _messageLabel.text = "Running Changes preview...";
            _deliveryPreview = null;
            _delta = ABRepositoryPreview.RunDiffPreview(_request)?.HeadDelta ?? new ArtifactDelta();
            _hasDelta = true;

            SetBadge("Changes Ready", new Color(0.18f, 0.48f, 0.28f));
            _messageLabel.text = IsDeltaEmpty(_delta) ? "Changes preview completed with no baseline changes." : "Changes preview completed.";
            RenderContent();
        }
        catch (Exception ex)
        {
            Debug.LogError($"[{nameof(ABBuildDiffPanel)}] 刷新 Changes 失败：{ex}");
            ClearPreviewState();
            SetBadge("Changes Failed", new Color(0.65f, 0.20f, 0.16f));
            _messageLabel.text = ex.Message;
            RenderContent();
        }
    }

    private void RunPreviewDelivery()
    {
        try
        {
            _messageLabel.text = "Running delivery preview...";
            _deliveryPreview = ABRepositoryPreview.RunDeliveryPreview(_request);
            _delta = _deliveryPreview?.HeadDelta ?? new ArtifactDelta();
            _hasDelta = true;

            SetBadge("Delivery Ready", new Color(0.18f, 0.48f, 0.28f));
            _messageLabel.text = "Delivery preview completed.";
            RenderContent();
        }
        catch (Exception ex)
        {
            Debug.LogError($"[{nameof(ABBuildDiffPanel)}] Delivery 预览失败：{ex}");
            _deliveryPreview = new ABRepositoryPreviewResult
            {
                HeadDelta = _delta ?? new ArtifactDelta(),
                DeliveryAvailable = false,
                DeliveryMessage = ex.Message
            };
            SetBadge("Delivery Failed", new Color(0.65f, 0.20f, 0.16f));
            _messageLabel.text = ex.Message;
            RenderContent();
        }
    }

    private void RenderContent()
    {
        if (_artifactList == null || _detailContent == null)
            return;

        _summaryRow.Clear();
        _deliverySummary.Clear();
        _artifactList.Clear();
        _detailContent.Clear();
        _items.Clear();

        RenderDiffStats();
        RenderDeliverySummary();

        if (!_hasDelta)
        {
            _artifactList.Add(CreateEmptyState("点击 Refresh Changes 对比当前 output 与 Latest baseline。"));
            RenderEmptyDetail("尚未加载差异。");
            return;
        }

        _items.AddRange(BuildDiffItems(_delta, CurrentLatestArtifacts()));
        if (_items.Count == 0)
        {
            _artifactList.Add(CreateEmptyState("无差异。"));
            RenderEmptyDetail("未选择 artifact。");
            return;
        }

        EnsureSelection();
        for (int i = 0; i < _items.Count; i++)
            _artifactList.Add(CreateArtifactRow(_items[i]));
        RenderDetail(FindSelectedArtifact());
    }

    private IReadOnlyList<BuildDiffEntry> CurrentLatestArtifacts()
    {
        try
        {
            string channelKey = BuildBaselineStore.GetChannelKey(
                _request != null ? _request.Version : default, BackendModeNames.AB);
            return BuildBaselineStore.Load(channelKey)?.Latest?.Artifacts;
        }
        catch (BuildBaselineException)
        {
            return null;
        }
    }

    private void RenderDiffStats()
    {
        AddDiffStat("Added", _delta?.Added?.Count ?? 0, new Color(0.20f, 0.55f, 0.30f));
        AddDiffStat("Modified", _delta?.Modified?.Count ?? 0, new Color(0.70f, 0.48f, 0.16f));
        AddDiffStat("Removed", _delta?.Removed?.Count ?? 0, new Color(0.65f, 0.20f, 0.16f));
    }

    private void AddDiffStat(string title, int count, Color color)
    {
        Label stat = PublishTargetPanel.CreateBadge($"{title} {count}", color);
        stat.style.marginRight = 6f;
        stat.style.marginBottom = 4f;
        _summaryRow.Add(stat);
    }

    private void RenderDeliverySummary()
    {
        if (_deliveryPreview == null)
            return;

        if (!_deliveryPreview.DeliveryAvailable)
        {
            _deliverySummary.Add(PublishTargetPanel.CreateBadge("Hotfix Delivery Unavailable", new Color(0.42f, 0.42f, 0.42f)));
            _deliverySummary.Add(BuildPipelineUI.SmallText(string.IsNullOrEmpty(_deliveryPreview.DeliveryMessage)
                ? "点击 Preview Delivery 计算 Full baseline 到当前 output 的交付量。"
                : _deliveryPreview.DeliveryMessage));
            return;
        }

        int count = _deliveryPreview.DeliveryBundles != null ? _deliveryPreview.DeliveryBundles.Count : 0;
        _deliverySummary.Add(PublishTargetPanel.CreateBadge($"Hotfix Delivery {count}", new Color(0.17f, 0.36f, 0.53f)));
        _deliverySummary.Add(BuildPipelineUI.SmallText(
            $"Full baseline -> current output, {FileHelper.FormatBytes(_deliveryPreview.DeliverySizeBytes)}"));
    }

    private VisualElement CreateArtifactRow(DiffItem item)
    {
        var row = new VisualElement();
        row.style.paddingLeft = 8f;
        row.style.paddingRight = 8f;
        row.style.paddingTop = 7f;
        row.style.paddingBottom = 7f;
        row.style.marginBottom = 5f;
        row.style.backgroundColor = IsSelected(item)
            ? new Color(0.17f, 0.36f, 0.53f, 0.55f)
            : new Color(0f, 0f, 0f, 0.08f);
        row.style.flexDirection = FlexDirection.Row;
        row.style.alignItems = Align.Center;
        row.RegisterCallback<PointerDownEvent>(evt =>
        {
            if (evt.button != 0)
                return;

            _selectedArtifactKey = MakeItemKey(item);
            RenderContent();
            evt.StopPropagation();
        });

        row.Add(CreateMarkerLabel(item.Kind));

        var text = new VisualElement();
        text.style.flexGrow = 1f;
        text.style.minWidth = 0f;
        var name = new Label(SafeText(item.Name));
        name.style.whiteSpace = WhiteSpace.Normal;
        text.Add(name);
        BuildDiffEntry meta = item.NewArtifact ?? item.OldArtifact;
        string metaText = meta != null
            ? $"{FileHelper.FormatBytes(meta.Size)}  |  {ShortHash(meta.Hash)}"
            : "metadata unavailable";
        text.Add(BuildPipelineUI.SmallText(metaText));
        row.Add(text);
        return row;
    }

    private void RenderDetail(DiffItem item)
    {
        _detailContent.Clear();
        if (item == null)
        {
            RenderEmptyDetail("未选择 artifact。");
            return;
        }

        _detailTitle.text = "Artifact Detail";
        _detailContent.Add(CreateDetailLine("Status", GetKindText(item.Kind)));
        _detailContent.Add(CreateDetailLine("Name", SafeText(item.Name)));

        if (item.Kind == DiffKind.Modified)
        {
            _detailContent.Add(CreateSectionLabel("Old"));
            AddArtifactMetadata(item.OldArtifact);
            _detailContent.Add(CreateSectionLabel("New"));
            AddArtifactMetadata(item.NewArtifact);
            return;
        }

        _detailContent.Add(CreateSectionLabel(item.Kind == DiffKind.Added ? "New" : "Old"));
        AddArtifactMetadata(item.Kind == DiffKind.Added ? item.NewArtifact : item.OldArtifact);
    }

    private void RenderEmptyDetail(string message)
    {
        _detailTitle.text = "Artifact Detail";
        _detailContent.Clear();
        _detailContent.Add(CreateEmptyState(message));
    }

    private void AddArtifactMetadata(BuildDiffEntry artifact)
    {
        if (artifact == null)
        {
            _detailContent.Add(CreateEmptyState("Metadata unavailable."));
            return;
        }

        _detailContent.Add(CreateDetailLine("Hash", artifact.Hash));
        _detailContent.Add(CreateDetailLine("CRC", "0x" + artifact.CRC.ToString("X8", CultureInfo.InvariantCulture)));
        _detailContent.Add(CreateDetailLine("Size", FileHelper.FormatBytes(artifact.Size)));
    }

    private static List<DiffItem> BuildDiffItems(ArtifactDelta delta, IReadOnlyList<BuildDiffEntry> oldArtifacts)
    {
        var items = new List<DiffItem>();
        if (delta == null)
            return items;

        Dictionary<string, BuildDiffEntry> oldByName = BuildArtifactMap(oldArtifacts);
        AddItems(items, DiffKind.Added, delta.Added, oldByName);
        AddItems(items, DiffKind.Modified, delta.Modified, oldByName);
        if (delta.Removed != null)
        {
            for (int i = 0; i < delta.Removed.Count; i++)
            {
                string name = delta.Removed[i];
                oldByName.TryGetValue(name ?? string.Empty, out BuildDiffEntry oldArtifact);
                items.Add(new DiffItem { Kind = DiffKind.Removed, Name = name, OldArtifact = oldArtifact });
            }
        }
        return items;
    }

    private static void AddItems(
        List<DiffItem> items,
        DiffKind kind,
        List<BuildDiffEntry> artifacts,
        Dictionary<string, BuildDiffEntry> oldByName)
    {
        if (artifacts == null)
            return;

        for (int i = 0; i < artifacts.Count; i++)
        {
            BuildDiffEntry artifact = artifacts[i];
            string name = artifact != null ? artifact.Name : string.Empty;
            oldByName.TryGetValue(name ?? string.Empty, out BuildDiffEntry oldArtifact);
            items.Add(new DiffItem { Kind = kind, Name = name, OldArtifact = oldArtifact, NewArtifact = artifact });
        }
    }

    private static Dictionary<string, BuildDiffEntry> BuildArtifactMap(IReadOnlyList<BuildDiffEntry> artifacts)
    {
        var map = new Dictionary<string, BuildDiffEntry>(StringComparer.Ordinal);
        if (artifacts == null)
            return map;

        for (int i = 0; i < artifacts.Count; i++)
        {
            BuildDiffEntry artifact = artifacts[i];
            if (artifact != null && !string.IsNullOrEmpty(artifact.Name) && !map.ContainsKey(artifact.Name))
                map.Add(artifact.Name, artifact);
        }
        return map;
    }

    private void ClearPreviewState()
    {
        _delta = null;
        _deliveryPreview = null;
        _hasDelta = false;
        _selectedArtifactKey = null;
    }

    private void EnsureSelection()
    {
        if (_items.Count == 0)
        {
            _selectedArtifactKey = null;
            return;
        }

        if (FindSelectedArtifact() == null)
            _selectedArtifactKey = MakeItemKey(_items[0]);
    }

    private DiffItem FindSelectedArtifact()
    {
        if (string.IsNullOrEmpty(_selectedArtifactKey))
            return null;

        for (int i = 0; i < _items.Count; i++)
        {
            if (string.Equals(MakeItemKey(_items[i]), _selectedArtifactKey, StringComparison.Ordinal))
                return _items[i];
        }
        return null;
    }

    private bool IsSelected(DiffItem item)
    {
        return item != null && string.Equals(MakeItemKey(item), _selectedArtifactKey, StringComparison.Ordinal);
    }

    private static string MakeItemKey(DiffItem item)
    {
        return item == null ? string.Empty : $"{item.Kind}:{item.Name}";
    }

    private static bool IsDeltaEmpty(ArtifactDelta delta)
    {
        return (delta?.Added?.Count ?? 0) + (delta?.Modified?.Count ?? 0) + (delta?.Removed?.Count ?? 0) == 0;
    }

    private Label CreateMarkerLabel(DiffKind kind)
    {
        string marker = kind switch
        {
            DiffKind.Added => "+",
            DiffKind.Modified => "~",
            DiffKind.Removed => "-",
            _ => "?"
        };
        var mark = new Label(marker);
        mark.style.width = 24f;
        mark.style.minWidth = 24f;
        mark.style.unityTextAlign = TextAnchor.MiddleCenter;
        mark.style.unityFontStyleAndWeight = FontStyle.Bold;
        mark.style.color = kind switch
        {
            DiffKind.Added => new Color(0.20f, 0.55f, 0.30f),
            DiffKind.Modified => new Color(0.70f, 0.48f, 0.16f),
            DiffKind.Removed => new Color(0.65f, 0.20f, 0.16f),
            _ => Color.gray
        };
        return mark;
    }

    private static string GetKindText(DiffKind kind)
    {
        return kind switch
        {
            DiffKind.Added => "Added",
            DiffKind.Modified => "Modified",
            DiffKind.Removed => "Removed",
            _ => "Unknown"
        };
    }

    private static VisualElement CreateDetailLine(string label, string value)
    {
        var row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        row.style.marginBottom = 4f;

        Label key = BuildPipelineUI.SmallText(label);
        key.style.width = 84f;
        key.style.flexShrink = 0f;
        row.Add(key);

        var val = new Label(value ?? string.Empty);
        val.style.flexGrow = 1f;
        val.style.minWidth = 0f;
        val.style.whiteSpace = WhiteSpace.Normal;
        row.Add(val);
        return row;
    }

    private static Label CreateSectionLabel(string text)
    {
        var label = new Label(text);
        label.style.marginTop = 8f;
        label.style.marginBottom = 4f;
        label.style.unityFontStyleAndWeight = FontStyle.Bold;
        return label;
    }

    private static Label CreateEmptyState(string text)
    {
        Label label = BuildPipelineUI.SmallText(text);
        label.style.paddingLeft = 8f;
        label.style.paddingRight = 8f;
        label.style.paddingTop = 8f;
        label.style.paddingBottom = 8f;
        label.style.backgroundColor = new Color(0f, 0f, 0f, 0.08f);
        return label;
    }

    private static Label AddStat(VisualElement parent, string title, string value)
    {
        var box = new VisualElement();
        box.style.flexGrow = 1f;
        box.style.minWidth = 0f;
        box.style.marginRight = 6f;
        box.style.paddingLeft = 8f;
        box.style.paddingRight = 8f;
        box.style.paddingTop = 6f;
        box.style.paddingBottom = 6f;

        box.Add(BuildPipelineUI.SmallText(title));
        var valueLabel = new Label(value);
        valueLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
        valueLabel.style.whiteSpace = WhiteSpace.Normal;
        box.Add(valueLabel);
        parent.Add(box);
        return valueLabel;
    }

    private void SetBadge(string text, Color color)
    {
        if (_statusBadge == null)
            return;

        _statusBadge.text = text;
        _statusBadge.style.backgroundColor = color;
    }

    private static string ShortHash(string hash)
    {
        return string.IsNullOrEmpty(hash) ? "-" : (hash.Length <= 8 ? hash : hash.Substring(0, 8));
    }

    private static string SafeText(string value)
    {
        return string.IsNullOrEmpty(value) ? "-" : value;
    }
}
#endif
