using UnityEditor;
using UnityEngine;

/// <summary>
/// CollectorSetting 总览面板 —— 左侧 Package / Group 导航，右侧详情与 Collector 列表。
/// </summary>
public class CollectorSettingPanel : IBuildPipelinePanel
{
    private enum SelectionType
    {
        None,
        Package,
        Group
    }

    private EditorWindow _window;
    private CollectorSetting _setting;
    private SerializedObject _so;

    private Vector2 _sidebarScroll;
    private Vector2 _detailScroll;
    private float _sidebarWidth = 220f;
    private bool _isDraggingSplitter;

    private SelectionType _selectionType = SelectionType.None;
    private int _selectedPackageIndex = -1;
    private int _selectedGroupIndex = -1;
    private int _selectedCollectorIndex = -1;

    private static GUIStyle _packageLabelStyle;
    private static GUIStyle _groupLabelStyle;

    public string PanelName => "Collect Config";

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
            GUILayout.BeginArea(windowRect);
            DrawToolbar();
            DrawNoSetting();
            GUILayout.EndArea();
            return;
        }

        Rect sidebarRect = new Rect(windowRect.x, windowRect.y + 24f, _sidebarWidth, Mathf.Max(0f, windowRect.height - 24f));
        Rect splitterRect = new Rect(sidebarRect.xMax - 2f, sidebarRect.y, 6f, sidebarRect.height);
        Rect detailRect = new Rect(sidebarRect.xMax + 4f, windowRect.y + 24f, Mathf.Max(0f, windowRect.width - _sidebarWidth - 4f), Mathf.Max(0f, windowRect.height - 24f));

        GUILayout.BeginArea(windowRect);
        DrawToolbar();
        GUILayout.EndArea();

        EditorGUIUtility.AddCursorRect(splitterRect, MouseCursor.ResizeHorizontal);
        Event evt = Event.current;
        if (evt.type == EventType.MouseDown && splitterRect.Contains(evt.mousePosition))
        {
            _isDraggingSplitter = true;
            evt.Use();
        }
        if (_isDraggingSplitter)
        {
            if (evt.type == EventType.MouseDrag)
            {
                _sidebarWidth = Mathf.Clamp(evt.mousePosition.x - windowRect.x, 140f, windowRect.width * 0.5f);
                _window?.Repaint();
                evt.Use();
            }
            if (evt.type == EventType.MouseUp)
            {
                _isDraggingSplitter = false;
                evt.Use();
            }
        }

        DrawSidebar(sidebarRect);
        DrawDetail(detailRect);
        ApplyChanges();
    }

    private void DrawToolbar()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

        if (GUILayout.Button("Reload", EditorStyles.toolbarButton, GUILayout.Width(60f)))
            LoadSetting();

        GUILayout.FlexibleSpace();
        GUILayout.Label(FYAssetSettings.Instance.CollectorSettingPath, EditorStyles.miniLabel);
        EditorGUILayout.EndHorizontal();
    }

    private void DrawSidebar(Rect rect)
    {
        GUILayout.BeginArea(rect);
        EditorGUI.DrawRect(new Rect(0, 0, rect.width, rect.height), EditorGUIUtility.isProSkin ? new Color(0.18f, 0.18f, 0.18f) : new Color(0.82f, 0.82f, 0.82f));

        Rect scrollRect = new Rect(0, 0, rect.width, rect.height);
        Rect contentRect = new Rect(0, 0, rect.width - 16f, GetSidebarContentHeight());
        _sidebarScroll = GUI.BeginScrollView(scrollRect, _sidebarScroll, contentRect);

        float y = 8f;
        SerializedProperty packagesProp = GetPackagesProperty();
        if (packagesProp != null)
        {
            for (int i = 0; i < packagesProp.arraySize; i++)
            {
                SerializedProperty packageProp = packagesProp.GetArrayElementAtIndex(i);
                y = DrawPackageEntry(new Rect(8f, y, contentRect.width - 16f, 30f), packageProp, i);

                SerializedProperty groupsProp = packageProp.FindPropertyRelative("Groups");
                if (groupsProp == null)
                    continue;

                for (int j = 0; j < groupsProp.arraySize; j++)
                {
                    SerializedProperty groupProp = groupsProp.GetArrayElementAtIndex(j);
                    y = DrawGroupEntry(new Rect(20f, y, contentRect.width - 28f, 24f), groupProp, i, j);
                }

                y += 8f;
            }
        }

        GUI.EndScrollView();

        Event evt = Event.current;
        if (evt.type == EventType.ContextClick)
        {
            ShowSidebarContextMenu();
            evt.Use();
        }

        GUILayout.EndArea();
    }

    private float DrawPackageEntry(Rect rect, SerializedProperty packageProp, int packageIndex)
    {
        bool isSelected = _selectionType == SelectionType.Package && _selectedPackageIndex == packageIndex;
        if (isSelected)
            EditorGUI.DrawRect(rect, new Color(0.17f, 0.36f, 0.53f, 1f));
        else if (rect.Contains(Event.current.mousePosition))
            EditorGUI.DrawRect(rect, new Color(0.28f, 0.28f, 0.28f, 0.45f));

        string packageName = packageProp.FindPropertyRelative("PackageName")?.stringValue;
        if (string.IsNullOrEmpty(packageName))
            packageName = "(unnamed package)";

        if (_packageLabelStyle == null)
            _packageLabelStyle = new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleLeft };
        _packageLabelStyle.normal.textColor = isSelected ? Color.white : EditorGUIUtility.isProSkin ? Color.white : Color.black;

        GUI.Label(new Rect(rect.x + 10f, rect.y, rect.width - 16f, rect.height), "📦 " + packageName, _packageLabelStyle);

        Event evt = Event.current;
        if (evt.type == EventType.MouseDown && rect.Contains(evt.mousePosition))
        {
            if (evt.button == 1)
            {
                ShowPackageContextMenu(packageIndex);
                evt.Use();
            }
            else
            {
                SelectPackage(packageIndex);
                evt.Use();
                GUI.FocusControl(null);
            }
        }

        return rect.yMax + 2f;
    }

    private float DrawGroupEntry(Rect rect, SerializedProperty groupProp, int packageIndex, int groupIndex)
    {
        bool isSelected = _selectionType == SelectionType.Group && _selectedPackageIndex == packageIndex && _selectedGroupIndex == groupIndex;
        if (isSelected)
            EditorGUI.DrawRect(rect, new Color(0.17f, 0.36f, 0.53f, 0.82f));
        else if (rect.Contains(Event.current.mousePosition))
            EditorGUI.DrawRect(rect, new Color(0.28f, 0.28f, 0.28f, 0.3f));

        string groupName = groupProp.FindPropertyRelative("GroupName")?.stringValue;
        if (string.IsNullOrEmpty(groupName))
            groupName = "(unnamed group)";

        bool enabled = groupProp.FindPropertyRelative("Enabled")?.boolValue ?? true;
        string suffix = enabled ? string.Empty : "  [Disabled]";

        if (_groupLabelStyle == null)
            _groupLabelStyle = new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleLeft };
        _groupLabelStyle.normal.textColor = isSelected ? Color.white : EditorGUIUtility.isProSkin ? new Color(0.9f, 0.9f, 0.9f) : Color.black;

        GUI.Label(new Rect(rect.x + 8f, rect.y, rect.width - 8f, rect.height), "📁 " + groupName + suffix, _groupLabelStyle);

        Event evt = Event.current;
        if (evt.type == EventType.MouseDown && rect.Contains(evt.mousePosition))
        {
            if (evt.button == 1)
            {
                ShowGroupContextMenu(packageIndex, groupIndex);
                evt.Use();
            }
            else
            {
                SelectGroup(packageIndex, groupIndex);
                evt.Use();
                GUI.FocusControl(null);
            }
        }

        return rect.yMax + 1f;
    }

    private void DrawDetail(Rect rect)
    {
        GUILayout.BeginArea(rect);
        GUILayout.BeginVertical("box");
        _detailScroll = EditorGUILayout.BeginScrollView(_detailScroll);

        switch (_selectionType)
        {
            case SelectionType.Package:
                DrawPackageDetail();
                break;
            case SelectionType.Group:
                DrawGroupDetail();
                break;
            default:
                DrawEmptyDetail();
                break;
        }

        EditorGUILayout.EndScrollView();
        GUILayout.EndVertical();
        GUILayout.EndArea();
    }

    private void DrawPackageDetail()
    {
        SerializedProperty packageProp = GetSelectedPackageProperty();
        if (packageProp == null)
        {
            DrawEmptyDetail();
            return;
        }

        EditorGUILayout.LabelField("Package Overview", EditorStyles.boldLabel);
        EditorGUILayout.Space(6f);

        EditorGUILayout.PropertyField(packageProp.FindPropertyRelative("PackageName"), new GUIContent("Package Name"));

        EditorGUILayout.Space(10f);
        EditorGUILayout.LabelField("Share Policy", EditorStyles.boldLabel);

        SerializedProperty sharePolicyProp = packageProp.FindPropertyRelative("SharePolicy");
        if (sharePolicyProp != null)
        {
            EditorGUILayout.PropertyField(sharePolicyProp.FindPropertyRelative("MinReferenceCount"), new GUIContent("Min Reference Count"));
            EditorGUILayout.PropertyField(sharePolicyProp.FindPropertyRelative("MinAssetSizeBytes"), new GUIContent("Min Asset Size Bytes"));
            EditorGUILayout.PropertyField(sharePolicyProp.FindPropertyRelative("NoSharePatterns"), new GUIContent("No Share Patterns"), true);
            EditorGUILayout.PropertyField(sharePolicyProp.FindPropertyRelative("ForceSharePatterns"), new GUIContent("Force Share Patterns"), true);
        }
    }

    private void DrawGroupDetail()
    {
        SerializedProperty groupProp = GetSelectedGroupProperty();
        if (groupProp == null)
        {
            DrawEmptyDetail();
            return;
        }

        EditorGUILayout.LabelField("Group Overview", EditorStyles.boldLabel);
        EditorGUILayout.Space(6f);

        EditorGUILayout.PropertyField(groupProp.FindPropertyRelative("GroupName"), new GUIContent("Group Name"));
        EditorGUILayout.PropertyField(groupProp.FindPropertyRelative("Enabled"), new GUIContent("Enabled"));
        EditorGUILayout.PropertyField(groupProp.FindPropertyRelative("Labels"), new GUIContent("Group Labels"), true);

        EditorGUILayout.Space(10f);
        DrawCollectorTable(groupProp.FindPropertyRelative("Collectors"));
    }

    private void DrawCollectorTable(SerializedProperty collectorsProp)
    {
        if (collectorsProp == null)
            return;

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Collectors", EditorStyles.boldLabel);
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("+ Folder", GUILayout.Width(80f)))
            AddCollector(collectorsProp, false);
        if (GUILayout.Button("+ File", GUILayout.Width(72f)))
            AddCollector(collectorsProp, true);
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(4f);

        if (collectorsProp.arraySize == 0)
        {
            EditorGUILayout.HelpBox("No collectors in this group.", MessageType.Info);
            return;
        }

        for (int i = 0; i < collectorsProp.arraySize; i++)
        {
            SerializedProperty collectorProp = collectorsProp.GetArrayElementAtIndex(i);
            DrawCollectorRow(collectorProp, collectorsProp, i);
            GUILayout.Space(4f);
        }
    }

    private void DrawCollectorRow(SerializedProperty collectorProp, SerializedProperty collectorsProp, int collectorIndex)
    {
        bool isSelected = _selectedCollectorIndex == collectorIndex;
        Rect rowRect = EditorGUILayout.BeginVertical("box");

        if (Event.current.type == EventType.Repaint && isSelected)
            EditorGUI.DrawRect(rowRect, new Color(0.17f, 0.36f, 0.53f, 0.15f));

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
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
            return;
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.PropertyField(collectorProp.FindPropertyRelative("CollectorType"), GUIContent.none, GUILayout.Width(92f));
        EditorGUILayout.PropertyField(collectorProp.FindPropertyRelative("ForcePayloadKind"), GUIContent.none, GUILayout.Width(108f));
        DrawRulePopupField("Addr", collectorProp.FindPropertyRelative("AddressRuleName"));
        DrawRulePopupField("Pack", collectorProp.FindPropertyRelative("PackRuleName"));
        DrawRulePopupField("Filter", collectorProp.FindPropertyRelative("FilterRuleName"));
        DrawRulePopupField("Group", collectorProp.FindPropertyRelative("GroupRuleName"));
        GUILayout.Space(28f + 24f);
        EditorGUILayout.EndHorizontal();

        if (isSelected)
        {
            EditorGUILayout.Space(4f);
            EditorGUILayout.PropertyField(collectorProp.FindPropertyRelative("Labels"), new GUIContent("Labels"), true);
            EditorGUILayout.PropertyField(collectorProp.FindPropertyRelative("IgnorePatterns"), new GUIContent("Ignore Patterns"), true);
        }

        GUILayout.EndVertical();

        if (Event.current.type == EventType.MouseDown && rowRect.Contains(Event.current.mousePosition))
        {
            _selectedCollectorIndex = collectorIndex;
            Event.current.Use();
        }
    }

    private void DrawRulePopupField(string shortLabel, SerializedProperty property)
    {
        GUILayout.Label(shortLabel, EditorStyles.miniLabel, GUILayout.Width(32f));
        Rect rect = GUILayoutUtility.GetRect(80f, EditorGUIUtility.singleLineHeight, GUILayout.MinWidth(60f));

        if (property.name == "AddressRuleName")
            property.stringValue = RuleDropdownHelper.AddressRulePopup(rect, property.stringValue);
        else if (property.name == "PackRuleName")
            property.stringValue = RuleDropdownHelper.PackRulePopup(rect, property.stringValue);
        else if (property.name == "FilterRuleName")
            property.stringValue = RuleDropdownHelper.FilterRulePopup(rect, property.stringValue);
        else
            property.stringValue = RuleDropdownHelper.GroupRulePopup(rect, property.stringValue);
    }

    private void DrawEmptyDetail()
    {
        GUILayout.Space(40f);
        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        GUILayout.BeginVertical("box", GUILayout.Width(320f));
        GUILayout.Space(10f);
        GUILayout.Label("Select a Package or Group", EditorStyles.boldLabel);
        GUILayout.Space(6f);
        GUILayout.Label("Left side manages hierarchy. Right side edits package policy, group fields, and collectors.", EditorStyles.wordWrappedLabel);
        GUILayout.Space(10f);
        GUILayout.EndVertical();
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();
    }

    private void DrawNoSetting()
    {
        GUILayout.FlexibleSpace();
        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        GUILayout.BeginVertical("box", GUILayout.Width(420f));
        GUILayout.Space(10f);
        GUILayout.Label("CollectorSetting not found", EditorStyles.boldLabel);
        GUILayout.Space(6f);
        GUILayout.Label(FYAssetSettings.Instance.CollectorSettingPath, EditorStyles.wordWrappedMiniLabel);
        GUILayout.Space(10f);
        if (GUILayout.Button("Create CollectorSetting", GUILayout.Height(36f)))
            CreateSetting();
        GUILayout.Space(10f);
        GUILayout.EndVertical();
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();
        GUILayout.FlexibleSpace();
    }

    private void LoadSetting()
    {
        CollectorDataMigrator.EnsureDataFolder();
        CollectorDataMigrator.MigrateFromLegacyPath();
        _setting = AssetDatabase.LoadAssetAtPath<CollectorSetting>(FYAssetSettings.Instance.CollectorSettingPath);
        _so = _setting != null ? new SerializedObject(_setting) : null;
        EnsureSelection();
    }

    private void CreateSetting()
    {
        CollectorDataMigrator.EnsureDataFolder();
        CollectorSetting asset = ScriptableObject.CreateInstance<CollectorSetting>();
        AssetDatabase.CreateAsset(asset, FYAssetSettings.Instance.CollectorSettingPath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        CollectorReverseIndex.Instance.MarkDirty();
        LoadSetting();
    }

    private void AddPackage()
    {
        if (_so == null)
            return;

        SerializedProperty packagesProp = GetPackagesProperty();
        if (packagesProp == null)
            return;

        Undo.RecordObject(_setting, "Add Package");
        int index = packagesProp.arraySize;
        packagesProp.arraySize++;
        SerializedProperty packageProp = packagesProp.GetArrayElementAtIndex(index);
        packageProp.FindPropertyRelative("PackageName").stringValue = "NewPackage" + (index + 1);
        packageProp.FindPropertyRelative("Groups").arraySize = 0;
        packageProp.FindPropertyRelative("SharePolicy.MinReferenceCount").intValue = 2;
        packageProp.FindPropertyRelative("SharePolicy.MinAssetSizeBytes").longValue = 0;
        packageProp.FindPropertyRelative("SharePolicy.NoSharePatterns").arraySize = 0;
        packageProp.FindPropertyRelative("SharePolicy.ForceSharePatterns").arraySize = 0;
        SelectPackage(index);
    }

    private void AddGroup(int packageIndex)
    {
        SerializedProperty packageProp = GetPackageProperty(packageIndex);
        if (packageProp == null)
            return;

        SerializedProperty groupsProp = packageProp.FindPropertyRelative("Groups");
        Undo.RecordObject(_setting, "Add Group");
        int index = groupsProp.arraySize;
        groupsProp.arraySize++;
        SerializedProperty groupProp = groupsProp.GetArrayElementAtIndex(index);
        groupProp.FindPropertyRelative("GroupName").stringValue = "NewGroup" + (index + 1);
        groupProp.FindPropertyRelative("Enabled").boolValue = true;
        groupProp.FindPropertyRelative("Labels").arraySize = 0;
        groupProp.FindPropertyRelative("Collectors").arraySize = 0;
        SelectGroup(packageIndex, index);
    }

    private void AddCollector(SerializedProperty collectorsProp, bool isFile)
    {
        Undo.RecordObject(_setting, isFile ? "Add File Collector" : "Add Folder Collector");
        int index = collectorsProp.arraySize;
        collectorsProp.arraySize++;

        SerializedProperty collectorProp = collectorsProp.GetArrayElementAtIndex(index);
        collectorProp.FindPropertyRelative("CollectPath").stringValue = string.Empty;
        collectorProp.FindPropertyRelative("CollectPathType").enumValueIndex = isFile ? (int)ECollectPathType.File : (int)ECollectPathType.Folder;
        collectorProp.FindPropertyRelative("CollectorType").enumValueIndex = (int)ECollectorType.Main;
        collectorProp.FindPropertyRelative("ForcePayloadKind").enumValueIndex = (int)EForcePayloadKind.Auto;
        collectorProp.FindPropertyRelative("AddressRuleName").stringValue = FYAssetSettings.RULE_ADDRESS_BY_FILE_NAME;
        collectorProp.FindPropertyRelative("PackRuleName").stringValue = isFile ? FYAssetSettings.RULE_PACK_SEPARATELY : FYAssetSettings.RULE_PACK_BY_DIRECTORY;
        collectorProp.FindPropertyRelative("FilterRuleName").stringValue = FYAssetSettings.RULE_COLLECT_ALL;
        collectorProp.FindPropertyRelative("GroupRuleName").stringValue = FYAssetSettings.RULE_GROUP_ALL;
        collectorProp.FindPropertyRelative("Labels").arraySize = 0;
        collectorProp.FindPropertyRelative("IgnorePatterns").arraySize = 0;
        _selectedCollectorIndex = index;
    }

    private void RemoveCollector(SerializedProperty collectorsProp, int collectorIndex)
    {
        Undo.RecordObject(_setting, "Remove Collector");
        collectorsProp.DeleteArrayElementAtIndex(collectorIndex);
        if (_selectedCollectorIndex >= collectorsProp.arraySize)
            _selectedCollectorIndex = collectorsProp.arraySize - 1;
    }

    private void DeleteCurrentSelection()
    {
        SerializedProperty packagesProp = GetPackagesProperty();
        if (packagesProp == null)
            return;

        Undo.RecordObject(_setting, "Delete Collector Selection");

        if (_selectionType == SelectionType.Group && _selectedPackageIndex >= 0)
        {
            SerializedProperty groupsProp = GetPackageProperty(_selectedPackageIndex)?.FindPropertyRelative("Groups");
            if (groupsProp != null && _selectedGroupIndex >= 0 && _selectedGroupIndex < groupsProp.arraySize)
            {
                groupsProp.DeleteArrayElementAtIndex(_selectedGroupIndex);
                _selectedGroupIndex = Mathf.Clamp(_selectedGroupIndex, 0, groupsProp.arraySize - 1);
                if (groupsProp.arraySize > 0)
                    SelectGroup(_selectedPackageIndex, _selectedGroupIndex);
                else
                    SelectPackage(_selectedPackageIndex);
            }
        }
        else if (_selectionType == SelectionType.Package && _selectedPackageIndex >= 0 && _selectedPackageIndex < packagesProp.arraySize)
        {
            packagesProp.DeleteArrayElementAtIndex(_selectedPackageIndex);
            if (packagesProp.arraySize > 0)
                SelectPackage(Mathf.Clamp(_selectedPackageIndex, 0, packagesProp.arraySize - 1));
            else
            {
                _selectionType = SelectionType.None;
                _selectedPackageIndex = -1;
                _selectedGroupIndex = -1;
                _selectedCollectorIndex = -1;
            }
        }
    }

    private void ShowSidebarContextMenu()
    {
        GenericMenu menu = new GenericMenu();
        menu.AddItem(new GUIContent("Add Package"), false, () =>
        {
            AddPackage();
            _so?.ApplyModifiedProperties();
            EditorUtility.SetDirty(_setting);
            _window?.Repaint();
        });
        menu.ShowAsContext();
    }

    private void ShowPackageContextMenu(int packageIndex)
    {
        GenericMenu menu = new GenericMenu();
        menu.AddItem(new GUIContent("Add Group"), false, () =>
        {
            AddGroup(packageIndex);
            _so?.ApplyModifiedProperties();
            EditorUtility.SetDirty(_setting);
            _window?.Repaint();
        });
        menu.AddSeparator("");
        menu.AddItem(new GUIContent("Delete Package"), false, () =>
        {
            SelectPackage(packageIndex);
            DeleteCurrentSelection();
            _so?.ApplyModifiedProperties();
            EditorUtility.SetDirty(_setting);
            _window?.Repaint();
        });
        menu.ShowAsContext();
    }

    private void ShowGroupContextMenu(int packageIndex, int groupIndex)
    {
        GenericMenu menu = new GenericMenu();
        menu.AddItem(new GUIContent("Delete Group"), false, () =>
        {
            SelectGroup(packageIndex, groupIndex);
            DeleteCurrentSelection();
            _so?.ApplyModifiedProperties();
            EditorUtility.SetDirty(_setting);
            _window?.Repaint();
        });
        menu.ShowAsContext();
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
            _selectionType = SelectionType.None;
            _selectedPackageIndex = -1;
            _selectedGroupIndex = -1;
            _selectedCollectorIndex = -1;
            return;
        }

        if (_selectedPackageIndex < 0 || _selectedPackageIndex >= _setting.Packages.Count)
            _selectedPackageIndex = 0;

        CollectorPackage package = _setting.Packages[_selectedPackageIndex];
        if (_selectionType == SelectionType.Group && package != null && package.Groups != null && package.Groups.Count > 0)
        {
            _selectedGroupIndex = Mathf.Clamp(_selectedGroupIndex, 0, package.Groups.Count - 1);
            _selectedCollectorIndex = Mathf.Max(-1, _selectedCollectorIndex);
            return;
        }

        _selectionType = SelectionType.Package;
        _selectedGroupIndex = -1;
        _selectedCollectorIndex = -1;
    }

    private float GetSidebarContentHeight()
    {
        float height = 12f;
        if (_setting?.Packages == null)
            return height;

        for (int i = 0; i < _setting.Packages.Count; i++)
        {
            height += 32f;
            CollectorPackage package = _setting.Packages[i];
            if (package?.Groups != null)
                height += package.Groups.Count * 25f;
            height += 8f;
        }

        return Mathf.Max(height, 100f);
    }

    private void SelectPackage(int packageIndex)
    {
        _selectionType = SelectionType.Package;
        _selectedPackageIndex = packageIndex;
        _selectedGroupIndex = -1;
        _selectedCollectorIndex = -1;
    }

    private void SelectGroup(int packageIndex, int groupIndex)
    {
        _selectionType = SelectionType.Group;
        _selectedPackageIndex = packageIndex;
        _selectedGroupIndex = groupIndex;
        _selectedCollectorIndex = 0;
    }

    private SerializedProperty GetPackagesProperty()
    {
        if (_so == null)
            return null;

        _so.Update();
        return _so.FindProperty("Packages");
    }

    private SerializedProperty GetPackageProperty(int packageIndex)
    {
        SerializedProperty packagesProp = GetPackagesProperty();
        if (packagesProp == null || packageIndex < 0 || packageIndex >= packagesProp.arraySize)
            return null;

        return packagesProp.GetArrayElementAtIndex(packageIndex);
    }

    private SerializedProperty GetSelectedPackageProperty()
    {
        return GetPackageProperty(_selectedPackageIndex);
    }

    private SerializedProperty GetSelectedGroupProperty()
    {
        SerializedProperty packageProp = GetSelectedPackageProperty();
        SerializedProperty groupsProp = packageProp?.FindPropertyRelative("Groups");
        if (groupsProp == null || _selectedGroupIndex < 0 || _selectedGroupIndex >= groupsProp.arraySize)
            return null;

        return groupsProp.GetArrayElementAtIndex(_selectedGroupIndex);
    }
}
