using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// FYAsset 构建管线主窗口。
/// 使用 UI Toolkit 承载侧边栏、禁用提示与各子面板内容。
/// </summary>
public sealed class BuildPipelineWindow : EditorWindow
{
    #region Types

    /// <summary>
    /// 侧边栏分组元数据。
    /// </summary>
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
    private Vector2 _dragStartMouse;
    private float _dragStartWidth;

    private IBuildPipelinePanel[] _panels;
    private VisualElement[] _panelContents;
    private Button[] _panelButtons;
    private VisualElement[] _groupBodies;
    private Label[] _groupHeaders;

    private VisualElement _root;
    private VisualElement _sidebar;
    private VisualElement _contentHost;
    private VisualElement _disabledHint;
    private Label _disabledHintLabel;

    private int _activePanelIndex;
    private int _lastVisiblePanelIndex = -1;
    private int _expandedGroupIndex;

    private static readonly SidebarGroup[] Groups =
    {
        new SidebarGroup { Label = "设置", StartIndex = 0, Count = 1, Collapsible = false },
        new SidebarGroup { Label = "AA", StartIndex = 1, Count = 3, Collapsible = true },
        new SidebarGroup { Label = "AB", StartIndex = 4, Count = 4, Collapsible = true },
        new SidebarGroup { Label = "管理", StartIndex = 8, Count = 3, Collapsible = false },
    };

    #endregion

    #region Menu

    [MenuItem(FYAssetSettings.BUILD_PIPELINE_WINDOW_MENU_PATH)]
    private static void Open()
    {
        BuildPipelineWindow window = GetWindow<BuildPipelineWindow>();
        window.titleContent = new GUIContent("构建面板");
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
        if (_panels == null)
            return;

        for (int i = 0; i < _panels.Length; i++)
            _panels[i].OnDisable();
    }

    public void CreateGUI()
    {
        BuildRoot();
        BuildSidebar();
        BuildContent();
        SelectPanel(Mathf.Clamp(_activePanelIndex, 0, _panels.Length - 1), true);
    }

    #endregion

    #region UI Construction

    /// <summary>
    /// 构建窗口根布局：左侧导航、分隔条、右侧内容区。
    /// </summary>
    private void BuildRoot()
    {
        rootVisualElement.Clear();

        _root = new VisualElement { name = "BuildPipelineWindowRoot" };
        _root.style.flexDirection = FlexDirection.Row;
        _root.style.flexGrow = 1f;
        _root.style.backgroundColor = BuildPipelineUI.WindowBackgroundColor;
        rootVisualElement.Add(_root);

        _sidebar = new VisualElement { name = "Sidebar" };
        _sidebar.style.width = _sidebarWidth;
        _sidebar.style.minWidth = 100f;
        _sidebar.style.maxWidth = 300f;
        _sidebar.style.flexShrink = 0f;
        _sidebar.style.backgroundColor = BuildPipelineUI.SidebarBackgroundColor;
        _sidebar.style.paddingLeft = 8f;
        _sidebar.style.paddingRight = 8f;
        _sidebar.style.paddingTop = 12f;
        _root.Add(_sidebar);

        var splitter = new VisualElement { name = "SidebarSplitter" };
        splitter.style.width = 4f;
        splitter.style.flexShrink = 0f;
        splitter.style.backgroundColor = BuildPipelineUI.BorderColor;
        splitter.RegisterCallback<PointerDownEvent>(OnSplitterPointerDown);
        splitter.RegisterCallback<PointerMoveEvent>(OnSplitterPointerMove);
        splitter.RegisterCallback<PointerUpEvent>(OnSplitterPointerUp);
        _root.Add(splitter);

        var contentShell = new VisualElement { name = "ContentShell" };
        contentShell.style.flexGrow = 1f;
        contentShell.style.paddingLeft = 12f;
        contentShell.style.paddingRight = 12f;
        contentShell.style.paddingTop = 12f;
        contentShell.style.paddingBottom = 12f;
        contentShell.style.flexDirection = FlexDirection.Column;
        _root.Add(contentShell);

        _disabledHint = new VisualElement { name = "DisabledHint" };
        _disabledHint.style.height = 28f;
        _disabledHint.style.marginBottom = 2f;
        _disabledHint.style.backgroundColor = new Color(0.6f, 0.4f, 0.1f, 0.25f);
        _disabledHint.style.justifyContent = Justify.Center;
        _disabledHint.style.display = DisplayStyle.None;

        _disabledHintLabel = BuildPipelineUI.SmallText(string.Empty);
        _disabledHintLabel.style.marginLeft = 8f;
        _disabledHint.Add(_disabledHintLabel);
        contentShell.Add(_disabledHint);

        _contentHost = new VisualElement { name = "ContentHost" };
        _contentHost.style.flexGrow = 1f;
        _contentHost.style.flexDirection = FlexDirection.Column;
        contentShell.Add(_contentHost);
    }

