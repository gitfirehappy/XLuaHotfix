#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// AB Pipeline 构建结果面板。
/// 读取 BuildData/Reports/AB 下的 editor-only JSON 报告，不扫描或修改 package 输出。
/// </summary>
public sealed class ABReportPanel : BuildPipelineUIToolkitPanel
{
    private enum ReportTab
    {
        Summary,
        Explore,
        PotentialIssues,
        Packages
    }

    private enum ExploreMode
    {
        AssetBundles,
        Assets,
        Groups,
        Labels
    }

    private VisualElement _root;
    private VisualElement _body;
    private VisualElement _details;
    private DropdownField _reportDropdown;
    private PopupField<string> _exploreModeField;
    private TextField _searchField;
    private Label _statusLabel;
    private ABBuildReport _report;
    private List<string> _reportPaths = new();
    private string _selectedReportPath = string.Empty;
    private string _searchText = string.Empty;
    private ReportTab _activeTab = ReportTab.Summary;
    private ExploreMode _exploreMode = ExploreMode.AssetBundles;
    private readonly BuildPackageResultsView _packageResults = new();

    public override string PanelName => "AB Build Results";

    public override void SetVisible(bool visible)
    {
        if (visible && _root != null)
            LoadReports(false);
    }

    protected override void BuildContent(VisualElement root)
    {
        _root = root;
        _root.style.flexGrow = 1f;
        _root.style.flexDirection = FlexDirection.Column;

        DrawToolbar();
        DrawTabBar();

        var main = new VisualElement { style = { flexDirection = FlexDirection.Row, flexGrow = 1f, minHeight = 0f } };
        _body = new VisualElement { style = { flexGrow = 1f, minWidth = 0f, flexDirection = FlexDirection.Column } };
        _details = new VisualElement { style = { width = 300f, minWidth = 240f, flexShrink = 0f, marginLeft = 8f } };
        main.Add(_body);
        main.Add(_details);
        _root.Add(main);

        LoadReports(true);
    }

    #region Toolbar

    private void DrawToolbar()
    {
        VisualElement toolbar = BuildPipelineUI.Toolbar();
        toolbar.Add(BuildPipelineUI.ToolbarButton("Refresh", () => LoadReports(true), 60f));
        toolbar.Add(BuildPipelineUI.ToolbarButton("Reports Folder", ABBuildReportStore.RevealReportsDirectory, 96f));
        toolbar.Add(BuildPipelineUI.ToolbarButton("Open Package", RevealPackage, 96f));

        _reportDropdown = new DropdownField();
        _reportDropdown.style.minWidth = 260f;
        _reportDropdown.style.maxWidth = 420f;
        _reportDropdown.RegisterValueChangedCallback(evt =>
        {
            int index = _reportDropdown.index;
            if (index >= 0 && index < _reportPaths.Count)
                LoadReport(_reportPaths[index]);
        });
        toolbar.Add(_reportDropdown);

        _searchField = new TextField();
        _searchField.style.width = 220f;
        _searchField.style.marginLeft = 4f;
        _searchField.tooltip = "Search current report view";
        _searchField.RegisterValueChangedCallback(evt =>
        {
            _searchText = evt.newValue ?? string.Empty;
            RefreshBody();
        });
        toolbar.Add(_searchField);

        toolbar.Add(BuildPipelineUI.Spacer());
        _statusLabel = BuildPipelineUI.ToolbarLabel(string.Empty);
        _statusLabel.style.minWidth = 160f;
        toolbar.Add(_statusLabel);
        _root.Add(toolbar);
    }

