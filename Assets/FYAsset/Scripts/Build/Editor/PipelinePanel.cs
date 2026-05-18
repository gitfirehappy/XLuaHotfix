using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Pipeline 配置面板 —— 顶栏编辑构建选项，下方展示只读 DAG。
/// 统一管理 DAG 校验、构建执行、状态可视化和配置编辑四大职责。
/// </summary>
public class PipelinePanel : IBuildPipelinePanel, IBuildPipelinePanelVisibility
{
    #region Fields

    private BuildPipelineConfig _config;
    private SerializedObject _serializedConfig;
    private EditorWindow _window;
    private VisualElement _graphRoot;
    private BuildGraphView _graphView;
    private string _taskStatus = "0/0 tasks enabled";
    private string _validationStatus = string.Empty;
    private Color _validationColor = Color.gray;
    private BuildType _buildMode = BuildType.Hotfix;
    private bool _isBuildRunning;

    #endregion

    #region Properties

    public string PanelName => "Pipeline";

    #endregion

    #region Lifecycle

    /// <summary>
    /// 面板启用：创建 GraphView 并注入到 EditorWindow 的 VisualElement 树中。
    /// </summary>
    public void OnEnable(EditorWindow window)
    {
        _window = window;

        _graphRoot = new VisualElement();
        _graphRoot.style.position = Position.Absolute;
        _graphRoot.style.display = DisplayStyle.None;
        _graphRoot.style.flexDirection = FlexDirection.Column;
        _graphRoot.style.backgroundColor = new Color(0.235f, 0.235f, 0.235f);

        _graphView = new BuildGraphView();
        _graphView.style.flexGrow = 1;
        _graphView.OnConfigChanged += RefreshStatus;
        _graphRoot.Add(_graphView);

        window.rootVisualElement.Add(_graphRoot);
        LoadConfig();
    }

    /// <summary>
    /// 面板禁用：清理事件订阅并从 EditorWindow 中移除 GraphView。
    /// </summary>
    public void OnDisable()
    {
        if (_graphView != null)
        {
            _graphView.OnConfigChanged -= RefreshStatus;
        }

        if (_graphRoot != null && _window != null)
        {
            _window.rootVisualElement.Remove(_graphRoot);
        }
    }

    /// <summary>
    /// 面板绘制：决定走空配置视图（无 config）还是正常顶栏+构建选项+DAG 视图。
    /// </summary>
    public void OnGUI(Rect windowRect)
    {
        GUILayout.BeginArea(windowRect);

        if (_config == null)
        {
            DrawTopBar();
            DrawNoConfig();
        }
        else
        {
            DrawTopBar();
            DrawBuildOptionsBar();
            DrawGraphHost(windowRect);
        }

        GUILayout.EndArea();
    }

    /// <summary>
    /// 面板显隐控制。不销毁 GraphView，仅切换 display 避免重建开销。
    /// </summary>
    public void SetVisible(bool visible)
    {
        if (_graphRoot == null)
            return;

        _graphRoot.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
    }

    /// <summary>
    /// 加载 BuildPipelineConfig SO 并确保 backbone Task 存在，然后刷新 GraphView。
    /// </summary>
    private void LoadConfig()
    {
        _config = AssetDatabase.LoadAssetAtPath<BuildPipelineConfig>(FYAssetSettings.Instance.PipelineConfigPath);
        if (_config != null)
        {
            BuildPipelineConfigRepair.EnsureBackboneTasks(_config);
            _serializedConfig = new SerializedObject(_config);
        }

        _graphView?.Reload(_config);
        RefreshStatus();
    }

    #endregion

    #region Top Bar

