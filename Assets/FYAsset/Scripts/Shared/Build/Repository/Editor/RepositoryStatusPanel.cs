#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Build Repository status, git-style commit diff, staging diff, and push panel.
/// </summary>
public sealed class RepositoryStatusPanel : IBuildPipelinePanel, IBuildPipelinePanelVisibility
{
    private enum RepositoryViewMode
    {
        Changes,
        History
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

    private sealed class ArtifactPresentation
    {
        public string DisplayName;
        public string Address;
        public string AssetPath;
        public string Guid;
        public bool IsResolved;
    }

    private readonly BackendMode _backendMode;
    private readonly string _panelName;
    private readonly IRepositoryMaintenancePanel _maintenancePanel;

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
    private DropdownField _targetDropdown;
    private Toggle _clearPackageIndexToggle;
    private Toggle _deletePackagesToggle;
    private Toggle _clearStartupBaselineToggle;

    private RepositoryStatus _status;
    private RepositoryHealthReport _health;
    private RepositoryCommit _headCommit;
    private RepositoryCommit _selectedCommit;
    private BuildPackageRequest _request;
    private string _channelKey;
    private string _selectedArtifactKey;
    private RepositoryViewMode _viewMode = RepositoryViewMode.History;
    private ArtifactDelta _stagingDelta;
    private ABRepositoryPreviewResult _stagingABPreview;
    private bool _hasStagingDelta;
    private readonly List<RepositoryCommit> _commits = new();
    private readonly List<RepositoryDiffItem> _currentDiffItems = new();

    private const float LeftPaneWidth = 310f;
    private const float MiddlePaneWidth = 360f;
    private const float LeftPaneMinWidth = 180f;
    private const float MiddlePaneMinWidth = 220f;
    private const float RightPaneMinWidth = 260f;
    private const float MaxRememberedPaneWidth = 1200f;

    public RepositoryStatusPanel()
        : this(FYAssetSettings.Instance.UseABBackend ? BackendMode.ABManifest : BackendMode.AA, "Repository")
    {
    }

    public RepositoryStatusPanel(BackendMode backendMode, string panelName)
        : this(backendMode, panelName, null)
    {
    }

    public RepositoryStatusPanel(
        BackendMode backendMode,
        string panelName,
        IRepositoryMaintenancePanel maintenancePanel)
    {
        _backendMode = backendMode;
        _panelName = string.IsNullOrEmpty(panelName) ? "Repository" : panelName;
        _maintenancePanel = maintenancePanel;
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
        _targetDropdown = null;
        _clearPackageIndexToggle = null;
        _deletePackagesToggle = null;
        _clearStartupBaselineToggle = null;
        _status = null;
        _health = null;
        _headCommit = null;
        _selectedCommit = null;
        _request = null;
        _channelKey = null;
        _selectedArtifactKey = null;
        _commits.Clear();
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
        _channelKey = BuildRepositoryFacade.GetChannelKey(_request);

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
        top.Add(BuildPipelineUI.ToolbarButton("Health", RunHealth, 70f));
        top.Add(BuildPipelineUI.ToolbarButton("Refresh Changes", RunRefreshStaging, 118f));
        if (_backendMode == BackendMode.ABManifest)
            top.Add(BuildPipelineUI.ToolbarButton("Preview Delivery", RunPreviewDelivery, 118f));
        top.Add(BuildPipelineUI.ToolbarButton("Push", RunPush, 64f));
        header.Add(top);

        var statusRow = new VisualElement();
        statusRow.style.flexDirection = FlexDirection.Row;
        statusRow.style.alignItems = Align.Center;
        statusRow.style.marginTop = 8f;

        _statusBadge = CreateBadge("No HEAD", new Color(0.42f, 0.42f, 0.42f));
        statusRow.Add(_statusBadge);
        _messageLabel = BuildPipelineUI.SmallText(string.Empty);
        _messageLabel.style.marginLeft = 8f;
        _messageLabel.style.flexGrow = 1f;
        statusRow.Add(_messageLabel);
        header.Add(statusRow);

        var stats = new VisualElement();
        stats.style.flexDirection = FlexDirection.Row;
        stats.style.marginTop = 8f;
        _headLabel = AddStat(stats, "HEAD", "-");
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

        float middleWidth = LoadPaneWidth("Middle", MiddlePaneWidth, MiddlePaneMinWidth);
        var middleRightSplit = new TwoPaneSplitView(0, middleWidth, TwoPaneSplitViewOrientation.Horizontal)
        {
            name = "RepositoryMiddleRightSplit"
        };
        middleRightSplit.style.flexGrow = 1f;
        middleRightSplit.style.minWidth = 0f;
        middleRightSplit.Add(middlePane);
        middleRightSplit.Add(rightPane);
        RegisterPaneWidthPersistence(middlePane, "Middle", MiddlePaneMinWidth);

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
        return $"FYAsset.Repository.{_backendMode}.{paneName}PaneWidth";
    }

