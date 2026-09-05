using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// BuildPipeline 面板。
/// 负责构建触发、Build Options 编辑以及只读 Task 顺序列表展示。
/// </summary>
public class PipelinePanel : IBuildPipelinePanel, IBuildPipelinePanelVisibility
{
    private readonly string _panelName;
    private readonly Func<string> _configPathGetter;
    private readonly Func<System.Collections.Generic.List<TaskEntry>> _defaultTasksFactory;
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

    public PipelinePanel(
        string panelName,
        Func<string> configPathGetter,
        Func<System.Collections.Generic.List<TaskEntry>> defaultTasksFactory,
        string logPrefix,
        bool showBuildOptions,
        bool showBuildControls,
        BuildPanelActions actions)
    {
        _panelName = panelName;
        _configPathGetter = configPathGetter;
        _defaultTasksFactory = defaultTasksFactory;
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

    /// <summary>
    /// 按当前 BuildPipelineConfig 重建面板内容。
    /// </summary>
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
        RefreshStatus();
    }

    /// <summary>
    /// 绘制顶部工具栏：重载、Build Mode、构建按钮和状态文本。
    /// </summary>
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

    /// <summary>
    /// 绘制 Build Options 行，并绑定到 BuildPipelineConfig。
    /// </summary>
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

        PropertyField fileNameStyle = new PropertyField(_serializedConfig.FindProperty(nameof(BuildPipelineConfig.FileNameStyle)));
        fileNameStyle.label = string.Empty;
        fileNameStyle.style.minWidth = 180f;
        _optionsRow.Add(fileNameStyle);

        PropertyField compression = new PropertyField(_serializedConfig.FindProperty(nameof(BuildPipelineConfig.BundleCompression)));
        compression.label = string.Empty;
        compression.style.width = 130f;
        _optionsRow.Add(compression);

        _optionsRow.Add(BuildPipelineUI.Spacer());
        _optionsRow.Bind(_serializedConfig);
        _root.Add(_optionsRow);
    }

    /// <summary>
    /// 绘制当前配置中的 Task 顺序列表；从上到下即执行顺序。
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

        List<TaskEntry> tasks = _config.Tasks;
        if (tasks == null || tasks.Count == 0)
        {
            _taskListScroll.Add(BuildPipelineUI.SmallText("No tasks configured."));
            _root.Add(_taskListHost);
            return;
        }

        for (int i = 0; i < tasks.Count; i++)
            AddTaskRow(i, tasks[i]);

        _root.Add(_taskListHost);
    }

    private void AddTaskRow(int index, TaskEntry entry)
    {
        string taskName = entry?.TaskName ?? string.Empty;
        bool resolved = !string.IsNullOrWhiteSpace(taskName)
            && BuildTaskResolver.Exists(taskName);

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

        Label nameLabel = new Label(string.IsNullOrEmpty(taskName) ? "<empty>" : taskName);
        nameLabel.style.flexGrow = 1f;
        nameLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
        row.Add(nameLabel);

        Label stateLabel = BuildPipelineUI.SmallText(resolved ? "Resolved" : "Unresolved");
        stateLabel.style.width = 90f;
        stateLabel.style.flexShrink = 0f;
        stateLabel.style.unityTextAlign = TextAnchor.MiddleRight;
        stateLabel.style.color = resolved ? BuildPipelineUI.SecondaryTextColor : Color.red;
        row.Add(stateLabel);

        _taskListScroll.Add(row);

        var rowState = new TaskRowState(statusDot);
        SetTaskRowStatus(rowState, null);
        if (!string.IsNullOrEmpty(taskName))
            _taskRows[taskName] = rowState;
    }

    /// <summary>
    /// 加载 BuildPipelineConfig。
    /// </summary>
    private void LoadConfig()
    {
        _config = AssetDatabase.LoadAssetAtPath<BuildPipelineConfig>(GetConfigPath());
        if (_config != null)
        {
            _serializedConfig = new SerializedObject(_config);
        }
        else
        {
            _serializedConfig = null;
        }
    }

    /// <summary>
    /// 按当前 Build Mode 触发构建。
    /// </summary>
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
            TaskStatusChanged = OnTaskStatusChanged
        };

        try
        {
            switch (_buildMode)
            {
                case BuildType.Full:
                    BuildFullPackage(options);
                    break;
                case BuildType.Hotfix:
                    BuildHotfix(options);
                    break;
                case BuildType.Standalone when _actions.BuildStandalone != null:
                    _actions.BuildStandalone(options);
                    break;
                default:
                    throw new InvalidOperationException($"{_panelName} 不支持 {_buildMode} 构建。");
            }

            bool success = LastBuildSuccess();
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
        }
    }

    private void BuildFullPackage(BuildExecutionOptions options) => _actions.BuildFull(options);

    private void BuildHotfix(BuildExecutionOptions options) => _actions.BuildHotfix(options);

    private bool LastBuildSuccess() => _actions.LastBuildSuccess();

    /// <summary>
    /// 将构建过程中的单任务执行状态同步到顺序列表行。
    /// </summary>
    private void OnTaskStatusChanged(BuildTaskExecutionEvent evt)
    {
        if (!string.IsNullOrEmpty(evt.TaskName) && _taskRows.TryGetValue(evt.TaskName, out TaskRowState row))
            SetTaskRowStatus(row, evt.Status);

        _window?.Repaint();
    }

    /// <summary>
    /// 刷新任务计数摘要。
    /// </summary>
    private void RefreshStatus()
    {
        int taskCount = _config?.Tasks?.Count ?? 0;
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

    /// <summary>
    /// 构建运行时统一禁用可编辑控件，避免并发修改配置。
    /// </summary>
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

    /// <summary>
    /// BuildPipelineConfig 缺失时显示创建入口。
    /// </summary>
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
    /// </summary>
    private void CreateConfig()
    {
        BuildPipelineUI.EnsureAssetParentFolder(GetConfigPath());

        var config = ScriptableObject.CreateInstance<BuildPipelineConfig>();
        config.Tasks = _defaultTasksFactory();
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
