using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// BuildPipeline 面板。
/// 负责构建触发、配置校验、Build Options 编辑以及只读 Task 顺序列表展示。
/// </summary>
public class PipelinePanel : IBuildPipelinePanel, IBuildPipelinePanelVisibility
{
    private readonly string _panelName;
    private readonly Func<string> _configPathGetter;
    private readonly Func<System.Collections.Generic.List<TaskEntry>> _defaultTasksFactory;
    private readonly string _logPrefix;
    private readonly bool _showBuildOptions;
    private readonly bool _showBuildControls;
    private readonly BackendMode _backendMode;

    private BuildPipelineConfig _config;
    private SerializedObject _serializedConfig;
    private EditorWindow _window;
    private VisualElement _root;
    private VisualElement _optionsRow;
    private VisualElement _taskListHost;
    private ScrollView _taskListScroll;
    private VisualElement _validationSplitter;
    private VisualElement _validationDetailPane;
    private ScrollView _validationDetailScroll;
    private TextField _validationDetailText;
    private readonly Dictionary<string, TaskRowState> _taskRows = new(StringComparer.Ordinal);
    private Label _taskStatusLabel;
    private Label _validationStatusLabel;
    private EnumField _buildModeField;
    private BuildType _buildMode = BuildType.Hotfix;
    private bool _isBuildRunning;
    private bool _validationDetailVisible;
    private string _validationDetail = string.Empty;
    private string _validationSummaryText = string.Empty;
    private Color _validationSummaryColor = BuildPipelineUI.SecondaryTextColor;

    public PipelinePanel()
        : this(
            "Pipeline",
            () => FYAssetBuildSettingsProvider.AB.BuildPipelineConfigPath,
            BuildPipelineBackbone.CreateABTasks,
            "PipelinePanel",
            true,
            true,
            BackendMode.ABManifest)
    {
    }

    public PipelinePanel(
        string panelName,
        Func<string> configPathGetter,
        Func<System.Collections.Generic.List<TaskEntry>> defaultTasksFactory,
        string logPrefix,
        bool showBuildOptions,
        bool showBuildControls)
        : this(
            panelName,
            configPathGetter,
            defaultTasksFactory,
            logPrefix,
            showBuildOptions,
            showBuildControls,
            BackendMode.ABManifest)
    {
    }

    public PipelinePanel(
        string panelName,
        Func<string> configPathGetter,
        Func<System.Collections.Generic.List<TaskEntry>> defaultTasksFactory,
        string logPrefix,
        bool showBuildOptions,
        bool showBuildControls,
        BackendMode backendMode)
    {
        _panelName = panelName;
        _configPathGetter = configPathGetter;
        _defaultTasksFactory = defaultTasksFactory;
        _logPrefix = logPrefix;
        _showBuildOptions = showBuildOptions;
        _showBuildControls = showBuildControls;
        _backendMode = backendMode;
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
        _validationSplitter = null;
        _validationDetailPane = null;
        _validationDetailScroll = null;
        _validationDetailText = null;
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
        DrawValidationDetailBar();
        RefreshStatus();
    }

