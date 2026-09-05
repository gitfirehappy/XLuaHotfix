#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Build Baseline 状态、staging diff、AB Delivery 预览与 Push 面板。
/// </summary>
public sealed class RepositoryStatusPanel : IBuildPipelinePanel, IBuildPipelinePanelVisibility
{
    private enum RepositoryViewMode
    {
        Changes
    }

    private enum RepositoryDiffKind
    {
        Added,
        Modified,
        Removed
    }

    private sealed class RepositoryDiffItem
    {
        public RepositoryDiffKind Kind;
        public string Name;
        public ArtifactDigest OldArtifact;
        public ArtifactDigest NewArtifact;
    }


    private readonly string _backendKey;
    private readonly string _panelName;
    private readonly IRepositoryMaintenancePanel _maintenancePanel;
    private readonly IRepositorySettingsSink _settingsSink;
    private readonly IRepositoryPreviewProvider _previewProvider;
    private readonly IRepositoryArtifactPresenter _artifactPresenter;
    private readonly IRepositoryDataCleaner _dataCleaner;

    private VisualElement _root;
    private VisualElement _tabs;
    private VisualElement _leftList;
    private VisualElement _summaryRow;
    private VisualElement _deliverySummary;
    private VisualElement _artifactList;
    private VisualElement _detailContent;
    private Label _artifactTitle;
    private Label _detailTitle;
    private Label _statusBadge;
    private Label _channelLabel;
    private Label _headLabel;
    private Label _versionLabel;
    private Label _packageLabel;
    private Label _artifactLabel;
    private Label _messageLabel;
    private Label _localServerStatusLabel;
    private DropdownField _targetDropdown;
    private IntegerField _localServerPortField;
    private Toggle _clearPackageIndexToggle;
    private Toggle _deletePackagesToggle;
    private Toggle _clearStartupBaselineToggle;

    private BuildBaselineState _baselineState;
    private BuildPackageRequest _request;
    private string _channelKey;
    private string _selectedArtifactKey;
    private string _baselineError;
    private RepositoryViewMode _viewMode = RepositoryViewMode.Changes;
    private ArtifactDelta _stagingDelta;
    private RepositoryDeliveryPreview _stagingDeliveryPreview;
    private bool _hasStagingDelta;
    private readonly List<RepositoryDiffItem> _currentDiffItems = new();

    private const float LeftPaneWidth = 310f;
    private const float RightPaneWidth = 420f;
    private const float LeftPaneMinWidth = 180f;
    private const float MiddlePaneMinWidth = 220f;
    private const float RightPaneMinWidth = 360f;
    private const float MaxRememberedPaneWidth = 1200f;

    public RepositoryStatusPanel(string backendKey, string panelName)
        : this(backendKey, panelName, null)
    {
    }

    public RepositoryStatusPanel(
        string backendKey,
        string panelName,
        IRepositoryMaintenancePanel maintenancePanel,
        IRepositorySettingsSink settingsSink = null,
        IRepositoryPreviewProvider previewProvider = null,
        IRepositoryArtifactPresenter artifactPresenter = null,
        IRepositoryDataCleaner dataCleaner = null)
    {
        _backendKey = backendKey;
        _panelName = string.IsNullOrEmpty(panelName) ? "Repository" : panelName;
        _maintenancePanel = maintenancePanel;
        _settingsSink = settingsSink;
        _previewProvider = previewProvider;
        _artifactPresenter = artifactPresenter;
        _dataCleaner = dataCleaner;
    }

    public string PanelName => _panelName;

    public void OnEnable(EditorWindow window)
    {
    }

    public VisualElement CreateContent()
    {
        _root = new VisualElement { name = nameof(RepositoryStatusPanel) };
        _root.style.flexGrow = 1f;
        _root.style.flexDirection = FlexDirection.Column;
        Rebuild();
        return _root;
    }

    public void OnDisable()
    {
        _root = null;
        _tabs = null;
        _leftList = null;
        _summaryRow = null;
        _deliverySummary = null;
        _artifactList = null;
        _detailContent = null;
        _artifactTitle = null;
        _detailTitle = null;
        _statusBadge = null;
        _channelLabel = null;
        _headLabel = null;
        _versionLabel = null;
        _packageLabel = null;
        _artifactLabel = null;
        _messageLabel = null;
        _localServerStatusLabel = null;
        _targetDropdown = null;
        _localServerPortField = null;
        _clearPackageIndexToggle = null;
        _deletePackagesToggle = null;
        _clearStartupBaselineToggle = null;
        _baselineState = null;

        _request = null;
        _channelKey = null;
        _selectedArtifactKey = null;

        _currentDiffItems.Clear();
        ClearStagingState();
    }

    public void SetVisible(bool visible)
    {
        if (visible)
            RefreshRepositoryState();
    }

    private void Rebuild()
    {
        if (_root == null)
            return;

        _root.Clear();
        ClearStagingState();

        _request = CreatePreviewRequest();
        _channelKey = BuildBaselineStore.GetChannelKey(
            _request != null ? _request.Version : null,
            _request != null ? _request.BackendKey : _backendKey);

        _root.Add(CreateHeader());
        _root.Add(CreateBody());

        RefreshRepositoryState();
    }

    private VisualElement CreateHeader()
    {
        var header = new VisualElement { name = "RepositoryHeader" };
        header.style.flexShrink = 0f;
        header.style.marginBottom = 8f;
        header.style.paddingLeft = 12f;
        header.style.paddingRight = 12f;
        header.style.paddingTop = 10f;
        header.style.paddingBottom = 10f;
        header.style.backgroundColor = BuildPipelineUI.CardBackgroundColor;
        ApplyBorder(header);

        var top = new VisualElement();
        top.style.flexDirection = FlexDirection.Row;
        top.style.alignItems = Align.Center;

        var titleBox = new VisualElement();
        titleBox.style.flexGrow = 1f;
        titleBox.style.minWidth = 0f;

        var title = new Label("Build Repository");
        title.style.unityFontStyleAndWeight = FontStyle.Bold;
        title.style.fontSize = 15f;
        titleBox.Add(title);

        _channelLabel = BuildPipelineUI.SmallText("Channel: -");
        _channelLabel.style.marginTop = 2f;
        titleBox.Add(_channelLabel);
        top.Add(titleBox);

        top.Add(BuildPipelineUI.ToolbarButton("Refresh", Rebuild, 70f));

        top.Add(BuildPipelineUI.ToolbarButton("Refresh Changes", RunRefreshStaging, 118f));
        if (_previewProvider != null && _previewProvider.SupportsDeliveryPreview)
            top.Add(BuildPipelineUI.ToolbarButton("Preview Delivery", RunPreviewDelivery, 118f));
        top.Add(BuildPipelineUI.ToolbarButton("Push", RunPush, 64f));
        header.Add(top);

        var statusRow = new VisualElement();
        statusRow.style.flexDirection = FlexDirection.Row;
        statusRow.style.alignItems = Align.Center;
        statusRow.style.marginTop = 8f;

        _statusBadge = CreateBadge("No Baseline", new Color(0.42f, 0.42f, 0.42f));
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
        _artifactLabel = AddStat(stats, "Artifacts", "-");
        header.Add(stats);

        return header;
    }