    private void DrawTabBar()
    {
        VisualElement tabs = BuildPipelineUI.Toolbar();
        tabs.Add(CreateTabButton("Summary", ReportTab.Summary));
        tabs.Add(CreateTabButton("Explore", ReportTab.Explore));
        tabs.Add(CreateTabButton("Potential Issues", ReportTab.PotentialIssues));
        tabs.Add(CreateTabButton("Packages", ReportTab.Packages));
        tabs.Add(BuildPipelineUI.Spacer());

        _exploreModeField = new PopupField<string>(
            new List<string> { "AssetBundles", "Assets", "Groups", "Labels" },
            _exploreMode.ToString());
        _exploreModeField.style.width = 160f;
        _exploreModeField.RegisterValueChangedCallback(evt =>
        {
            if (Enum.TryParse(evt.newValue, out ExploreMode parsed))
            {
                _exploreMode = parsed;
                RefreshBody();
            }
        });
        tabs.Add(_exploreModeField);
        _root.Add(tabs);
    }

    private Button CreateTabButton(string text, ReportTab tab)
    {
        Button button = BuildPipelineUI.ToolbarButton(text, () =>
        {
            _activeTab = tab;
            RefreshBody();
        }, tab == ReportTab.PotentialIssues ? 120f : 80f);
        button.style.unityFontStyleAndWeight = _activeTab == tab ? FontStyle.Bold : FontStyle.Normal;
        return button;
    }

    #endregion

    #region Loading

    private void LoadReports(bool forceLatest)
    {
        _reportPaths = ABBuildReportStore.ListReportPaths();
        _reportDropdown.choices = BuildReportChoices(_reportPaths);

        if (_reportPaths.Count == 0)
        {
            _selectedReportPath = string.Empty;
            _report = null;
            _reportDropdown.value = string.Empty;
            SetStatus("暂无 AB 报告", BuildPipelineUI.SecondaryTextColor);
            RefreshBody();
            return;
        }

        int selectedIndex = 0;
        if (!forceLatest && !string.IsNullOrEmpty(_selectedReportPath))
        {
            int existing = _reportPaths.FindIndex(path => string.Equals(path, _selectedReportPath, StringComparison.OrdinalIgnoreCase));
            if (existing >= 0)
                selectedIndex = existing;
        }

        _reportDropdown.index = selectedIndex;
        LoadReport(_reportPaths[selectedIndex]);
    }

    private static List<string> BuildReportChoices(List<string> paths)
    {
        var choices = new List<string>();
        for (int i = 0; i < paths.Count; i++)
            choices.Add(Path.GetFileNameWithoutExtension(paths[i]));
        return choices;
    }

    private void LoadReport(string path)
    {
        _selectedReportPath = path ?? string.Empty;
        try
        {
            _report = ABBuildReportStore.Read(path);
            if (_report == null)
            {
                SetStatus("报告为空", Color.red);
                return;
            }

            SetStatus(_report.Header.Success ? "构建成功" : "构建失败",
                _report.Header.Success ? new Color(0.35f, 0.95f, 0.35f) : Color.red);
        }
        catch (Exception ex)
        {
            _report = null;
            SetStatus("读取失败", Color.red);
            Debug.LogWarning($"[{nameof(ABReportPanel)}] 读取 AB 构建报告失败: {path}, {ex.Message}");
        }

        RefreshBody();
    }

    private void SetStatus(string text, Color color)
    {
        if (_statusLabel == null)
            return;

        _statusLabel.text = text ?? string.Empty;
        _statusLabel.style.color = color;
    }

    #endregion

    #region Rendering

    private void RefreshBody()
    {
        if (_body == null || _details == null)
            return;

        _body.Clear();
        _details.Clear();
        _exploreModeField?.SetEnabled(_activeTab == ReportTab.Explore);

        if (_activeTab == ReportTab.Packages)
        {
            _packageResults.Build(_body);
            DrawDetailsText("Select packages in the table, then delete from the Packages toolbar.");
            return;
        }

        if (_report == null)
        {
            DrawEmptyState();
            return;
        }

        switch (_activeTab)
        {
            case ReportTab.Summary:
                DrawSummary();
                break;
            case ReportTab.Explore:
                DrawExplore();
                break;
            case ReportTab.PotentialIssues:
                DrawIssues();
                break;
        }
    }