    /// <summary>
    /// 根据分组定义构建侧边栏按钮树。
    /// </summary>
    private void BuildSidebar()
    {
        _panelButtons = new Button[_panels.Length];
        _groupBodies = new VisualElement[Groups.Length];
        _groupHeaders = new Label[Groups.Length];

        for (int groupIndex = 0; groupIndex < Groups.Length; groupIndex++)
        {
            SidebarGroup group = Groups[groupIndex];
            Label header = CreateGroupHeader(groupIndex, group);
            _groupHeaders[groupIndex] = header;
            _sidebar.Add(header);

            var body = new VisualElement();
            body.style.marginBottom = 6f;
            _groupBodies[groupIndex] = body;
            _sidebar.Add(body);

            for (int i = group.StartIndex; i < group.StartIndex + group.Count; i++)
            {
                if (i < 0 || i >= _panels.Length)
                    continue;

                int panelIndex = i;
                Button button = CreatePanelButton(panelIndex, _panels[i].PanelName);
                _panelButtons[panelIndex] = button;
                body.Add(button);
            }
        }

        RefreshSidebar();
    }

    /// <summary>
    /// 创建所有子面板的 UI Toolkit 内容，并先隐藏到内容宿主中。
    /// </summary>
    private void BuildContent()
    {
        _contentHost.Clear();
        _panelContents = new VisualElement[_panels.Length];

        for (int i = 0; i < _panels.Length; i++)
        {
            VisualElement content = _panels[i].CreateContent();
            content.style.flexGrow = 1f;
            content.style.display = DisplayStyle.None;
            _panelContents[i] = content;
            _contentHost.Add(content);
        }
    }

