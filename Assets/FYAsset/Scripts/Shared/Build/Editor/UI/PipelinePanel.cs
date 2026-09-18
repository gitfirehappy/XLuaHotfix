using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// BuildPipeline 面板。
/// 负责构建触发、Build Options 编辑，以及用 Composer 结果渲染本次将执行的 Task 顺序。
/// </summary>
/// <remarks>
/// 面板不再编辑 Task 顺序：主干阶段由后端 PipelineBackbone 固定定义，
/// 配置只能声明自定义 Task 的插入槽位。展示时使用与后端完全相同的 Compose，
/// 组装失败直接把异常显示为红字，避免面板与真实执行顺序不一致。
/// </remarks>
public class PipelinePanel : IBuildPipelinePanel, IBuildPipelinePanelVisibility
{
    private readonly string _panelName;
    private readonly Func<string> _configPathGetter;
    private readonly Func<IReadOnlyList<CoreTaskSlot>> _coreSlotsFactory;
    private readonly Func<BuildPipelineConfig, bool> _configUpgrader;
    private readonly Func<CompleteBuildSummary> _lastSummaryProvider;
    private readonly string _logPrefix;
    private readonly bool _showBuildOptions;
    private readonly bool _showBuildControls;
    private readonly BuildPanelActions _actions;

    private BuildPipelineConfig _config;
    private SerializedObject _serializedConfig;
    private EditorWindow _window;
    private VisualElement _root;
    private VisualElement _optionsRow;
    private VisualElement _taskListHost;
    private ScrollView _taskListScroll;
    private readonly Dictionary<string, TaskRowState> _taskRows = new(StringComparer.Ordinal);
    private Label _taskStatusLabel;
    private Label _buildStatusLabel;
    private DropdownField _buildModeField;
    private BuildType _buildMode = BuildType.Hotfix;
    private bool _isBuildRunning;

    /// <summary>构建确认行的通道选择：0 = 继承当前全局通道，其余为显式选择。</summary>
    private static readonly string[] ChannelSelections = { "inherit", "alpha", "beta", "rc", "release" };
    private int _channelSelectionIndex;

    public PipelinePanel(
        string panelName,
        Func<string> configPathGetter,
        Func<IReadOnlyList<CoreTaskSlot>> coreSlotsFactory,
        string logPrefix,
        bool showBuildOptions,
        bool showBuildControls,
        BuildPanelActions actions,
        Func<BuildPipelineConfig, bool> configUpgrader = null,
        Func<CompleteBuildSummary> lastSummaryProvider = null)
    {
        _panelName = panelName;
        _configPathGetter = configPathGetter;
        _coreSlotsFactory = coreSlotsFactory;
        _configUpgrader = configUpgrader;
        _lastSummaryProvider = lastSummaryProvider;
        _logPrefix = logPrefix;
        _showBuildOptions = showBuildOptions;
        _showBuildControls = showBuildControls;
        _actions = actions ?? throw new ArgumentNullException(nameof(actions));
    }

    public string PanelName => _panelName;

    public void OnEnable(EditorWindow window)
    {
        _window = window;
        LoadConfig();
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
        _root?.Unbind();
        _root = null;
        _taskRows.Clear();
    }

    public void SetVisible(bool visible)
    {
    }

    /// <summary>按当前 BuildPipelineConfig 重建面板内容。</summary>
    private void Rebuild()
    {
        if (_root == null)
            return;

        _root.Clear();
        _root.Unbind();
        _taskListHost = null;
        _taskListScroll = null;
        _taskRows.Clear();

        DrawTopBar();

        if (_config == null)
        {
            DrawNoConfig();
            return;
        }

        if (_showBuildOptions)
            DrawBuildOptionsBar();

        DrawTaskList();
        DrawSummary();
        RefreshStatus();
    }