    private void DrawEmptyState()
    {
        VisualElement panel = CreateCenteredPanel(_body, 460f);
        panel.Add(CreateTitle("暂无可显示的 AB 构建报告"));
        panel.Add(CreateBody("执行 AB Full 或 Hotfix 构建后，报告会写入 BuildData/Reports/AB。"));
    }

    private void DrawSummary()
    {
        ScrollView scroll = CreateScroll();
        VisualElement header = BuildPipelineUI.Card();
        header.Add(BuildPipelineUI.Header("General Information"));
        AddKeyValue(header, "Backend", _report.Header.Backend);
        AddKeyValue(header, "Build Type", _report.Header.BuildType);
        AddKeyValue(header, "Build Target", _report.Header.BuildTarget);
        AddKeyValue(header, "Version", _report.Header.Version);
        AddKeyValue(header, "Package", _report.Header.PackageName);
        AddKeyValue(header, "Package Path", _report.Header.PackagePath);
        AddKeyValue(header, "Started UTC", _report.Header.StartedAtUtc);
        AddKeyValue(header, "Duration", _report.Header.DurationSeconds.ToString("F2") + "s");
        if (!_report.Header.Success)
            AddKeyValue(header, "Error", FormatIssueText(_report.Header.ErrorCode, _report.Header.ErrorMessage));
        scroll.Add(header);

        VisualElement aggregate = BuildPipelineUI.Card();
        aggregate.Add(BuildPipelineUI.Header("Aggregate Information"));
        AddMetricGrid(aggregate, new[]
        {
            ("Bundles", _report.Summary.BundleCount.ToString()),
            ("Assets", _report.Summary.AssetCount.ToString()),
            ("Groups", _report.Summary.GroupCount.ToString()),
            ("Labels", _report.Summary.LabelCount.ToString()),
            ("Total Size", FormatBytes(_report.Summary.TotalBundleSize)),
            ("Delivery", $"{_report.Summary.DeliveryBundleCount} / {FormatBytes(_report.Summary.DeliveryBundleSize)}"),
            ("Tasks", $"{_report.Summary.CompletedTasks}/{_report.Summary.TotalTasks} completed"),
            ("Issues", $"{_report.Issues.Count}")
        });
        scroll.Add(aggregate);

        VisualElement verification = BuildPipelineUI.Card();
        verification.Add(BuildPipelineUI.Header("Verification"));
        AddKeyValue(verification, "Errors", _report.Summary.VerificationErrorCount.ToString());
        AddKeyValue(verification, "Warnings", _report.Summary.VerificationWarningCount.ToString());
        AddKeyValue(verification, "Task Warnings", _report.Summary.WarningCount.ToString());
        AddKeyValue(verification, "Failed Tasks", _report.Summary.FailedTasks.ToString());
        scroll.Add(verification);

        _body.Add(scroll);
        DrawDetailsText("Select Explore rows to inspect bundle or asset details.");
    }

    private void DrawExplore()
    {
        ScrollView scroll = CreateScroll();
        switch (_exploreMode)
        {
            case ExploreMode.AssetBundles:
                DrawBundleRows(scroll);
                break;
            case ExploreMode.Assets:
                DrawAssetRows(scroll);
                break;
            case ExploreMode.Groups:
                DrawGroupRows(scroll);
                break;
            case ExploreMode.Labels:
                DrawLabelRows(scroll);
                break;
        }
        _body.Add(scroll);
        DrawDetailsText("Select a row to view details.");
    }

    private void DrawIssues()
    {
        ScrollView scroll = CreateScroll();
        scroll.Add(BuildPipelineUI.Header("Potential Issues"));
        if (_report.Issues == null || _report.Issues.Count == 0)
        {
            scroll.Add(BuildPipelineUI.SmallText("No issues recorded in this report."));
            _body.Add(scroll);
            DrawDetailsText("No issue selected.");
            return;
        }

        for (int i = 0; i < _report.Issues.Count; i++)
        {
            ABBuildReportIssue issue = _report.Issues[i];
            if (!MatchesSearch(issue.Severity, issue.Source, issue.Code, issue.Subject, issue.Message))
                continue;

            VisualElement row = CreateDataRow();
            row.Add(CreateCell(issue.Severity, 70f, issue.Severity == "Error" ? Color.red : new Color(1f, 0.78f, 0.25f)));
            row.Add(CreateCell(issue.Source, 150f));
            row.Add(CreateCell(issue.Code, 140f));
            row.Add(CreateCell(issue.Subject, 180f));
            row.Add(CreateCell(issue.Message, 0f, BuildPipelineUI.SecondaryTextColor, true));
            row.RegisterCallback<PointerDownEvent>(_ => DrawIssueDetails(issue));
            scroll.Add(row);
        }

        _body.Add(scroll);
        DrawDetailsText("Select an issue to inspect.");
    }

