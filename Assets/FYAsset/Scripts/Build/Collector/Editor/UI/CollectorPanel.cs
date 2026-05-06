using System.Collections.Generic;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

/// <summary>
/// Collector 面板：树状图 + 底部扫描结果
/// </summary>
public sealed class CollectorPanel : IBuildPipelinePanel
{
    #region Fields

    private EditorWindow _window;
    private CollectorSetting _setting;

    private CollectorTreeView _treeView;
    private TreeViewState _treeState;

    private List<BuildMessage> _validationMessages;
    private bool _isDraggingBottomSplitter;
    private float _toolbarHeight = 22f;
    private float _bottomResultHeight = 120f;
    private float _minBottomHeight = 60f;
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
    }

    public void OnGUI(Rect windowRect)
    {
        if (_setting == null)
        {
            DrawNoSetting(windowRect);
            return;
        }

        float reservedBottomHeight = Mathf.Min(_bottomResultHeight, Mathf.Max(0f, windowRect.height - _toolbarHeight - _minBottomHeight));
        float middleHeight = Mathf.Max(0f, windowRect.height - _toolbarHeight - reservedBottomHeight);

        Rect topToolbarRect = new Rect(windowRect.x, windowRect.y, windowRect.width, _toolbarHeight);
        Rect middleContentRect = new Rect(windowRect.x, topToolbarRect.yMax, windowRect.width, middleHeight);
        Rect bottomSplitterRect = new Rect(windowRect.x, middleContentRect.yMax - 2f, windowRect.width, 4f);
        Rect bottomResultRect = new Rect(windowRect.x, middleContentRect.yMax, windowRect.width, reservedBottomHeight);

        DrawToolbar(topToolbarRect);
        DrawTree(middleContentRect);
        DrawBottomSplitter(bottomSplitterRect, windowRect);

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
        _setting = AssetDatabase.LoadAssetAtPath<CollectorSetting>(FYAssetConstants.COLLECTOR_SETTING_ASSET_PATH);

        if (_treeState == null)
            _treeState = new TreeViewState();

        _treeView = new CollectorTreeView(_treeState, _setting);
        _treeView.Reload();

        if (_setting != null)
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

        EditorGUI.BeginChangeCheck();
        _searchFilter = EditorGUILayout.TextField(_searchFilter, EditorStyles.toolbarSearchField, GUILayout.Width(200));
        if (EditorGUI.EndChangeCheck())
            _treeView.searchString = _searchFilter;

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

    #region Tree

    private void DrawTree(Rect rect)
    {
        if (_treeView == null || rect.width <= 0f || rect.height <= 0f) return;
        _treeView.OnGUI(rect);
    }

    #endregion

    #region Bottom Panel

    private void DrawBottomSplitter(Rect rect, Rect windowRect)
    {
        EditorGUIUtility.AddCursorRect(rect, MouseCursor.ResizeVertical);

        Event evt = Event.current;
        if (evt.type == EventType.MouseDown && rect.Contains(evt.mousePosition))
        {
            _isDraggingBottomSplitter = true;
            evt.Use();
        }

        if (_isDraggingBottomSplitter)
        {
            if (evt.type == EventType.MouseDrag)
            {
                float newBottomHeight = windowRect.yMax - evt.mousePosition.y;
                _bottomResultHeight = Mathf.Clamp(newBottomHeight, _minBottomHeight, windowRect.height - _toolbarHeight - _minBottomHeight);
                _window.Repaint();
                evt.Use();
            }

            if (evt.type == EventType.MouseUp)
            {
                _isDraggingBottomSplitter = false;
                evt.Use();
            }
        }
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

    #endregion
}