    /// <summary>
    /// 绘制顶部工具栏：重载、校验、Build Mode、构建按钮和状态文本。
    /// </summary>
    private void DrawTopBar()
    {
        VisualElement toolbar = BuildPipelineUI.Toolbar();
        toolbar.Add(BuildPipelineUI.ToolbarButton("Refresh", () =>
        {
            LoadConfig();
            Rebuild();
        }, 60f));
        toolbar.Add(BuildPipelineUI.ToolbarButton("Validate", HandleValidate, 70f));

        if (_showBuildControls)
        {
            toolbar.Add(BuildPipelineUI.ToolbarLabel("Mode"));

            _buildModeField = new EnumField(_buildMode);
            _buildModeField.style.width = 84f;
            _buildModeField.RegisterValueChangedCallback(evt => _buildMode = (BuildType)evt.newValue);
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

        _validationStatusLabel = BuildPipelineUI.ToolbarLabel(string.Empty);
        _validationStatusLabel.style.minWidth = 120f;
        _validationStatusLabel.text = _validationSummaryText;
        _validationStatusLabel.style.color = _validationSummaryColor;
        toolbar.Add(_validationStatusLabel);
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
    /// 绘制按需显示的 Validate 明细底栏，提供关闭和复制入口。
    /// </summary>
    private void DrawValidationDetailBar()
    {
        if (!_validationDetailVisible)
            return;

        _validationSplitter = BuildPipelineUI.Splitter(false);
        _root.Add(_validationSplitter);

        _validationDetailPane = new VisualElement();
        _validationDetailPane.style.height = 150f;
        _validationDetailPane.style.minHeight = 86f;
        _validationDetailPane.style.flexShrink = 0f;
        _validationDetailPane.style.flexDirection = FlexDirection.Column;
        _validationDetailPane.style.borderTopWidth = 1f;
        _validationDetailPane.style.borderTopColor = BuildPipelineUI.BorderColor;
        _validationDetailPane.style.backgroundColor = BuildPipelineUI.CardBackgroundColor;

        VisualElement toolbar = BuildPipelineUI.Toolbar();
        toolbar.Add(BuildPipelineUI.ToolbarLabel("Validation Details"));
        toolbar.Add(BuildPipelineUI.Spacer());
        toolbar.Add(BuildPipelineUI.ToolbarButton("Copy", () =>
        {
            EditorGUIUtility.systemCopyBuffer = _validationDetail ?? string.Empty;
        }, 48f));
        toolbar.Add(BuildPipelineUI.ToolbarButton("Close", () =>
        {
            _validationDetailVisible = false;
            Rebuild();
        }, 52f));
        _validationDetailPane.Add(toolbar);

        _validationDetailText = new TextField { multiline = true, value = _validationDetail ?? string.Empty };
        _validationDetailText.isReadOnly = true;
        _validationDetailText.style.marginLeft = 4f;
        _validationDetailText.style.marginRight = 4f;
        _validationDetailText.style.marginBottom = 4f;

        _validationDetailScroll = new ScrollView();
        _validationDetailScroll.style.flexGrow = 1f;
        _validationDetailScroll.style.minHeight = 0f;
        _validationDetailScroll.Add(_validationDetailText);
        _validationDetailPane.Add(_validationDetailScroll);
        _root.Add(_validationDetailPane);
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
    /// 先执行 Pipeline 校验，再按当前 Build Mode 触发 Full 或 Hotfix 构建。
    /// </summary>
    private void HandleBuild()
    {
        if (_config == null || _isBuildRunning)
            return;

        BuildResult validation;
        try
        {
            validation = BuildPipelineRunner.Validate(_config);
            SetValidationStatus(validation);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[{_logPrefix}] 构建前校验失败: {ex}");
            SetValidationText("校验失败", Color.red);
            ShowValidationDetail($"构建前校验异常:{Environment.NewLine}{ex}");
            return;
        }

        if (validation == null || !validation.Success)
        {
            Debug.LogError($"[{_logPrefix}] 构建被校验失败阻断。");
            return;
        }

        _isBuildRunning = true;
            SetValidationText("构建中", new Color(1f, 0.85f, 0.3f));
        ResetTaskStatuses();
        SetRunningEnabled(false);

        var options = new BuildExecutionOptions
        {
            TaskStatusChanged = OnTaskStatusChanged
        };

        try
        {
            if (_buildMode == BuildType.Full)
                BuildFullPackage(options);
            else
                BuildHotfix(options);

            bool success = LastBuildSuccess();
            SetValidationText(success ? "构建完成" : "构建失败",
                success ? new Color(0.3f, 1f, 0.3f) : Color.red);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[{_logPrefix}] 构建失败: {ex}");
            SetValidationText("构建异常", Color.red);
            ShowValidationDetail($"构建异常:{Environment.NewLine}{ex}");
        }
        finally
        {
            _isBuildRunning = false;
            SetRunningEnabled(true);
        }
    }

    private void BuildFullPackage(BuildExecutionOptions options)
    {
        if (_backendMode == BackendMode.ABManifest)
            ABBuildProjectManager.BuildFullPackage(options);
        else
            AABuildProjectManager.BuildFullPackage(options);
    }

    private void BuildHotfix(BuildExecutionOptions options)
    {
        if (_backendMode == BackendMode.ABManifest)
            ABBuildProjectManager.BuildHotfix(options);
        else
            AABuildProjectManager.BuildHotfix(options);
    }

    private bool LastBuildSuccess()
    {
        return _backendMode == BackendMode.ABManifest
            ? ABBuildProjectManager.LastBuildSuccess
            : AABuildProjectManager.LastBuildSuccess;
    }

    /// <summary>
    /// 执行一次显式校验，并把结果写到状态栏。
    /// </summary>
    private void HandleValidate()
    {
        if (_config == null)
        {
            SetValidationText("校验失败", Color.red);
            ShowValidationDetail("校验失败：BuildPipelineConfig 为空。");
            return;
        }

        try
        {
            SetValidationStatus(BuildPipelineRunner.Validate(_config));
        }
        catch (Exception ex)
        {
            Debug.LogError($"[{_logPrefix}] 校验失败: {ex.Message}");
            SetValidationText("校验失败", Color.red);
            ShowValidationDetail($"Validation exception:{Environment.NewLine}{ex}");
        }
    }

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

    /// <summary>
    /// 根据 BuildResult 更新顶部校验状态文本。
    /// </summary>
    private void SetValidationStatus(BuildResult result)
    {
        if (result == null)
        {
            SetValidationText("校验失败", Color.red);
            ShowValidationDetail("校验失败：BuildPipelineRunner 返回空结果。");
            return;
        }

        if (result.Success)
        {
            int warnings = result.TaskResults?.FindAll(r => !r.Success).Count ?? 0;
            SetValidationText(warnings > 0 ? $"{warnings} 警告" : $"{result.TotalTasks} 任务通过",
                warnings > 0 ? new Color(1f, 0.85f, 0.3f) : new Color(0.3f, 1f, 0.3f));
            ShowValidationDetail(BuildValidationDetail(result));
            return;
        }

        var errors = result.TaskResults?.FindAll(r => r.IsFatal);
        if (errors != null && errors.Count > 0)
        {
            string first = errors[0].ErrorMessage;
            SetValidationText(first.Length > 60 ? first.Substring(0, 57) + "..." : first, Color.red);
        }
        else
        {
            SetValidationText("校验失败", Color.red);
        }

        ShowValidationDetail(BuildValidationDetail(result));
    }

    /// <summary>
    /// 统一设置顶部状态文本及颜色。
    /// </summary>
    private void SetValidationText(string text, Color color)
    {
        if (_validationStatusLabel == null)
            return;

        _validationSummaryText = text;
        _validationSummaryColor = color;
        _validationStatusLabel.text = text;
        _validationStatusLabel.style.color = color;
    }

    /// <summary>
    /// 更新底部 Validate 明细文本，并确保底栏可见。
    /// </summary>
    private void ShowValidationDetail(string text)
    {
        _validationDetail = text ?? string.Empty;
        _validationDetailVisible = true;
        if (_validationDetailText != null)
            _validationDetailText.value = _validationDetail;
        else
            Rebuild();
    }

    /// <summary>
    /// 将 BuildResult 转为可复制的完整校验报告。
    /// </summary>
    private static string BuildValidationDetail(BuildResult result)
    {
        if (result == null)
            return "Validation result: null";

        var builder = new StringBuilder();
        builder.AppendLine(result.Success ? "校验通过。" : "校验失败。");
        builder.AppendLine($"TotalTasks: {result.TotalTasks}");
        builder.AppendLine($"CompletedTasks: {result.CompletedTasks}");
        builder.AppendLine($"SkippedTasks: {result.SkippedTasks}");

        int count = result.TaskResults?.Count ?? 0;
        builder.AppendLine($"Messages: {count}");
        if (count == 0)
            return builder.ToString();

        for (int i = 0; i < result.TaskResults.Count; i++)
        {
            BuildTaskResult taskResult = result.TaskResults[i];
            if (taskResult == null)
            {
                builder.AppendLine($"{i + 1}. <null>");
                continue;
            }

            builder.Append(i + 1)
                .Append(". ")
                .Append(taskResult.Success ? "通过" : "问题")
                .Append(taskResult.IsFatal ? " 严重" : " 非严重");

            if (!string.IsNullOrEmpty(taskResult.ErrorCode))
                builder.Append(" [").Append(taskResult.ErrorCode).Append(']');
            builder.AppendLine();

            if (!string.IsNullOrEmpty(taskResult.ErrorMessage))
                builder.AppendLine("   " + taskResult.ErrorMessage);

            if (taskResult.Warnings == null || taskResult.Warnings.Count == 0)
                continue;

            for (int warningIndex = 0; warningIndex < taskResult.Warnings.Count; warningIndex++)
                builder.AppendLine("   警告: " + taskResult.Warnings[warningIndex]);
        }

        return builder.ToString();
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