    private VisualElement CreateRightPane()
    {
        var pane = new VisualElement();
        pane.style.flexGrow = 1f;
        pane.style.minWidth = RightPaneMinWidth;
        pane.style.flexDirection = FlexDirection.Column;

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
        panel.style.maxHeight = 360f;

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
        panel.Add(row);

        panel.Add(BuildPipelineUI.SmallText("Push publishes the current Repository HEAD to the selected Target."));
        panel.Add(CreatePushTargetEditor());
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

        panel.Add(BuildPipelineUI.SmallText("Clears Repository HEAD and commit objects for the current Channel/Backend."));
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
        var row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        row.style.alignItems = Align.Center;
        row.style.marginTop = 4f;
        row.style.width = Length.Percent(100f);
        row.style.minWidth = 0f;

        var idField = new TextField("Id")
        {
            value = config != null ? config.Id : string.Empty,
            isDelayed = true
        };
        idField.style.width = 104f;
        idField.style.minWidth = 72f;
        idField.style.flexShrink = 1f;
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

        SerializedProperty pathProperty = new SerializedObject(FYAssetSettings.Instance)
            .FindProperty(nameof(FYAssetSettings.PushTargets))
            .GetArrayElementAtIndex(index)
            .FindPropertyRelative(nameof(PushTargetConfig.Path));
        VisualElement path = BuildPipelineUI.PathField(pathProperty, "Path", BuildPipelineUI.PathPickerMode.ProjectFolder, 34f);
        path.style.flexGrow = 1f;
        path.style.flexShrink = 1f;
        path.style.flexBasis = 0f;
        path.style.minWidth = 0f;
        path.style.maxWidth = Length.Percent(100f);
        row.Add(path);

        Button remove = BuildPipelineUI.ToolbarButton("Remove", () => RemovePushTarget(index), 64f);
        remove.style.marginLeft = 6f;
        remove.style.flexShrink = 0f;
        row.Add(remove);
        return row;
    }

    private void RefreshRepositoryState()
    {
        if (_root == null)
            return;

        _request = CreatePreviewRequest();
        _channelKey = BuildRepositoryFacade.GetChannelKey(_request);
        _status = BuildRepositoryFacade.GetStatus(_request);
        _health = BuildRepositoryFacade.GetHealth(_channelKey);
        _headCommit = _status != null && _status.HasHead ? BuildRepositoryFacade.GetHeadCommit(_channelKey) : null;
        _commits.Clear();
        _commits.AddRange(BuildRepositoryFacade.ListCommits(_channelKey));
        EnsureHistorySelection();

        RefreshHeader();
        RenderNavigation();
        RenderDiffContent();
        _maintenancePanel?.Refresh();
    }