    /// <summary>绘制顶部工具栏：重载、Build Mode、构建按钮和状态文本。</summary>
    private void DrawTopBar()
    {
        VisualElement toolbar = BuildPipelineUI.Toolbar();
        toolbar.Add(BuildPipelineUI.ToolbarButton("Refresh", () =>
        {
            LoadConfig();
            Rebuild();
        }, 60f));
        if (_showBuildControls)
        {
            toolbar.Add(BuildPipelineUI.ToolbarLabel("Mode"));

            var buildModes = new List<string>
            {
                BuildType.Full.ToString(),
                BuildType.Hotfix.ToString()
            };
            if (_actions.BuildStandalone != null)
                buildModes.Add(BuildType.Standalone.ToString());

            _buildModeField = new DropdownField(buildModes, _buildMode.ToString());
            _buildModeField.style.width = 84f;
            _buildModeField.RegisterValueChangedCallback(evt =>
                _buildMode = (BuildType)Enum.Parse(typeof(BuildType), evt.newValue));
            toolbar.Add(_buildModeField);

            toolbar.Add(BuildPipelineUI.ToolbarButton("Build", HandleBuild, 56f));
        }
        else
        {
            _buildModeField = null;
        }

        toolbar.Add(BuildPipelineUI.Spacer());

        _taskStatusLabel = BuildPipelineUI.ToolbarLabel("0/0 任务");
        _taskStatusLabel.style.width = 120f;
        toolbar.Add(_taskStatusLabel);

        _buildStatusLabel = BuildPipelineUI.ToolbarLabel(string.Empty);
        _buildStatusLabel.style.minWidth = 120f;
        toolbar.Add(_buildStatusLabel);
        _root.Add(toolbar);
    }

    /// <summary>绘制 Build Options 行，并绑定到 BuildPipelineConfig。</summary>
    private void DrawBuildOptionsBar()
    {
        if (_serializedConfig == null)
            return;

        _optionsRow = BuildPipelineUI.Card();
        _optionsRow.style.flexDirection = FlexDirection.Row;
        _optionsRow.style.alignItems = Align.Center;
        _optionsRow.style.paddingTop = 4f;
        _optionsRow.style.paddingBottom = 4f;

        Label label = BuildPipelineUI.Header("Build");
        label.style.width = 96f;
        label.style.marginBottom = 0f;
        _optionsRow.Add(label);

        PropertyField compression = new PropertyField(_serializedConfig.FindProperty(nameof(BuildPipelineConfig.BundleCompression)));
        compression.label = string.Empty;
        compression.style.width = 130f;
        _optionsRow.Add(compression);

        var channel = new PopupField<string>("Channel", new List<string>(ChannelSelections), _channelSelectionIndex);
        channel.label = string.Empty;
        channel.style.width = 120f;
        channel.tooltip = "继承当前全局通道，或显式选择 alpha/beta/rc/release；禁止通道降级。";
        channel.RegisterValueChangedCallback(evt =>
        {
            _channelSelectionIndex = Mathf.Max(0, Array.IndexOf(ChannelSelections, evt.newValue));
        });
        _optionsRow.Add(channel);

        _optionsRow.Add(BuildPipelineUI.Spacer());
        _optionsRow.Bind(_serializedConfig);
        _root.Add(_optionsRow);
    }

