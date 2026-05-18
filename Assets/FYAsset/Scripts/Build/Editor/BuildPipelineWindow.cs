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
        public bool Collapsible;
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
    private int _lastVisiblePanelIndex = -1;
    private int _expandedGroupIndex = 0;

    // 侧边栏分组：SETTINGS → LEGACY PIPELINE → AB PIPELINE → MANAGE
    private static readonly SidebarGroup[] Groups = new[]
    {
        new SidebarGroup { Label = "SETTINGS",        StartIndex = 0, Count = 1, Collapsible = false },
        new SidebarGroup { Label = "LEGACY PIPELINE", StartIndex = 1, Count = 3, Collapsible = true },
        new SidebarGroup { Label = "AB PIPELINE",     StartIndex = 4, Count = 4, Collapsible = true },
        new SidebarGroup { Label = "MANAGE",          StartIndex = 8, Count = 1, Collapsible = false },
    };

    #endregion

    #region Menu

    [MenuItem(FYAssetSettings.BUILD_PIPELINE_WINDOW_MENU_PATH)]
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
        InitPanels();
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

        for (int groupIndex = 0; groupIndex < Groups.Length; groupIndex++)
        {
            SidebarGroup group = Groups[groupIndex];
            Rect headerRect = DrawGroupHeader(group.Label);

            bool groupExpanded = IsGroupExpanded(groupIndex);
            if (group.Collapsible)
            {
                if (Event.current.type == EventType.MouseDown && headerRect.Contains(Event.current.mousePosition))
                {
                    _expandedGroupIndex = groupIndex;
                    Repaint();
                    Event.current.Use();
                }
            }

            if (!groupExpanded)
            {
                GUILayout.Space(6);
                continue;
            }

            bool prevEnabled = GUI.enabled;
            if (group.Label == "AB PIPELINE" && !FYAssetSettings.Instance.UseABBackend)
                GUI.enabled = false;
            else if (group.Label == "LEGACY PIPELINE" && FYAssetSettings.Instance.UseABBackend)
                GUI.enabled = false;

            for (int i = group.StartIndex; i < group.StartIndex + group.Count; i++)
            {
                if (_panels == null || i >= _panels.Length) continue;
                DrawPanelButton(i, _panels[i].PanelName);
            }

            if (group.Label == "AB PIPELINE" && !FYAssetSettings.Instance.UseABBackend)
            {
                GUI.enabled = prevEnabled;
            }
            else if (group.Label == "LEGACY PIPELINE" && FYAssetSettings.Instance.UseABBackend)
            {
                GUI.enabled = prevEnabled;
            }

            GUILayout.Space(6);
        }

        GUILayout.EndVertical();
        GUILayout.Space(8);
        GUILayout.EndHorizontal();

        GUILayout.EndArea();
    }

    private Rect DrawGroupHeader(string label)
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
        string displayLabel = label;
        int groupIndex = GetGroupIndexByLabel(label);
        if (groupIndex >= 0 && Groups[groupIndex].Collapsible)
        {
            displayLabel = (IsGroupExpanded(groupIndex) ? "▼ " : "▶ ") + label;
        }
        GUI.Label(textRect, displayLabel, headerStyle);

        // 分组标题下方的分隔线
        float lineY = headerRect.yMax - 1;
        EditorGUI.DrawRect(new Rect(headerRect.x + 4, lineY, headerRect.width - 8, 1),
            EditorGUIUtility.isProSkin ? new Color(0.35f, 0.35f, 0.35f) : new Color(0.6f, 0.6f, 0.6f));

        return headerRect;
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
        if (_panels == null || _activePanelIndex < 0 || _activePanelIndex >= _panels.Length)
            return;

        UpdatePanelVisibility();

        // LEGACY PIPELINE 与 AB PIPELINE 互斥灰显
        string activeGroup = GetGroupLabelByPanelIndex(_activePanelIndex);
        bool abEnabled = FYAssetSettings.Instance.UseABBackend;
        bool isAbPanel = activeGroup == "AB PIPELINE";
        bool isLegacyPanel = activeGroup == "LEGACY PIPELINE";

        if ((isAbPanel && !abEnabled) || (isLegacyPanel && abEnabled))
        {
            Rect hintRect = new Rect(_contentInnerRect.x, _contentInnerRect.y, _contentInnerRect.width, 28f);
            EditorGUI.DrawRect(hintRect, new Color(0.6f, 0.4f, 0.1f, 0.25f));
            string hint = isAbPanel
                ? "  AB Backend is disabled. Enable UseABBackend in Settings to edit."
                : "  Legacy Pipeline is disabled while UseABBackend is enabled.";
            GUI.Label(hintRect, hint, EditorStyles.miniLabel);

            Rect panelRect = new Rect(_contentInnerRect.x, _contentInnerRect.y + 30f,
                _contentInnerRect.width, Mathf.Max(0f, _contentInnerRect.height - 30f));
            bool prev = GUI.enabled;
            GUI.enabled = false;
            _panels[_activePanelIndex].OnGUI(panelRect);
            GUI.enabled = prev;
        }
        else
        {
            _panels[_activePanelIndex].OnGUI(_contentInnerRect);
        }
    }

    private void UpdatePanelVisibility()
    {
        if (_lastVisiblePanelIndex == _activePanelIndex)
            return;

        if (_lastVisiblePanelIndex >= 0 && _lastVisiblePanelIndex < _panels.Length
            && _panels[_lastVisiblePanelIndex] is IBuildPipelinePanelVisibility previous)
        {
            previous.SetVisible(false);
        }

        if (_panels[_activePanelIndex] is IBuildPipelinePanelVisibility current)
            current.SetVisible(true);

        _lastVisiblePanelIndex = _activePanelIndex;
    }

    #endregion

    #region Public API

    public void InitPanels()
    {
        _panels = new IBuildPipelinePanel[]
        {
            // 设置（索引 0）
            new SettingsPanel(),
            // 旧管线（索引 1-3）
            new LegacyConfigPanel(),
            new LegacyBuildPanel(),
            new LegacyReportPanel(),
            // AB 管线（索引 4-7）
            new CollectorSettingPanel(),
            new CollectorPanel(),
            new PipelinePanel(),
            new BuilderPanel(),
            // 管理（索引 8）
            new VersionPanel(),
        };

        for (int i = 0; i < _panels.Length; i++)
            _panels[i].OnEnable(this);

        _expandedGroupIndex = 0;
        _lastVisiblePanelIndex = -1;
    }

    private bool IsGroupExpanded(int groupIndex)
    {
        SidebarGroup group = Groups[groupIndex];
        if (!group.Collapsible)
            return true;

        int activeGroupIndex = GetGroupIndexByPanelIndex(_activePanelIndex);
        if (activeGroupIndex == groupIndex)
            return true;

        if (_expandedGroupIndex < 0 || _expandedGroupIndex >= Groups.Length)
            return true;

        return _expandedGroupIndex == groupIndex;
    }

    private int GetGroupIndexByLabel(string label)
    {
        for (int i = 0; i < Groups.Length; i++)
        {
            if (Groups[i].Label == label)
                return i;
        }

        return -1;
    }

    private int GetGroupIndexByPanelIndex(int panelIndex)
    {
        for (int i = 0; i < Groups.Length; i++)
        {
            SidebarGroup group = Groups[i];
            if (panelIndex >= group.StartIndex && panelIndex < group.StartIndex + group.Count)
                return i;
        }

        return -1;
    }

    private string GetGroupLabelByPanelIndex(int panelIndex)
    {
        for (int i = 0; i < Groups.Length; i++)
        {
            SidebarGroup group = Groups[i];
            if (panelIndex >= group.StartIndex && panelIndex < group.StartIndex + group.Count)
                return group.Label;
        }

        return string.Empty;
    }

    #endregion
}