    private VisualElement CreateBody()
    {
        VisualElement leftPane = CreateLeftPane();
        VisualElement middlePane = CreateMiddlePane();
        VisualElement rightPane = CreateRightPane();

        float rightWidth = LoadPaneWidth("Right", RightPaneWidth, RightPaneMinWidth);
        var middleRightSplit = new TwoPaneSplitView(1, rightWidth, TwoPaneSplitViewOrientation.Horizontal)
        {
            name = "RepositoryMiddleRightSplit"
        };
        middleRightSplit.style.flexGrow = 1f;
        middleRightSplit.style.minWidth = 0f;
        middleRightSplit.Add(middlePane);
        middleRightSplit.Add(rightPane);
        RegisterPaneWidthPersistence(rightPane, "Right", RightPaneMinWidth);

        float leftWidth = LoadPaneWidth("Left", LeftPaneWidth, LeftPaneMinWidth);
        var body = new TwoPaneSplitView(0, leftWidth, TwoPaneSplitViewOrientation.Horizontal)
        {
            name = "RepositoryBody"
        };
        body.style.flexGrow = 1f;
        body.style.minHeight = 0f;
        body.style.minWidth = 0f;
        body.Add(leftPane);
        body.Add(middleRightSplit);
        RegisterPaneWidthPersistence(leftPane, "Left", LeftPaneMinWidth);
        return body;
    }

    private VisualElement CreateLeftPane()
    {
        var pane = BuildPipelineUI.Card();
        pane.style.minWidth = LeftPaneMinWidth;
        pane.style.marginBottom = 0f;

        _tabs = new VisualElement();
        _tabs.style.flexDirection = FlexDirection.Row;
        _tabs.style.marginBottom = 8f;
        pane.Add(_tabs);

        var scroll = new ScrollView();
        scroll.style.flexGrow = 1f;
        scroll.style.minHeight = 0f;
        _leftList = new VisualElement();
        scroll.Add(_leftList);
        pane.Add(scroll);
        return pane;
    }

    private VisualElement CreateMiddlePane()
    {
        var pane = BuildPipelineUI.Card();
        pane.style.minWidth = MiddlePaneMinWidth;
        pane.style.marginBottom = 0f;

        _artifactTitle = BuildPipelineUI.Header("Commit Diff");
        pane.Add(_artifactTitle);

        _summaryRow = new VisualElement();
        _summaryRow.style.flexDirection = FlexDirection.Row;
        _summaryRow.style.flexWrap = Wrap.Wrap;
        _summaryRow.style.marginBottom = 6f;
        pane.Add(_summaryRow);

        _deliverySummary = new VisualElement();
        _deliverySummary.style.marginBottom = 6f;
        pane.Add(_deliverySummary);

        var scroll = new ScrollView();
        scroll.style.flexGrow = 1f;
        scroll.style.minHeight = 0f;
        _artifactList = new VisualElement();
        scroll.Add(_artifactList);
        pane.Add(scroll);
        return pane;
    }

    private float LoadPaneWidth(string paneName, float defaultWidth, float minWidth)
    {
        float width = EditorPrefs.GetFloat(GetPaneWidthKey(paneName), defaultWidth);
        return Mathf.Clamp(width, minWidth, MaxRememberedPaneWidth);
    }

    private void RegisterPaneWidthPersistence(
        VisualElement fixedPane,
        string paneName,
        float minWidth)
    {
        fixedPane.RegisterCallback<GeometryChangedEvent>(_ =>
        {
            float width = fixedPane.resolvedStyle.width;
            if (width >= minWidth && width <= MaxRememberedPaneWidth)
                EditorPrefs.SetFloat(GetPaneWidthKey(paneName), width);
        });
    }

    private string GetPaneWidthKey(string paneName)
    {
        return $"FYAsset.Repository.{_backendKey}.{paneName}PaneWidth";
    }

    private VisualElement CreateRightPane()
    {
        var pane = new ScrollView(ScrollViewMode.Vertical);
        pane.style.flexGrow = 1f;
        pane.style.minWidth = RightPaneMinWidth;

        var detail = BuildPipelineUI.Card();
        detail.style.flexGrow = 1f;
        detail.style.minHeight = 220f;
        _detailTitle = BuildPipelineUI.Header("Artifact Detail");
        detail.Add(_detailTitle);
        _detailContent = new VisualElement();
        detail.Add(_detailContent);
        pane.Add(detail);

        pane.Add(CreatePushPanel());
        if (_maintenancePanel != null)
            pane.Add(_maintenancePanel.CreateContent());
        pane.Add(CreateResetPanel());
        return pane;
    }

    private VisualElement CreatePushPanel()
    {
        var panel = BuildPipelineUI.Card();
        panel.style.marginBottom = 0f;
        panel.style.flexShrink = 0f;

        var header = new VisualElement();
        header.style.flexDirection = FlexDirection.Row;
        header.style.alignItems = Align.Center;
        header.style.minWidth = 0f;
        var title = BuildPipelineUI.Header("Push");
        title.style.flexGrow = 1f;
        title.style.minWidth = 0f;
        header.Add(title);
        header.Add(BuildPipelineUI.ToolbarButton("+ Target", AddPushTarget, 86f));
        panel.Add(header);

        var row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        row.style.alignItems = Align.Center;
        row.style.marginBottom = 6f;
        row.style.width = Length.Percent(100f);
        row.style.minWidth = 0f;

        _targetDropdown = new DropdownField("Target", GetPushTargetLabels(), 0);
        _targetDropdown.style.width = 0f;
        _targetDropdown.style.flexGrow = 1f;
        _targetDropdown.style.flexShrink = 1f;
        _targetDropdown.style.flexBasis = 0f;
        _targetDropdown.style.minWidth = 0f;
        _targetDropdown.style.maxWidth = Length.Percent(100f);
        SetCompactFieldLabel(_targetDropdown, 48f);
        row.Add(_targetDropdown);
        Button applyUrl = BuildPipelineUI.ToolbarButton("Apply URL", RunApplyTargetUrl, 78f);
        // 设置落盘能力由组合窗口注入（Shared 面板不感知各后端 settings 类型）。
        applyUrl.style.display = _settingsSink == null ? DisplayStyle.None : DisplayStyle.Flex;
        applyUrl.style.marginLeft = 6f;
        row.Add(applyUrl);
        panel.Add(row);

        panel.Add(BuildPipelineUI.SmallText("Push publishes the Latest baseline package to the selected Target. Extended target types (e.g. CloudflarePages) are created by Compat glue; use the CLI for those."));
        panel.Add(BuildPipelineUI.SmallText("Apply URL explicitly updates only the current backend HotfixUrl; Push never changes it."));
        panel.Add(CreatePushTargetEditor());
        panel.Add(CreateLocalServerControls());
        return panel;
    }

    private VisualElement CreateResetPanel()
    {
        var panel = BuildPipelineUI.Card();
        panel.style.marginBottom = 0f;
        panel.style.flexShrink = 0f;

        var header = new VisualElement();
        header.style.flexDirection = FlexDirection.Row;
        header.style.alignItems = Align.Center;
        var title = BuildPipelineUI.Header("Test Reset");
        title.style.flexGrow = 1f;
        header.Add(title);
        header.Add(BuildPipelineUI.ToolbarButton("Reset Version", RunResetVersionForTest, 104f));
        header.Add(BuildPipelineUI.ToolbarButton("Clear Channel", RunClearChannelForTest, 104f));
        panel.Add(header);

        panel.Add(BuildPipelineUI.SmallText("Clears the baseline record for the current Channel/Backend."));
        _clearPackageIndexToggle = new Toggle("Clear output PackageIndex.json");
        _deletePackagesToggle = new Toggle("Delete local package folders");
        _clearStartupBaselineToggle = new Toggle("Clear startup BuildIndex / StreamingAssets baseline");
        panel.Add(_clearPackageIndexToggle);
        panel.Add(_deletePackagesToggle);
        panel.Add(_clearStartupBaselineToggle);
        return panel;
    }

