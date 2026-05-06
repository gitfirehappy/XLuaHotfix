using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Collector 操作面板 —— 当前 Group 的高密度 Collector 列表与验证/扫描结果。
/// </summary>
public sealed class CollectorPanel : IBuildPipelinePanel
{
    private enum BottomMode
    {
        Validation,
        ScanPreview
    }

    private EditorWindow _window;
    private CollectorSetting _setting;
    private SerializedObject _so;

    private Vector2 _tableScroll;
    private Vector2 _detailScroll;
    private List<BuildMessage> _validationMessages;

    private int _selectedPackageIndex = -1;
    private int _selectedGroupIndex = -1;
    private int _selectedCollectorIndex = -1;

    private bool _isDraggingBottomSplitter;
    private bool _isDraggingDetailSplitter;
    private float _toolbarHeight = 24f;
    private float _bottomResultHeight = 140f;
    private float _minBottomHeight = 72f;
    private float _detailWidth = 320f;

    private BottomMode _bottomMode = BottomMode.Validation;
    private ScanResult _lastScanResult;
    private bool _isScanning;
    private string _searchFilter = string.Empty;

    public string PanelName => "Collector";

    public void OnEnable(EditorWindow window)
    {
        _window = window;
        LoadSetting();
    }

    public void OnDisable() { }

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
        DrawMainContent(middleContentRect);
        HandleTableDragAndDrop(middleContentRect);
        DrawBottomSplitter(bottomSplitterRect, windowRect);

        const float tabStripHeight = 20f;
        Rect tabRect = new Rect(bottomResultRect.x, bottomResultRect.y, bottomResultRect.width, Mathf.Min(tabStripHeight, bottomResultRect.height));
        Rect resultContentRect = new Rect(bottomResultRect.x, tabRect.yMax, bottomResultRect.width, Mathf.Max(0f, bottomResultRect.height - tabStripHeight));
        DrawBottomTabs(tabRect);
        CollectorResultPanel.Render(resultContentRect, _validationMessages, _lastScanResult, _isScanning, _bottomMode == BottomMode.Validation);

