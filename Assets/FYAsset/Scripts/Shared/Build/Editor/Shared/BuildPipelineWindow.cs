using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Shared shell for backend-specific build pipeline windows.
/// </summary>
public abstract class BuildPipelineWindowBase : EditorWindow
{
    private float _sidebarWidth = 160f;
    private bool _isDraggingSidebar;
    private Vector2 _dragStartMouse;
    private float _dragStartWidth;

    private IBuildPipelinePanel[] _panels;
    private VisualElement[] _panelContents;
    private Button[] _panelButtons;
    private VisualElement _sidebar;
    private VisualElement _contentHost;
    private int _activePanelIndex;
    private int _lastVisiblePanelIndex = -1;

    protected abstract IBuildPipelinePanel[] CreatePanels();

    private void OnEnable()
    {
        _panels = CreatePanels();
        for (int i = 0; i < _panels.Length; i++)
            _panels[i].OnEnable(this);
        _lastVisiblePanelIndex = -1;
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

    private void BuildRoot()
    {
        rootVisualElement.Clear();

        var root = new VisualElement { name = "BuildPipelineWindowRoot" };
        root.style.flexDirection = FlexDirection.Row;
        root.style.flexGrow = 1f;
        root.style.backgroundColor = BuildPipelineUI.WindowBackgroundColor;
        rootVisualElement.Add(root);

        _sidebar = new VisualElement { name = "Sidebar" };
        _sidebar.style.width = _sidebarWidth;
        _sidebar.style.minWidth = 100f;
        _sidebar.style.maxWidth = 300f;
        _sidebar.style.flexShrink = 0f;
        _sidebar.style.backgroundColor = BuildPipelineUI.SidebarBackgroundColor;
        _sidebar.style.paddingLeft = 8f;
        _sidebar.style.paddingRight = 8f;
        _sidebar.style.paddingTop = 12f;
        root.Add(_sidebar);

        VisualElement splitter = BuildPipelineUI.Splitter(true);
        splitter.name = "SidebarSplitter";
        splitter.RegisterCallback<PointerDownEvent>(OnSplitterPointerDown);
        splitter.RegisterCallback<PointerMoveEvent>(OnSplitterPointerMove);
        splitter.RegisterCallback<PointerUpEvent>(OnSplitterPointerUp);
        root.Add(splitter);

        var contentShell = new VisualElement { name = "ContentShell" };
        contentShell.style.flexGrow = 1f;
        contentShell.style.minWidth = 0f;
        contentShell.style.paddingLeft = 12f;
        contentShell.style.paddingRight = 12f;
        contentShell.style.paddingTop = 12f;
        contentShell.style.paddingBottom = 12f;
        contentShell.style.flexDirection = FlexDirection.Column;
        root.Add(contentShell);

        _contentHost = new VisualElement { name = "ContentHost" };
        _contentHost.style.flexGrow = 1f;
        _contentHost.style.minHeight = 0f;
        _contentHost.style.flexDirection = FlexDirection.Column;
        contentShell.Add(_contentHost);
    }

    private void BuildSidebar()
    {
        _panelButtons = new Button[_panels.Length];
        for (int i = 0; i < _panels.Length; i++)
        {
            int panelIndex = i;
            var button = new Button(() => SelectPanel(panelIndex, false)) { text = _panels[i].PanelName };
            button.style.height = 34f;
            button.style.marginBottom = 2f;
            button.style.paddingLeft = 16f;
            button.style.unityTextAlign = TextAnchor.MiddleLeft;
            button.style.whiteSpace = WhiteSpace.Normal;
            button.style.borderTopWidth = 0f;
            button.style.borderRightWidth = 0f;
            button.style.borderBottomWidth = 0f;
            button.style.borderLeftWidth = 0f;
            _panelButtons[i] = button;
            _sidebar.Add(button);
        }
    }

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

    private void SelectPanel(int panelIndex, bool force)
    {
        if (_panels == null || panelIndex < 0 || panelIndex >= _panels.Length)
            return;
        if (!force && _activePanelIndex == panelIndex)
            return;

        if (_lastVisiblePanelIndex >= 0 && _lastVisiblePanelIndex < _panels.Length &&
            _panels[_lastVisiblePanelIndex] is IBuildPipelinePanelVisibility previous)
            previous.SetVisible(false);

        _activePanelIndex = panelIndex;
        _lastVisiblePanelIndex = panelIndex;
        for (int i = 0; i < _panelContents.Length; i++)
            _panelContents[i].style.display = i == panelIndex ? DisplayStyle.Flex : DisplayStyle.None;

        if (_panels[panelIndex] is IBuildPipelinePanelVisibility current)
            current.SetVisible(true);

        for (int i = 0; i < _panelButtons.Length; i++)
        {
            bool active = i == panelIndex;
            _panelButtons[i].style.backgroundColor = active ? BuildPipelineUI.ActiveColor : Color.clear;
            _panelButtons[i].style.color = active ? Color.white : new Color(0.8f, 0.8f, 0.8f);
            _panelButtons[i].style.unityFontStyleAndWeight = active ? FontStyle.Bold : FontStyle.Normal;
        }
    }

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

    private void OnSplitterPointerMove(PointerMoveEvent evt)
    {
        if (!_isDraggingSidebar)
            return;
        _sidebarWidth = Mathf.Clamp(_dragStartWidth + evt.position.x - _dragStartMouse.x, 100f, 300f);
        _sidebar.style.width = _sidebarWidth;
        evt.StopPropagation();
    }

    private void OnSplitterPointerUp(PointerUpEvent evt)
    {
        if (!_isDraggingSidebar)
            return;
        _isDraggingSidebar = false;
        evt.target.ReleasePointer(evt.pointerId);
        evt.StopPropagation();
    }

}