    /// <summary>
    /// 用 Composer 渲染本次将要执行的 Task 顺序：主干阶段 + 各槽位自定义 Task。
    /// 组装失败时显示红字错误，并退化为“主干 + 原始配置条目”的只读展示。
    /// </summary>
    private void DrawTaskList()
    {
        _taskListHost = new VisualElement();
        _taskListHost.style.flexGrow = 1f;
        _taskListHost.style.backgroundColor = BuildPipelineUI.WindowBackgroundColor;
        _taskListHost.style.paddingLeft = 8f;
        _taskListHost.style.paddingRight = 8f;
        _taskListHost.style.paddingTop = 8f;
        _taskListHost.style.paddingBottom = 8f;

        _taskListScroll = new ScrollView();
        _taskListScroll.style.flexGrow = 1f;
        _taskListHost.Add(_taskListScroll);

        IReadOnlyList<CoreTaskSlot> coreSlots = _coreSlotsFactory != null
            ? _coreSlotsFactory()
            : Array.Empty<CoreTaskSlot>();

        try
        {
            IReadOnlyList<IBuildTask> composed = BuildPipelineComposer.Compose(coreSlots, _config.Tasks);
            var coreNames = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < coreSlots.Count; i++)
                coreNames.Add(coreSlots[i].Slot);

            for (int i = 0; i < composed.Count; i++)
            {
                string taskName = composed[i].TaskName;
                AddTaskRow(i, taskName, coreNames.Contains(taskName) ? "Core" : "Custom");
            }
        }
        catch (Exception ex)
        {
            AddComposeError(ex);
            AddFallbackRows(coreSlots);
        }