    private void DrawBundleRows(VisualElement parent)
    {
        AddTableHeader(parent, "Bundle", "Size", "Type", "Group", "Assets", "Deps", "Delivery");
        for (int i = 0; i < _report.Bundles.Count; i++)
        {
            ABBuildReportBundle bundle = _report.Bundles[i];
            if (!MatchesSearch(bundle.BundleName, bundle.BundleType, bundle.Group, bundle.Tags))
                continue;

            VisualElement row = CreateDataRow();
            row.Add(CreateCell(bundle.BundleName, 260f));
            row.Add(CreateCell(FormatBytes(bundle.FileSize), 92f));
            row.Add(CreateCell(bundle.BundleType, 90f));
            row.Add(CreateCell(bundle.Group, 130f));
            row.Add(CreateCell(bundle.AssetCount.ToString(), 56f));
            row.Add(CreateCell(bundle.DependencyCount.ToString(), 52f));
            row.Add(CreateCell(bundle.Delivered ? "Yes" : "No", 70f, bundle.Delivered ? new Color(0.35f, 0.95f, 0.35f) : BuildPipelineUI.SecondaryTextColor));
            row.RegisterCallback<PointerDownEvent>(_ => DrawBundleDetails(bundle));
            parent.Add(row);
        }
    }

    private void DrawAssetRows(VisualElement parent)
    {
        AddTableHeader(parent, "Asset", "Address", "Type", "Group", "Bundle", "Delivery");
        for (int i = 0; i < _report.Assets.Count; i++)
        {
            ABBuildReportAsset asset = _report.Assets[i];
            if (!MatchesSearch(asset.SourcePath, asset.Address, asset.PrimaryType, asset.Group, asset.Labels, asset.BundleName))
                continue;

            VisualElement row = CreateDataRow();
            row.Add(CreateCell(asset.SourcePath, 300f));
            row.Add(CreateCell(asset.Address, 180f));
            row.Add(CreateCell(asset.PrimaryType, 100f));
            row.Add(CreateCell(asset.Group, 120f));
            row.Add(CreateCell(asset.BundleName, 220f));
            row.Add(CreateCell(asset.Delivered ? "Yes" : "No", 70f, asset.Delivered ? new Color(0.35f, 0.95f, 0.35f) : BuildPipelineUI.SecondaryTextColor));
            row.RegisterCallback<PointerDownEvent>(_ => DrawAssetDetails(asset));
            parent.Add(row);
        }
    }

    private void DrawGroupRows(VisualElement parent)
    {
        AddTableHeader(parent, "Group", "Assets", "Bundles", "Total Size");
        for (int i = 0; i < _report.Groups.Count; i++)
        {
            ABBuildReportGroup group = _report.Groups[i];
            if (!MatchesSearch(group.Group))
                continue;

            VisualElement row = CreateDataRow();
            row.Add(CreateCell(group.Group, 260f));
            row.Add(CreateCell(group.AssetCount.ToString(), 80f));
            row.Add(CreateCell(group.BundleCount.ToString(), 80f));
            row.Add(CreateCell(FormatBytes(group.TotalSize), 120f));
            row.RegisterCallback<PointerDownEvent>(_ => DrawAggregateDetails("Group", group.Group, group.AssetCount, group.BundleCount, group.TotalSize));
            parent.Add(row);
        }
    }