    private VisualElement CreatePushTargetEditor()
    {
        var box = new VisualElement();
        box.style.flexShrink = 0f;
        box.style.marginTop = 8f;
        box.style.marginBottom = 4f;
        box.style.paddingLeft = 6f;
        box.style.paddingRight = 6f;
        box.style.paddingTop = 6f;
        box.style.paddingBottom = 6f;
        ApplyBorder(box);

        FYAssetSettings settings = FYAssetSettings.Instance;
        if (settings.PushTargets == null || settings.PushTargets.Count == 0)
        {
            box.Add(BuildPipelineUI.SmallText("No Push Target configured."));
            return box;
        }

        for (int i = 0; i < settings.PushTargets.Count; i++)
            box.Add(CreatePushTargetRow(settings.PushTargets[i], i));
        return box;
    }

    private VisualElement CreatePushTargetRow(PushTargetConfig config, int index)
    {
        var container = new VisualElement();
        container.style.flexShrink = 0f;
        container.style.marginTop = 6f;
        container.style.paddingBottom = 6f;

        var row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        row.style.alignItems = Align.Center;
        row.style.width = Length.Percent(100f);
        row.style.minWidth = 0f;
        row.style.minHeight = 20f;

        var idField = new TextField("Id")
        {
            value = config != null ? config.Id : string.Empty,
            isDelayed = true
        };
        idField.style.width = 0f;
        idField.style.minWidth = 120f;
        idField.style.flexGrow = 1f;
        idField.style.flexShrink = 1f;
        idField.style.flexBasis = 0f;
        idField.style.marginRight = 6f;
        SetCompactFieldLabel(idField, 22f);
        idField.RegisterValueChangedCallback(evt =>
        {
            if (config == null)
                return;

            Undo.RecordObject(FYAssetSettings.Instance, "Edit Push Target");
            config.Id = (evt.newValue ?? string.Empty).Trim();
            SaveRepositorySettings();
            Rebuild();
        });
        row.Add(idField);

        var typeField = new EnumField("Type", config != null ? config.Type : PushTargetType.LocalDirectory);
        typeField.RegisterValueChangedCallback(evt =>
        {
            if (config == null || evt.newValue is not PushTargetType value)
                return;

            Undo.RecordObject(FYAssetSettings.Instance, "Edit Push Target Type");
            config.Type = value;
            SaveRepositorySettings();
            Rebuild();
        });

        Button remove = BuildPipelineUI.ToolbarButton("Remove", () => RemovePushTarget(index), 64f);
        remove.style.marginLeft = 6f;
        remove.style.flexShrink = 0f;
        row.Add(remove);
        container.Add(row);
        typeField.style.marginTop = 4f;
        container.Add(typeField);

        SerializedProperty pathProperty = new SerializedObject(FYAssetSettings.Instance)
            .FindProperty(nameof(FYAssetSettings.PushTargets))
            .GetArrayElementAtIndex(index)
            .FindPropertyRelative(nameof(PushTargetConfig.Path));
        VisualElement path = BuildPipelineUI.PathField(pathProperty, "Path", BuildPipelineUI.PathPickerMode.ProjectFolder, 34f);
        path.style.minWidth = 0f;
        path.style.maxWidth = Length.Percent(100f);
        path.style.marginTop = 4f;
        container.Add(path);

        var urlField = new TextField("Public URL")
        {
            value = config != null ? config.PublicBaseUrl : string.Empty,
            isDelayed = true
        };
        urlField.style.marginTop = 4f;
        urlField.RegisterValueChangedCallback(evt =>
        {
            if (config == null)
                return;

            Undo.RecordObject(FYAssetSettings.Instance, "Edit Push Target URL");
            config.PublicBaseUrl = (evt.newValue ?? string.Empty).Trim();
            SaveRepositorySettings();
            Rebuild();
        });
        container.Add(urlField);

        string backendName = _backendKey;
        string resolvedRoot = config != null
            ? "(resolved at publish; empty Path falls back to OutputRoot)" + "/" + backendName
            : backendName;
        container.Add(BuildPipelineUI.SmallText($"Current backend publishes under: {resolvedRoot}"));
        if (config != null && config.Type == PushTargetType.CloudflarePages)
        {
            container.Add(BuildPipelineUI.SmallText(
                "Cloudflare project name uses FYAssetSettings.ProjectName; changing it also changes the runtime persistentData root."));
        }

        return container;
    }

    private VisualElement CreateLocalServerControls()
    {
        var box = new VisualElement();
        box.style.flexShrink = 0f;
        box.style.marginTop = 8f;
        box.style.paddingTop = 6f;
        ApplyBorder(box);

        _localServerPortField = new IntegerField("Local Port")
        {
            value = LocalHotfixServerController.Port,
            isDelayed = true
        };
        _localServerPortField.style.width = Length.Percent(100f);
        _localServerPortField.style.minWidth = 0f;
        _localServerPortField.style.paddingLeft = 6f;
        _localServerPortField.style.paddingRight = 6f;
        _localServerPortField.RegisterValueChangedCallback(evt =>
        {
            LocalHotfixServerController.Port = evt.newValue;
            _localServerPortField.SetValueWithoutNotify(LocalHotfixServerController.Port);
            RefreshLocalServerStatus();
        });
        box.Add(_localServerPortField);

        var actions = new VisualElement();
        actions.style.flexDirection = FlexDirection.Row;
        actions.style.paddingLeft = 6f;
        actions.style.paddingRight = 6f;
        actions.style.marginTop = 4f;
        Button start = BuildPipelineUI.ToolbarButton("Start", RunStartLocalServer);
        Button stop = BuildPipelineUI.ToolbarButton("Stop", RunStopLocalServer);
        Button status = BuildPipelineUI.ToolbarButton("Status", RefreshLocalServerStatus);
        start.style.flexGrow = 1f;
        stop.style.flexGrow = 1f;
        status.style.flexGrow = 1f;
        actions.Add(start);
        actions.Add(stop);
        actions.Add(status);
        box.Add(actions);

        _localServerStatusLabel = BuildPipelineUI.SmallText(string.Empty);
        _localServerStatusLabel.style.marginLeft = 6f;
        _localServerStatusLabel.style.marginRight = 6f;
        _localServerStatusLabel.style.marginBottom = 6f;
        box.Add(_localServerStatusLabel);
        RefreshLocalServerStatus();
        return box;
    }

    private void RefreshRepositoryState()
    {
        if (_root == null)
            return;

        _request = CreatePreviewRequest();
        _channelKey = BuildBaselineStore.GetChannelKey(
            _request != null ? _request.Version : null,
            _request != null ? _request.BackendKey : _backendKey);
        _baselineError = null;
        try
        {
            _baselineState = BuildBaselineStore.Load(_channelKey);
        }
        catch (BuildBaselineException ex)
        {
            _baselineState = new BuildBaselineState();
            _baselineError = ex.Message;
        }

        RefreshHeader();
        RenderNavigation();
        RenderDiffContent();
        _maintenancePanel?.Refresh();
    }

