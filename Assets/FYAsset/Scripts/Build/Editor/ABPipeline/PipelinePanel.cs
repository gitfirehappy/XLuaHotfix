using System;
using System.Text;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// BuildPipeline 面板。
/// 负责构建触发、配置校验、Build Options 编辑以及只读 BuildGraph 展示。
/// </summary>
public class PipelinePanel : IBuildPipelinePanel, IBuildPipelinePanelVisibility
{
    private readonly string _panelName;
    private readonly Func<string> _configPathGetter;
    private readonly Func<System.Collections.Generic.List<TaskEntry>> _defaultTasksFactory;
    private readonly string _logPrefix;
    private readonly bool _showBuildOptions;
    private readonly bool _showBuildControls;

    private BuildPipelineConfig _config;
    private SerializedObject _serializedConfig;
    private EditorWindow _window;
    private VisualElement _root;
    private VisualElement _optionsRow;
    private VisualElement _graphHost;
    private VisualElement _validationSplitter;
    private VisualElement _validationDetailPane;
    private ScrollView _validationDetailScroll;
    private TextField _validationDetailText;
    private BuildGraphView _graphView;
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
            () => FYAssetSettings.Instance.PipelineConfigPath,
            BuildPipelineBackbone.CreateABTasks,
            "PipelinePanel",
            true,
            true)
    {
    }

    public PipelinePanel(
        string panelName,
        Func<string> configPathGetter,
        Func<System.Collections.Generic.List<TaskEntry>> defaultTasksFactory,
        string logPrefix,
        bool showBuildOptions,
        bool showBuildControls)
    {
        _panelName = panelName;
        _configPathGetter = configPathGetter;
        _defaultTasksFactory = defaultTasksFactory;
        _logPrefix = logPrefix;
        _showBuildOptions = showBuildOptions;
        _showBuildControls = showBuildControls;
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
        if (_graphView != null)
            _graphView.OnConfigChanged -= RefreshStatus;

        _root?.Unbind();
        _root = null;
        _graphView = null;
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

        DrawTopBar();

        if (_config == null)
        {
            DrawNoConfig();
            return;
        }

        if (_showBuildOptions)
            DrawBuildOptionsBar();

        DrawGraph();
        DrawValidationDetailBar();
        RefreshStatus();
    }

    /// <summary>
    /// 绘制顶部工具栏：重载、校验、Build Mode、构建按钮和状态文本。
    /// </summary>
    private void DrawTopBar()
    {
        VisualElement toolbar = BuildPipelineUI.Toolbar();
        toolbar.Add(BuildPipelineUI.ToolbarButton("Reload", () =>
        {
            LoadConfig();
            Rebuild();
        }, 60f));
        toolbar.Add(BuildPipelineUI.ToolbarButton("Validate", HandleValidate, 70f));

        if (_showBuildControls)
        {
            toolbar.Add(BuildPipelineUI.ToolbarLabel("Build Mode"));

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

        _taskStatusLabel = BuildPipelineUI.ToolbarLabel("0/0 tasks enabled");
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

        Label label = BuildPipelineUI.Header("Build Options");
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

        PropertyField sequential = new PropertyField(_serializedConfig.FindProperty(nameof(BuildPipelineConfig.SequentialMode)), "Sequential");
        sequential.style.width = 140f;
        _optionsRow.Add(sequential);
        _optionsRow.Add(BuildPipelineUI.Spacer());
        _optionsRow.Bind(_serializedConfig);
        _root.Add(_optionsRow);
    }

    /// <summary>
    /// 创建 BuildGraphView 并重新装载当前配置。
    /// </summary>
    private void DrawGraph()
    {
        _graphHost = new VisualElement();
        _graphHost.style.flexGrow = 1f;
        _graphHost.style.backgroundColor = new Color(0.235f, 0.235f, 0.235f);

        _graphView = new BuildGraphView();
        _graphView.style.flexGrow = 1f;
        _graphView.OnConfigChanged += RefreshStatus;
        _graphView.Reload(_config);
        _graphHost.Add(_graphView);
        _root.Add(_graphHost);
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
    /// 先执行 DAG 校验，再按当前 Build Mode 触发 Full 或 Hotfix 构建。
    /// </summary>
    private void HandleBuild()
    {
        if (_config == null || _isBuildRunning)
            return;

        BuildResult validation;
        try
        {
            validation = DAGScheduler.Validate(_config);
            SetValidationStatus(validation);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[{_logPrefix}] 构建前校验失败: {ex}");
            SetValidationText("Validation error", Color.red);
            ShowValidationDetail($"Validation exception before build:{Environment.NewLine}{ex}");
            return;
        }

        if (validation == null || !validation.Success)
        {
            Debug.LogError($"[{_logPrefix}] 构建被校验失败阻断。");
            return;
        }

        _isBuildRunning = true;
        SetValidationText("Build running...", new Color(1f, 0.85f, 0.3f));
        _graphView?.ResetExecutionStatuses();
        _graphView?.SetBuildRunning(true);
        SetRunningEnabled(false);

        var options = new BuildExecutionOptions
        {
            TaskStatusChanged = OnTaskStatusChanged
        };

        try
        {
            if (_buildMode == BuildType.Full)
                BuildProjectManager.BuildFullPackage(options);
            else
                BuildProjectManager.BuildHotfix(options);

            SetValidationText(BuildProjectManager.LastBuildSuccess ? "Build complete" : "Build failed",
                BuildProjectManager.LastBuildSuccess ? new Color(0.3f, 1f, 0.3f) : Color.red);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[{_logPrefix}] 构建失败: {ex}");
            SetValidationText("Build exception", Color.red);
            ShowValidationDetail($"Build exception:{Environment.NewLine}{ex}");
        }
        finally
        {
            _isBuildRunning = false;
            _graphView?.SetBuildRunning(false);
            SetRunningEnabled(true);
        }
    }

    /// <summary>
    /// 执行一次显式校验，并把结果写到状态栏。
    /// </summary>
    private void HandleValidate()
    {
        if (_config == null)
        {
            SetValidationText("Validation error", Color.red);
            ShowValidationDetail("Validation error: BuildPipelineConfig is null.");
            return;
        }

        try
        {
            SetValidationStatus(DAGScheduler.Validate(_config));
        }
        catch (Exception ex)
        {
            Debug.LogError($"[{_logPrefix}] 校验失败: {ex.Message}");
            SetValidationText("Validation error", Color.red);
            ShowValidationDetail($"Validation exception:{Environment.NewLine}{ex}");
        }
    }

    /// <summary>
    /// 将构建过程中的单任务执行状态同步到 BuildGraphView。
    /// </summary>
    private void OnTaskStatusChanged(BuildTaskExecutionEvent evt)
    {
        _graphView?.SetTaskExecutionStatus(evt);
        _window?.Repaint();
    }

    /// <summary>
    /// 刷新任务启用计数摘要。
    /// </summary>
    private void RefreshStatus()
    {
        int taskCount = _config?.Tasks?.Count ?? 0;
        int enabledCount = _config?.Tasks?.FindAll(e => e.Enabled).Count ?? 0;
        if (_taskStatusLabel != null)
            _taskStatusLabel.text = $"{enabledCount}/{taskCount} tasks enabled";
    }

    /// <summary>
    /// 根据 BuildResult 更新顶部校验状态文本。
    /// </summary>
    private void SetValidationStatus(BuildResult result)
    {
        if (result == null)
        {
            SetValidationText("Validation error", Color.red);
            ShowValidationDetail("Validation error: DAGScheduler returned null result.");
            return;
        }

        if (result.Success)
        {
            int warnings = result.TaskResults?.FindAll(r => !r.Success).Count ?? 0;
            SetValidationText(warnings > 0 ? $"{warnings} warning(s)" : $"{result.TotalTasks} tasks OK",
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
            SetValidationText("Validation failed", Color.red);
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
        builder.AppendLine(result.Success ? "Validation passed." : "Validation failed.");
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
                .Append(taskResult.Success ? "OK" : "ISSUE")
                .Append(taskResult.IsFatal ? " Fatal" : " NonFatal");

            if (!string.IsNullOrEmpty(taskResult.ErrorCode))
                builder.Append(" [").Append(taskResult.ErrorCode).Append(']');
            builder.AppendLine();

            if (!string.IsNullOrEmpty(taskResult.ErrorMessage))
                builder.AppendLine("   " + taskResult.ErrorMessage);

            if (taskResult.Warnings == null || taskResult.Warnings.Count == 0)
                continue;

            for (int warningIndex = 0; warningIndex < taskResult.Warnings.Count; warningIndex++)
                builder.AppendLine("   Warning: " + taskResult.Warnings[warningIndex]);
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
        _graphHost?.SetEnabled(enabled);
    }

    /// <summary>
    /// BuildPipelineConfig 缺失时显示创建入口。
    /// </summary>
    private void DrawNoConfig()
    {
        VisualElement panel = BuildPipelineUIToolkitPanel.CreateCenteredPanel(_root, 460f);
        panel.Add(BuildPipelineUIToolkitPanel.CreateBody("No BuildPipelineConfig found at " + GetConfigPath()));
        panel.Add(new Button(CreateConfig)
        {
            text = "Create BuildPipelineConfig"
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
}