    private void RefreshHeader()
    {
        if (_status == null)
            return;

        _channelLabel.text = $"Channel: {_channelKey}    Backend: {GetBackendDisplayName(_request.BackendMode)}";
        _headLabel.text = _status.HasHead ? SafeText(_status.HeadVersion) : "-";
        _versionLabel.text = _request?.Version != null ? _request.Version.GetReleaseVersionString() : "-";
        _packageLabel.text = _status.HasHead ? SafeText(_status.PackageName) : "-";
        _artifactLabel.text = _status.HasHead ? _status.ArtifactCount.ToString(CultureInfo.InvariantCulture) : "0";

        if (_health != null && _health.HasFatalIssue)
        {
            SetBadge("Health Error", new Color(0.65f, 0.20f, 0.16f));
            _messageLabel.text = _health.Summary;
            return;
        }

        if (_status.HasHeadError)
        {
            SetBadge("HEAD Error", new Color(0.65f, 0.20f, 0.16f));
            _messageLabel.text = _status.HeadErrorReason;
            return;
        }

        if (_health != null && _health.WarningCount > 0)
        {
            SetBadge("Cleanup Warning", new Color(0.70f, 0.46f, 0.12f));
            _messageLabel.text = _health.Summary;
            return;
        }

        if (!_status.HasHead)
        {
            SetBadge("No HEAD", new Color(0.42f, 0.42f, 0.42f));
            _messageLabel.text = "Build a package first to create Repository HEAD.";
            return;
        }

        string currentVersion = _request != null && _request.Version != null ? _request.Version.GetReleaseVersionString() : string.Empty;
        if (!string.IsNullOrEmpty(currentVersion) && !string.Equals(currentVersion, _status.HeadVersion, StringComparison.Ordinal))
        {
            SetBadge("Version Warning", new Color(0.70f, 0.46f, 0.12f));
            _messageLabel.text = $"VersionDataBase.CurrentVersion is {currentVersion}, Repository HEAD is {_status.HeadVersion}. Push still uses HEAD.";
            return;
        }

        if (_headCommit != null && _headCommit.IsDirty)
        {
            SetBadge("Dirty Git Source", new Color(0.70f, 0.46f, 0.12f));
            _messageLabel.text = $"HEAD was created from a dirty Git worktree. Git {ShortHash(_headCommit.GitCommitHash)}";
            return;
        }

        SetBadge("HEAD OK", new Color(0.18f, 0.48f, 0.28f));
        _messageLabel.text = _headCommit != null && !string.IsNullOrEmpty(_headCommit.GitCommitHash)
            ? $"Git {ShortHash(_headCommit.GitCommitHash)}"
            : "Repository HEAD has no Git metadata.";
    }

    private void RenderNavigation()
    {
        if (_tabs == null || _leftList == null)
            return;

        RenderTabs();
        _leftList.Clear();
        if (_viewMode == RepositoryViewMode.History)
            RenderCommitNavigation();
        else
            RenderChangesNavigation();
    }

    private void RenderTabs()
    {
        _tabs.Clear();
        _tabs.Add(CreateTabButton("Changes", RepositoryViewMode.Changes));
        _tabs.Add(CreateTabButton("History", RepositoryViewMode.History));
    }