    /// <summary>
    /// 顶栏：构建模式选择、Validate / Build 按钮、当前 Task 状态和校验结果。
    /// </summary>
    private void DrawTopBar()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        EditorGUI.BeginDisabledGroup(_isBuildRunning);
        if (GUILayout.Button("Reload", EditorStyles.toolbarButton, GUILayout.Width(60)))
        {
            LoadConfig();
        }
        if (GUILayout.Button("Validate", EditorStyles.toolbarButton, GUILayout.Width(70)))
        {
            HandleValidate();
        }
        GUILayout.Space(10);
        GUILayout.Label("Build Mode", EditorStyles.miniLabel, GUILayout.Width(66));
        _buildMode = (BuildType)EditorGUILayout.EnumPopup(_buildMode, EditorStyles.toolbarPopup, GUILayout.Width(72));
        if (GUILayout.Button("Build", EditorStyles.toolbarButton, GUILayout.Width(56)))
        {
            HandleBuild();
        }
        EditorGUI.EndDisabledGroup();
        GUILayout.FlexibleSpace();
        GUILayout.Label(_taskStatus, EditorStyles.miniLabel, GUILayout.Width(120));
        if (!string.IsNullOrEmpty(_validationStatus))
        {
            Color prev = GUI.color;
            GUI.color = _validationColor;
            GUILayout.Label(_validationStatus, EditorStyles.miniLabel, GUILayout.MinWidth(120));
            GUI.color = prev;
        }
        EditorGUILayout.EndHorizontal();
    }

    #endregion

    #region Build Options

    /// <summary>
    /// 构建选项栏：FileNameStyle、BundleCompression、SequentialMode 三项配置。
    /// 通过 SerializedObject 直接编辑 BuildPipelineConfig SO 属性，修改后自动标记 Dirty。
    /// </summary>
    private void DrawBuildOptionsBar()
    {
        if (_serializedConfig == null)
            return;

        EditorGUI.BeginDisabledGroup(_isBuildRunning);
        _serializedConfig.Update();

        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Build Options", EditorStyles.boldLabel, GUILayout.Width(96));
        EditorGUILayout.PropertyField(
            _serializedConfig.FindProperty(nameof(BuildPipelineConfig.FileNameStyle)),
            GUIContent.none,
            GUILayout.MinWidth(180));
        EditorGUILayout.PropertyField(
            _serializedConfig.FindProperty(nameof(BuildPipelineConfig.BundleCompression)),
            GUIContent.none,
            GUILayout.Width(130));
        EditorGUILayout.PropertyField(
            _serializedConfig.FindProperty(nameof(BuildPipelineConfig.SequentialMode)),
            new GUIContent("Sequential"),
            GUILayout.Width(120));
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.EndVertical();

        if (_serializedConfig.ApplyModifiedProperties())
        {
            EditorUtility.SetDirty(_config);
            RefreshStatus();
        }
        EditorGUI.EndDisabledGroup();
    }

    #endregion

    #region Graph Host

    /// <summary>
    /// 将 BuildGraphView（UIElement）嵌入到 IMGUI 区域的宿主方法。
    /// 通过 GUIToScreenRect 将 IMGUI 矩形映射为 VisualElement 的绝对坐标。
    /// </summary>
    private void DrawGraphHost(Rect windowRect)
    {
        SetVisible(true);

        // 留给顶栏 + 构建选项栏共约 74px 高度，其余给 GraphView
        Rect hostRect = GUILayoutUtility.GetRect(
            1f,
            Mathf.Max(1f, windowRect.height - 74f),
            GUILayout.ExpandWidth(true),
            GUILayout.ExpandHeight(true));

        Rect screenRect = GUIUtility.GUIToScreenRect(hostRect);
        Vector2 windowPos = _window.position.position;

        _graphRoot.style.left = screenRect.x - windowPos.x;
        _graphRoot.style.top = screenRect.y - windowPos.y;
        _graphRoot.style.width = hostRect.width;
        _graphRoot.style.height = hostRect.height;
        bool graphEnabled = GUI.enabled && !_isBuildRunning;
        _graphRoot.SetEnabled(graphEnabled);
        _graphRoot.style.opacity = GUI.enabled ? 1f : 0.4f;
        _graphView?.SetBuildRunning(_isBuildRunning);
    }

    #endregion

    #region Build Actions

    /// <summary>
    /// 处理构建按钮点击。流程：
    /// 1. DAGScheduler.Validate 校验 DAG 拓扑
    /// 2. 校验失败则阻断构建
    /// 3. 设置构建运行状态并刷新 UI
    /// 4. 根据 BuildMode 调用 BuildFullPackage / BuildHotfix
    /// 5. 读取 BuildProjectManager.LastBuildSuccess 反馈状态栏
    /// </summary>
    private void HandleBuild()
    {
        if (_config == null || _isBuildRunning)
            return;

        // 1. 预校验
        BuildResult validation;
        try
        {
            validation = DAGScheduler.Validate(_config);
            SetValidationStatus(validation);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[PipelinePanel] Validate before build failed: {ex}");
            _validationStatus = "Validation error";
            _validationColor = Color.red;
            return;
        }

        // 2. 校验失败阻断
        if (validation == null || !validation.Success)
        {
            Debug.LogError("[PipelinePanel] Build blocked by validation failure.");
            return;
        }

        // 3. 设置运行态 UI
        _isBuildRunning = true;
        _validationStatus = "Build running...";
        _validationColor = new Color(1f, 0.85f, 0.3f);
        _graphView?.ResetExecutionStatuses();
        _graphView?.SetBuildRunning(true);
        _window?.Repaint();

        var options = new BuildExecutionOptions
        {
            TaskStatusChanged = OnTaskStatusChanged
        };

        // 4. 执行构建
        try
        {
            if (_buildMode == BuildType.Full)
                BuildProjectManager.BuildFullPackage(options);
            else
                BuildProjectManager.BuildHotfix(options);

            // 5. 反馈状态
            _validationStatus = BuildProjectManager.LastBuildSuccess ? "Build complete" : "Build failed";
            _validationColor = BuildProjectManager.LastBuildSuccess
                ? new Color(0.3f, 1f, 0.3f)
                : Color.red;
        }
        catch (Exception ex)
        {
            Debug.LogError($"[PipelinePanel] Build failed: {ex}");
            _validationStatus = "Build exception";
            _validationColor = Color.red;
        }
        finally
        {
            _isBuildRunning = false;
            _graphView?.SetBuildRunning(false);
            _window?.Repaint();
        }
    }

    /// <summary>
    /// 处理 Validate 按钮点击。调用 DAGScheduler.Validate 并更新状态栏颜色。
    /// </summary>
    private void HandleValidate()
    {
        if (_config == null)
        {
            _validationStatus = "Validation error";
            _validationColor = Color.red;
            return;
        }

        try
        {
            BuildResult result = DAGScheduler.Validate(_config);
            SetValidationStatus(result);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[PipelinePanel] Validate failed: {ex.Message}");
            _validationStatus = "Validation error";
            _validationColor = Color.red;
        }
    }

    /// <summary>
    /// 构建过程中每个 Task 状态变化时回调，将事件转发给 GraphView 更新节点颜色。
    /// </summary>
    private void OnTaskStatusChanged(BuildTaskExecutionEvent evt)
    {
        _graphView?.SetTaskExecutionStatus(evt);
        _window?.Repaint();
    }

    /// <summary>
    /// 刷新顶栏 Task 状态计数（enabled / total）。
    /// </summary>
    private void RefreshStatus()
    {
        int taskCount = _config?.Tasks?.Count ?? 0;
        int enabledCount = _config?.Tasks?.FindAll(e => e.Enabled).Count ?? 0;
        _taskStatus = $"{enabledCount}/{taskCount} tasks enabled";
    }

    /// <summary>
    /// 将 BuildResult 映射为状态栏文字和颜色。
    /// Success -> 绿色 "N tasks OK"（有 Warning 时黄色 "N warning(s)"）；
    /// 失败 -> 红色，显示第一个 Fatal Error 的前 60 字符。
    /// </summary>
    private void SetValidationStatus(BuildResult result)
    {
        if (result == null)
        {
            _validationStatus = "Validation error";
            _validationColor = Color.red;
            return;
        }

        if (result.Success)
        {
            int warnings = result.TaskResults?.FindAll(r => !r.Success).Count ?? 0;
            _validationStatus = warnings > 0 ? $"{warnings} warning(s)" : $"{result.TotalTasks} tasks OK";
            _validationColor = warnings > 0 ? new Color(1f, 0.85f, 0.3f) : new Color(0.3f, 1f, 0.3f);
            return;
        }

        var errors = result.TaskResults?.FindAll(r => r.IsFatal);
        if (errors != null && errors.Count > 0)
        {
            string first = errors[0].ErrorMessage;
            _validationStatus = first.Length > 60 ? first.Substring(0, 57) + "..." : first;
        }
        else
        {
            _validationStatus = "Validation failed";
        }
        _validationColor = Color.red;
    }

    #endregion

    #region Empty Config State

    /// <summary>
    /// 无 BuildPipelineConfig 时的空状态视图，提供创建入口。
    /// </summary>
    private void DrawNoConfig()
    {
        SetVisible(false);

        GUILayout.FlexibleSpace();
        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        GUILayout.Label("No BuildPipelineConfig found at " + FYAssetSettings.Instance.PipelineConfigPath, EditorStyles.centeredGreyMiniLabel);
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("Create BuildPipelineConfig", GUILayout.Width(200), GUILayout.Height(36)))
        {
            CreateConfig();
        }
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();
        GUILayout.FlexibleSpace();
    }

    /// <summary>
    /// 在 Assets/Build 目录下创建 BuildPipelineConfig SO 并刷新面板。
    /// </summary>
    private void CreateConfig()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Build"))
        {
            AssetDatabase.CreateFolder("Assets", "Build");
        }
        var config = ScriptableObject.CreateInstance<BuildPipelineConfig>();
        AssetDatabase.CreateAsset(config, FYAssetSettings.Instance.PipelineConfigPath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        LoadConfig();
    }

    #endregion
}