        _root.Add(_taskListHost);
    }

    /// <summary>组装失败：红字显示错误码与原因，不做任何静默降级。</summary>
    private void AddComposeError(Exception ex)
    {
        var error = BuildPipelineUI.SmallText("Compose failed: " + ex.Message);
        error.style.color = Color.red;
        error.style.whiteSpace = WhiteSpace.Normal;
        error.style.paddingBottom = 6f;
        _taskListScroll.Add(error);
    }

    /// <summary>组装失败时的兜底展示：先列主干阶段，再列配置里的原始自定义条目。</summary>
    private void AddFallbackRows(IReadOnlyList<CoreTaskSlot> coreSlots)
    {
        int index = 0;
        for (int i = 0; i < coreSlots.Count; i++)
            AddTaskRow(index++, coreSlots[i].Slot, "Core");

        List<CustomTaskEntry> configured = _config.Tasks;
        if (configured == null)
            return;

        for (int i = 0; i < configured.Count; i++)
        {
            string name = string.IsNullOrWhiteSpace(configured[i].TaskName) ? "<empty>" : configured[i].TaskName;
            AddTaskRow(index++, name, "Custom@" + (configured[i].Slot ?? "<empty>"));
        }
    }

    private void AddTaskRow(int index, string taskName, string tag)
    {
        bool resolved = !string.IsNullOrWhiteSpace(taskName) && BuildTaskResolver.Exists(taskName);
        bool isCore = string.Equals(tag, "Core", StringComparison.Ordinal);

        var row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        row.style.alignItems = Align.Center;
        row.style.minHeight = 30f;
        row.style.paddingLeft = 8f;
        row.style.paddingRight = 8f;
        row.style.borderBottomWidth = 1f;
        row.style.borderBottomColor = BuildPipelineUI.BorderColor;

        Label indexLabel = BuildPipelineUI.SmallText((index + 1).ToString("00"));
        indexLabel.style.width = 36f;
        indexLabel.style.flexShrink = 0f;
        row.Add(indexLabel);

        var statusDot = new VisualElement();
        statusDot.style.width = 10f;
        statusDot.style.height = 10f;
        statusDot.style.marginRight = 10f;
        statusDot.style.borderTopLeftRadius = 5f;
        statusDot.style.borderTopRightRadius = 5f;
        statusDot.style.borderBottomLeftRadius = 5f;
        statusDot.style.borderBottomRightRadius = 5f;
        row.Add(statusDot);

        Label nameLabel = new Label(taskName);
        nameLabel.style.flexGrow = 1f;
        nameLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
        row.Add(nameLabel);

        Label tagLabel = BuildPipelineUI.SmallText(tag);
        tagLabel.style.width = 110f;
        tagLabel.style.flexShrink = 0f;
        tagLabel.style.unityTextAlign = TextAnchor.MiddleRight;
        tagLabel.style.color = BuildPipelineUI.SecondaryTextColor;
        row.Add(tagLabel);

        Label stateLabel = BuildPipelineUI.SmallText(resolved ? "Resolved" : "Unresolved");
        stateLabel.style.width = 90f;
        stateLabel.style.flexShrink = 0f;
        stateLabel.style.unityTextAlign = TextAnchor.MiddleRight;
        // 主干 Task 由后端直接实例化，因此未在 resolver 注册属正常；自定义 Task 必须可解析。
        stateLabel.style.color = resolved || isCore ? BuildPipelineUI.SecondaryTextColor : Color.red;
        if (isCore && !resolved)
            stateLabel.text = "Core";
        row.Add(stateLabel);

        _taskListScroll.Add(row);

        var rowState = new TaskRowState(statusDot);
        SetTaskRowStatus(rowState, null);
        if (!string.IsNullOrEmpty(taskName) && !_taskRows.ContainsKey(taskName))
            _taskRows[taskName] = rowState;
    }

    /// <summary>
    /// 构建结果摘要区：CompleteBuildSummary 是构建结果面板的唯一数据源。
    /// </summary>
    private void DrawSummary()
    {
        CompleteBuildSummary summary = _lastSummaryProvider?.Invoke();
        if (summary == null)
            return;

        VisualElement card = BuildPipelineUI.Card();
        card.style.flexShrink = 0f;
        card.Add(BuildPipelineUI.SmallText(
            $"{summary.BuildId} | {summary.BackendId} | {summary.BuildType}/{summary.RuntimeMode} | "
            + $"v{summary.Version.GetReleaseVersionString()} | {summary.Platform} | {summary.Duration.TotalSeconds:F1}s | "
            + $"Files {summary.Statistics.FileCount} | Contents {summary.Statistics.ContentCount} | "
            + $"Messages {summary.Messages.Count} | Success {summary.Success}"));

        Label status = BuildPipelineUI.SmallText(summary.Success ? "Last build succeeded." : "Last build failed.");
        status.style.color = summary.Success ? new Color(0.3f, 1f, 0.3f) : Color.red;
        card.Add(status);
        _root.Add(card);
    }

    private void LoadConfig()
    {
        _config = AssetDatabase.LoadAssetAtPath<BuildPipelineConfig>(GetConfigPath());
        if (_config != null)
        {
            _configUpgrader?.Invoke(_config);
            _serializedConfig = new SerializedObject(_config);
        }
        else
        {
            _serializedConfig = null;
        }
    }

    private void HandleBuild()
    {
        if (_config == null || _isBuildRunning)
            return;

        _isBuildRunning = true;
        SetBuildStatus("构建中", new Color(1f, 0.85f, 0.3f));
        ResetTaskStatuses();
        SetRunningEnabled(false);

        var options = new BuildExecutionOptions
        {
            TaskStatusChanged = OnTaskStatusChanged,
            // 未选择时保持 null，由版本规划器继承当前全局通道，不被默认参数清空。
            RequestedChannel = _channelSelectionIndex <= 0 ? null : ChannelSelections[_channelSelectionIndex]
        };

        try
        {
            BuildResult result;
            switch (_buildMode)
            {
                case BuildType.Full:
                    result = _actions.BuildFull(options);
                    break;
                case BuildType.Hotfix:
                    result = _actions.BuildHotfix(options);
                    break;
                case BuildType.Standalone when _actions.BuildStandalone != null:
                    result = _actions.BuildStandalone(options);
                    break;
                default:
                    throw new InvalidOperationException($"{_panelName} 不支持 {_buildMode} 构建。");
            }

            bool success = result != null && result.Success;
            SetBuildStatus(success ? "构建完成" : "构建失败",
                success ? new Color(0.3f, 1f, 0.3f) : Color.red);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[{_logPrefix}] 构建失败: {ex}");
            SetBuildStatus("构建异常", Color.red);
        }
        finally
        {
            _isBuildRunning = false;
            SetRunningEnabled(true);
            Rebuild();
        }
    }

    private void BuildFullPackage(BuildExecutionOptions options) => _actions.BuildFull(options);

    private void BuildHotfix(BuildExecutionOptions options) => _actions.BuildHotfix(options);

    /// <summary>将构建过程中的单任务执行状态同步到顺序列表行。</summary>
    private void OnTaskStatusChanged(BuildTaskExecutionEvent evt)
    {
        if (!string.IsNullOrEmpty(evt.TaskName) && _taskRows.TryGetValue(evt.TaskName, out TaskRowState row))
            SetTaskRowStatus(row, evt.Status);

        _window?.Repaint();
    }

    private void RefreshStatus()
    {
        int taskCount = 0;
        try
        {
            IReadOnlyList<CoreTaskSlot> coreSlots = _coreSlotsFactory != null
                ? _coreSlotsFactory()
                : Array.Empty<CoreTaskSlot>();
            taskCount = BuildPipelineComposer.Compose(coreSlots, _config?.Tasks).Count;
        }
        catch (Exception)
        {
            // 组装失败的具体原因已经在列表区显示，这里只退化为 0。
        }

        if (_taskStatusLabel != null)
            _taskStatusLabel.text = $"{taskCount} tasks";
    }

    private void SetBuildStatus(string text, Color color)
    {
        if (_buildStatusLabel == null)
            return;

        _buildStatusLabel.text = text;
        _buildStatusLabel.style.color = color;
    }

    /// <summary>构建运行时统一禁用可编辑控件，避免并发修改配置。</summary>
    private void SetRunningEnabled(bool enabled)
    {
        _buildModeField?.SetEnabled(enabled);
        _optionsRow?.SetEnabled(enabled);
        _taskListHost?.SetEnabled(enabled);
    }

    private void ResetTaskStatuses()
    {
        foreach (TaskRowState row in _taskRows.Values)
            SetTaskRowStatus(row, null);
    }

    private static void SetTaskRowStatus(TaskRowState row, BuildTaskExecutionStatus? status)
    {
        if (row?.StatusDot == null)
            return;

        row.StatusDot.style.backgroundColor = status switch
        {
            BuildTaskExecutionStatus.Pending => new Color(0.28f, 0.28f, 0.28f),
            BuildTaskExecutionStatus.Running => new Color(1f, 0.85f, 0.2f),
            BuildTaskExecutionStatus.Success => new Color(0.25f, 0.85f, 0.35f),
            BuildTaskExecutionStatus.Failed => new Color(0.95f, 0.2f, 0.2f),
            BuildTaskExecutionStatus.Skipped => new Color(0.45f, 0.45f, 0.45f),
            _ => new Color(0.42f, 0.42f, 0.42f)
        };

        row.StatusDot.tooltip = status?.ToString() ?? "Idle";
    }

    /// <summary>BuildPipelineConfig 缺失时显示创建入口。</summary>
    private void DrawNoConfig()
    {
        VisualElement panel = BuildPipelineUIToolkitPanel.CreateCenteredPanel(_root, 460f);
        panel.Add(BuildPipelineUIToolkitPanel.CreateBody("未找到 BuildPipelineConfig: " + GetConfigPath()));
        panel.Add(new Button(CreateConfig)
        {
            text = "Create"
        });
    }

    /// <summary>
    /// 创建新的 BuildPipelineConfig 资产并立即加载。
    /// 主干阶段不由配置声明，因此新配置的自定义 Task 列表为空。
    /// </summary>
    private void CreateConfig()
    {
        BuildPipelineUI.EnsureAssetParentFolder(GetConfigPath());

        var config = ScriptableObject.CreateInstance<BuildPipelineConfig>();
        config.Tasks = new List<CustomTaskEntry>();
        AssetDatabase.CreateAsset(config, GetConfigPath());
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        LoadConfig();
        Rebuild();
    }

    private string GetConfigPath()
    {
        return _configPathGetter();
    }

    private sealed class TaskRowState
    {
        public TaskRowState(VisualElement statusDot)
        {
            StatusDot = statusDot;
        }

        public VisualElement StatusDot { get; }
    }
}
