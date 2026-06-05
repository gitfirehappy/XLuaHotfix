#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Build Repository 状态、Diff Preview 与 Push 面板。
/// </summary>
public sealed class RepositoryStatusPanel : IBuildPipelinePanel, IBuildPipelinePanelVisibility
{
    #region Fields

    private VisualElement _root;
    private VisualElement _commitPane;
    private VisualElement _detailPane;
    private VisualElement _diffPanel;
    private VisualElement _pushPanel;
    private VisualElement _commitList;
    private VisualElement _diffSummary;
    private VisualElement _diffList;
    private VisualElement _pushHistoryList;
    private Label _statusBadge;
    private Label _channelLabel;
    private Label _headLabel;
    private Label _packageLabel;
    private Label _artifactLabel;
    private Label _lastPushLabel;
    private Label _messageLabel;
    private DropdownField _targetDropdown;
    private TextField _fromVersionField;
    private TextField _toVersionField;

    private RepositoryStatus _status;
    private RepositoryCommit _headCommit;
    private BuildPackageRequest _request;
    private string _channelKey;
    private bool _isDraggingCommitSplitter;
    private bool _isDraggingPushSplitter;
    private Vector2 _dragStartMouse;
    private float _dragStartSize;
    private float _commitPaneWidth = 300f;
    private float _pushPanelHeight = 300f;

    private const float MinCommitPaneWidth = 220f;
    private const float MinDetailPaneWidth = 320f;
    private const float MinDiffPanelHeight = 180f;
    private const float MinPushPanelHeight = 220f;

    #endregion

    #region IBuildPipelinePanel