        ApplyChanges();
    }

    private void LoadSetting()
    {
        CollectorDataMigrator.EnsureDataFolder();
        CollectorDataMigrator.MigrateFromLegacyPath();
        _setting = AssetDatabase.LoadAssetAtPath<CollectorSetting>(FYAssetConstants.COLLECTOR_SETTING_ASSET_PATH);
        _so = _setting != null ? new SerializedObject(_setting) : null;
        EnsureSelection();
        if (_setting != null)
            _validationMessages = CollectorSettingValidator.Validate(_setting);
    }

    private void DrawNoSetting(Rect rect)
    {
        GUILayout.BeginArea(rect);
        GUILayout.FlexibleSpace();
        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        GUILayout.BeginVertical("box", GUILayout.Width(420f));
        GUILayout.Space(10f);
        GUILayout.Label("CollectorSetting not found", EditorStyles.boldLabel);
        GUILayout.Space(6f);
        GUILayout.Label(FYAssetConstants.COLLECTOR_SETTING_ASSET_PATH, EditorStyles.wordWrappedMiniLabel);
        GUILayout.Space(10f);
        if (GUILayout.Button("Create CollectorSetting", GUILayout.Height(36f)))
            CreateCollectorSetting();
        GUILayout.Space(10f);
        GUILayout.EndVertical();
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();
        GUILayout.FlexibleSpace();
        GUILayout.EndArea();
    }

    private void CreateCollectorSetting()
    {
        CollectorDataMigrator.EnsureDataFolder();
        CollectorSetting newSetting = ScriptableObject.CreateInstance<CollectorSetting>();
        AssetDatabase.CreateAsset(newSetting, FYAssetConstants.COLLECTOR_SETTING_ASSET_PATH);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        CollectorReverseIndex.Instance.MarkDirty();
        LoadSetting();
    }

    private void DrawToolbar(Rect rect)
    {
        GUILayout.BeginArea(rect);
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

        DrawPackagePopup();
        DrawGroupPopup();

        GUILayout.Space(6f);

        using (new EditorGUI.DisabledScope(GetCurrentGroupProperty() == null))
        {
            if (GUILayout.Button("+ Folder", EditorStyles.toolbarButton, GUILayout.Width(72f)))
                AddCollector(false);
            if (GUILayout.Button("+ File", EditorStyles.toolbarButton, GUILayout.Width(60f)))
                AddCollector(true);
            if (GUILayout.Button("- Remove", EditorStyles.toolbarButton, GUILayout.Width(74f)))
                RemoveSelectedCollector();
        }

        GUILayout.Space(8f);
        _searchFilter = EditorGUILayout.TextField(_searchFilter, EditorStyles.toolbarSearchField, GUILayout.Width(180f));
        GUILayout.FlexibleSpace();

        if (GUILayout.Button("Reload", EditorStyles.toolbarButton, GUILayout.Width(54f)))
            LoadSetting();
        if (GUILayout.Button("Re-Validate", EditorStyles.toolbarButton, GUILayout.Width(84f)))
            _validationMessages = CollectorSettingValidator.Validate(_setting);
        if (GUILayout.Button("Run Scan", EditorStyles.toolbarButton, GUILayout.Width(64f)))
            RunScan();

        EditorGUILayout.EndHorizontal();
        GUILayout.EndArea();
    }

    private void DrawPackagePopup()
    {
        string[] packageNames = GetPackageNames();
        if (packageNames.Length == 0)
        {
            EditorGUILayout.Popup(0, new[] { "No Package" }, EditorStyles.toolbarPopup, GUILayout.Width(130f));
            return;
        }

        int newIndex = EditorGUILayout.Popup(_selectedPackageIndex, packageNames, EditorStyles.toolbarPopup, GUILayout.Width(130f));
        if (newIndex != _selectedPackageIndex)
        {
            _selectedPackageIndex = newIndex;
            _selectedGroupIndex = 0;
            _selectedCollectorIndex = 0;
            EnsureSelection();
        }
    }

    private void DrawGroupPopup()
    {
        string[] groupNames = GetGroupNames();
        if (groupNames.Length == 0)
        {
            EditorGUILayout.Popup(0, new[] { "No Group" }, EditorStyles.toolbarPopup, GUILayout.Width(140f));
            return;
        }

        int newIndex = EditorGUILayout.Popup(_selectedGroupIndex, groupNames, EditorStyles.toolbarPopup, GUILayout.Width(140f));
        if (newIndex != _selectedGroupIndex)
        {
            _selectedGroupIndex = newIndex;
            _selectedCollectorIndex = 0;
            EnsureSelection();
        }
    }

    private void DrawMainContent(Rect rect)
    {
        float tableWidth = Mathf.Max(100f, rect.width - _detailWidth - 8f);
        Rect tableRect = new Rect(rect.x, rect.y, tableWidth, rect.height);
        Rect splitterRect = new Rect(tableRect.xMax, rect.y, 8f, rect.height);
        Rect detailRect = new Rect(splitterRect.xMax, rect.y, Mathf.Max(0f, _detailWidth), rect.height);

        EditorGUIUtility.AddCursorRect(splitterRect, MouseCursor.ResizeHorizontal);
        Event evt = Event.current;
        if (evt.type == EventType.MouseDown && splitterRect.Contains(evt.mousePosition))
        {
            _isDraggingDetailSplitter = true;
            evt.Use();
        }
        if (_isDraggingDetailSplitter)
        {
            if (evt.type == EventType.MouseDrag)
            {
                _detailWidth = Mathf.Clamp(rect.xMax - evt.mousePosition.x, 180f, rect.width * 0.6f);
                _window?.Repaint();
                evt.Use();
            }
            if (evt.type == EventType.MouseUp)
            {
                _isDraggingDetailSplitter = false;
                evt.Use();
            }
        }

        DrawCollectorTable(tableRect);
        DrawCollectorDetail(detailRect);
    }

    private void DrawCollectorTable(Rect rect)
    {
        GUILayout.BeginArea(rect, GUI.skin.box);
        DrawTableHeader();

        SerializedProperty collectorsProp = GetCurrentCollectorsProperty();
        if (collectorsProp == null || collectorsProp.arraySize == 0)
        {
            GUILayout.FlexibleSpace();
            GUILayout.Label("No collectors in current group.", EditorStyles.centeredGreyMiniLabel);
            GUILayout.FlexibleSpace();
            GUILayout.EndArea();
            return;
        }

        _tableScroll = EditorGUILayout.BeginScrollView(_tableScroll);
        for (int i = 0; i < collectorsProp.arraySize; i++)
        {
            SerializedProperty collectorProp = collectorsProp.GetArrayElementAtIndex(i);
            if (!MatchesSearch(collectorProp))
                continue;

            DrawCollectorRow(collectorProp, i, collectorsProp);
            GUILayout.Space(4f);
        }
        EditorGUILayout.EndScrollView();
        GUILayout.EndArea();
    }

    private void HandleTableDragAndDrop(Rect rect)
    {
        Event evt = Event.current;
        if (evt == null)
            return;

        if (evt.type != EventType.DragUpdated && evt.type != EventType.DragPerform)
            return;
        if (!rect.Contains(evt.mousePosition))
            return;
        if (GetCurrentGroupProperty() == null)
            return;

        string[] assetPaths = GetDraggedAssetPaths();
        if (assetPaths.Length == 0)
            return;

        DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
        if (evt.type == EventType.DragPerform)
        {
            DragAndDrop.AcceptDrag();
            AddDraggedCollectors(assetPaths);
        }

        evt.Use();
    }

    private void DrawTableHeader()
    {
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("Path Type", EditorStyles.miniBoldLabel, GUILayout.Width(92f));
        GUILayout.Label("Collect Path", EditorStyles.miniBoldLabel);
        GUILayout.Label("", GUILayout.Width(28f));
        GUILayout.Label("", GUILayout.Width(24f));
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("Type", EditorStyles.miniBoldLabel, GUILayout.Width(92f));
        GUILayout.Label("Payload", EditorStyles.miniBoldLabel, GUILayout.Width(104f));
        GUILayout.Label("Addr", EditorStyles.miniBoldLabel, GUILayout.Width(92f));
        GUILayout.Label("Pack", EditorStyles.miniBoldLabel, GUILayout.Width(92f));
        GUILayout.Label("Filter", EditorStyles.miniBoldLabel, GUILayout.Width(92f));
        GUILayout.Label("Group", EditorStyles.miniBoldLabel, GUILayout.Width(92f));
        EditorGUILayout.EndHorizontal();
        GUILayout.Space(4f);
    }

    private void DrawCollectorRow(SerializedProperty collectorProp, int collectorIndex, SerializedProperty collectorsProp)
    {
        bool isSelected = _selectedCollectorIndex == collectorIndex;
        Rect rowRect = EditorGUILayout.BeginVertical("box");
        if (Event.current.type == EventType.Repaint && isSelected)
            EditorGUI.DrawRect(rowRect, new Color(0.17f, 0.36f, 0.53f, 0.18f));

        EditorGUILayout.BeginHorizontal();

        SerializedProperty pathTypeProp = collectorProp.FindPropertyRelative("CollectPathType");
        SerializedProperty pathProp = collectorProp.FindPropertyRelative("CollectPath");

        EditorGUILayout.PropertyField(pathTypeProp, GUIContent.none, GUILayout.Width(92f));
        EditorGUILayout.PropertyField(pathProp, GUIContent.none);
        if (GUILayout.Button("…", GUILayout.Width(28f)))
            PickCollectPath(pathProp, pathTypeProp.enumValueIndex == (int)ECollectPathType.File);
        if (GUILayout.Button("×", GUILayout.Width(24f)))
        {
            RemoveCollector(collectorsProp, collectorIndex);
            EditorGUILayout.EndHorizontal();
            GUILayout.EndVertical();
            return;
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.PropertyField(collectorProp.FindPropertyRelative("CollectorType"), GUIContent.none, GUILayout.Width(92f));
        EditorGUILayout.PropertyField(collectorProp.FindPropertyRelative("ForcePayloadKind"), GUIContent.none, GUILayout.Width(104f));
        DrawRulePopup(collectorProp.FindPropertyRelative("AddressRuleName"), 92f);
        DrawRulePopup(collectorProp.FindPropertyRelative("PackRuleName"), 92f);
        DrawRulePopup(collectorProp.FindPropertyRelative("FilterRuleName"), 92f);
        DrawRulePopup(collectorProp.FindPropertyRelative("GroupRuleName"), 92f);
        EditorGUILayout.EndHorizontal();

        GUILayout.EndVertical();

        if (Event.current.type == EventType.MouseDown && rowRect.Contains(Event.current.mousePosition))
        {
            _selectedCollectorIndex = collectorIndex;
            Event.current.Use();
        }
    }

    private void DrawCollectorDetail(Rect rect)
    {
        GUILayout.BeginArea(rect, GUI.skin.box);
        _detailScroll = EditorGUILayout.BeginScrollView(_detailScroll);

        SerializedProperty collectorProp = GetSelectedCollectorProperty();
        if (collectorProp == null)
        {
            GUILayout.Space(20f);
            GUILayout.Label("Select a collector row", EditorStyles.boldLabel);
            GUILayout.Space(6f);
            GUILayout.Label("Use the table on the left to change the current collector. Details for labels and ignore patterns appear here.", EditorStyles.wordWrappedLabel);
            EditorGUILayout.EndScrollView();
            GUILayout.EndArea();
            return;
        }

        GUILayout.Label("Collector Detail", EditorStyles.boldLabel);
        GUILayout.Space(6f);
        EditorGUILayout.PropertyField(collectorProp.FindPropertyRelative("CollectPathType"), new GUIContent("Path Type"));
        EditorGUILayout.PropertyField(collectorProp.FindPropertyRelative("CollectPath"), new GUIContent("Collect Path"));
        EditorGUILayout.PropertyField(collectorProp.FindPropertyRelative("CollectorType"), new GUIContent("Collector Type"));
        EditorGUILayout.PropertyField(collectorProp.FindPropertyRelative("ForcePayloadKind"), new GUIContent("Payload"));

        GUILayout.Space(8f);
        GUILayout.Label("Rules", EditorStyles.boldLabel);
        DrawLabeledRulePopup("Address", collectorProp.FindPropertyRelative("AddressRuleName"));
        DrawLabeledRulePopup("Pack", collectorProp.FindPropertyRelative("PackRuleName"));
        DrawLabeledRulePopup("Filter", collectorProp.FindPropertyRelative("FilterRuleName"));
        DrawLabeledRulePopup("Group", collectorProp.FindPropertyRelative("GroupRuleName"));

        GUILayout.Space(8f);
        EditorGUILayout.PropertyField(collectorProp.FindPropertyRelative("Labels"), new GUIContent("Labels"), true);
        EditorGUILayout.PropertyField(collectorProp.FindPropertyRelative("IgnorePatterns"), new GUIContent("Ignore Patterns"), true);
        EditorGUILayout.EndScrollView();
        GUILayout.EndArea();
    }

    private void DrawRulePopup(SerializedProperty property, float width)
    {
        Rect rect = GUILayoutUtility.GetRect(width, EditorGUIUtility.singleLineHeight, GUILayout.Width(width));
        if (property.name == "AddressRuleName")
            property.stringValue = RuleDropdownHelper.AddressRulePopup(rect, property.stringValue);
        else if (property.name == "PackRuleName")
            property.stringValue = RuleDropdownHelper.PackRulePopup(rect, property.stringValue);
        else if (property.name == "FilterRuleName")
            property.stringValue = RuleDropdownHelper.FilterRulePopup(rect, property.stringValue);
        else
            property.stringValue = RuleDropdownHelper.GroupRulePopup(rect, property.stringValue);
    }

    private void DrawLabeledRulePopup(string label, SerializedProperty property)
    {
        Rect rect = EditorGUILayout.GetControlRect();
        rect = EditorGUI.PrefixLabel(rect, new GUIContent(label));
        if (property.name == "AddressRuleName")
            property.stringValue = RuleDropdownHelper.AddressRulePopup(rect, property.stringValue);
        else if (property.name == "PackRuleName")
            property.stringValue = RuleDropdownHelper.PackRulePopup(rect, property.stringValue);
        else if (property.name == "FilterRuleName")
            property.stringValue = RuleDropdownHelper.FilterRulePopup(rect, property.stringValue);
        else
            property.stringValue = RuleDropdownHelper.GroupRulePopup(rect, property.stringValue);
    }

    private void RunScan()
    {
        if (_setting == null || _isScanning)
            return;

        try
        {
            _isScanning = true;
            _lastScanResult = CollectionScanner.Scan(_setting);
            _bottomMode = BottomMode.ScanPreview;
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
            _lastScanResult = null;
        }
        finally
        {
            _isScanning = false;
        }
    }

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
        if (rect.height <= 0f || rect.width <= 0f)
            return;

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

    private void AddCollector(bool isFile)
    {
        SerializedProperty collectorsProp = GetCurrentCollectorsProperty();
        if (collectorsProp == null)
            return;

        Undo.RecordObject(_setting, isFile ? "Add File Collector" : "Add Folder Collector");
        int index = collectorsProp.arraySize;
        collectorsProp.arraySize++;

        SerializedProperty collectorProp = collectorsProp.GetArrayElementAtIndex(index);
        collectorProp.FindPropertyRelative("CollectPath").stringValue = string.Empty;
        collectorProp.FindPropertyRelative("CollectPathType").enumValueIndex = isFile ? (int)ECollectPathType.File : (int)ECollectPathType.Folder;
        collectorProp.FindPropertyRelative("CollectorType").enumValueIndex = (int)ECollectorType.Main;
        collectorProp.FindPropertyRelative("ForcePayloadKind").enumValueIndex = (int)EForcePayloadKind.Auto;
        collectorProp.FindPropertyRelative("AddressRuleName").stringValue = FYAssetConstants.RULE_ADDRESS_BY_FILE_NAME;
        collectorProp.FindPropertyRelative("PackRuleName").stringValue = isFile ? FYAssetConstants.RULE_PACK_SEPARATELY : FYAssetConstants.RULE_PACK_BY_DIRECTORY;
        collectorProp.FindPropertyRelative("FilterRuleName").stringValue = FYAssetConstants.RULE_COLLECT_ALL;
        collectorProp.FindPropertyRelative("GroupRuleName").stringValue = FYAssetConstants.RULE_GROUP_ALL;
        collectorProp.FindPropertyRelative("Labels").arraySize = 0;
        collectorProp.FindPropertyRelative("IgnorePatterns").arraySize = 0;
        _selectedCollectorIndex = index;
    }

    private void AddDraggedCollectors(string[] assetPaths)
    {
        SerializedProperty collectorsProp = GetCurrentCollectorsProperty();
        if (collectorsProp == null)
            return;

        Undo.RecordObject(_setting, "Add Dragged Collectors");
        for (int i = 0; i < assetPaths.Length; i++)
        {
            string assetPath = assetPaths[i];
            bool isFile = !AssetDatabase.IsValidFolder(assetPath);
            int index = collectorsProp.arraySize;
            collectorsProp.arraySize++;
            SerializedProperty collectorProp = collectorsProp.GetArrayElementAtIndex(index);
            collectorProp.FindPropertyRelative("CollectPath").stringValue = assetPath;
            collectorProp.FindPropertyRelative("CollectPathType").enumValueIndex = isFile ? (int)ECollectPathType.File : (int)ECollectPathType.Folder;
            collectorProp.FindPropertyRelative("CollectorType").enumValueIndex = (int)ECollectorType.Main;
            collectorProp.FindPropertyRelative("ForcePayloadKind").enumValueIndex = (int)EForcePayloadKind.Auto;
            collectorProp.FindPropertyRelative("AddressRuleName").stringValue = FYAssetConstants.RULE_ADDRESS_BY_FILE_NAME;
            collectorProp.FindPropertyRelative("PackRuleName").stringValue = isFile ? FYAssetConstants.RULE_PACK_SEPARATELY : FYAssetConstants.RULE_PACK_BY_DIRECTORY;
            collectorProp.FindPropertyRelative("FilterRuleName").stringValue = FYAssetConstants.RULE_COLLECT_ALL;
            collectorProp.FindPropertyRelative("GroupRuleName").stringValue = FYAssetConstants.RULE_GROUP_ALL;
            collectorProp.FindPropertyRelative("Labels").arraySize = 0;
            collectorProp.FindPropertyRelative("IgnorePatterns").arraySize = 0;
            _selectedCollectorIndex = index;
        }
    }

    private void RemoveSelectedCollector()
    {
        SerializedProperty collectorsProp = GetCurrentCollectorsProperty();
        if (collectorsProp == null || _selectedCollectorIndex < 0 || _selectedCollectorIndex >= collectorsProp.arraySize)
            return;

        RemoveCollector(collectorsProp, _selectedCollectorIndex);
    }

    private void RemoveCollector(SerializedProperty collectorsProp, int collectorIndex)
    {
        Undo.RecordObject(_setting, "Remove Collector");
        collectorsProp.DeleteArrayElementAtIndex(collectorIndex);
        if (_selectedCollectorIndex >= collectorsProp.arraySize)
            _selectedCollectorIndex = collectorsProp.arraySize - 1;
    }

    private void PickCollectPath(SerializedProperty pathProp, bool isFile)
    {
        string absolutePath = isFile
            ? EditorUtility.OpenFilePanel("Select Collect File", Application.dataPath, string.Empty)
            : EditorUtility.OpenFolderPanel("Select Collect Folder", Application.dataPath, string.Empty);

        if (string.IsNullOrEmpty(absolutePath))
            return;

        string projectDataPath = Application.dataPath.Replace('\\', '/');
        string normalizedAbsolute = absolutePath.Replace('\\', '/');
        if (!normalizedAbsolute.StartsWith(projectDataPath))
            return;

        pathProp.stringValue = "Assets" + normalizedAbsolute.Substring(projectDataPath.Length);
    }

    private bool MatchesSearch(SerializedProperty collectorProp)
    {
        if (string.IsNullOrEmpty(_searchFilter))
            return true;

        string token = _searchFilter.Trim();
        if (token.Length == 0)
            return true;

        StringComparison comparison = StringComparison.OrdinalIgnoreCase;
        return Contains(collectorProp.FindPropertyRelative("CollectPath")?.stringValue, token, comparison)
            || Contains(collectorProp.FindPropertyRelative("AddressRuleName")?.stringValue, token, comparison)
            || Contains(collectorProp.FindPropertyRelative("PackRuleName")?.stringValue, token, comparison)
            || Contains(collectorProp.FindPropertyRelative("FilterRuleName")?.stringValue, token, comparison)
            || Contains(collectorProp.FindPropertyRelative("GroupRuleName")?.stringValue, token, comparison);
    }

    private static bool Contains(string value, string token, StringComparison comparison)
    {
        return !string.IsNullOrEmpty(value) && value.IndexOf(token, comparison) >= 0;
    }

    private string[] GetDraggedAssetPaths()
    {
        List<string> result = new List<string>();
        UnityEngine.Object[] objects = DragAndDrop.objectReferences;
        for (int i = 0; i < objects.Length; i++)
        {
            string assetPath = NormalizePath(AssetDatabase.GetAssetPath(objects[i]));
            if (string.IsNullOrEmpty(assetPath))
                continue;
            result.Add(assetPath);
        }

        return result.ToArray();
    }

    private void ApplyChanges()
    {
        if (_so == null)
            return;

        if (_so.ApplyModifiedProperties())
        {
            EditorUtility.SetDirty(_setting);
            CollectorReverseIndex.Instance.MarkDirty();
            _window?.Repaint();
        }
    }

    private void EnsureSelection()
    {
        if (_setting == null || _setting.Packages == null || _setting.Packages.Count == 0)
        {
            _selectedPackageIndex = -1;
            _selectedGroupIndex = -1;
            _selectedCollectorIndex = -1;
            return;
        }

        _selectedPackageIndex = Mathf.Clamp(_selectedPackageIndex < 0 ? 0 : _selectedPackageIndex, 0, _setting.Packages.Count - 1);
        CollectorPackage package = _setting.Packages[_selectedPackageIndex];

        if (package?.Groups == null || package.Groups.Count == 0)
        {
            _selectedGroupIndex = -1;
            _selectedCollectorIndex = -1;
            return;
        }

        _selectedGroupIndex = Mathf.Clamp(_selectedGroupIndex < 0 ? 0 : _selectedGroupIndex, 0, package.Groups.Count - 1);
        CollectorGroup group = package.Groups[_selectedGroupIndex];
        int collectorCount = group?.Collectors?.Count ?? 0;
        _selectedCollectorIndex = collectorCount == 0 ? -1 : Mathf.Clamp(_selectedCollectorIndex < 0 ? 0 : _selectedCollectorIndex, 0, collectorCount - 1);
    }

    private string[] GetPackageNames()
    {
        if (_setting?.Packages == null || _setting.Packages.Count == 0)
            return Array.Empty<string>();

        string[] names = new string[_setting.Packages.Count];
        for (int i = 0; i < _setting.Packages.Count; i++)
        {
            string value = _setting.Packages[i]?.PackageName;
            names[i] = string.IsNullOrEmpty(value) ? "(unnamed package)" : value;
        }

        return names;
    }

    private string[] GetGroupNames()
    {
        CollectorPackage package = GetCurrentPackage();
        if (package?.Groups == null || package.Groups.Count == 0)
            return Array.Empty<string>();

        string[] names = new string[package.Groups.Count];
        for (int i = 0; i < package.Groups.Count; i++)
        {
            string value = package.Groups[i]?.GroupName;
            names[i] = string.IsNullOrEmpty(value) ? "(unnamed group)" : value;
        }

        return names;
    }

    private CollectorPackage GetCurrentPackage()
    {
        if (_setting?.Packages == null || _selectedPackageIndex < 0 || _selectedPackageIndex >= _setting.Packages.Count)
            return null;

        return _setting.Packages[_selectedPackageIndex];
    }

    private SerializedProperty GetPackagesProperty()
    {
        if (_so == null)
            return null;

        _so.Update();
        return _so.FindProperty("Packages");
    }

    private SerializedProperty GetCurrentPackageProperty()
    {
        SerializedProperty packagesProp = GetPackagesProperty();
        if (packagesProp == null || _selectedPackageIndex < 0 || _selectedPackageIndex >= packagesProp.arraySize)
            return null;

        return packagesProp.GetArrayElementAtIndex(_selectedPackageIndex);
    }

    private SerializedProperty GetCurrentGroupProperty()
    {
        SerializedProperty packageProp = GetCurrentPackageProperty();
        SerializedProperty groupsProp = packageProp?.FindPropertyRelative("Groups");
        if (groupsProp == null || _selectedGroupIndex < 0 || _selectedGroupIndex >= groupsProp.arraySize)
            return null;

        return groupsProp.GetArrayElementAtIndex(_selectedGroupIndex);
    }

    private SerializedProperty GetCurrentCollectorsProperty()
    {
        return GetCurrentGroupProperty()?.FindPropertyRelative("Collectors");
    }

    private SerializedProperty GetSelectedCollectorProperty()
    {
        SerializedProperty collectorsProp = GetCurrentCollectorsProperty();
        if (collectorsProp == null || _selectedCollectorIndex < 0 || _selectedCollectorIndex >= collectorsProp.arraySize)
            return null;

        return collectorsProp.GetArrayElementAtIndex(_selectedCollectorIndex);
    }

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrEmpty(path))
            return string.Empty;

        return path.Replace('\\', '/').TrimEnd('/');
    }
}