    private Button CreateTabButton(string text, RepositoryViewMode mode)
    {
        bool active = _viewMode == mode;
        var button = new Button(() =>
        {
            _viewMode = mode;
            _selectedArtifactKey = null;
            EnsureHistorySelection();
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

    private void RenderCommitNavigation()
    {
        if (_commits.Count == 0)
        {
            _leftList.Add(CreateEmptyState("No Repository commits."));
            return;
        }

        for (int i = _commits.Count - 1; i >= 0; i--)
        {
            RepositoryCommit commit = _commits[i];
            _leftList.Add(CreateCommitRow(commit));
        }
    }

    private void RenderChangesNavigation()
    {
        if (!_hasStagingDelta)
        {
            _leftList.Add(CreateEmptyState("Click Refresh Changes to compare current preview output with Repository HEAD."));
            return;
        }

        List<RepositoryDiffItem> items = BuildDiffItems(_stagingDelta, _headCommit != null ? _headCommit.Artifacts : null, null);
        if (items.Count == 0)
        {
            _leftList.Add(CreateEmptyState("No staging changes."));
            return;
        }

        for (int i = 0; i < items.Count; i++)
            _leftList.Add(CreateCompactArtifactRow(items[i]));
    }

    private VisualElement CreateCommitRow(RepositoryCommit commit)
    {
        bool selected = IsSelectedCommit(commit);
        bool isHead = IsHeadCommit(commit);
        var row = CreateClickableRow(selected, () =>
        {
            _viewMode = RepositoryViewMode.History;
            _selectedCommit = commit;
            _selectedArtifactKey = null;
            RenderNavigation();
            RenderDiffContent();
        });

        string version = commit != null && commit.Version != null ? commit.Version.GetReleaseVersionString() : "(unknown)";
        string titleText = isHead ? $"HEAD  {version}" : version;
        var title = new Label(titleText);
        title.style.unityFontStyleAndWeight = FontStyle.Bold;
        title.style.whiteSpace = WhiteSpace.Normal;
        row.Add(title);

        row.Add(BuildPipelineUI.SmallText($"{SafeText(commit?.BuildType)}  |  {FormatUtc(commit?.CreatedAtUtc)}"));
        row.Add(BuildPipelineUI.SmallText($"{GetCommitDeltaSummary(commit)}  |  Artifacts {CountArtifacts(commit)}"));
        return row;
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

        if (_viewMode == RepositoryViewMode.History)
            RenderHistoryDiff();
        else
            RenderStagingDiff();
    }

    private void RenderHistoryDiff()
    {
        _artifactTitle.text = "Commit Diff";

        if (_selectedCommit == null)
        {
            AddDiffStats(null);
            _artifactList.Add(CreateEmptyState("Select a commit."));
            RenderEmptyDetail("Select an artifact to inspect metadata.");
            return;
        }

        if (_selectedCommit.CommitDelta == null)
        {
            AddDiffStats(null);
            _artifactList.Add(CreateEmptyState("No persisted diff in this commit."));
            RenderEmptyDetail("This commit was created before CommitDelta was persisted.");
            return;
        }

        RepositoryCommit parent = FindCommitByVersion(_selectedCommit.ParentVersion);
        _currentDiffItems.AddRange(BuildDiffItems(
            _selectedCommit.CommitDelta,
            parent != null ? parent.Artifacts : null,
            _selectedCommit.Artifacts));
        AddDiffStats(_selectedCommit.CommitDelta);
        RenderArtifactList("No artifact changes in this commit.");
    }

    private void RenderStagingDiff()
    {
        _artifactTitle.text = "Staging Diff";

        if (!_hasStagingDelta)
        {
            AddDiffStats(null);
            _artifactList.Add(CreateEmptyState("Click Refresh Changes to run current preview output vs Repository HEAD."));
            RenderEmptyDetail("Staging diff is not loaded.");
            return;
        }

        _currentDiffItems.AddRange(BuildDiffItems(_stagingDelta, _headCommit != null ? _headCommit.Artifacts : null, null));
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
        text.Add(BuildPipelineUI.SmallText(meta != null ? $"{FormatBytes(meta.Size)}  |  {ShortHash(meta.Hash)}" : "metadata unavailable"));
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

            if (_backendMode == BackendMode.ABManifest)
            {
                _stagingABPreview = RepositoryPreviewRunner.RunABPreviewDetailed(_request);
                _stagingDelta = _stagingABPreview != null ? _stagingABPreview.HeadDelta : new ArtifactDelta();
            }
            else
            {
                _stagingABPreview = null;
                _stagingDelta = RepositoryPreviewRunner.RunAAPreview(_request);
            }

            _hasStagingDelta = true;
            SetBadge("Changes Ready", new Color(0.18f, 0.48f, 0.28f));
            _messageLabel.text = IsDeltaEmpty(_stagingDelta) ? "Changes preview completed with no HEAD changes." : "Changes preview completed.";
            RenderNavigation();
            RenderDiffContent();
        }
        catch (Exception ex)
        {
            Debug.LogError($"[RepositoryStatusPanel] Refresh Changes failed: {ex}");
            ClearStagingState();
            _viewMode = RepositoryViewMode.Changes;
            SetBadge("Changes Failed", new Color(0.65f, 0.20f, 0.16f));
            _messageLabel.text = ex.Message;
            RenderNavigation();
            RenderDiffContent();
        }
    }

    private void RunHealth()
    {
        try
        {
            _health = BuildRepositoryFacade.GetHealth(_channelKey);
            LogHealth(_health);
            RefreshHeader();
        }
        catch (Exception ex)
        {
            Debug.LogError($"[RepositoryStatusPanel] Health check failed: {ex}");
            SetBadge("Health Failed", new Color(0.65f, 0.20f, 0.16f));
            _messageLabel.text = ex.Message;
        }
    }

    private void RunPreviewDelivery()
    {
        if (_backendMode != BackendMode.ABManifest)
            return;

        try
        {
            _viewMode = RepositoryViewMode.Changes;
            _selectedArtifactKey = null;
            _messageLabel.text = "Running AB delivery preview...";

            _stagingABPreview = RepositoryPreviewRunner.RunABDeliveryPreview(_request);
            _stagingDelta = _stagingABPreview != null ? _stagingABPreview.HeadDelta : new ArtifactDelta();
            _hasStagingDelta = true;

            SetBadge("Delivery Ready", new Color(0.18f, 0.48f, 0.28f));
            _messageLabel.text = "AB delivery preview completed.";
            RenderNavigation();
            RenderDiffContent();
        }
        catch (Exception ex)
        {
            Debug.LogError($"[RepositoryStatusPanel] Preview Delivery failed: {ex}");
            _viewMode = RepositoryViewMode.Changes;
            _stagingABPreview = new ABRepositoryPreviewResult
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
            if (HasFatalHealthIssue("Push"))
                return;

            if (_headCommit == null)
            {
                SetBadge("Push Failed", new Color(0.65f, 0.20f, 0.16f));
                _messageLabel.text = "Repository has no HEAD to push.";
                return;
            }

            IPushTarget target = CreatePushTarget();
            PushReceipt receipt = BuildRepositoryFacade.PushHead(_channelKey, target);
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
            Debug.LogError($"[RepositoryStatusPanel] Push failed: {ex}");
            SetBadge("Push Failed", new Color(0.65f, 0.20f, 0.16f));
            _messageLabel.text = ex.Message;
        }
    }

    private bool HasFatalHealthIssue(string action)
    {
        _health = BuildRepositoryFacade.GetHealth(_channelKey);
        if (_health == null || !_health.HasFatalIssue)
            return false;

        SetBadge(action + " Blocked", new Color(0.65f, 0.20f, 0.16f));
        _messageLabel.text = _health.Summary;
        LogHealth(_health);
        return true;
    }

    private void RunClearChannelForTest()
    {
        bool clearPackageIndex = _clearPackageIndexToggle?.value == true;
        bool deletePackages = _deletePackagesToggle?.value == true;
        bool clearStartupBaseline = _clearStartupBaselineToggle?.value == true;

        string message = $"Clear Repository channel for test?\n\nChannel: {_channelKey}\nBackend: {GetBackendDisplayName(_backendMode)}\n\nThis deletes HEAD.json, objects/*.json, and legacy PushHistory.json residue.";
        if (clearPackageIndex)
            message += "\n- Clear output PackageIndex.json";
        if (deletePackages)
            message += "\n- Delete local package folders";
        if (clearStartupBaseline)
            message += "\n- Clear startup BuildIndex / StreamingAssets baseline";

        if (!EditorUtility.DisplayDialog("Clear Repository Channel", message, "Clear", "Cancel"))
            return;

        try
        {
            BuildRepositoryFacade.ClearChannelForTest(_channelKey);
            if (clearPackageIndex)
                WriteEmptyPackageIndex();
            if (deletePackages)
                DeleteLocalPackageFolders();
            if (clearStartupBaseline)
                ClearStartupBaseline();

            AssetDatabase.Refresh();
            RefreshRepositoryState();
            SetBadge("Channel Cleared", new Color(0.18f, 0.48f, 0.28f));
            _messageLabel.text = $"Repository channel cleared: {_channelKey}";
        }
        catch (Exception ex)
        {
            Debug.LogError($"[RepositoryStatusPanel] Clear channel failed: {ex}");
            SetBadge("Clear Failed", new Color(0.65f, 0.20f, 0.16f));
            _messageLabel.text = ex.Message;
        }
    }

    private void RunResetVersionForTest()
    {
        VersionDataBase versionDB = AssetDatabase.LoadAssetAtPath<VersionDataBase>(FYAssetSettings.Instance.VersionDataBasePath);
        if (versionDB == null)
        {
            EditorUtility.DisplayDialog("Reset Version", "VersionDataBase not found:\n\n" + FYAssetSettings.Instance.VersionDataBasePath, "OK");
            return;
        }

        if (!EditorUtility.DisplayDialog(
                "Reset Version",
                "Reset VersionDataBase to 1.0.0 for testing?\n\nThis clears Channel, LastBuildTime, and DailyBuildCount.",
                "Reset",
                "Cancel"))
            return;

        Undo.RecordObject(versionDB, "Reset VersionDataBase");
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
                Debug.LogError($"[RepositoryStatusPanel] Refused to delete path outside PackagesDir: {dirs[i]}");
                continue;
            }

            FileHelper.TryDeleteDirectory(dirs[i], true);
        }
    }