    private void RefreshHeader()
    {
        if (_baselineError != null)
        {
            // baseline.txt/json 损坏：保留错误呈现实，不被下方的默认 badge 逻辑覆盖。
            SetBadge("Baseline Error", new Color(0.65f, 0.20f, 0.16f));
            _messageLabel.text = _baselineError;
            _channelLabel.text = $"Channel: {_channelKey}    Backend: {GetBackendDisplayName(_request.BackendKey)}";
            _headLabel.text = "-";
            _versionLabel.text = _request?.Version != null ? _request.Version.GetReleaseVersionString() : "-";
            _packageLabel.text = "-";
            _artifactLabel.text = "0";
            return;
        }

        BuildBaseline latest = _baselineState?.Latest;
        _channelLabel.text = $"Channel: {_channelKey}    Backend: {GetBackendDisplayName(_request.BackendKey)}";
        _headLabel.text = latest?.Version != null ? SafeText(latest.Version.GetReleaseVersionString()) : "-";
        _versionLabel.text = _request?.Version != null ? _request.Version.GetReleaseVersionString() : "-";
        _packageLabel.text = latest != null ? SafeText(latest.PackageName) : "-";
        _artifactLabel.text = latest?.Artifacts != null ? latest.Artifacts.Count.ToString(CultureInfo.InvariantCulture) : "0";

        if (latest == null)
        {
            SetBadge("No Baseline", new Color(0.42f, 0.42f, 0.42f));
            _messageLabel.text = "完成一次成功交付（构建+发布）后生成 baseline；历史审计走 git log。";
            return;
        }

        string latestFull = _baselineState.LatestFull?.Version != null
            ? _baselineState.LatestFull.Version.GetReleaseVersionString()
            : "-";
        SetBadge("Baseline OK", new Color(0.18f, 0.48f, 0.28f));
        _messageLabel.text = $"Latest={latest.Version?.GetReleaseVersionString()} | LatestFull={latestFull} | {latest.PackageName}";
    }

    private void RenderNavigation()
    {
        if (_tabs == null || _leftList == null)
            return;

        RenderTabs();
        _leftList.Clear();
        RenderChangesNavigation();
    }

    private void RenderTabs()
    {
        _tabs.Clear();
        _tabs.Add(CreateTabButton("Changes", RepositoryViewMode.Changes));
    }

    private Button CreateTabButton(string text, RepositoryViewMode mode)
    {
        bool active = _viewMode == mode;
        var button = new Button(() =>
        {
            _viewMode = mode;
            _selectedArtifactKey = null;
            RenderNavigation();
            RenderDiffContent();
        })
        {
            text = text
        };
        button.style.flexGrow = 1f;
        button.style.height = 24f;
        button.style.backgroundColor = active ? BuildPipelineUI.ActiveColor : new Color(0f, 0f, 0f, 0.08f);
        button.style.color = active ? Color.white : BuildPipelineUI.SecondaryTextColor;
        return button;
    }

    private void RenderChangesNavigation()
    {
        List<RepositoryDiffItem> items = _currentDiffItems;
        if (items == null || items.Count == 0)
        {
            var empty = new Label("No changes");
            empty.style.color = BuildPipelineUI.SecondaryTextColor;
            empty.style.paddingLeft = 6f;
            _leftList.Add(empty);
            return;
        }
        foreach (RepositoryDiffItem item in items)
            _leftList.Add(CreateCompactArtifactRow(item));
    }

    private VisualElement CreateCompactArtifactRow(RepositoryDiffItem item)
    {
        bool selected = IsSelectedArtifact(item);
        var row = CreateClickableRow(selected, () => SelectArtifact(item));
        row.style.paddingTop = 5f;
        row.style.paddingBottom = 5f;

        var line = new VisualElement();
        line.style.flexDirection = FlexDirection.Row;
        line.style.alignItems = Align.Center;
        line.Add(CreateMarkerLabel(item.Kind));

        var name = new Label(GetArtifactPresentation(item.Name).DisplayName);
        name.style.flexGrow = 1f;
        name.style.minWidth = 0f;
        name.style.whiteSpace = WhiteSpace.Normal;
        line.Add(name);
        row.Add(line);
        return row;
    }

    private void RenderDiffContent()
    {
        if (_summaryRow == null || _artifactList == null || _detailContent == null)
            return;

        _summaryRow.Clear();
        _deliverySummary.Clear();
        _artifactList.Clear();
        _detailContent.Clear();
        _currentDiffItems.Clear();

        RenderStagingDiff();
    }

    private void RenderStagingDiff()
    {
        _artifactTitle.text = "Staging Diff";

        if (!_hasStagingDelta)
        {
            AddDiffStats(null);
            _artifactList.Add(CreateEmptyState("Click Refresh Changes to run current preview output vs the Latest baseline."));
            RenderEmptyDetail("Staging diff is not loaded.");
            return;
        }

        _currentDiffItems.AddRange(BuildDiffItems(_stagingDelta, _baselineState?.Latest?.Artifacts, null));
        AddDiffStats(_stagingDelta);
        RenderDeliverySummary();
        RenderArtifactList("No staging changes.");
    }

    private void RenderArtifactList(string emptyText)
    {
        if (_currentDiffItems.Count == 0)
        {
            _artifactList.Add(CreateEmptyState(emptyText));
            RenderEmptyDetail("No artifact selected.");
            return;
        }

        EnsureArtifactSelection();
        RepositoryDiffItem selected = FindCurrentSelectedArtifact();
        for (int i = 0; i < _currentDiffItems.Count; i++)
            _artifactList.Add(CreateArtifactRow(_currentDiffItems[i]));
        RenderArtifactDetail(selected);
    }

    private VisualElement CreateArtifactRow(RepositoryDiffItem item)
    {
        bool selected = IsSelectedArtifact(item);
        var row = CreateClickableRow(selected, () => SelectArtifact(item));
        row.style.flexDirection = FlexDirection.Row;
        row.style.alignItems = Align.Center;
        row.style.paddingTop = 6f;
        row.style.paddingBottom = 6f;

        row.Add(CreateMarkerLabel(item.Kind));

        var text = new VisualElement();
        text.style.flexGrow = 1f;
        text.style.minWidth = 0f;

        var name = new Label(GetArtifactPresentation(item.Name).DisplayName);
        name.style.whiteSpace = WhiteSpace.Normal;
        text.Add(name);

        ArtifactDigest meta = item.NewArtifact ?? item.OldArtifact;
        text.Add(BuildPipelineUI.SmallText(meta != null ? $"{FileHelper.FormatBytes(meta.Size)}  |  {ShortHash(meta.Hash)}" : "metadata unavailable"));
        row.Add(text);
        return row;
    }

    private void RenderArtifactDetail(RepositoryDiffItem item)
    {
        _detailContent.Clear();
        if (item == null)
        {
            RenderEmptyDetail("No artifact selected.");
            return;
        }

        _detailTitle.text = "Artifact Detail";
        _detailContent.Add(CreateDetailLine("Status", GetKindText(item.Kind)));
        AddArtifactIdentityDetails(_detailContent, item.Name);

        if (item.Kind == RepositoryDiffKind.Modified)
        {
            _detailContent.Add(CreateSectionLabel("Old"));
            AddArtifactMetadata(_detailContent, item.OldArtifact);
            _detailContent.Add(CreateSectionLabel("New"));
            AddArtifactMetadata(_detailContent, item.NewArtifact);
            if (item.OldArtifact == null)
                _detailContent.Add(CreateWarning("Old metadata is unavailable because the parent artifact could not be found."));
            return;
        }

        if (item.Kind == RepositoryDiffKind.Added)
        {
            _detailContent.Add(CreateSectionLabel("New"));
            AddArtifactMetadata(_detailContent, item.NewArtifact);
            return;
        }

        _detailContent.Add(CreateSectionLabel("Old"));
        AddArtifactMetadata(_detailContent, item.OldArtifact);
        if (item.OldArtifact == null)
            _detailContent.Add(CreateWarning("Removed artifact metadata is unavailable; only the name was persisted in the delta."));
    }

