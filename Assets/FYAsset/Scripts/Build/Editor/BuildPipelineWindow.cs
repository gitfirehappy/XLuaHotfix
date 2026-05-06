using UnityEditor;
using UnityEngine;

/// <summary>
/// 包含构建管线各大模块面板
/// </summary>
public sealed class BuildPipelineWindow : EditorWindow
{
    #region Types

    /// <summary>侧边栏分组定义：标签 + 按钮起始索引 + 按钮数量</summary>
    private struct SidebarGroup
    {
        public string Label;
        public int StartIndex;
        public int Count;
    }

    #endregion

    #region Fields

    private float _sidebarWidth = 160f;
    private bool _isDraggingSidebar;

    private IBuildPipelinePanel[] _panels;
    private int _activePanelIndex;
    private Rect _sidebarRect;
    private Rect _contentRect;
    private Rect _contentInnerRect;

    // Sidebar groups in build-pipeline order
    private static readonly SidebarGroup[] Groups = new[]
    {
        new SidebarGroup { Label = "COLLECT", StartIndex = 0, Count = 2 },
        new SidebarGroup { Label = "BUILD",   StartIndex = 2, Count = 2 },
        new SidebarGroup { Label = "MANAGE",  StartIndex = 4, Count = 1 },
    };

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
        if (_panels == null) return;
        for (int i = 0; i < _panels.Length; i++)
            _panels[i].OnDisable();
    }

    private void OnGUI()
    {
        _sidebarRect = new Rect(0, 0, _sidebarWidth, position.height);
        _contentRect = new Rect(_sidebarWidth, 0, position.width - _sidebarWidth, position.height);

        const float contentPadding = 12f;
        _contentInnerRect = new Rect(
            _contentRect.x + contentPadding,
            _contentRect.y + contentPadding,
            Mathf.Max(0, _contentRect.width - contentPadding * 2),
            Mathf.Max(0, _contentRect.height - contentPadding * 2)
        );

        DrawSidebar();

        Rect splitterRect = new Rect(_sidebarWidth - 2, 0, 4, position.height);
        EditorGUIUtility.AddCursorRect(splitterRect, MouseCursor.ResizeHorizontal);

        if (Event.current.type == EventType.MouseDown && splitterRect.Contains(Event.current.mousePosition))
        {
            _isDraggingSidebar = true;
            Event.current.Use();
        }

        if (_isDraggingSidebar)
        {
            if (Event.current.type == EventType.MouseDrag)
            {
                _sidebarWidth = Mathf.Clamp(Event.current.mousePosition.x, 100f, 300f);
                Repaint();
                Event.current.Use();
            }
            else if (Event.current.type == EventType.MouseUp)
            {
                _isDraggingSidebar = false;
                Event.current.Use();
            }
        }

        DrawContent();
    }

    #endregion

    #region Sidebar

    private void DrawSidebar()
    {
        GUILayout.BeginArea(_sidebarRect);
        EditorGUI.DrawRect(new Rect(0, 0, _sidebarRect.width, _sidebarRect.height),
            EditorGUIUtility.isProSkin ? new Color(0.18f, 0.18f, 0.18f) : new Color(0.76f, 0.76f, 0.76f));

        GUILayout.BeginHorizontal();
        GUILayout.Space(8);
        GUILayout.BeginVertical();
        GUILayout.Space(12);

        foreach (var group in Groups)
        {
            DrawGroupHeader(group.Label);

            for (int i = group.StartIndex; i < group.StartIndex + group.Count; i++)
            {
                if (_panels == null || i >= _panels.Length) continue;
                DrawPanelButton(i, _panels[i].PanelName);
            }

            GUILayout.Space(6);
        }

        GUILayout.EndVertical();
        GUILayout.Space(8);
        GUILayout.EndHorizontal();

        GUILayout.EndArea();
    }

    private void DrawGroupHeader(string label)
    {
        Rect headerRect = EditorGUILayout.GetControlRect(false, 20);

        GUIStyle headerStyle = new GUIStyle(EditorStyles.miniLabel)
        {
            alignment = TextAnchor.MiddleLeft,
            fontSize = 10,
            fontStyle = FontStyle.Bold,
            normal = { textColor = EditorGUIUtility.isProSkin ? new Color(0.5f, 0.5f, 0.5f) : new Color(0.4f, 0.4f, 0.4f) }
        };

        Rect textRect = headerRect;
        textRect.xMin += 4;
        GUI.Label(textRect, label, headerStyle);

        // 分组标题下方的分隔线
        float lineY = headerRect.yMax - 1;
        EditorGUI.DrawRect(new Rect(headerRect.x + 4, lineY, headerRect.width - 8, 1),
            EditorGUIUtility.isProSkin ? new Color(0.35f, 0.35f, 0.35f) : new Color(0.6f, 0.6f, 0.6f));
    }

    private void DrawPanelButton(int index, string panelName)
    {
        bool isActive = _activePanelIndex == index;
        Rect btnRect = EditorGUILayout.GetControlRect(false, 34);

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
            fontSize = 12,
            fontStyle = isActive ? FontStyle.Bold : FontStyle.Normal,
            normal = { textColor = isActive ? Color.white : new Color(0.8f, 0.8f, 0.8f) },
            wordWrap = true
        };

        Rect textRect = btnRect;
        textRect.xMin += 16;
        GUI.Label(textRect, panelName, labelStyle);

        if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && btnRect.Contains(Event.current.mousePosition))
        {
            _activePanelIndex = index;
            Event.current.Use();
            GUI.FocusControl(null);
        }

        GUILayout.Space(2);
    }

    #endregion

    #region Content

    private void DrawContent()
    {
        if (_panels != null && _activePanelIndex >= 0 && _activePanelIndex < _panels.Length)
            _panels[_activePanelIndex].OnGUI(_contentInnerRect);
    }

    #endregion

    #region Public API

    public void InitPanels(IBuildPipelinePanel collectorPanel)
    {
        _panels = new IBuildPipelinePanel[]
        {
            // 采集阶段（索引 0-1）
            new CollectorSettingPanel(),
            collectorPanel ?? new PlaceholderPanel("Collector"),
            // 构建阶段（索引 2-3）
            new PipelinePanel(),
            new BuilderPanel(),
            // 管理阶段（索引 4）
            new VersionPanel(),
        };

        for (int i = 0; i < _panels.Length; i++)
            _panels[i].OnEnable(this);
    }

    #endregion
}