    private void DrawLabelRows(VisualElement parent)
    {
        AddTableHeader(parent, "Label", "Assets", "Bundles", "Total Size");
        for (int i = 0; i < _report.Labels.Count; i++)
        {
            ABBuildReportLabel label = _report.Labels[i];
            if (!MatchesSearch(label.Label))
                continue;

            VisualElement row = CreateDataRow();
            row.Add(CreateCell(label.Label, 260f));
            row.Add(CreateCell(label.AssetCount.ToString(), 80f));
            row.Add(CreateCell(label.BundleCount.ToString(), 80f));
            row.Add(CreateCell(FormatBytes(label.TotalSize), 120f));
            row.RegisterCallback<PointerDownEvent>(_ => DrawAggregateDetails("Label", label.Label, label.AssetCount, label.BundleCount, label.TotalSize));
            parent.Add(row);
        }
    }

    #endregion

    #region Details

    private void DrawBundleDetails(ABBuildReportBundle bundle)
    {
        _details.Clear();
        _details.Add(BuildPipelineUI.Header("Bundle"));
        AddKeyValue(_details, "Name", bundle.BundleName);
        AddKeyValue(_details, "Size", FormatBytes(bundle.FileSize));
        AddKeyValue(_details, "Hash", bundle.FileHash);
        AddKeyValue(_details, "CRC", bundle.FileCRC.ToString());
        AddKeyValue(_details, "Type", bundle.BundleType);
        AddKeyValue(_details, "Group", bundle.Group);
        AddKeyValue(_details, "Tags", bundle.Tags);
        AddKeyValue(_details, "Delivered", bundle.Delivered ? "Yes" : "No");
        AddList(_details, "Dependencies", bundle.Dependencies);
        AddList(_details, "Assets", bundle.Assets);
    }

    private void DrawAssetDetails(ABBuildReportAsset asset)
    {
        _details.Clear();
        _details.Add(BuildPipelineUI.Header("Asset"));
        AddKeyValue(_details, "Source", asset.SourcePath);
        AddKeyValue(_details, "EntryId", asset.EntryId);
        AddKeyValue(_details, "Address", asset.Address);
        AddKeyValue(_details, "Type", asset.PrimaryType);
        AddKeyValue(_details, "Group", asset.Group);
        AddKeyValue(_details, "Labels", asset.Labels);
        AddKeyValue(_details, "Bundle", asset.BundleName);
        AddKeyValue(_details, "Delivered", asset.Delivered ? "Yes" : "No");
    }

    private void DrawIssueDetails(ABBuildReportIssue issue)
    {
        _details.Clear();
        _details.Add(BuildPipelineUI.Header("Issue"));
        AddKeyValue(_details, "Severity", issue.Severity);
        AddKeyValue(_details, "Source", issue.Source);
        AddKeyValue(_details, "Code", issue.Code);
        AddKeyValue(_details, "Subject", issue.Subject);
        AddKeyValue(_details, "Message", issue.Message);
    }

    private void DrawAggregateDetails(string kind, string name, int assetCount, int bundleCount, long totalSize)
    {
        _details.Clear();
        _details.Add(BuildPipelineUI.Header(kind));
        AddKeyValue(_details, "Name", name);
        AddKeyValue(_details, "Assets", assetCount.ToString());
        AddKeyValue(_details, "Bundles", bundleCount.ToString());
        AddKeyValue(_details, "Total Size", FormatBytes(totalSize));
    }

    private void DrawDetailsText(string text)
    {
        _details.Clear();
        _details.Add(BuildPipelineUI.Header("Details"));
        _details.Add(BuildPipelineUI.SmallText(text));
    }

    #endregion

    #region Shared UI

    private static ScrollView CreateScroll()
    {
        var scroll = new ScrollView();
        scroll.style.flexGrow = 1f;
        scroll.style.minHeight = 0f;
        scroll.style.paddingLeft = 8f;
        scroll.style.paddingRight = 8f;
        scroll.style.paddingTop = 8f;
        return scroll;
    }

