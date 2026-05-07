using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

/// <summary>
/// Collector 属性面板 —— 根据选中的 TreeView 节点类型渲染 Package / Group / Collector 三级编辑器。
/// 所有编辑操作基于 SerializedObject，支持 Undo/Redo。
/// </summary>
public sealed class CollectorPropertyPanel
{
    #region Fields

    private SerializedObject _so;
    private SerializedProperty _activeProperty;
    private CollectorTreeViewItem _selectedItem;

    private ReorderableList _labelsList;
    private ReorderableList _ignorePatternsList;
    private ReorderableList _groupLabelsList;

    private Vector2 _scrollPos;

    #endregion

    #region Public API

    /// <summary>切换当前选中的节点并刷新 SerializedProperty</summary>
    public void SetSelection(CollectorTreeViewItem item, CollectorSetting setting)
    {
        if (setting == null)
        {
            _so = null;
            _activeProperty = null;
            _selectedItem = null;
            return;
        }

        _so = new SerializedObject(setting);
        _selectedItem = item;
        _activeProperty = null;
        _labelsList = null;
        _ignorePatternsList = null;
        _groupLabelsList = null;

        if (item != null)
        {
            switch (item.Type)
            {
                case CollectorTreeViewItem.NodeType.Package:
                    _activeProperty = _so.FindProperty(
                        string.Concat("Packages.Array.data[", item.PackageIndex, "]"));
                    break;
                case CollectorTreeViewItem.NodeType.Group:
                    _activeProperty = _so.FindProperty(
                        string.Concat("Packages.Array.data[", item.PackageIndex,
                            "].Groups.Array.data[", item.GroupIndex, "]"));
                    break;
                case CollectorTreeViewItem.NodeType.Collector:
                    _activeProperty = _so.FindProperty(
                        string.Concat("Packages.Array.data[", item.PackageIndex,
                            "].Groups.Array.data[", item.GroupIndex,
                            "].Collectors.Array.data[", item.CollectorIndex, "]"));
                    break;
            }
        }
    }

    /// <summary>渲染属性面板</summary>
    public void OnGUI(Rect rect)
    {
        // 将所有绘制约束在传入的 rect 内，防止面板溢出边界。
        // 使用 BeginArea + BeginScrollView + 显式宽高确保整个面板不超出 CollectorPanel 的 rect。
        if (_so == null)
        {
            GUILayout.BeginArea(rect);
            GUILayout.FlexibleSpace();
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            GUILayout.Label("No CollectorSetting loaded.", EditorStyles.centeredGreyMiniLabel);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.FlexibleSpace();
            GUILayout.EndArea();
            return;
        }

        GUILayout.BeginArea(rect);
        // 使用显式宽高，确保 ScrollView 不会超出 rect 范围
        _scrollPos = GUILayout.BeginScrollView(_scrollPos, GUILayout.Width(Mathf.Max(0, rect.width)), GUILayout.Height(Mathf.Max(0, rect.height)));

        if (_selectedItem == null)
        {
            // 无选中节点时显示居中空状态卡片，用 bounded box 防止文字溢出面板区域
            GUILayout.FlexibleSpace();
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            int cardWidth = Mathf.Min(420, (int)rect.width - 40);
            GUILayout.BeginVertical("box", GUILayout.Width(cardWidth));
            GUILayout.FlexibleSpace();
            GUILayout.Label("No node selected", EditorStyles.boldLabel);
            GUILayout.Space(6);
            GUILayout.Label("Select a node in the tree to edit.", EditorStyles.wordWrappedLabel);
            GUILayout.FlexibleSpace();
            GUILayout.EndVertical();
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.FlexibleSpace();
            GUILayout.EndScrollView();
            GUILayout.EndArea();
            return;
        }

        _so.Update();

        switch (_selectedItem.Type)
        {
            case CollectorTreeViewItem.NodeType.Package:
                DrawPackageFields();
                break;
            case CollectorTreeViewItem.NodeType.Group:
                DrawGroupFields();
                break;
            case CollectorTreeViewItem.NodeType.Collector:
                DrawCollectorFields();
                break;
        }

        if (_so.ApplyModifiedProperties())
            CollectorReverseIndex.Instance.MarkDirty();
        GUILayout.EndScrollView();
        GUILayout.EndArea();
    }

    #endregion

    #region Package Fields

    /// <summary>渲染 Package 节点属性：PackageName + SharePolicy 四项</summary>
    private void DrawPackageFields()
    {
        EditorGUILayout.LabelField("Package", EditorStyles.boldLabel);
        EditorGUILayout.Space(4);

        SerializedProperty nameProp = _activeProperty.FindPropertyRelative("PackageName");
        EditorGUILayout.PropertyField(nameProp, new GUIContent("PackageName"));

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("SharePolicy", EditorStyles.boldLabel);

        SerializedProperty sharePolicy = _activeProperty.FindPropertyRelative("SharePolicy");
        if (sharePolicy != null)
        {
            EditorGUILayout.PropertyField(
                sharePolicy.FindPropertyRelative("MinReferenceCount"),
                new GUIContent("MinReferenceCount"));
            EditorGUILayout.PropertyField(
                sharePolicy.FindPropertyRelative("MinAssetSizeBytes"),
                new GUIContent("MinAssetSizeBytes"));

            EditorGUILayout.LabelField("NoSharePatterns", EditorStyles.boldLabel);
            DrawStringList(sharePolicy.FindPropertyRelative("NoSharePatterns"), ref _ignorePatternsList,
                "No Share Pattern");

            EditorGUILayout.LabelField("ForceSharePatterns", EditorStyles.boldLabel);
            DrawStringList(sharePolicy.FindPropertyRelative("ForceSharePatterns"), ref _groupLabelsList,
                "Force Share Pattern");
        }
    }

