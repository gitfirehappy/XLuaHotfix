using System;
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
    private BuildPipelineConfig _config;
    private SerializedObject _serializedConfig;
    private EditorWindow _window;
    private VisualElement _root;
    private VisualElement _optionsRow;
    private VisualElement _graphHost;
    private BuildGraphView _graphView;
    private Label _taskStatusLabel;
    private Label _validationStatusLabel;
    private EnumField _buildModeField;
    private BuildType _buildMode = BuildType.Hotfix;
    private bool _isBuildRunning;

    public string PanelName => "Pipeline";

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

        DrawTopBar();

        if (_config == null)
        {
            DrawNoConfig();
            return;
        }

        DrawBuildOptionsBar();
        DrawGraph();
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
        toolbar.Add(BuildPipelineUI.ToolbarLabel("Build Mode"));

        _buildModeField = new EnumField(_buildMode);
        _buildModeField.style.width = 84f;
        _buildModeField.RegisterValueChangedCallback(evt => _buildMode = (BuildType)evt.newValue);
        toolbar.Add(_buildModeField);

        toolbar.Add(BuildPipelineUI.ToolbarButton("Build", HandleBuild, 56f));
        toolbar.Add(BuildPipelineUI.Spacer());

        _taskStatusLabel = BuildPipelineUI.ToolbarLabel("0/0 tasks enabled");
        _taskStatusLabel.style.width = 120f;
        toolbar.Add(_taskStatusLabel);

        _validationStatusLabel = BuildPipelineUI.ToolbarLabel(string.Empty);
        _validationStatusLabel.style.minWidth = 120f;
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
    /// 加载 BuildPipelineConfig，并补齐骨架任务。
    /// </summary>
    private void LoadConfig()
    {
        _config = AssetDatabase.LoadAssetAtPath<BuildPipelineConfig>(FYAssetSettings.Instance.PipelineConfigPath);
        if (_config != null)
        {
            BuildPipelineConfigRepair.EnsureBackboneTasks(_config);
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
            Debug.LogError($"[PipelinePanel] 构建前校验失败: {ex}");
            SetValidationText("Validation error", Color.red);
            return;
        }

        if (validation == null || !validation.Success)
        {
            Debug.LogError("[PipelinePanel] 构建被校验失败阻断。");
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
            Debug.LogError($"[PipelinePanel] 构建失败: {ex}");
            SetValidationText("Build exception", Color.red);
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
            return;
        }

        try
        {
            SetValidationStatus(DAGScheduler.Validate(_config));
        }
        catch (Exception ex)
        {
            Debug.LogError($"[PipelinePanel] 校验失败: {ex.Message}");
            SetValidationText("Validation error", Color.red);
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
            return;
        }

        if (result.Success)
        {
            int warnings = result.TaskResults?.FindAll(r => !r.Success).Count ?? 0;
            SetValidationText(warnings > 0 ? $"{warnings} warning(s)" : $"{result.TotalTasks} tasks OK",
                warnings > 0 ? new Color(1f, 0.85f, 0.3f) : new Color(0.3f, 1f, 0.3f));
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
    }

    /// <summary>
    /// 统一设置顶部状态文本及颜色。
    /// </summary>
    private void SetValidationText(string text, Color color)
    {
        if (_validationStatusLabel == null)
            return;

        _validationStatusLabel.text = text;
        _validationStatusLabel.style.color = color;
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
        panel.Add(BuildPipelineUIToolkitPanel.CreateBody("No BuildPipelineConfig found at " + FYAssetSettings.Instance.PipelineConfigPath));
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
        if (!AssetDatabase.IsValidFolder("Assets/Build"))
            AssetDatabase.CreateFolder("Assets", "Build");

        var config = ScriptableObject.CreateInstance<BuildPipelineConfig>();
        AssetDatabase.CreateAsset(config, FYAssetSettings.Instance.PipelineConfigPath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        LoadConfig();
        Rebuild();
    }
}