    private static VisualElement CreateDataRow()
    {
        var row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        row.style.minHeight = 22f;
        row.style.alignItems = Align.Center;
        row.style.borderBottomWidth = 1f;
        row.style.borderBottomColor = new Color(0f, 0f, 0f, 0.16f);
        row.style.paddingLeft = 4f;
        row.style.paddingRight = 4f;
        return row;
    }

    private static Label CreateCell(string text, float width, Color? color = null, bool grow = false)
    {
        var label = BuildPipelineUI.SmallText(text ?? string.Empty);
        label.style.whiteSpace = WhiteSpace.NoWrap;
        label.style.overflow = Overflow.Hidden;
        label.style.textOverflow = TextOverflow.Ellipsis;
        label.style.marginRight = 8f;
        if (width > 0f)
        {
            label.style.width = width;
            label.style.flexShrink = 0f;
        }
        else if (grow)
        {
            label.style.flexGrow = 1f;
            label.style.minWidth = 0f;
        }
        if (color.HasValue)
            label.style.color = color.Value;
        return label;
    }

    private static void AddTableHeader(VisualElement parent, params string[] columns)
    {
        VisualElement row = CreateDataRow();
        row.style.backgroundColor = new Color(0f, 0f, 0f, 0.12f);
        for (int i = 0; i < columns.Length; i++)
        {
            float width = i == 0 ? 260f : 90f;
            Label label = CreateCell(columns[i], width);
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            row.Add(label);
        }
        parent.Add(row);
    }

    private static void AddKeyValue(VisualElement parent, string key, string value)
    {
        var row = new VisualElement { style = { flexDirection = FlexDirection.Row, marginBottom = 2f, minWidth = 0f } };
        Label k = BuildPipelineUI.SmallText(key);
        k.style.width = 96f;
        k.style.flexShrink = 0f;
        k.style.unityFontStyleAndWeight = FontStyle.Bold;
        Label v = BuildPipelineUI.SmallText(value ?? string.Empty);
        v.style.flexGrow = 1f;
        v.style.minWidth = 0f;
        row.Add(k);
        row.Add(v);
        parent.Add(row);
    }

    private static void AddMetricGrid(VisualElement parent, (string label, string value)[] items)
    {
        var grid = new VisualElement { style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap } };
        for (int i = 0; i < items.Length; i++)
        {
            var card = BuildPipelineUI.Card();
            card.style.width = 170f;
            card.style.marginRight = 8f;
            card.Add(BuildPipelineUI.SmallText(items[i].label));
            Label value = BuildPipelineUI.Header(items[i].value);
            value.style.marginBottom = 0f;
            card.Add(value);
            grid.Add(card);
        }
        parent.Add(grid);
    }

    private static void AddList(VisualElement parent, string title, List<string> values)
    {
        parent.Add(BuildPipelineUI.Header(title));
        if (values == null || values.Count == 0)
        {
            parent.Add(BuildPipelineUI.SmallText("(none)"));
            return;
        }

        for (int i = 0; i < values.Count; i++)
            parent.Add(BuildPipelineUI.SmallText(values[i]));
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB" };
        double value = bytes;
        int unit = 0;
        while (value >= 1024d && unit < units.Length - 1)
        {
            value /= 1024d;
            unit++;
        }
        return $"{value:F2} {units[unit]}";
    }

    private static string FormatIssueText(string code, string message)
    {
        if (string.IsNullOrEmpty(code))
            return message ?? string.Empty;
        return $"[{code}] {message}";
    }

    private bool MatchesSearch(params string[] values)
    {
        if (string.IsNullOrWhiteSpace(_searchText))
            return true;

        for (int i = 0; i < values.Length; i++)
        {
            if (!string.IsNullOrEmpty(values[i]) &&
                values[i].IndexOf(_searchText, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    private void RevealPackage()
    {
        if (_report == null || string.IsNullOrEmpty(_report.Header.PackagePath))
            return;

        EditorUtility.RevealInFinder(_report.Header.PackagePath);
    }

    #endregion
}
#endif