    private void RenderEmptyDetail(string message)
    {
        _detailTitle.text = "Artifact Detail";
        _detailContent.Clear();
        _detailContent.Add(CreateEmptyState(message));
    }

    private void RunRefreshStaging()
    {
        try
        {
            _viewMode = RepositoryViewMode.Changes;
            _selectedArtifactKey = null;
            _messageLabel.text = "Running Changes preview...";

            // preview 组合由后端注入（机制在 Shared，数据源在后端）；未注入显式失败，不伪装成无变更。
            if (_previewProvider == null)
            {
                SetBadge("Changes Unavailable", new Color(0.65f, 0.20f, 0.16f));
                _messageLabel.text = "No preview provider injected; this panel was composed without a backend data source.";
                RenderNavigation();
                RenderDiffContent();
                return;
            }
            _stagingDeliveryPreview = null;
            _stagingDelta = _previewProvider.RunChangesPreview(_request) ?? new ArtifactDelta();

            _hasStagingDelta = true;
            SetBadge("Changes Ready", new Color(0.18f, 0.48f, 0.28f));
            _messageLabel.text = IsDeltaEmpty(_stagingDelta) ? "Changes preview completed with no baseline changes." : "Changes preview completed.";
            RenderNavigation();
            RenderDiffContent();
        }
        catch (Exception ex)
        {
            Debug.LogError($"[RepositoryStatusPanel] 刷新 Changes 失败：{ex}");
            ClearStagingState();
            _viewMode = RepositoryViewMode.Changes;
            SetBadge("Changes Failed", new Color(0.65f, 0.20f, 0.16f));
            _messageLabel.text = ex.Message;
            RenderNavigation();
            RenderDiffContent();
        }
    }

    private void RunPreviewDelivery()
    {
        if (_previewProvider == null)
            return;

        try
        {
            _viewMode = RepositoryViewMode.Changes;
            _selectedArtifactKey = null;
            _messageLabel.text = "Running delivery preview...";

            _stagingDeliveryPreview = _previewProvider.RunDeliveryPreview(_request);
            _stagingDelta = _stagingDeliveryPreview != null ? _stagingDeliveryPreview.HeadDelta : new ArtifactDelta();
            _hasStagingDelta = true;

            SetBadge("Delivery Ready", new Color(0.18f, 0.48f, 0.28f));
            _messageLabel.text = "Delivery preview completed.";
            RenderNavigation();
            RenderDiffContent();
        }
        catch (Exception ex)
        {
            Debug.LogError($"[RepositoryStatusPanel] Delivery 预览失败：{ex}");
            _viewMode = RepositoryViewMode.Changes;
            _stagingDeliveryPreview = new RepositoryDeliveryPreview
            {
                HeadDelta = _stagingDelta ?? new ArtifactDelta(),
                DeliveryAvailable = false,
                DeliveryMessage = ex.Message
            };
            SetBadge("Delivery Failed", new Color(0.65f, 0.20f, 0.16f));
            _messageLabel.text = ex.Message;
            RenderNavigation();
            RenderDiffContent();
        }
    }