    private static void ClearStartupBaseline()
    {
        FileHelper.TryDelete(ResolveProjectPath(FYAssetSettings.Instance.BuildIndexJsonPath));
        FileHelper.TryDelete(FYAssetPathUtility.JoinFilePath(Application.streamingAssetsPath, FYAssetSettings.BUILD_INDEX_FILENAME));
        FileHelper.TryDelete(FYAssetPathUtility.JoinFilePath(Application.streamingAssetsPath, FYAssetSettings.ADDRESSABLES_CATALOG_FILE_NAME));
        FileHelper.TryDelete(FYAssetPathUtility.JoinFilePath(Application.streamingAssetsPath, FYAssetSettings.AA_MANIFEST_FILE_NAME));
        FileHelper.TryDelete(FYAssetPathUtility.JoinFilePath(Application.streamingAssetsPath, FYAssetSettings.AA_MANIFEST_FILE_NAME_BIN));
        FileHelper.TryDelete(FYAssetPathUtility.JoinFilePath(Application.streamingAssetsPath, FYAssetSettings.MANIFEST_FILE_NAME));
        FileHelper.TryDelete(FYAssetPathUtility.JoinFilePath(Application.streamingAssetsPath, FYAssetSettings.MANIFEST_FILE_NAME_BIN));
        FileHelper.TryDeleteDirectory(FYAssetPathUtility.JoinFilePath(Application.streamingAssetsPath, FYAssetSettings.BUNDLES_DIRECTORY_NAME), true);
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
            Path = string.Empty
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

    private static void LogHealth(RepositoryHealthReport health)
    {
        if (health == null)
            return;

        Debug.Log($"[RepositoryStatusPanel] Health: {health.Summary}");
        for (int i = 0; i < health.FatalIssues.Count; i++)
            Debug.LogError($"[RepositoryStatusPanel] {health.FatalIssues[i]}");
        for (int i = 0; i < health.Warnings.Count; i++)
            Debug.LogWarning($"[RepositoryStatusPanel] {health.Warnings[i]}");
    }

    private BuildPackageRequest CreatePreviewRequest()
    {
        VersionDataBase versionDB = AssetDatabase.LoadAssetAtPath<VersionDataBase>(FYAssetSettings.Instance.VersionDataBasePath);
        VersionNumber version = versionDB != null && versionDB.CurrentVersion != null
            ? versionDB.CurrentVersion
            : new VersionNumber { Major = 0, Minor = 0, Patch = 0 };
        return BuildPackageRequest.Create(version, BuildType.Full, _backendMode);
    }

    private IPushTarget CreatePushTarget()
    {
        FYAssetSettings settings = FYAssetSettings.Instance;
        string targetId = _targetDropdown != null && !string.IsNullOrEmpty(_targetDropdown.value)
            ? _targetDropdown.value
            : (settings.PushTargets != null && settings.PushTargets.Count > 0 ? settings.PushTargets[0].Id : string.Empty);
        for (int i = 0; settings.PushTargets != null && i < settings.PushTargets.Count; i++)
        {
            PushTargetConfig config = settings.PushTargets[i];
            if (config != null && string.Equals(config.Id, targetId, StringComparison.OrdinalIgnoreCase))
                return new LocalDirectoryPushTarget(config);
        }
        throw new InvalidOperationException("No push target configured.");
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

    private void EnsureHistorySelection()
    {
        if (_viewMode != RepositoryViewMode.History)
            return;

        if (_selectedCommit != null && FindCommitByVersion(GetCommitVersion(_selectedCommit)) != null)
            return;

        _selectedCommit = _headCommit != null
            ? FindCommitByVersion(GetCommitVersion(_headCommit)) ?? _headCommit
            : (_commits.Count > 0 ? _commits[_commits.Count - 1] : null);
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

    private RepositoryCommit FindCommitByVersion(string version)
    {
        if (string.IsNullOrEmpty(version))
            return null;

        for (int i = 0; i < _commits.Count; i++)
        {
            RepositoryCommit commit = _commits[i];
            if (string.Equals(GetCommitVersion(commit), version, StringComparison.Ordinal))
                return commit;
        }
        return null;
    }

    private bool IsSelectedCommit(RepositoryCommit commit)
    {
        return commit != null
            && _selectedCommit != null
            && string.Equals(GetCommitVersion(commit), GetCommitVersion(_selectedCommit), StringComparison.Ordinal);
    }

    private bool IsHeadCommit(RepositoryCommit commit)
    {
        return commit != null
            && _status != null
            && string.Equals(GetCommitVersion(commit), _status.HeadVersion, StringComparison.Ordinal);
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

    private ArtifactPresentation GetArtifactPresentation(string identity)
    {
        if (_backendMode != BackendMode.AA)
        {
            return new ArtifactPresentation
            {
                DisplayName = SafeText(identity)
            };
        }

        string guid = identity ?? string.Empty;
        string assetPath = string.IsNullOrEmpty(guid) ? string.Empty : AssetDatabase.GUIDToAssetPath(guid);
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        var entry = settings != null && !string.IsNullOrEmpty(guid) ? settings.FindAssetEntry(guid) : null;
        string address = entry != null ? entry.address ?? string.Empty : string.Empty;
        bool isResolved = !string.IsNullOrEmpty(address) && !string.IsNullOrEmpty(assetPath);
        string displayName;
        if (isResolved)
        {
            displayName = $"{address} | {assetPath}";
        }
        else if (!string.IsNullOrEmpty(address))
        {
            displayName = $"Unresolved AA asset: {address} (GUID: {SafeText(guid)})";
        }
        else if (!string.IsNullOrEmpty(assetPath))
        {
            displayName = $"Unresolved Addressables entry: {assetPath} (GUID: {SafeText(guid)})";
        }
        else
        {
            displayName = $"Unresolved AA asset (GUID: {SafeText(guid)})";
        }

        return new ArtifactPresentation
        {
            DisplayName = displayName,
            Address = address,
            AssetPath = assetPath,
            Guid = guid,
            IsResolved = isResolved
        };
    }

    private void AddArtifactIdentityDetails(VisualElement parent, string identity)
    {
        ArtifactPresentation presentation = GetArtifactPresentation(identity);
        if (_backendMode != BackendMode.AA)
        {
            parent.Add(CreateDetailLine("Name", presentation.DisplayName));
            return;
        }

        parent.Add(CreateDetailLine("Address", SafeText(presentation.Address)));
        parent.Add(CreateDetailLine("Asset Path", SafeText(presentation.AssetPath)));
        parent.Add(CreateDetailLine("GUID", SafeText(presentation.Guid)));
        if (!presentation.IsResolved)
            parent.Add(CreateWarning("Addressable asset cannot be fully resolved from the persisted GUID."));
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
        if (_backendMode != BackendMode.ABManifest || _stagingABPreview == null)
            return;

        int count = _stagingABPreview.DeliveryBundles != null ? _stagingABPreview.DeliveryBundles.Count : 0;
        if (!_stagingABPreview.DeliveryAvailable)
        {
            _deliverySummary.Add(CreateBadge("Hotfix Delivery Unavailable", new Color(0.42f, 0.42f, 0.42f)));
            _deliverySummary.Add(BuildPipelineUI.SmallText(string.IsNullOrEmpty(_stagingABPreview.DeliveryMessage)
                ? "Use Preview Delivery to calculate current output vs Full baseline."
                : _stagingABPreview.DeliveryMessage));
            return;
        }

        _deliverySummary.Add(CreateBadge($"Hotfix Delivery {count}", new Color(0.17f, 0.36f, 0.53f)));
        _deliverySummary.Add(BuildPipelineUI.SmallText($"Full baseline -> current output, {FormatBytes(_stagingABPreview.DeliverySizeBytes)}"));
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
        parent.Add(CreateDetailLine("Size", FormatBytes(artifact.Size)));
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
        _stagingABPreview = null;
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

    private static string GetCommitVersion(RepositoryCommit commit)
    {
        return commit != null && commit.Version != null ? commit.Version.GetReleaseVersionString() : string.Empty;
    }

    private static string GetCommitDeltaSummary(RepositoryCommit commit)
    {
        if (commit == null)
            return "+0 ~0 -0";
        if (commit.CommitDelta == null)
            return "No persisted diff";
        return $"+{CountAdded(commit.CommitDelta)} ~{CountModified(commit.CommitDelta)} -{CountRemoved(commit.CommitDelta)}";
    }

    private static int CountArtifacts(RepositoryCommit commit)
    {
        return commit != null && commit.Artifacts != null ? commit.Artifacts.Count : 0;
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

    private static string FormatUtc(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "-";

        if (DateTime.TryParse(value, null, DateTimeStyles.RoundtripKind, out DateTime date))
            return date.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        return value;
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024L)
            return bytes.ToString("N0", CultureInfo.InvariantCulture) + " B";
        double value = bytes / 1024d;
        if (value < 1024d)
            return value.ToString("N1", CultureInfo.InvariantCulture) + " KB";
        value /= 1024d;
        if (value < 1024d)
            return value.ToString("N1", CultureInfo.InvariantCulture) + " MB";
        value /= 1024d;
        return value.ToString("N1", CultureInfo.InvariantCulture) + " GB";
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

    private static string GetBackendDisplayName(BackendMode backendMode)
    {
        return backendMode == BackendMode.ABManifest ? "AB" : "AA";
    }
}

/// <summary>
/// Backend-owned maintenance content hosted by the shared Repository panel.
/// </summary>
public interface IRepositoryMaintenancePanel
{
    VisualElement CreateContent();
    void Refresh();
}
#endif