    /// <summary>
    /// 创建侧边栏分组标题；可折叠分组点击后会切换展开状态。
    /// </summary>
    private Label CreateGroupHeader(int groupIndex, SidebarGroup group)
    {
        var header = new Label();
        header.style.height = 20f;
        header.style.unityTextAlign = TextAnchor.MiddleLeft;
        header.style.fontSize = 10f;
        header.style.unityFontStyleAndWeight = FontStyle.Bold;
        header.style.color = EditorGUIUtility.isProSkin ? new Color(0.5f, 0.5f, 0.5f) : new Color(0.4f, 0.4f, 0.4f);
        header.style.borderBottomWidth = 1f;
        header.style.borderBottomColor = BuildPipelineUI.BorderColor;
        header.style.marginBottom = 4f;
        header.style.paddingLeft = 4f;

        if (group.Collapsible)
        {
            header.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0)
                    return;

                _expandedGroupIndex = groupIndex;
                RefreshSidebar();
                evt.StopPropagation();
            });
        }

        return header;
    }

    /// <summary>
    /// 创建单个面板入口按钮。
    /// </summary>
    private Button CreatePanelButton(int index, string panelName)
    {
        var button = new Button(() => SelectPanel(index, false))
        {
            text = panelName
        };
        button.style.height = 34f;
        button.style.marginBottom = 2f;
        button.style.paddingLeft = 16f;
        button.style.unityTextAlign = TextAnchor.MiddleLeft;
        button.style.whiteSpace = WhiteSpace.Normal;
        button.style.borderTopWidth = 0f;
        button.style.borderRightWidth = 0f;
        button.style.borderBottomWidth = 0f;
        button.style.borderLeftWidth = 0f;
        return button;
    }

    #endregion

    #region Interaction

    /// <summary>
    /// 开始拖拽侧边栏宽度。
    /// </summary>
    private void OnSplitterPointerDown(PointerDownEvent evt)
    {
        if (evt.button != 0)
            return;

        _isDraggingSidebar = true;
        _dragStartMouse = evt.position;
        _dragStartWidth = _sidebarWidth;
        evt.target.CapturePointer(evt.pointerId);
        evt.StopPropagation();
    }

    /// <summary>
    /// 拖拽过程中实时更新侧边栏宽度。
    /// </summary>
    private void OnSplitterPointerMove(PointerMoveEvent evt)
    {
        if (!_isDraggingSidebar)
            return;

        float delta = evt.position.x - _dragStartMouse.x;
        _sidebarWidth = Mathf.Clamp(_dragStartWidth + delta, 100f, 300f);
        _sidebar.style.width = _sidebarWidth;
        evt.StopPropagation();
    }

    /// <summary>
    /// 结束侧边栏拖拽。
    /// </summary>
    private void OnSplitterPointerUp(PointerUpEvent evt)
    {
        if (!_isDraggingSidebar)
            return;

        _isDraggingSidebar = false;
        evt.target.ReleasePointer(evt.pointerId);
        evt.StopPropagation();
    }

    /// <summary>
    /// 切换当前激活面板，并同步可见性回调、导航状态与禁用提示。
    /// </summary>
    private void SelectPanel(int panelIndex, bool force)
    {
        if (_panels == null || panelIndex < 0 || panelIndex >= _panels.Length)
            return;

        if (!force && _activePanelIndex == panelIndex)
            return;

        if (_lastVisiblePanelIndex >= 0 && _lastVisiblePanelIndex < _panels.Length &&
            _panels[_lastVisiblePanelIndex] is IBuildPipelinePanelVisibility previous)
        {
            previous.SetVisible(false);
        }

        _activePanelIndex = panelIndex;
        _lastVisiblePanelIndex = panelIndex;

        for (int i = 0; i < _panelContents.Length; i++)
            _panelContents[i].style.display = i == panelIndex ? DisplayStyle.Flex : DisplayStyle.None;

        if (_panels[panelIndex] is IBuildPipelinePanelVisibility current)
            current.SetVisible(true);

        RefreshSidebar();
        RefreshDisabledState();
    }

    /// <summary>
    /// 刷新侧边栏的展开状态、按钮高亮和 AA/AB 互斥禁用状态。
    /// </summary>
    private void RefreshSidebar()
    {
        if (_panels == null || _groupBodies == null)
            return;

        bool useAB = FYAssetSettings.Instance.UseABBackend;

        for (int groupIndex = 0; groupIndex < Groups.Length; groupIndex++)
        {
            SidebarGroup group = Groups[groupIndex];
            bool expanded = IsGroupExpanded(groupIndex);
            _groupBodies[groupIndex].style.display = expanded ? DisplayStyle.Flex : DisplayStyle.None;

            string prefix = group.Collapsible ? (expanded ? "▼ " : "▶ ") : string.Empty;
            _groupHeaders[groupIndex].text = prefix + group.Label;

            bool groupEnabled = true;
            if (group.Label == "AB" && !useAB)
                groupEnabled = false;
            else if (group.Label == "AA" && useAB)
                groupEnabled = false;

            for (int i = group.StartIndex; i < group.StartIndex + group.Count; i++)
            {
                if (i < 0 || i >= _panelButtons.Length || _panelButtons[i] == null)
                    continue;

                Button button = _panelButtons[i];
                bool active = i == _activePanelIndex;
                button.SetEnabled(groupEnabled);
                button.style.backgroundColor = active ? BuildPipelineUI.ActiveColor : Color.clear;
                button.style.color = active ? Color.white : new Color(0.8f, 0.8f, 0.8f);
                button.style.unityFontStyleAndWeight = active ? FontStyle.Bold : FontStyle.Normal;
                button.style.opacity = groupEnabled ? 1f : 0.45f;
            }
        }
    }

    /// <summary>
    /// 根据当前 Backend 状态刷新禁用提示，并将当前面板置灰。
    /// </summary>
    private void RefreshDisabledState()
    {
        string activeGroup = GetGroupLabelByPanelIndex(_activePanelIndex);
        bool abEnabled = FYAssetSettings.Instance.UseABBackend;
        bool isAbPanel = activeGroup == "AB";
        bool isAAPanel = activeGroup == "AA";
        bool disabled = (isAbPanel && !abEnabled) || (isAAPanel && abEnabled);

        _disabledHint.style.display = disabled ? DisplayStyle.Flex : DisplayStyle.None;
        _disabledHintLabel.text = isAbPanel
            ? "AB 已禁用。请在 Settings 打开 UseABBackend。"
            : "UseABBackend 开启时，AA 只读。";

        if (_activePanelIndex >= 0 && _activePanelIndex < _panelContents.Length)
        {
            _panelContents[_activePanelIndex].SetEnabled(!disabled);
            _panelContents[_activePanelIndex].style.opacity = disabled ? 0.45f : 1f;
        }
    }

    #endregion

    #region Public API

    /// <summary>
    /// 初始化窗口面板顺序。
    /// 面板实例顺序必须与侧边栏分组定义保持一致。
    /// </summary>
    public void InitPanels()
    {
        _panels = new IBuildPipelinePanel[]
        {
            new SettingsPanel(),
            new AAConfigPanel(),
            new AABuildPanel(),
            new AAReportPanel(),
            new ABConfigPanel(),
            new CollectorSettingPanel(),
            new CollectorPanel(),
            new PipelinePanel(),
            new RepositoryStatusPanel(),
            new BuilderPanel(),
            new VersionPanel(),
        };

        for (int i = 0; i < _panels.Length; i++)
            _panels[i].OnEnable(this);

        _expandedGroupIndex = 0;
        _lastVisiblePanelIndex = -1;
    }

    /// <summary>
    /// 在 Settings 等上层状态变化后刷新窗口壳层表现。
    /// </summary>
    public void RefreshShell()
    {
        RefreshSidebar();
        RefreshDisabledState();
    }

    private bool IsGroupExpanded(int groupIndex)
    {
        SidebarGroup group = Groups[groupIndex];
        if (!group.Collapsible)
            return true;

        int activeGroupIndex = GetGroupIndexByPanelIndex(_activePanelIndex);
        if (activeGroupIndex == groupIndex)
            return true;

        return _expandedGroupIndex == groupIndex;
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
