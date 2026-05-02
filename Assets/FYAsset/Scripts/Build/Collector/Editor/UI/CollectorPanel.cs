using System.Collections.Generic;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

/// <summary>
/// Collector 区域面板 —— 顶部工具栏 + 中部 TreeView/属性面板 + 底部结果区（Validation / Scan Preview）。
/// </summary>
public sealed class CollectorPanel : IBuildPipelinePanel
{
    #region Fields

    private const float SplitterWidth = 4f;

    private EditorWindow _window;
    private CollectorSetting _setting;
    private SerializedObject _settingSO;

    private CollectorTreeView _treeView;
    private TreeViewState _treeState;
    private CollectorPropertyPanel _propertyPanel;

    private List<BuildMessage> _validationMessages;
    private bool _isDraggingSplitter;
    private float _toolbarHeight = 22f;
    private float _bottomResultHeight = 120f;
    private float _treeWidth = 320f;
    private float _minTreeWidth = 200f;
    private float _minInspectorWidth = 240f;
    private enum BottomMode { Validation, ScanPreview }
    private BottomMode _bottomMode = BottomMode.Validation;
    private ScanResult _lastScanResult;
    private bool _isScanning = false;

    private string _searchFilter = "";

    #endregion

    #region IBuildPipelinePanel

    public string PanelName => "Collector";

    public void OnEnable(EditorWindow window)
    {
        _window = window;
        LoadSetting();
        _propertyPanel = new CollectorPropertyPanel();
    }

    public void OnGUI(Rect windowRect)
    {
        if (_setting == null)
        {
            DrawNoSetting(windowRect);
            return;
        }

        float reservedBottomHeight = Mathf.Min(_bottomResultHeight, Mathf.Max(0f, windowRect.height - _toolbarHeight));
        float middleHeight = Mathf.Max(0f, windowRect.height - _toolbarHeight - reservedBottomHeight);

        Rect topToolbarRect = new Rect(windowRect.x, windowRect.y, windowRect.width, _toolbarHeight);
        Rect middleContentRect = new Rect(windowRect.x, topToolbarRect.yMax, windowRect.width, middleHeight);
        Rect bottomResultRect = new Rect(windowRect.x, middleContentRect.yMax, windowRect.width, reservedBottomHeight);

        DrawToolbar(topToolbarRect);
        DrawSplitView(middleContentRect);

        const float tabStripHeight = 20f;
        Rect tabRect = new Rect(bottomResultRect.x, bottomResultRect.y, bottomResultRect.width, Mathf.Min(tabStripHeight, bottomResultRect.height));
        Rect resultContentRect = new Rect(bottomResultRect.x, tabRect.yMax, bottomResultRect.width, Mathf.Max(0f, bottomResultRect.height - tabStripHeight));
        DrawBottomTabs(tabRect);
        CollectorResultPanel.Render(resultContentRect, _validationMessages, _lastScanResult, _isScanning, _bottomMode == BottomMode.Validation);
    }

    public void OnDisable() { }

    #endregion

    #region Load & Init

    private void LoadSetting()
    {
        _setting = AssetDatabase.LoadAssetAtPath<CollectorSetting>(
            FYAssetConstants.COLLECTOR_SETTING_ASSET_PATH);
        _settingSO = _setting != null ? new SerializedObject(_setting) : null;

        if (_treeState == null)
            _treeState = new TreeViewState();

        _treeView = new CollectorTreeView(_treeState, _setting);
        _treeView.Reload();

        _validationMessages = CollectorSettingValidator.Validate(_setting);
    }

    private void DrawNoSetting(Rect rect)
    {
        GUILayout.BeginArea(rect);
        GUILayout.FlexibleSpace();
        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        GUILayout.Label("No CollectorSetting found at " + FYAssetConstants.COLLECTOR_SETTING_ASSET_PATH,
            EditorStyles.centeredGreyMiniLabel);
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();
        if (GUILayout.Button("Create CollectorSetting", GUILayout.Width(200), GUILayout.Height(36)))
            CreateCollectorSetting();
        GUILayout.FlexibleSpace();
        GUILayout.EndArea();
    }

    private void CreateCollectorSetting()
    {
        // Ensure directory exists
        string dir = System.IO.Path.GetDirectoryName(FYAssetConstants.COLLECTOR_SETTING_ASSET_PATH);
        if (!AssetDatabase.IsValidFolder(dir))
        {
            string parent = System.IO.Path.GetDirectoryName(dir);
            string folderName = System.IO.Path.GetFileName(dir);
            AssetDatabase.CreateFolder(parent, folderName);
        }

        var newSetting = ScriptableObject.CreateInstance<CollectorSetting>();
        AssetDatabase.CreateAsset(newSetting, FYAssetConstants.COLLECTOR_SETTING_ASSET_PATH);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        LoadSetting();
    }

    #endregion

    #region Toolbar

