using UnityEditor;
using UnityEngine;

/// <summary>
/// 构建管线主窗口 —— 左侧边栏 5 区域路由 + 右侧内容面板。
/// 菜单入口：XLua/Build Pipeline
/// </summary>
public sealed class BuildPipelineWindow : EditorWindow
{
    #region Fields

    private const float SidebarWidth = 120f;

    private IBuildPipelinePanel[] _panels;
    private int _activePanelIndex;
    private Rect _sidebarRect;
    private Rect _contentRect;
    private Rect _contentInnerRect;

    #endregion

    #region Menu

    [MenuItem(FYAssetConstants.BUILD_PIPELINE_WINDOW_MENU_PATH)]
    private static void Open()
    {
        BuildPipelineWindow window = GetWindow<BuildPipelineWindow>();
        window.titleContent = new GUIContent("Build Pipeline");
        window.minSize = new Vector2(800, 500);
        window.Show();
    }

    #endregion

    #region Unity Messages

    private void OnEnable()
    {
        InitPanels(new CollectorPanel());
    }

    private void OnDisable()
    {
        for (int i = 0; i < _panels.Length; i++)
            _panels[i].OnDisable();
    }

    private void OnGUI()
    {
        _sidebarRect = new Rect(0, 0, SidebarWidth, position.height);
        _contentRect = new Rect(SidebarWidth, 0, position.width - SidebarWidth, position.height);

        // Add a small consistent padding inside the content area so panels have breathing room.
        const float contentPadding = 12f;
        _contentInnerRect = new Rect(
            _contentRect.x + contentPadding,
            _contentRect.y + contentPadding,
            Mathf.Max(0, _contentRect.width - contentPadding * 2),
            Mathf.Max(0, _contentRect.height - contentPadding * 2)
        );

        DrawSidebar();
        DrawContent();
    }

    #endregion

    #region Sidebar

    private void DrawSidebar()
    {
        GUILayout.BeginArea(_sidebarRect);
        EditorGUI.DrawRect(new Rect(0, 0, _sidebarRect.width, _sidebarRect.height),
            EditorGUIUtility.isProSkin ? new Color(0.18f, 0.18f, 0.18f) : new Color(0.76f, 0.76f, 0.76f));

        // Inner padding so sidebar content doesn't touch the edges
        GUILayout.BeginHorizontal();
        GUILayout.Space(8);
        GUILayout.BeginVertical();
        GUILayout.Space(12);

        string[] areaNames = { "Collector Settings", "Collector", "Pipeline", "Builder", "Inspector" };
        for (int i = 0; i < areaNames.Length; i++)
        {
            bool isActive = _activePanelIndex == i;
            Rect btnRect = EditorGUILayout.GetControlRect(false, 38);
            
            if (isActive)
            {
                EditorGUI.DrawRect(btnRect, new Color(0.17f, 0.36f, 0.53f, 1f));
            }
            else if (btnRect.Contains(Event.current.mousePosition))
            {
                EditorGUI.DrawRect(btnRect, new Color(0.3f, 0.3f, 0.3f, 0.5f));
            }

            GUIStyle labelStyle = new GUIStyle(EditorStyles.label)
            {
                alignment = TextAnchor.MiddleLeft,
                fontSize = 13,
                fontStyle = isActive ? FontStyle.Bold : FontStyle.Normal,
                normal = { textColor = isActive ? Color.white : new Color(0.8f, 0.8f, 0.8f) }
            };

            Rect textRect = btnRect;
            textRect.xMin += 12;

            GUI.Label(textRect, areaNames[i], labelStyle);

            if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && btnRect.Contains(Event.current.mousePosition))
            {
                _activePanelIndex = i;
                Event.current.Use();
                GUI.FocusControl(null);
            }

            GUILayout.Space(2);
        }

        GUILayout.EndVertical();
        GUILayout.Space(8);
        GUILayout.EndHorizontal();

        GUILayout.EndArea();
    }

    #endregion

    #region Content

    private void DrawContent()
    {
        // Pass the padded inner rect to panels so their layout starts with consistent margins.
        if (_panels != null && _activePanelIndex >= 0 && _activePanelIndex < _panels.Length)
            _panels[_activePanelIndex].OnGUI(_contentInnerRect);
    }

    #endregion

    #region Public API

    /// <summary>注册面板（T11 调用，将 CollectorPanel 装入 index 0）</summary>
    public void InitPanels(IBuildPipelinePanel collectorPanel)
    {
        _panels = new IBuildPipelinePanel[]
        {
            new PlaceholderPanel("Collector Settings"),
            collectorPanel ?? new PlaceholderPanel("Collector"),
            new PlaceholderPanel("Pipeline"),
            new PlaceholderPanel("Builder"),
            new PlaceholderPanel("Inspector")
        };

        for (int i = 0; i < _panels.Length; i++)
            _panels[i].OnEnable(this);
    }

    #endregion
}