    #endregion

    #region Group Fields

    /// <summary>渲染 Group 节点属性：GroupName + Enabled + Labels</summary>
    private void DrawGroupFields()
    {
        EditorGUILayout.LabelField("Group", EditorStyles.boldLabel);
        EditorGUILayout.Space(4);

        SerializedProperty nameProp = _activeProperty.FindPropertyRelative("GroupName");
        EditorGUILayout.PropertyField(nameProp, new GUIContent("GroupName"));

        SerializedProperty enabledProp = _activeProperty.FindPropertyRelative("Enabled");
        EditorGUILayout.PropertyField(enabledProp, new GUIContent("Enabled"));

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Labels", EditorStyles.boldLabel);

        SerializedProperty labelsProp = _activeProperty.FindPropertyRelative("Labels");
        DrawStringList(labelsProp, ref _labelsList, "Label");
    }

    #endregion

    #region Collector Fields

    /// <summary>渲染 Collector 节点属性：CollectPath + PathType + 类型枚举 + 规则下拉 + Labels + IgnorePatterns</summary>
    private void DrawCollectorFields()
    {
        EditorGUILayout.LabelField("Collector", EditorStyles.boldLabel);
        EditorGUILayout.Space(4);

        SerializedProperty pathTypeProp = _activeProperty.FindPropertyRelative("CollectPathType");
        EditorGUILayout.PropertyField(pathTypeProp, new GUIContent("CollectPathType"));

        // CollectPath with legacy folder picker
        SerializedProperty pathProp = _activeProperty.FindPropertyRelative("CollectPath");
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.PropertyField(pathProp, new GUIContent("CollectPath"));
        if (GUILayout.Button("…", GUILayout.Width(28)))
        {
            string absPath = EditorUtility.OpenFolderPanel("Select Collect Path", "Assets/", "");
            if (!string.IsNullOrEmpty(absPath))
            {
                // 将绝对路径转为相对 Assets/ 路径
                string dataPath = Application.dataPath;
                if (absPath.StartsWith(dataPath))
                {
                    absPath = "Assets" + absPath.Substring(dataPath.Length);
                    pathProp.stringValue = absPath;
                }
            }
        }
        EditorGUILayout.EndHorizontal();

        SerializedProperty collType = _activeProperty.FindPropertyRelative("CollectorType");
        EditorGUILayout.PropertyField(collType, new GUIContent("CollectorType"));

        SerializedProperty forcePayload = _activeProperty.FindPropertyRelative("ForcePayloadKind");
        EditorGUILayout.PropertyField(forcePayload, new GUIContent("ForcePayloadKind"));

        EditorGUILayout.Space(4);

        // Rule dropdowns
        SerializedProperty addrRule = _activeProperty.FindPropertyRelative("AddressRuleName");
        addrRule.stringValue = RuleDropdownHelper.AddressRulePopup(
            EditorGUILayout.GetControlRect(), addrRule.stringValue);

        SerializedProperty packRule = _activeProperty.FindPropertyRelative("PackRuleName");
        packRule.stringValue = RuleDropdownHelper.PackRulePopup(
            EditorGUILayout.GetControlRect(), packRule.stringValue);

        SerializedProperty filterRule = _activeProperty.FindPropertyRelative("FilterRuleName");
        filterRule.stringValue = RuleDropdownHelper.FilterRulePopup(
            EditorGUILayout.GetControlRect(), filterRule.stringValue);

        SerializedProperty groupRule = _activeProperty.FindPropertyRelative("GroupRuleName");
        groupRule.stringValue = RuleDropdownHelper.GroupRulePopup(
            EditorGUILayout.GetControlRect(), groupRule.stringValue);

        EditorGUILayout.Space(8);

        // Labels
        EditorGUILayout.LabelField("Labels", EditorStyles.boldLabel);
        SerializedProperty labelsProp = _activeProperty.FindPropertyRelative("Labels");
        DrawStringList(labelsProp, ref _labelsList, "Label");

        EditorGUILayout.Space(4);

        // IgnorePatterns
        EditorGUILayout.LabelField("IgnorePatterns", EditorStyles.boldLabel);
        SerializedProperty patternsProp = _activeProperty.FindPropertyRelative("IgnorePatterns");
        DrawStringList(patternsProp, ref _ignorePatternsList, "Pattern");
    }

    #endregion

    #region ReorderableList Helper

    /// <summary>使用 ReorderableList 渲染字符串列表，自动缓存避免每帧重建</summary>
    private void DrawStringList(SerializedProperty listProp, ref ReorderableList list, string elementLabel)
    {
        if (list == null || list.serializedProperty != listProp)
        {
            list = new ReorderableList(listProp.serializedObject, listProp, true, true, true, true)
            {
                drawHeaderCallback = (Rect r) =>
                    GUI.Label(r, listProp.displayName),
                drawElementCallback = (Rect r, int index, bool isActive, bool isFocused) =>
                {
                    SerializedProperty elem = listProp.GetArrayElementAtIndex(index);
                    EditorGUI.PropertyField(
                        new Rect(r.x, r.y + 2, r.width, EditorGUIUtility.singleLineHeight),
                        elem, GUIContent.none);
                },
                onAddCallback = (ReorderableList rl) =>
                {
                    rl.serializedProperty.arraySize++;
                    rl.serializedProperty.GetArrayElementAtIndex(rl.serializedProperty.arraySize - 1).stringValue = "";
                }
            };
        }

        list.DoLayoutList();
    }

    #endregion
}