    public string PanelName => "仓库";

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
        _commitPane = null;
        _detailPane = null;
        _diffPanel = null;
        _pushPanel = null;
        _commitList = null;
        _diffSummary = null;
        _diffList = null;
        _pushHistoryList = null;
        _statusBadge = null;
        _channelLabel = null;
        _headLabel = null;
        _packageLabel = null;
        _artifactLabel = null;
        _lastPushLabel = null;
        _messageLabel = null;
        _targetDropdown = null;
        _fromVersionField = null;
        _toVersionField = null;
        _status = null;
        _headCommit = null;
        _request = null;
        _channelKey = null;
    }

    public void SetVisible(bool visible)
    {
        if (visible)
            RefreshRepositoryState();
    }

    #endregion

    #region Build UI

    private void Rebuild()
    {
        if (_root == null)
            return;

        _root.Clear();

        _request = CreatePreviewRequest();
        _channelKey = BuildRepositoryFacade.GetChannelKey(_request);

        _root.Add(CreateHeader());
        _root.Add(CreateBody());

        RefreshRepositoryState();
        RefreshDiffEmptyState();
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

        var top = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };
        var titleBox = new VisualElement { style = { flexGrow = 1f, minWidth = 0f } };
        var title = new Label("Build Repository 仓库");
        title.style.unityFontStyleAndWeight = FontStyle.Bold;
        title.style.fontSize = 15f;
        titleBox.Add(title);

        _channelLabel = BuildPipelineUI.SmallText("Channel: -");
        _channelLabel.style.marginTop = 2f;
        titleBox.Add(_channelLabel);
        top.Add(titleBox);

        top.Add(BuildPipelineUI.ToolbarButton("刷新", Rebuild, 60f));
        top.Add(BuildPipelineUI.ToolbarButton("Diff", RunDiff, 60f));
        top.Add(BuildPipelineUI.ToolbarButton("Push", RunPush, 60f));
        header.Add(top);

        var statusRow = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, marginTop = 8f } };
        _statusBadge = CreateBadge("No HEAD", new Color(0.42f, 0.42f, 0.42f));
        statusRow.Add(_statusBadge);
        _messageLabel = BuildPipelineUI.SmallText(string.Empty);
        _messageLabel.style.marginLeft = 8f;
        _messageLabel.style.flexGrow = 1f;
        statusRow.Add(_messageLabel);
        header.Add(statusRow);

        var stats = new VisualElement { style = { flexDirection = FlexDirection.Row, marginTop = 8f } };
        _headLabel = AddStat(stats, "HEAD", "-");
        _packageLabel = AddStat(stats, "Package", "-");
        _artifactLabel = AddStat(stats, "Artifacts", "-");
        _lastPushLabel = AddStat(stats, "Last Push", "-");
        header.Add(stats);

        return header;
    }

    private VisualElement CreateBody()
    {
        var body = new VisualElement { name = "RepositoryBody" };
        body.style.flexGrow = 1f;
        body.style.flexDirection = FlexDirection.Row;
        body.style.minHeight = 0f;

        _commitPane = BuildPipelineUI.Card();
        _commitPane.style.width = _commitPaneWidth;
        _commitPane.style.minWidth = MinCommitPaneWidth;
        _commitPane.style.marginBottom = 0f;
        _commitPane.style.flexShrink = 0f;
        _commitPane.Add(BuildPipelineUI.Header("提交记录"));
        _commitPane.Add(BuildPipelineUI.SmallText("当前 Channel 下的 Repository objects。"));
        var commitScroll = new ScrollView();
        commitScroll.style.marginTop = 8f;
        commitScroll.style.flexGrow = 1f;
        commitScroll.style.minHeight = 0f;
        _commitList = new VisualElement();
        commitScroll.Add(_commitList);
        _commitPane.Add(commitScroll);
        body.Add(_commitPane);

        body.Add(CreateCommitSplitter(body));

        _detailPane = new VisualElement { style = { flexGrow = 1f, flexDirection = FlexDirection.Column, minWidth = MinDetailPaneWidth } };
        _detailPane.Add(CreateDiffPanel());
        _detailPane.Add(CreatePushSplitter(_detailPane));
        _detailPane.Add(CreatePushPanel());
        body.Add(_detailPane);

        return body;
    }

    private VisualElement CreateDiffPanel()
    {
        _diffPanel = BuildPipelineUI.Card();
        _diffPanel.style.flexGrow = 1f;
        _diffPanel.style.minHeight = MinDiffPanelHeight;
        _diffPanel.Add(BuildPipelineUI.Header("Diff 预览"));

        _diffSummary = new VisualElement { style = { flexDirection = FlexDirection.Row, marginBottom = 8f } };
        _diffPanel.Add(_diffSummary);

        var scroll = new ScrollView();
        scroll.style.flexGrow = 1f;
        scroll.style.minHeight = 0f;
        _diffList = new VisualElement();
        scroll.Add(_diffList);
        _diffPanel.Add(scroll);
        return _diffPanel;
    }

    private VisualElement CreatePushPanel()
    {
        _pushPanel = BuildPipelineUI.Card();
        _pushPanel.style.height = _pushPanelHeight;
        _pushPanel.style.minHeight = MinPushPanelHeight;
        _pushPanel.style.marginBottom = 0f;
        _pushPanel.style.flexShrink = 0f;
        _pushPanel.Add(BuildPipelineUI.Header("Push"));

        var row = new VisualElement { style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap, alignItems = Align.Center } };
        row.style.minWidth = 0f;
        _targetDropdown = new DropdownField("Target", GetPushTargetLabels(), 0);
        _targetDropdown.style.width = 220f;
        _targetDropdown.style.maxWidth = 240f;
        _targetDropdown.style.minWidth = 140f;
        _targetDropdown.style.flexShrink = 1f;
        _targetDropdown.style.marginRight = 6f;
        _targetDropdown.style.marginBottom = 4f;
        row.Add(_targetDropdown);

        _fromVersionField = new TextField("From");
        _fromVersionField.style.width = 230f;
        _fromVersionField.style.minWidth = 190f;
        _fromVersionField.style.flexShrink = 1f;
        _fromVersionField.style.marginRight = 6f;
        _fromVersionField.style.marginBottom = 4f;
        SetCompactFieldLabel(_fromVersionField, 38f);
        row.Add(_fromVersionField);

        _toVersionField = new TextField("To")
        {
            value = _request != null && _request.Version != null ? _request.Version.GetFullVersionString() : string.Empty
        };
        _toVersionField.style.width = 230f;
        _toVersionField.style.minWidth = 190f;
        _toVersionField.style.flexShrink = 1f;
        _toVersionField.style.marginBottom = 4f;
        SetCompactFieldLabel(_toVersionField, 24f);
        row.Add(_toVersionField);
        _pushPanel.Add(row);

        _pushPanel.Add(BuildPipelineUI.SmallText("Push 使用现有仓库版本范围。点击左侧提交记录可填充 To，也可以手动编辑版本号。"));
        _pushPanel.Add(CreatePushTargetEditor());

        var historyTitle = new Label("历史记录");
        historyTitle.style.marginTop = 8f;
        historyTitle.style.marginBottom = 4f;
        historyTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
        _pushPanel.Add(historyTitle);

        var scroll = new ScrollView();
        scroll.style.flexGrow = 1f;
        scroll.style.minHeight = 0f;
        _pushHistoryList = new VisualElement();
        scroll.Add(_pushHistoryList);
        _pushPanel.Add(scroll);
        return _pushPanel;
    }

    private VisualElement CreatePushTargetEditor()
    {
        var box = new VisualElement();
        box.style.marginTop = 8f;
        box.style.marginBottom = 4f;
        ApplyBorder(box);
        box.style.paddingLeft = 6f;
        box.style.paddingRight = 6f;
        box.style.paddingTop = 6f;
        box.style.paddingBottom = 6f;

        var header = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };
        var title = new Label("Target 配置");
        title.style.unityFontStyleAndWeight = FontStyle.Bold;
        title.style.flexGrow = 1f;
        header.Add(title);
        header.Add(BuildPipelineUI.ToolbarButton("+ Target", AddPushTarget, 82f));
        box.Add(header);

        FYAssetSettings settings = FYAssetSettings.Instance;
        if (settings.PushTargets == null || settings.PushTargets.Count == 0)
        {
            box.Add(BuildPipelineUI.SmallText("没有 Push Target。"));
            return box;
        }

        for (int i = 0; i < settings.PushTargets.Count; i++)
        {
            PushTargetConfig config = settings.PushTargets[i];
            box.Add(CreatePushTargetRow(config, i));
        }

        return box;
    }

    private VisualElement CreatePushTargetRow(PushTargetConfig config, int index)
    {
        var row = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };
        row.style.marginTop = 4f;
        row.style.minWidth = 0f;

        var idField = new TextField("Id")
        {
            value = config != null ? config.Id : string.Empty,
            isDelayed = true
        };
        idField.style.width = 160f;
        idField.style.minWidth = 110f;
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

        var pathProperty = new SerializedObject(FYAssetSettings.Instance)
            .FindProperty(nameof(FYAssetSettings.PushTargets))
            .GetArrayElementAtIndex(index)
            .FindPropertyRelative(nameof(PushTargetConfig.Path));
        VisualElement path = BuildPipelineUI.PathField(pathProperty, "Path", BuildPipelineUI.PathPickerMode.ProjectFolder, 34f);
        path.style.flexGrow = 1f;
        path.style.minWidth = 0f;
        row.Add(path);

        Button remove = BuildPipelineUI.ToolbarButton("移除", () => RemovePushTarget(index), 54f);
        remove.style.marginLeft = 6f;
        row.Add(remove);
        return row;
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

    private VisualElement CreateCommitSplitter(VisualElement body)
    {
        var splitter = BuildPipelineUI.Splitter(true);
        splitter.style.marginLeft = 4f;
        splitter.style.marginRight = 4f;
        splitter.RegisterCallback<PointerDownEvent>(evt =>
        {
            if (evt.button != 0)
                return;

            _isDraggingCommitSplitter = true;
            _dragStartMouse = evt.position;
            _dragStartSize = _commitPaneWidth;
            splitter.CapturePointer(evt.pointerId);
            evt.StopPropagation();
        });
        splitter.RegisterCallback<PointerMoveEvent>(evt =>
        {
            if (!_isDraggingCommitSplitter)
                return;

            float maxWidth = Mathf.Max(MinCommitPaneWidth, body.resolvedStyle.width - MinDetailPaneWidth - 18f);
            _commitPaneWidth = Mathf.Clamp(_dragStartSize + evt.position.x - _dragStartMouse.x, MinCommitPaneWidth, maxWidth);
            _commitPane.style.width = _commitPaneWidth;
            evt.StopPropagation();
        });
        splitter.RegisterCallback<PointerUpEvent>(evt =>
        {
            if (!_isDraggingCommitSplitter)
                return;

            _isDraggingCommitSplitter = false;
            splitter.ReleasePointer(evt.pointerId);
            evt.StopPropagation();
        });
        return splitter;
    }

    private VisualElement CreatePushSplitter(VisualElement detailPane)
    {
        var splitter = BuildPipelineUI.Splitter(false);
        splitter.style.marginTop = 4f;
        splitter.style.marginBottom = 4f;
        splitter.RegisterCallback<PointerDownEvent>(evt =>
        {
            if (evt.button != 0)
                return;

            _isDraggingPushSplitter = true;
            _dragStartMouse = evt.position;
            _dragStartSize = _pushPanelHeight;
            splitter.CapturePointer(evt.pointerId);
            evt.StopPropagation();
        });
        splitter.RegisterCallback<PointerMoveEvent>(evt =>
        {
            if (!_isDraggingPushSplitter)
                return;

            float maxHeight = Mathf.Max(MinPushPanelHeight, detailPane.resolvedStyle.height - MinDiffPanelHeight - 18f);
            _pushPanelHeight = Mathf.Clamp(_dragStartSize - (evt.position.y - _dragStartMouse.y), MinPushPanelHeight, maxHeight);
            _pushPanel.style.height = _pushPanelHeight;
            evt.StopPropagation();
        });
        splitter.RegisterCallback<PointerUpEvent>(evt =>
        {
            if (!_isDraggingPushSplitter)
                return;

            _isDraggingPushSplitter = false;
            splitter.ReleasePointer(evt.pointerId);
            evt.StopPropagation();
        });
        return splitter;
    }

    #endregion

    #region Refresh

    private void RefreshRepositoryState()
    {
        if (_root == null)
            return;

        _request = CreatePreviewRequest();
        _channelKey = BuildRepositoryFacade.GetChannelKey(_request);
        _status = BuildRepositoryFacade.GetStatus(_request);
        _headCommit = _status != null && _status.HasHead ? BuildRepositoryFacade.GetHeadCommit(_channelKey) : null;

        RefreshHeader();
        RefreshCommits();
        RefreshPushHistory();
    }

    private void RefreshHeader()
    {
        if (_status == null)
            return;

        _channelLabel.text = $"Channel: {_channelKey}    Backend: {(_request.BackendMode == BackendMode.ABManifest ? "AB" : "AA")}";
        _headLabel.text = _status.HasHead ? SafeText(_status.HeadVersion) : "-";
        _packageLabel.text = _status.HasHead ? SafeText(_status.PackageName) : "-";
        _artifactLabel.text = _status.HasHead ? _status.ArtifactCount.ToString(CultureInfo.InvariantCulture) : "0";
        _lastPushLabel.text = string.IsNullOrEmpty(_status.LastPushAtUtc)
            ? "-"
            : $"{SafeText(_status.LastPushTargetId)} {FormatUtc(_status.LastPushAtUtc)}";

        if (_status.HasHeadError)
        {
            SetBadge("HEAD Error", new Color(0.65f, 0.20f, 0.16f));
            _messageLabel.text = _status.HeadErrorReason;
        }
        else if (!_status.HasHead)
        {
            SetBadge("No HEAD", new Color(0.42f, 0.42f, 0.42f));
            _messageLabel.text = "先构建一个包以创建 Repository HEAD。";
        }
        else if (_headCommit != null && _headCommit.IsDirty)
        {
            SetBadge("Dirty Git Source", new Color(0.70f, 0.46f, 0.12f));
            _messageLabel.text = $"HEAD 创建时 Git 工作区未清理。Git {ShortHash(_headCommit.GitCommitHash)}";
        }
        else
        {
            SetBadge("HEAD OK", new Color(0.18f, 0.48f, 0.28f));
            _messageLabel.text = _headCommit != null && !string.IsNullOrEmpty(_headCommit.GitCommitHash)
                ? $"Git {ShortHash(_headCommit.GitCommitHash)}"
                : "当前 HEAD 没有可用的 Git metadata。";
        }
    }

    private void RefreshCommits()
    {
        _commitList.Clear();
        var commits = BuildRepositoryFacade.ListCommits(_channelKey);
        if (commits.Count == 0)
        {
            _commitList.Add(CreateEmptyState("当前 Channel 没有提交记录。"));
            return;
        }

        for (int i = commits.Count - 1; i >= 0; i--)
        {
            var commit = commits[i];
            _commitList.Add(CreateCommitRow(commit, IsHeadCommit(commit)));
        }
    }

    private void RefreshDiffEmptyState()
    {
        _diffSummary.Clear();
        AddDiffStat("新增", 0, new Color(0.20f, 0.55f, 0.30f));
        AddDiffStat("修改", 0, new Color(0.70f, 0.48f, 0.16f));
        AddDiffStat("删除", 0, new Color(0.65f, 0.20f, 0.16f));

        _diffList.Clear();
        _diffList.Add(CreateEmptyState("点击 Diff 预览当前产物相对 Repository HEAD 的变化。"));
    }

    private void RefreshPushHistory()
    {
        _pushHistoryList.Clear();
        var history = BuildRepositoryFacade.ListPushHistory(_channelKey);
        if (history.Count == 0)
        {
            _pushHistoryList.Add(CreateEmptyState("当前 Channel 没有 Push 历史。"));
            return;
        }

        for (int i = history.Count - 1; i >= 0; i--)
            _pushHistoryList.Add(CreatePushHistoryRow(history[i]));
    }

    #endregion

    #region Commands

    private void RunDiff()
    {
        try
        {
            _messageLabel.text = "正在运行 Diff 预览...";
            ArtifactDelta delta = FYAssetSettings.Instance.UseABBackend
                ? RepositoryPreviewRunner.RunABPreview(_request)
                : RepositoryPreviewRunner.RunAAPreview(_request);
            RenderDelta(delta);
            _messageLabel.text = delta != null && delta.IsEmpty
                ? "Diff 完成，未检测到变化。"
                : "Diff 完成。";
        }
        catch (Exception ex)
        {
            Debug.LogError($"[RepositoryStatusPanel] Diff 失败: {ex}");
            _diffSummary.Clear();
            _diffList.Clear();
            _diffList.Add(CreateEmptyState(ex.Message));
            SetBadge("Diff Failed", new Color(0.65f, 0.20f, 0.16f));
            _messageLabel.text = ex.Message;
        }
    }

    private void RunPush()
    {
        try
        {
            var target = CreatePushTarget();
            var fromVersion = ParseVersion(_fromVersionField != null ? _fromVersionField.value : string.Empty);
            var toVersion = ParseVersion(_toVersionField != null && !string.IsNullOrEmpty(_toVersionField.value)
                ? _toVersionField.value
                : _request.Version.GetFullVersionString());
            var receipt = BuildRepositoryFacade.Push(_channelKey, fromVersion, toVersion, target);
            RefreshRepositoryState();

            if (receipt != null && receipt.Success)
            {
                SetBadge("Push OK", new Color(0.18f, 0.48f, 0.28f));
                _messageLabel.text = $"Push 成功: {receipt.TargetId} -> {receipt.TargetLocation}";
            }
            else
            {
                SetBadge("Push Failed", new Color(0.65f, 0.20f, 0.16f));
                _messageLabel.text = receipt != null ? receipt.FailureReason : "Push 返回了空 receipt。";
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[RepositoryStatusPanel] Push 失败: {ex}");
            SetBadge("Push Failed", new Color(0.65f, 0.20f, 0.16f));
            _messageLabel.text = ex.Message;
        }
    }

    #endregion

    #region Render Helpers

    private VisualElement CreateCommitRow(RepositoryCommit commit, bool isHead)
    {
        var row = new VisualElement();
        row.style.paddingLeft = 8f;
        row.style.paddingRight = 8f;
        row.style.paddingTop = 7f;
        row.style.paddingBottom = 7f;
        row.style.marginBottom = 5f;
        row.style.backgroundColor = isHead ? new Color(0.17f, 0.36f, 0.53f, 0.55f) : new Color(0f, 0f, 0f, 0.08f);
        ApplyBorder(row);

        var version = commit != null && commit.Version != null ? commit.Version.GetFullVersionString() : "(unknown)";
        var title = new Label(isHead ? $"HEAD  {version}" : version);
        title.style.unityFontStyleAndWeight = FontStyle.Bold;
        title.style.whiteSpace = WhiteSpace.Normal;
        row.Add(title);

        row.Add(BuildPipelineUI.SmallText($"{FormatUtc(commit?.CreatedAtUtc)}  |  {SafeText(commit?.PackageName)}"));
        row.Add(BuildPipelineUI.SmallText($"Git {ShortHash(commit?.GitCommitHash)}  |  Artifacts {CountArtifacts(commit)}{(commit != null && commit.IsDirty ? "  |  Dirty" : string.Empty)}"));

        row.RegisterCallback<PointerDownEvent>(evt =>
        {
            if (evt.button != 0 || commit == null || commit.Version == null)
                return;

            _toVersionField.value = commit.Version.GetFullVersionString();
            _messageLabel.text = $"已选择 {commit.Version.GetFullVersionString()} 作为 Push To。";
            evt.StopPropagation();
        });
        return row;
    }

    private VisualElement CreatePushHistoryRow(PushHistoryEntry item)
    {
        var row = new VisualElement();
        row.style.paddingLeft = 8f;
        row.style.paddingRight = 8f;
        row.style.paddingTop = 5f;
        row.style.paddingBottom = 5f;
        row.style.marginBottom = 4f;
        row.style.backgroundColor = new Color(0f, 0f, 0f, 0.08f);
        ApplyBorder(row);

        var title = new Label($"{SafeText(item.FromVersion)} -> {SafeText(item.ToVersion)}");
        title.style.unityFontStyleAndWeight = FontStyle.Bold;
        row.Add(title);
        row.Add(BuildPipelineUI.SmallText($"{SafeText(item.TargetId)}  |  {FormatUtc(item.PushedAtUtc)}  |  {item.DeltaFileCount} files"));
        row.Add(BuildPipelineUI.SmallText(SafeText(item.TargetLocation)));
        return row;
    }

    private void RenderDelta(ArtifactDelta delta)
    {
        delta ??= new ArtifactDelta();
        _diffSummary.Clear();
        AddDiffStat("新增", delta.Added.Count, new Color(0.20f, 0.55f, 0.30f));
        AddDiffStat("修改", delta.Modified.Count, new Color(0.70f, 0.48f, 0.16f));
        AddDiffStat("删除", delta.Removed.Count, new Color(0.65f, 0.20f, 0.16f));

        _diffList.Clear();
        if (delta.IsEmpty)
        {
            _diffList.Add(CreateEmptyState("没有产物变化。"));
            return;
        }

        AddArtifactSection("新增", "+", delta.Added, new Color(0.20f, 0.55f, 0.30f));
        AddArtifactSection("修改", "*", delta.Modified, new Color(0.70f, 0.48f, 0.16f));
        AddRemovedSection(delta.Removed);
    }

    private void AddArtifactSection(string title, string marker, List<ArtifactDigest> items, Color color)
    {
        if (items == null || items.Count == 0)
            return;

        _diffList.Add(CreateSectionHeader(title, color));
        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            _diffList.Add(CreateDiffRow(marker, item != null ? item.Name : string.Empty, item != null ? FormatBytes(item.Size) : string.Empty, color));
        }
    }

    private void AddRemovedSection(List<string> items)
    {
        if (items == null || items.Count == 0)
            return;

        var color = new Color(0.65f, 0.20f, 0.16f);
        _diffList.Add(CreateSectionHeader("删除", color));
        for (int i = 0; i < items.Count; i++)
            _diffList.Add(CreateDiffRow("-", items[i], string.Empty, color));
    }

    private Label CreateSectionHeader(string text, Color color)
    {
        var label = new Label(text);
        label.style.unityFontStyleAndWeight = FontStyle.Bold;
        label.style.color = color;
        label.style.marginTop = 6f;
        label.style.marginBottom = 4f;
        return label;
    }

    private VisualElement CreateDiffRow(string marker, string name, string meta, Color color)
    {
        var row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        row.style.alignItems = Align.Center;
        row.style.paddingTop = 4f;
        row.style.paddingBottom = 4f;
        row.style.borderBottomWidth = 1f;
        row.style.borderBottomColor = new Color(BuildPipelineUI.BorderColor.r, BuildPipelineUI.BorderColor.g, BuildPipelineUI.BorderColor.b, 0.45f);

        var mark = new Label(marker);
        mark.style.width = 22f;
        mark.style.unityTextAlign = TextAnchor.MiddleCenter;
        mark.style.color = color;
        mark.style.unityFontStyleAndWeight = FontStyle.Bold;
        row.Add(mark);

        var label = new Label(SafeText(name));
        label.style.flexGrow = 1f;
        label.style.minWidth = 0f;
        label.style.whiteSpace = WhiteSpace.Normal;
        row.Add(label);

        if (!string.IsNullOrEmpty(meta))
        {
            var metaLabel = BuildPipelineUI.SmallText(meta);
            metaLabel.style.marginLeft = 8f;
            row.Add(metaLabel);
        }

        return row;
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

        var titleLabel = BuildPipelineUI.SmallText(title);
        box.Add(titleLabel);
        var valueLabel = new Label(value);
        valueLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
        valueLabel.style.whiteSpace = WhiteSpace.Normal;
        box.Add(valueLabel);
        parent.Add(box);
        return valueLabel;
    }

    private void AddDiffStat(string title, int count, Color color)
    {
        var stat = CreateBadge($"{title} {count}", color);
        stat.style.marginRight = 6f;
        _diffSummary.Add(stat);
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
        var label = BuildPipelineUI.SmallText(text);
        label.style.paddingLeft = 8f;
        label.style.paddingRight = 8f;
        label.style.paddingTop = 8f;
        label.style.paddingBottom = 8f;
        label.style.backgroundColor = new Color(0f, 0f, 0f, 0.08f);
        return label;
    }

    private void SetBadge(string text, Color color)
    {
        _statusBadge.text = text;
        _statusBadge.style.backgroundColor = color;
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

        var label = field.Q<Label>();
        if (label == null)
            return;

        label.style.minWidth = labelWidth;
        label.style.width = labelWidth;
        label.style.marginRight = 4f;
        label.style.flexShrink = 0f;
    }

    #endregion

    #region Data Helpers

    private static BuildPackageRequest CreatePreviewRequest()
    {
        var versionDB = AssetDatabase.LoadAssetAtPath<VersionDataBase>(FYAssetSettings.Instance.VersionDataBasePath);
        var version = versionDB != null && versionDB.CurrentVersion != null
            ? versionDB.CurrentVersion
            : new VersionNumber { Major = 0, Minor = 0, Patch = 0 };
        var backendMode = FYAssetSettings.Instance.UseABBackend ? BackendMode.ABManifest : BackendMode.AA;
        return BuildPackageRequest.Create(version, BuildType.Full, backendMode);
    }

    private static VersionNumber ParseVersion(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : VersionNumber.Parse(value);
    }

    private IPushTarget CreatePushTarget()
    {
        var settings = FYAssetSettings.Instance;
        string targetId = _targetDropdown != null && !string.IsNullOrEmpty(_targetDropdown.value)
            ? _targetDropdown.value
            : (settings.PushTargets != null && settings.PushTargets.Count > 0 ? settings.PushTargets[0].Id : string.Empty);
        for (int i = 0; settings.PushTargets != null && i < settings.PushTargets.Count; i++)
        {
            var config = settings.PushTargets[i];
            if (config != null && string.Equals(config.Id, targetId, StringComparison.OrdinalIgnoreCase))
                return new LocalDirectoryPushTarget(config);
        }
        throw new InvalidOperationException("No push target configured.");
    }

    private static List<string> GetPushTargetLabels()
    {
        var labels = new List<string>();
        var settings = FYAssetSettings.Instance;
        for (int i = 0; settings.PushTargets != null && i < settings.PushTargets.Count; i++)
        {
            var config = settings.PushTargets[i];
            if (config != null && !string.IsNullOrEmpty(config.Id))
                labels.Add(config.Id);
        }
        if (labels.Count == 0)
            labels.Add("(none)");
        return labels;
    }

    private bool IsHeadCommit(RepositoryCommit commit)
    {
        if (commit == null || commit.Version == null || _status == null)
            return false;
        return string.Equals(commit.Version.GetFullVersionString(), _status.HeadVersion, StringComparison.Ordinal);
    }

    private static int CountArtifacts(RepositoryCommit commit)
    {
        return commit != null && commit.Artifacts != null ? commit.Artifacts.Count : 0;
    }

    private static string FormatUtc(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "-";

        if (DateTime.TryParse(value, null, DateTimeStyles.RoundtripKind, out var date))
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

    #endregion
}
#endif