    private void RunPush()
    {
        try
        {


            IPushTarget target = CreatePushTarget();
            PushReceipt receipt = BuildPublisher.PushLatest(_channelKey, target);
            RefreshRepositoryState();

            if (receipt != null && receipt.Success)
            {
                SetBadge("Push OK", new Color(0.18f, 0.48f, 0.28f));
                _messageLabel.text = $"Push succeeded: {receipt.TargetId} -> {receipt.TargetLocation}";
            }
            else
            {
                SetBadge("Push Failed", new Color(0.65f, 0.20f, 0.16f));
                _messageLabel.text = receipt != null ? receipt.FailureReason : "Push returned null receipt.";
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[RepositoryStatusPanel] Push 失败：{ex}");
            SetBadge("Push Failed", new Color(0.65f, 0.20f, 0.16f));
            _messageLabel.text = ex.Message;
        }
    }

    private void RunApplyTargetUrl()
    {
        try
        {
            if (_settingsSink == null)
                throw new InvalidOperationException("No settings sink injected; cannot persist HotfixUrl.");
            PushTargetConfig config = GetSelectedTargetConfig();
            string url = PushTargetUtility.GetBackendHotfixUrl(config, _backendKey);
            _settingsSink.ApplyHotfixUrl(url);

            SetBadge("URL Applied", new Color(0.18f, 0.48f, 0.28f));
            _messageLabel.text = $"{GetBackendDisplayName(_backendKey)} HotfixUrl -> {url}";
        }
        catch (Exception ex)
        {
            SetBadge("URL Failed", new Color(0.65f, 0.20f, 0.16f));
            _messageLabel.text = ex.Message;
        }
    }

    private void RunStartLocalServer()
    {
        LocalHotfixServerStatus status = LocalHotfixServerController.Start();
        RefreshLocalServerStatus(status);
    }

    private void RunStopLocalServer()
    {
        LocalHotfixServerStatus status = LocalHotfixServerController.Stop();
        RefreshLocalServerStatus(status);
    }

    private void RefreshLocalServerStatus()
    {
        RefreshLocalServerStatus(LocalHotfixServerController.GetStatus());
    }

    private void RefreshLocalServerStatus(LocalHotfixServerStatus status)
    {
        if (_localServerStatusLabel == null)
            return;

        string state = status.IsRunning ? "Running" : "Stopped";
        _localServerStatusLabel.text = $"{state} | {status.Message}";
    }

    private void RunClearChannelForTest()
    {
        bool clearPackageIndex = _clearPackageIndexToggle?.value == true;
        bool deletePackages = _deletePackagesToggle?.value == true;
        bool clearStartupBaseline = _clearStartupBaselineToggle?.value == true;

        string message = $"Clear Repository channel for test?\n\nChannel: {_channelKey}\nBackend: {GetBackendDisplayName(_backendKey)}\n\nThis deletes the baseline.json for this channel.";
        if (clearPackageIndex)
            message += "\n- Clear output PackageIndex.json";
        if (deletePackages)
            message += "\n- Delete local package folders";
        if (clearStartupBaseline)
            message += "\n- Clear startup BuildIndex / StreamingAssets baseline";

        if (!EditorUtility.DisplayDialog("Clear Baseline Channel", message, "Clear", "Cancel"))
            return;

        try
        {
            BuildBaselineStore.ClearForTest(_channelKey);
            if (clearPackageIndex)
                WriteEmptyPackageIndex();
            if (deletePackages)
                DeleteLocalPackageFolders();
            if (clearStartupBaseline)
                ClearStartupBaseline();

            AssetDatabase.Refresh();
            RefreshRepositoryState();
            SetBadge("Channel Cleared", new Color(0.18f, 0.48f, 0.28f));
            _messageLabel.text = $"Baseline channel cleared: {_channelKey}";
        }
        catch (Exception ex)
        {
            Debug.LogError($"[RepositoryStatusPanel] 清理 Channel 失败：{ex}");
            SetBadge("Clear Failed", new Color(0.65f, 0.20f, 0.16f));
            _messageLabel.text = ex.Message;
        }
    }

    private void RunResetVersionForTest()
    {
        VersionRecord versionDB = AssetDatabase.LoadAssetAtPath<VersionRecord>(FYAssetSettings.Instance.VersionRecordPath);
        if (versionDB == null)
        {
            EditorUtility.DisplayDialog("Reset Version", "VersionRecord not found:\n\n" + FYAssetSettings.Instance.VersionRecordPath, "OK");
            return;
        }

        if (!EditorUtility.DisplayDialog(
                "Reset Version",
                "Reset VersionRecord to 1.0.0 for testing?\n\nThis clears Channel, LastBuildTime, and DailyBuildCount.",
                "Reset",
                "Cancel"))
            return;

        Undo.RecordObject(versionDB, "Reset VersionRecord");
        versionDB.CurrentVersion = new VersionNumber { Major = 1, Minor = 0, Patch = 0, Build = 0, Channel = string.Empty };
        versionDB.LastBuildTime = string.Empty;
        versionDB.DailyBuildCount = 0;
        EditorUtility.SetDirty(versionDB);
        AssetDatabase.SaveAssets();
        Rebuild();
    }

    private static void WriteEmptyPackageIndex()
    {
        var empty = new PackageIndex
        {
            LatestPackage = string.Empty,
            LatestVersion = null,
            BackendMode = string.Empty
        };
        FileHelper.WriteAllTextAtomic(BuildPathManager.PackageIndexPath, SerializationUtility.SerializeToJson(empty, true));
    }

    private static void DeleteLocalPackageFolders()
    {
        string root = BuildPathManager.PackagesDir;
        string[] dirs = FileHelper.GetDirectories(root, "Build_*");
        for (int i = 0; i < dirs.Length; i++)
        {
            if (!IsSafeChildPath(root, dirs[i]))
            {
                Debug.LogError($"[RepositoryStatusPanel] 拒绝删除 PackagesDir 外的路径：{dirs[i]}");
                continue;
            }

            FileHelper.TryDeleteDirectory(dirs[i], true);
        }
    }

    private void ClearStartupBaseline()
    {
        // 共享启动件在 Shared 清理；后端专属 manifest/catalog 由注入 cleaner 负责。
        FileHelper.TryDelete(ResolveProjectPath(FYAssetSettings.Instance.BuildIndexJsonPath));
        FileHelper.TryDelete(FYAssetPathUtility.JoinFilePath(Application.streamingAssetsPath, FYAssetSettings.BUILD_INDEX_FILENAME));
        FileHelper.TryDeleteDirectory(FYAssetPathUtility.JoinFilePath(Application.streamingAssetsPath, FYAssetSettings.BUNDLES_DIRECTORY_NAME), true);
        _dataCleaner?.ClearStartupData();
    }

    private static string ResolveProjectPath(string path)
    {
        return Path.IsPathRooted(path)
            ? FYAssetPathUtility.NormalizePath(path)
            : FYAssetPathUtility.ResolveFilePath(BuildPathManager.ProjectRoot, path);
    }

    private static bool IsSafeChildPath(string rootPath, string childPath)
    {
        if (string.IsNullOrEmpty(rootPath) || string.IsNullOrEmpty(childPath))
            return false;

        string root = Path.GetFullPath(rootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        string child = Path.GetFullPath(childPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        return child.StartsWith(root, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(child, root, StringComparison.OrdinalIgnoreCase);
    }

    private void AddPushTarget()
    {
        FYAssetSettings settings = FYAssetSettings.Instance;
        Undo.RecordObject(settings, "Add Push Target");
        settings.PushTargets ??= new List<PushTargetConfig>();
        int index = settings.PushTargets.Count + 1;
        settings.PushTargets.Add(new PushTargetConfig
        {
            Id = "target" + index,
            Type = PushTargetType.LocalDirectory,
            Path = string.Empty,
            PublicBaseUrl = string.Empty
        });
        SaveRepositorySettings();
        Rebuild();
    }

    private void RemovePushTarget(int index)
    {
        FYAssetSettings settings = FYAssetSettings.Instance;
        if (settings.PushTargets == null || index < 0 || index >= settings.PushTargets.Count)
            return;

        Undo.RecordObject(settings, "Remove Push Target");
        settings.PushTargets.RemoveAt(index);
        SaveRepositorySettings();
        Rebuild();
    }

    private static void SaveRepositorySettings()
    {
        EditorUtility.SetDirty(FYAssetSettings.Instance);
        AssetDatabase.SaveAssets();
    }

    private BuildPackageRequest CreatePreviewRequest()
    {
        VersionRecord versionDB = AssetDatabase.LoadAssetAtPath<VersionRecord>(FYAssetSettings.Instance.VersionRecordPath);
        VersionNumber version = versionDB != null && versionDB.CurrentVersion != null
            ? versionDB.CurrentVersion
            : new VersionNumber { Major = 0, Minor = 0, Patch = 0 };
        return BuildPackageRequest.Create(version, BuildType.Full, _backendKey);
    }

    private IPushTarget CreatePushTarget()
    {
        return PushTargetUtility.Create(GetSelectedTargetConfig());
    }

    private PushTargetConfig GetSelectedTargetConfig()
    {
        FYAssetSettings settings = FYAssetSettings.Instance;
        string targetId = _targetDropdown != null && !string.IsNullOrEmpty(_targetDropdown.value)
            ? _targetDropdown.value
            : (settings.PushTargets != null && settings.PushTargets.Count > 0 ? settings.PushTargets[0].Id : string.Empty);
        PushTargetConfig config = PushTargetUtility.FindConfig(targetId);
        if (config == null)
            throw new InvalidOperationException("未配置 Push Target。");
        return config;
    }

    private static List<string> GetPushTargetLabels()
    {
        var labels = new List<string>();
        FYAssetSettings settings = FYAssetSettings.Instance;
        for (int i = 0; settings.PushTargets != null && i < settings.PushTargets.Count; i++)
        {
            PushTargetConfig config = settings.PushTargets[i];
            if (config != null && !string.IsNullOrEmpty(config.Id))
                labels.Add(config.Id);
        }
        if (labels.Count == 0)
            labels.Add("(none)");
        return labels;
    }

    private void EnsureArtifactSelection()
    {
        if (_currentDiffItems.Count == 0)
        {
            _selectedArtifactKey = null;
            return;
        }

        if (FindCurrentSelectedArtifact() != null)
            return;

        _selectedArtifactKey = MakeArtifactKey(_currentDiffItems[0]);
    }

    private void SelectArtifact(RepositoryDiffItem item)
    {
        _selectedArtifactKey = MakeArtifactKey(item);
        RenderNavigation();
        RenderDiffContent();
    }

    private RepositoryDiffItem FindCurrentSelectedArtifact()
    {
        if (string.IsNullOrEmpty(_selectedArtifactKey))
            return null;

        for (int i = 0; i < _currentDiffItems.Count; i++)
        {
            RepositoryDiffItem item = _currentDiffItems[i];
            if (string.Equals(MakeArtifactKey(item), _selectedArtifactKey, StringComparison.Ordinal))
                return item;
        }
        return null;
    }

    private bool IsSelectedArtifact(RepositoryDiffItem item)
    {
        return item != null && string.Equals(MakeArtifactKey(item), _selectedArtifactKey, StringComparison.Ordinal);
    }

    private List<RepositoryDiffItem> BuildDiffItems(
        ArtifactDelta delta,
        IReadOnlyList<ArtifactDigest> oldArtifacts,
        IReadOnlyList<ArtifactDigest> newArtifacts)
    {
        var items = new List<RepositoryDiffItem>();
        if (delta == null)
            return items;

        Dictionary<string, ArtifactDigest> oldByName = BuildArtifactMap(oldArtifacts);
        Dictionary<string, ArtifactDigest> newByName = BuildArtifactMap(newArtifacts);

        AddArtifactItems(items, RepositoryDiffKind.Added, delta.Added, oldByName, newByName);
        AddArtifactItems(items, RepositoryDiffKind.Modified, delta.Modified, oldByName, newByName);
        if (delta.Removed != null)
        {
            for (int i = 0; i < delta.Removed.Count; i++)
            {
                string name = delta.Removed[i];
                oldByName.TryGetValue(name ?? string.Empty, out ArtifactDigest oldArtifact);
                items.Add(new RepositoryDiffItem
                {
                    Kind = RepositoryDiffKind.Removed,
                    Name = name,
                    OldArtifact = oldArtifact,
                    NewArtifact = null
                });
            }
        }

        return items;
    }

    private static void AddArtifactItems(
        List<RepositoryDiffItem> items,
        RepositoryDiffKind kind,
        List<ArtifactDigest> artifacts,
        Dictionary<string, ArtifactDigest> oldByName,
        Dictionary<string, ArtifactDigest> newByName)
    {
        if (artifacts == null)
            return;

        for (int i = 0; i < artifacts.Count; i++)
        {
            ArtifactDigest artifact = artifacts[i];
            string name = artifact != null ? artifact.Name : string.Empty;
            oldByName.TryGetValue(name ?? string.Empty, out ArtifactDigest oldArtifact);
            newByName.TryGetValue(name ?? string.Empty, out ArtifactDigest newArtifact);
            items.Add(new RepositoryDiffItem
            {
                Kind = kind,
                Name = name,
                OldArtifact = oldArtifact,
                NewArtifact = artifact ?? newArtifact
            });
        }
    }

    private static Dictionary<string, ArtifactDigest> BuildArtifactMap(IReadOnlyList<ArtifactDigest> artifacts)
    {
        var map = new Dictionary<string, ArtifactDigest>(StringComparer.Ordinal);
        if (artifacts == null)
            return map;

        for (int i = 0; i < artifacts.Count; i++)
        {
            ArtifactDigest artifact = artifacts[i];
            if (artifact == null || string.IsNullOrEmpty(artifact.Name))
                continue;
            if (!map.ContainsKey(artifact.Name))
                map.Add(artifact.Name, artifact);
        }
        return map;
    }

    private RepositoryArtifactPresentation GetArtifactPresentation(string identity)
    {
        // AA 的 GUID→Address/路径解析由后端注入的呈现器负责；未注入展示中性名字。
        return _artifactPresenter != null && !string.IsNullOrEmpty(identity)
            ? _artifactPresenter.Present(identity)
            : new RepositoryArtifactPresentation { DisplayName = SafeText(identity) };
    }

    private void AddArtifactIdentityDetails(VisualElement parent, string identity)
    {
        RepositoryArtifactPresentation presentation = GetArtifactPresentation(identity);
        if (presentation.Details == null || presentation.Details.Count == 0)
        {
            parent.Add(CreateDetailLine("Name", presentation.DisplayName));
            return;
        }

        foreach (KeyValuePair<string, string> detail in presentation.Details)
            parent.Add(CreateDetailLine(detail.Key, detail.Value));
        if (!string.IsNullOrEmpty(presentation.UnresolvedWarning))
            parent.Add(CreateWarning(presentation.UnresolvedWarning));
    }

    private void AddDiffStats(ArtifactDelta delta)
    {
        _summaryRow.Clear();
        AddDiffStat("Added", CountAdded(delta), new Color(0.20f, 0.55f, 0.30f));
        AddDiffStat("Modified", CountModified(delta), new Color(0.70f, 0.48f, 0.16f));
        AddDiffStat("Removed", CountRemoved(delta), new Color(0.65f, 0.20f, 0.16f));
    }

    private void RenderDeliverySummary()
    {
        _deliverySummary.Clear();
        if (_stagingDeliveryPreview == null)
            return;

        int count = _stagingDeliveryPreview.DeliveryBundleCount;
        if (!_stagingDeliveryPreview.DeliveryAvailable)
        {
            _deliverySummary.Add(CreateBadge("Hotfix Delivery Unavailable", new Color(0.42f, 0.42f, 0.42f)));
            _deliverySummary.Add(BuildPipelineUI.SmallText(string.IsNullOrEmpty(_stagingDeliveryPreview.DeliveryMessage)
                ? "Use Preview Delivery to calculate current output vs Full baseline."
                : _stagingDeliveryPreview.DeliveryMessage));
            return;
        }

        _deliverySummary.Add(CreateBadge($"Hotfix Delivery {count}", new Color(0.17f, 0.36f, 0.53f)));
        _deliverySummary.Add(BuildPipelineUI.SmallText($"Full baseline -> current output, {FileHelper.FormatBytes(_stagingDeliveryPreview.DeliverySizeBytes)}"));
    }

    private void AddDiffStat(string title, int count, Color color)
    {
        Label stat = CreateBadge($"{title} {count}", color);
        stat.style.marginRight = 6f;
        stat.style.marginBottom = 4f;
        _summaryRow.Add(stat);
    }

    private VisualElement CreateClickableRow(bool selected, Action clicked)
    {
        var row = new VisualElement();
        row.style.paddingLeft = 8f;
        row.style.paddingRight = 8f;
        row.style.paddingTop = 7f;
        row.style.paddingBottom = 7f;
        row.style.marginBottom = 5f;
        row.style.backgroundColor = selected ? new Color(0.17f, 0.36f, 0.53f, 0.55f) : new Color(0f, 0f, 0f, 0.08f);
        ApplyBorder(row);
        row.RegisterCallback<PointerDownEvent>(evt =>
        {
            if (evt.button != 0)
                return;

            clicked?.Invoke();
            evt.StopPropagation();
        });
        return row;
    }

    private Label CreateMarkerLabel(RepositoryDiffKind kind)
    {
        var mark = new Label(GetKindMarker(kind));
        mark.style.width = 24f;
        mark.style.minWidth = 24f;
        mark.style.unityTextAlign = TextAnchor.MiddleCenter;
        mark.style.unityFontStyleAndWeight = FontStyle.Bold;
        mark.style.color = GetKindColor(kind);
        return mark;
    }

    private VisualElement CreateDetailLine(string label, string value)
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

    private Label CreateSectionLabel(string text)
    {
        var label = new Label(text);
        label.style.marginTop = 8f;
        label.style.marginBottom = 4f;
        label.style.unityFontStyleAndWeight = FontStyle.Bold;
        return label;
    }

    private Label CreateWarning(string text)
    {
        Label label = BuildPipelineUI.SmallText(text);
        label.style.marginTop = 6f;
        label.style.color = new Color(0.90f, 0.62f, 0.20f);
        return label;
    }

    private void AddArtifactMetadata(VisualElement parent, ArtifactDigest artifact)
    {
        if (artifact == null)
        {
            parent.Add(CreateEmptyState("Metadata unavailable."));
            return;
        }

        parent.Add(CreateDetailLine("Hash", SafeText(artifact.Hash)));
        parent.Add(CreateDetailLine("CRC", FormatCrc(artifact.CRC)));
        parent.Add(CreateDetailLine("Size", FileHelper.FormatBytes(artifact.Size)));
    }

    private static Label CreateBadge(string text, Color color)
    {
        var badge = new Label(text);
        badge.style.paddingLeft = 8f;
        badge.style.paddingRight = 8f;
        badge.style.paddingTop = 3f;
        badge.style.paddingBottom = 3f;
        badge.style.unityFontStyleAndWeight = FontStyle.Bold;
        badge.style.color = Color.white;
        badge.style.backgroundColor = color;
        badge.style.borderTopLeftRadius = 4f;
        badge.style.borderTopRightRadius = 4f;
        badge.style.borderBottomLeftRadius = 4f;
        badge.style.borderBottomRightRadius = 4f;
        return badge;
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

    private Label AddStat(VisualElement parent, string title, string value)
    {
        var box = new VisualElement();
        box.style.flexGrow = 1f;
        box.style.minWidth = 0f;
        box.style.marginRight = 6f;
        box.style.paddingLeft = 8f;
        box.style.paddingRight = 8f;
        box.style.paddingTop = 6f;
        box.style.paddingBottom = 6f;
        box.style.backgroundColor = new Color(0f, 0f, 0f, 0.10f);
        ApplyBorder(box);

        Label titleLabel = BuildPipelineUI.SmallText(title);
        box.Add(titleLabel);
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

    private void ClearStagingState()
    {
        _stagingDelta = null;
        _stagingDeliveryPreview = null;
        _hasStagingDelta = false;
    }

    private static void ApplyBorder(VisualElement element)
    {
        element.style.borderTopWidth = 1f;
        element.style.borderRightWidth = 1f;
        element.style.borderBottomWidth = 1f;
        element.style.borderLeftWidth = 1f;
        element.style.borderTopColor = BuildPipelineUI.BorderColor;
        element.style.borderRightColor = BuildPipelineUI.BorderColor;
        element.style.borderBottomColor = BuildPipelineUI.BorderColor;
        element.style.borderLeftColor = BuildPipelineUI.BorderColor;
        element.style.borderTopLeftRadius = 4f;
        element.style.borderTopRightRadius = 4f;
        element.style.borderBottomLeftRadius = 4f;
        element.style.borderBottomRightRadius = 4f;
    }

    private static void SetCompactFieldLabel(BaseField<string> field, float labelWidth)
    {
        if (field == null)
            return;

        Label label = field.Q<Label>();
        if (label == null)
            return;

        label.style.minWidth = labelWidth;
        label.style.width = labelWidth;
        label.style.marginRight = 4f;
        label.style.flexShrink = 0f;

        var input = field.Q(className: "unity-base-field__input");
        if (input == null)
            return;

        input.style.minWidth = 0f;
        input.style.flexShrink = 1f;
    }

    private static int CountAdded(ArtifactDelta delta)
    {
        return delta != null && delta.Added != null ? delta.Added.Count : 0;
    }

    private static int CountModified(ArtifactDelta delta)
    {
        return delta != null && delta.Modified != null ? delta.Modified.Count : 0;
    }

    private static int CountRemoved(ArtifactDelta delta)
    {
        return delta != null && delta.Removed != null ? delta.Removed.Count : 0;
    }

    private static bool IsDeltaEmpty(ArtifactDelta delta)
    {
        return CountAdded(delta) == 0 && CountModified(delta) == 0 && CountRemoved(delta) == 0;
    }

    private static string MakeArtifactKey(RepositoryDiffItem item)
    {
        return item == null ? string.Empty : $"{item.Kind}:{item.Name}";
    }

    private static string GetKindMarker(RepositoryDiffKind kind)
    {
        return kind switch
        {
            RepositoryDiffKind.Added => "+",
            RepositoryDiffKind.Modified => "~",
            RepositoryDiffKind.Removed => "-",
            _ => "?"
        };
    }

    private static string GetKindText(RepositoryDiffKind kind)
    {
        return kind switch
        {
            RepositoryDiffKind.Added => "Added",
            RepositoryDiffKind.Modified => "Modified",
            RepositoryDiffKind.Removed => "Removed",
            _ => "Unknown"
        };
    }

    private static Color GetKindColor(RepositoryDiffKind kind)
    {
        return kind switch
        {
            RepositoryDiffKind.Added => new Color(0.20f, 0.55f, 0.30f),
            RepositoryDiffKind.Modified => new Color(0.70f, 0.48f, 0.16f),
            RepositoryDiffKind.Removed => new Color(0.65f, 0.20f, 0.16f),
            _ => Color.gray
        };
    }


    private static string FormatCrc(uint crc)
    {
        return "0x" + crc.ToString("X8", CultureInfo.InvariantCulture);
    }

    private static string ShortHash(string hash)
    {
        if (string.IsNullOrEmpty(hash))
            return "-";
        return hash.Length <= 8 ? hash : hash.Substring(0, 8);
    }

    private static string SafeText(string value)
    {
        return string.IsNullOrEmpty(value) ? "-" : value;
    }

    private static string GetBackendDisplayName(string backendKey)
    {
        return backendKey;
    }
}

/// <summary>
/// 中性的 artifact 展示面：Shared 面板只消费这个形状。
/// </summary>
public sealed class RepositoryArtifactPresentation
{
    public string DisplayName;
    public System.Collections.Generic.List<KeyValuePair<string, string>> Details;
    public string UnresolvedWarning;
}

/// <summary>
/// artifact identity → 展示面 的注入契约（如 AA 的 GUID → Addressables Address/AssetPath 解析）。
/// </summary>
public interface IRepositoryArtifactPresenter
{
    RepositoryArtifactPresentation Present(string artifactIdentity);
}

/// <summary>
/// 后端启动数据清理的注入契约：Shared 面板只认识共享启动件，后端 manifest/catalog 各自清理。
/// </summary>
public interface IRepositoryDataCleaner
{
    void ClearStartupData();
}

/// <summary>
/// 由组合窗口注入的 settings 落盘契约：Shared 面板不感知 AA/AB settings 类型。
/// </summary>
public interface IRepositorySettingsSink
{
    /// <summary>把精确 Hotfix URL 持久化到本后端的 settings 资产。</summary>
    void ApplyHotfixUrl(string url);
}

/// <summary>
/// 中性的 delivery preview 数据形（面板只消费这一面）。
/// </summary>
public sealed class RepositoryDeliveryPreview
{
    public ArtifactDelta HeadDelta;
    public int DeliveryBundleCount;
    public long DeliverySizeBytes;
    public bool DeliveryAvailable;
    public string DeliveryMessage;
}

/// <summary>
/// 后端 preview 数据源的注入契约：Shared 面板不感知 AA/AB preview 静态类。
/// </summary>
public interface IRepositoryPreviewProvider
{
    /// <summary>是否提供 delivery preview（决定是否显示对应控件）。</summary>
    bool SupportsDeliveryPreview { get; }

    /// <summary>当前输出 vs 基线的变化量；失败抛异常。</summary>
    ArtifactDelta RunChangesPreview(BuildPackageRequest request);

    /// <summary>Full 基线 to 当前输出的投递预估；不支持时返回 null。</summary>
    RepositoryDeliveryPreview RunDeliveryPreview(BuildPackageRequest request);
}

/// <summary>
/// 由后端维护、显示在共享 Repository 面板中的内容。
/// </summary>
public interface IRepositoryMaintenancePanel
{
    VisualElement CreateContent();
    void Refresh();
}
#endif