    private void DrawToolbar(Rect rect)
    {
        GUILayout.BeginArea(rect);
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

        // Search field
        EditorGUI.BeginChangeCheck();
        _searchFilter = EditorGUILayout.TextField(_searchFilter, EditorStyles.toolbarSearchField,
            GUILayout.Width(200));
        if (EditorGUI.EndChangeCheck())
        {
            _treeView.searchString = _searchFilter;
        }

        GUILayout.FlexibleSpace();

        if (GUILayout.Button("Expand All", EditorStyles.toolbarButton))
            _treeView.ExpandAll();

        if (GUILayout.Button("Collapse All", EditorStyles.toolbarButton))
            _treeView.CollapseAll();

        if (GUILayout.Button("Reload", EditorStyles.toolbarButton))
            LoadSetting();

        if (GUILayout.Button("Re-Validate", EditorStyles.toolbarButton))
            _validationMessages = CollectorSettingValidator.Validate(_setting);

        if (GUILayout.Button("Run Scan", EditorStyles.toolbarButton))
        {
            if (_setting != null && !_isScanning)
            {
                try
                {
                    _isScanning = true;
                    _lastScanResult = CollectionScanner.Scan(_setting);
                    _bottomMode = BottomMode.ScanPreview;
                    Debug.Log("[CollectorPanel] Scan complete. Assets: " +
                        (_lastScanResult?.Assets?.Count ?? 0) +
                        ", Messages: " + (_lastScanResult?.Messages?.Count ?? 0));
                }
                catch (System.Exception ex)
                {
                    Debug.LogException(ex);
                    _lastScanResult = null;
                }
                finally
                {
                    _isScanning = false;
                }
            }
        }

        EditorGUILayout.EndHorizontal();
        GUILayout.EndArea();
    }

    #endregion

    #region Split View

    private void DrawSplitView(Rect middleContentRect)
    {
        // TreeView (may be null if ctor failed on first load)
        if (_treeView == null || middleContentRect.width <= 0f || middleContentRect.height <= 0f)
            return;

        // Splitter math is now derived from the middle content rect only, so the reserved
        // bottom band never affects drag behavior and the inspector can clamp against its own space.
        float treeWidth = ClampTreeWidth(_treeWidth, middleContentRect.width);
        float inspectorWidth = Mathf.Max(0f, middleContentRect.width - treeWidth - SplitterWidth);

        Rect treeRect = new Rect(middleContentRect.x, middleContentRect.y, treeWidth, middleContentRect.height);
        Rect splitterRect = new Rect(treeRect.xMax, middleContentRect.y, SplitterWidth, middleContentRect.height);
        Rect propRect = new Rect(splitterRect.xMax, middleContentRect.y, inspectorWidth, middleContentRect.height);

        _treeWidth = treeWidth;
        _treeView.OnGUI(treeRect);

        // Selection change handling
        var selected = _treeView.GetSelectedItem();
        _propertyPanel.SetSelection(selected, _setting);

        // Check if SO was modified (Undo or another panel)
        if (_settingSO != null)
        {
            _settingSO.Update();
            if (_settingSO.hasModifiedProperties)
            {
                _validationMessages = CollectorSettingValidator.Validate(_setting);
                _settingSO.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        // Splitter
        EditorGUIUtility.AddCursorRect(splitterRect, MouseCursor.ResizeHorizontal);
        DrawSplitter(splitterRect, middleContentRect);

        // Property Panel
        _propertyPanel.OnGUI(propRect);

    }

    private void DrawBottomTabs(Rect rect)
    {
        if (rect.height <= 0f || rect.width <= 0f) return;
        GUILayout.BeginArea(rect, EditorStyles.toolbar);
        EditorGUILayout.BeginHorizontal();
        bool wantValidation = GUILayout.Toggle(_bottomMode == BottomMode.Validation, "Validation", EditorStyles.toolbarButton);
        bool wantScan = GUILayout.Toggle(_bottomMode == BottomMode.ScanPreview, "Scan Preview", EditorStyles.toolbarButton);
        if (wantScan && _bottomMode != BottomMode.ScanPreview)
            _bottomMode = BottomMode.ScanPreview;
        else if (wantValidation && _bottomMode != BottomMode.Validation)
            _bottomMode = BottomMode.Validation;
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();
        GUILayout.EndArea();
    }

    private float ClampTreeWidth(float requestedWidth, float middleWidth)
    {
        float availableWidth = Mathf.Max(0f, middleWidth - SplitterWidth);
        if (availableWidth <= 0f)
            return 0f;

        float maxTreeWidth = availableWidth - _minInspectorWidth;
        if (maxTreeWidth >= _minTreeWidth)
            return Mathf.Clamp(requestedWidth, _minTreeWidth, maxTreeWidth);

        // If the host window becomes smaller than the combined minimum widths, keep all widths valid
        // and prefer a stable split instead of allowing negative inspector sizes.
        return Mathf.Clamp(requestedWidth, 0f, availableWidth);
    }

    private void DrawSplitter(Rect rect, Rect middleContentRect)
    {
        EditorGUI.DrawRect(rect, EditorGUIUtility.isProSkin
            ? new Color(0.12f, 0.12f, 0.12f)
            : new Color(0.55f, 0.55f, 0.55f));

        Event evt = Event.current;
        if (evt.type == EventType.MouseDown && rect.Contains(evt.mousePosition))
        {
            _isDraggingSplitter = true;
            evt.Use();
        }

        if (_isDraggingSplitter)
        {
            if (evt.type == EventType.MouseDrag)
            {
                float localMouseX = evt.mousePosition.x - middleContentRect.x;
                _treeWidth = ClampTreeWidth(localMouseX, middleContentRect.width);
                _window.Repaint();
                evt.Use();
            }

            if (evt.type == EventType.MouseUp)
            {
                _isDraggingSplitter = false;
                evt.Use();
            }
        }
    }

    #endregion
}
