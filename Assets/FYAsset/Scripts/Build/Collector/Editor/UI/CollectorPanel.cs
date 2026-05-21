using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// 当前 Group 的 Collector 操作面板。
/// 包含 Collector 表格、详情区、校验结果和扫描预览。
/// </summary>
public sealed class CollectorPanel : IBuildPipelinePanel
{
    /// <summary>
    /// 底部结果区当前显示模式。
    /// </summary>
    private enum BottomMode
    {
        Validation,
        ScanPreview
    }

    private EditorWindow _window;
    private CollectorSetting _setting;
    private SerializedObject _so;
    private VisualElement _root;
    private VisualElement _tablePane;
    private VisualElement _detailPane;
    private VisualElement _bottomPane;
    private VisualElement _middle;
    private VisualElement _bottomSplitter;
    private VisualElement _detailSplitter;
    private ScrollView _tableScroll;
    private ScrollView _detailScroll;
    private TextField _searchField;
    private PopupField<string> _packagePopup;
    private PopupField<string> _groupPopup;
    private List<BuildMessage> _validationMessages;
    private ScanResult _lastScanResult;

    private int _selectedPackageIndex = -1;
    private int _selectedGroupIndex = -1;
    private int _selectedCollectorIndex = -1;
    private float _bottomResultHeight = 140f;
    private float _detailWidth = 320f;
    private bool _draggingBottomSplitter;
    private bool _draggingDetailSplitter;
    private Vector2 _dragStartMouse;
    private float _dragStartValue;
    private BottomMode _bottomMode = BottomMode.Validation;
    private bool _isScanning;
    private string _searchFilter = string.Empty;

    public string PanelName => "Collector";

    public void OnEnable(EditorWindow window)
    {
        _window = window;
        LoadSetting();
    }

    public VisualElement CreateContent()
    {
        _root = new VisualElement();
        _root.style.flexGrow = 1f;
        _root.style.flexDirection = FlexDirection.Column;
        Rebuild();
        return _root;
    }

    public void OnDisable()
    {
        _root?.Unbind();
        _root = null;
    }

    /// <summary>
    /// 依据当前 Setting 与选择状态重建整个面板。
    /// </summary>
    private void Rebuild()
    {
        if (_root == null)
            return;

        _root.Clear();
        _root.Unbind();

        if (_setting == null || _so == null)
        {
            DrawNoSetting();
            return;
        }

        DrawToolbar();
        DrawMainContent();
        DrawBottomPanel();
        ApplyChanges();
    }

    /// <summary>
    /// 加载 CollectorSetting，并预先计算一次校验结果。
    /// </summary>
    private void LoadSetting()
    {
        CollectorDataMigrator.EnsureDataFolder();
        CollectorDataMigrator.MigrateFromAAPath();
        _setting = AssetDatabase.LoadAssetAtPath<CollectorSetting>(FYAssetSettings.Instance.CollectorSettingPath);
        _so = _setting != null ? new SerializedObject(_setting) : null;
        EnsureSelection();
        if (_setting != null)
            _validationMessages = CollectorSettingValidator.Validate(_setting);
    }

    /// <summary>
    /// 绘制顶部工具栏：Package / Group 切换、Collector 操作、搜索与校验入口。
    /// </summary>
    private void DrawToolbar()
    {
        VisualElement toolbar = BuildPipelineUI.Toolbar();

        _packagePopup = CreatePopup(GetPackageNames(), _selectedPackageIndex, value =>
        {
            _selectedPackageIndex = Array.IndexOf(GetPackageNames(), value);
            _selectedGroupIndex = 0;
            _selectedCollectorIndex = 0;
            EnsureSelection();
            Rebuild();
        }, 130f);
        toolbar.Add(_packagePopup);

        _groupPopup = CreatePopup(GetGroupNames(), _selectedGroupIndex, value =>
        {
            _selectedGroupIndex = Array.IndexOf(GetGroupNames(), value);
            _selectedCollectorIndex = 0;
            EnsureSelection();
            Rebuild();
        }, 140f);
        toolbar.Add(_groupPopup);

        toolbar.Add(BuildPipelineUI.ToolbarButton("+ Folder", () =>
        {
            AddCollector(false);
            SaveAndRebuild();
        }, 72f));
        toolbar.Add(BuildPipelineUI.ToolbarButton("+ File", () =>
        {
            AddCollector(true);
            SaveAndRebuild();
        }, 60f));
        toolbar.Add(BuildPipelineUI.ToolbarButton("- Remove", () =>
        {
            RemoveSelectedCollector();
            SaveAndRebuild();
        }, 74f));

        _searchField = new TextField();
        _searchField.value = _searchFilter;
        _searchField.style.width = 180f;
        _searchField.RegisterValueChangedCallback(evt =>
        {
            _searchFilter = evt.newValue ?? string.Empty;
            BuildCollectorTable();
        });
        toolbar.Add(_searchField);

        toolbar.Add(BuildPipelineUI.Spacer());
        toolbar.Add(BuildPipelineUI.ToolbarButton("Reload", () =>
        {
            LoadSetting();
            Rebuild();
        }, 54f));
        toolbar.Add(BuildPipelineUI.ToolbarButton("Re-Validate", () =>
        {
            _validationMessages = CollectorSettingValidator.Validate(_setting);
            BuildBottomContent();
        }, 84f));
        toolbar.Add(BuildPipelineUI.ToolbarButton("Run Scan", RunScan, 64f));
        _root.Add(toolbar);
    }

    /// <summary>
    /// 创建通用 PopupField；当候选为空时填充占位项避免控件报空。
    /// </summary>
    private static PopupField<string> CreatePopup(string[] choices, int index, Action<string> onChanged, float width)
    {
        List<string> list = new List<string>(choices);
        if (list.Count == 0)
            list.Add("No Item");
        index = Mathf.Clamp(index, 0, list.Count - 1);
        var popup = new PopupField<string>(list, index);
        popup.style.width = width;
        popup.RegisterValueChangedCallback(evt => onChanged(evt.newValue));
        return popup;
    }

    /// <summary>
    /// 构建中部主区域：左侧表格、右侧详情以及拖拽接收区域。
    /// </summary>
    private void DrawMainContent()
    {
        _middle = new VisualElement();
        _middle.style.flexGrow = 1f;
        _middle.style.flexDirection = FlexDirection.Row;
        _middle.RegisterCallback<DragUpdatedEvent>(OnDragUpdated);
        _middle.RegisterCallback<DragPerformEvent>(OnDragPerform);
        _root.Add(_middle);

        _tablePane = new VisualElement();
        _tablePane.style.flexGrow = 1f;
        _tablePane.style.minWidth = 100f;
        _middle.Add(_tablePane);

        _detailSplitter = BuildPipelineUI.Splitter(true);
        _detailSplitter.RegisterCallback<PointerDownEvent>(evt => BeginSplitter(evt, true));
        _detailSplitter.RegisterCallback<PointerMoveEvent>(OnSplitterMove);
        _detailSplitter.RegisterCallback<PointerUpEvent>(OnSplitterUp);
        _middle.Add(_detailSplitter);

        _detailPane = new VisualElement();
        _detailPane.style.width = _detailWidth;
        _detailPane.style.minWidth = 180f;
        _detailPane.style.flexShrink = 0f;
        _middle.Add(_detailPane);

        BuildCollectorTable();
        BuildCollectorDetail();
    }

    /// <summary>
    /// 按当前筛选条件重建 Collector 表格。
    /// </summary>
    private void BuildCollectorTable()
    {
        if (_tablePane == null)
            return;

        _tablePane.Clear();
        _tablePane.Add(CreateTableHeader());

        _tableScroll = new ScrollView();
        _tableScroll.style.flexGrow = 1f;
        SerializedProperty collectorsProp = GetCurrentCollectorsProperty();
        if (collectorsProp == null || collectorsProp.arraySize == 0)
        {
            _tableScroll.Add(BuildPipelineUI.SmallText("No collectors in current group."));
            _tablePane.Add(_tableScroll);
            return;
        }

        _tableScroll.Bind(_so);
        for (int i = 0; i < collectorsProp.arraySize; i++)
        {
            SerializedProperty collectorProp = collectorsProp.GetArrayElementAtIndex(i);
            if (!MatchesSearch(collectorProp))
                continue;
            _tableScroll.Add(CreateCollectorRow(collectorProp, collectorsProp, i));
        }

        _tablePane.Add(_tableScroll);
    }

    /// <summary>
    /// 创建 Collector 表格标题行。
    /// </summary>
    private VisualElement CreateTableHeader()
    {
        var header = new VisualElement();
        header.style.paddingLeft = 6f;
        header.style.paddingRight = 6f;
        header.style.paddingTop = 4f;
        header.style.paddingBottom = 4f;
        header.Add(BuildPipelineUI.SmallText("Path Type        Collect Path"));
        header.Add(BuildPipelineUI.SmallText("Type             Payload          Addr         Pack         Filter       Group"));
        return header;
    }

    /// <summary>
    /// 创建单个 Collector 行，包含路径、规则概览与快速删除入口。
    /// </summary>
    private VisualElement CreateCollectorRow(SerializedProperty collectorProp, SerializedProperty collectorsProp, int collectorIndex)
    {
        bool selected = _selectedCollectorIndex == collectorIndex;
        VisualElement row = BuildPipelineUI.Card();
        row.style.marginLeft = 4f;
        row.style.marginRight = 4f;
        row.style.marginBottom = 4f;
        if (selected)
            row.style.backgroundColor = new Color(0.17f, 0.36f, 0.53f, 0.18f);

        VisualElement first = new VisualElement { style = { flexDirection = FlexDirection.Row } };
        AddCompactProperty(first, collectorProp.FindPropertyRelative("CollectPathType"), 92f);
        PropertyField path = new PropertyField(collectorProp.FindPropertyRelative("CollectPath"));
        path.label = string.Empty;
        path.style.flexGrow = 1f;
        first.Add(path);
        first.Add(new Button(() => PickCollectPath(collectorProp.FindPropertyRelative("CollectPath"), collectorProp.FindPropertyRelative("CollectPathType").enumValueIndex == (int)ECollectPathType.File)) { text = "..." });
        first.Add(new Button(() =>
        {
            RemoveCollector(collectorsProp, collectorIndex);
            SaveAndRebuild();
        }) { text = "x" });
        row.Add(first);

        VisualElement second = new VisualElement { style = { flexDirection = FlexDirection.Row } };
        AddCompactProperty(second, collectorProp.FindPropertyRelative("CollectorType"), 92f);
        AddCompactProperty(second, collectorProp.FindPropertyRelative("ForcePayloadKind"), 104f);
        AddRulePopup(second, collectorProp.FindPropertyRelative("AddressRuleName"), RuleDropdownHelper.GetAddressRuleNames(), 92f);
        AddRulePopup(second, collectorProp.FindPropertyRelative("PackRuleName"), RuleDropdownHelper.GetPackRuleNames(), 92f);
        AddRulePopup(second, collectorProp.FindPropertyRelative("FilterRuleName"), RuleDropdownHelper.GetFilterRuleNames(), 92f);
        AddRulePopup(second, collectorProp.FindPropertyRelative("GroupRuleName"), RuleDropdownHelper.GetGroupRuleNames(), 92f);
        row.Add(second);

        row.RegisterCallback<PointerDownEvent>(evt =>
        {
            _selectedCollectorIndex = collectorIndex;
            BuildCollectorTable();
            BuildCollectorDetail();
            evt.StopPropagation();
        });
        return row;
    }

    /// <summary>
    /// 重建右侧详情区，显示当前选中 Collector 的完整字段。
    /// </summary>
    private void BuildCollectorDetail()
    {
        _detailPane.Clear();
        _detailScroll = new ScrollView();
        _detailScroll.style.flexGrow = 1f;
        _detailScroll.Bind(_so);
        _detailPane.Add(_detailScroll);

        SerializedProperty collectorProp = GetSelectedCollectorProperty();
        if (collectorProp == null)
        {
            _detailScroll.Add(BuildPipelineUI.Header("Select a collector row"));
            _detailScroll.Add(BuildPipelineUI.SmallText("Use the table on the left to change the current collector. Details for labels and ignore patterns appear here."));
            return;
        }

        _detailScroll.Add(BuildPipelineUI.Header("Collector Detail"));
        _detailScroll.Add(new PropertyField(collectorProp.FindPropertyRelative("CollectPathType"), "Path Type"));
        _detailScroll.Add(new PropertyField(collectorProp.FindPropertyRelative("CollectPath"), "Collect Path"));
        _detailScroll.Add(new PropertyField(collectorProp.FindPropertyRelative("CollectorType"), "Collector Type"));
        _detailScroll.Add(new PropertyField(collectorProp.FindPropertyRelative("ForcePayloadKind"), "Payload"));
        _detailScroll.Add(BuildPipelineUI.Header("Rules"));
        AddLabeledRulePopup(_detailScroll, "Address", collectorProp.FindPropertyRelative("AddressRuleName"), RuleDropdownHelper.GetAddressRuleNames());
        AddLabeledRulePopup(_detailScroll, "Pack", collectorProp.FindPropertyRelative("PackRuleName"), RuleDropdownHelper.GetPackRuleNames());
        AddLabeledRulePopup(_detailScroll, "Filter", collectorProp.FindPropertyRelative("FilterRuleName"), RuleDropdownHelper.GetFilterRuleNames());
        AddLabeledRulePopup(_detailScroll, "Group", collectorProp.FindPropertyRelative("GroupRuleName"), RuleDropdownHelper.GetGroupRuleNames());
        _detailScroll.Add(new PropertyField(collectorProp.FindPropertyRelative("Labels"), "Labels"));
        _detailScroll.Add(new PropertyField(collectorProp.FindPropertyRelative("IgnorePatterns"), "Ignore Patterns"));
    }

    /// <summary>
    /// 构建底部结果区与顶部 Tab。
    /// </summary>
    private void DrawBottomPanel()
    {
        _bottomSplitter = BuildPipelineUI.Splitter(false);
        _bottomSplitter.RegisterCallback<PointerDownEvent>(evt => BeginSplitter(evt, false));
        _bottomSplitter.RegisterCallback<PointerMoveEvent>(OnSplitterMove);
        _bottomSplitter.RegisterCallback<PointerUpEvent>(OnSplitterUp);
        _root.Add(_bottomSplitter);

        _bottomPane = new VisualElement();
        _bottomPane.style.height = _bottomResultHeight;
        _bottomPane.style.minHeight = 72f;
        _bottomPane.style.flexShrink = 0f;
        _bottomPane.style.flexDirection = FlexDirection.Column;
        _root.Add(_bottomPane);
        BuildBottomContent();
    }

    /// <summary>
    /// 根据当前 BottomMode 重建底部内容。
    /// </summary>
    private void BuildBottomContent()
    {
        if (_bottomPane == null)
            return;

        _bottomPane.Clear();
        VisualElement tabs = BuildPipelineUI.Toolbar();
        tabs.Add(BuildPipelineUI.ToolbarButton("Validation", () =>
        {
            _bottomMode = BottomMode.Validation;
            BuildBottomContent();
        }));
        tabs.Add(BuildPipelineUI.ToolbarButton("Scan Preview", () =>
        {
            _bottomMode = BottomMode.ScanPreview;
            BuildBottomContent();
        }));
        tabs.Add(BuildPipelineUI.Spacer());
        _bottomPane.Add(tabs);

        ScrollView content = new ScrollView();
        content.style.flexGrow = 1f;
        content.style.minHeight = 0f;
        _bottomPane.Add(content);

        if (_bottomMode == BottomMode.Validation)
            RenderValidation(content);
        else
            RenderScanPreview(content);
    }

    /// <summary>
    /// 渲染校验结果列表。
    /// </summary>
    private void RenderValidation(VisualElement parent)
    {
        if (_validationMessages == null || _validationMessages.Count == 0)
        {
            parent.Add(BuildPipelineUI.SmallText("No validation messages."));
            return;
        }

        for (int i = 0; i < _validationMessages.Count; i++)
        {
            BuildMessage message = _validationMessages[i];
            Label row = BuildPipelineUI.SmallText($"{message.Severity}    {message.Code}    {message.Message}");
            if (message.Severity == BuildSeverity.Error)
                row.style.unityFontStyleAndWeight = FontStyle.Bold;
            parent.Add(row);
        }
    }

    /// <summary>
    /// 渲染扫描预览结果，展示收集到的 Asset 与 Bundle 对应关系。
    /// </summary>
    private void RenderScanPreview(VisualElement parent)
    {
        if (_isScanning)
        {
            parent.Add(BuildPipelineUI.SmallText("Scanning..."));
            return;
        }

        if (_lastScanResult == null)
        {
            parent.Add(BuildPipelineUI.SmallText("No scan executed."));
            return;
        }

        int assetCount = _lastScanResult.Assets != null ? _lastScanResult.Assets.Count : 0;
        parent.Add(BuildPipelineUI.Header($"Assets: {assetCount}"));

        if (assetCount == 0)
        {
            parent.Add(BuildPipelineUI.SmallText("No assets collected."));
            return;
        }

        StringBuilder builder = new StringBuilder();
        for (int i = 0; i < _lastScanResult.Assets.Count; i++)
        {
            CollectedAssetInfo asset = _lastScanResult.Assets[i];
            builder.Append(asset.AssetPath)
                .Append("  ->  ")
                .Append(asset.BundleName)
                .AppendLine();
        }

        TextField area = new TextField { multiline = true, value = builder.ToString() };
        area.isReadOnly = true;
        area.style.flexGrow = 1f;
        area.style.minHeight = 120f;
        area.style.whiteSpace = WhiteSpace.Normal;
        parent.Add(area);
    }

    /// <summary>
    /// 添加定宽 PropertyField，用于压缩表格行中的枚举/短字段显示。
    /// </summary>
    private static void AddCompactProperty(VisualElement parent, SerializedProperty property, float width)
    {
        PropertyField field = new PropertyField(property);
        field.label = string.Empty;
        field.style.width = width;
        parent.Add(field);
    }

    /// <summary>
    /// 为规则类名字段创建下拉框，并在变更后立即写回 SerializedProperty。
    /// </summary>
    private void AddRulePopup(VisualElement parent, SerializedProperty property, string[] choices, float width)
    {
        List<string> list = new List<string>(choices ?? Array.Empty<string>());
        if (list.Count == 0)
            list.Add(property.stringValue);
        if (!list.Contains(property.stringValue))
            list.Insert(0, property.stringValue);

        var popup = new PopupField<string>(list, property.stringValue);
        popup.style.width = width;
        popup.RegisterValueChangedCallback(evt =>
        {
            property.stringValue = evt.newValue;
            ApplyChanges();
        });
        parent.Add(popup);
    }

    /// <summary>
    /// 在详情区中绘制带标题的规则下拉框。
    /// </summary>
    private void AddLabeledRulePopup(VisualElement parent, string label, SerializedProperty property, string[] choices)
    {
        VisualElement row = new VisualElement { style = { flexDirection = FlexDirection.Row } };
        Label title = BuildPipelineUI.SmallText(label);
        title.style.width = 70f;
        row.Add(title);
        AddRulePopup(row, property, choices, 220f);
        parent.Add(row);
    }

    /// <summary>
    /// 运行 Collector 扫描，并将结果切换到底部预览区。
    /// </summary>
    private void RunScan()
    {
        if (_setting == null || _isScanning)
            return;

        try
        {
            _isScanning = true;
            BuildBottomContent();
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
            BuildBottomContent();
        }
    }

    /// <summary>
    /// 在拖拽进入主区域时判断是否可接受资产路径。
    /// </summary>
    private void OnDragUpdated(DragUpdatedEvent evt)
    {
        if (GetCurrentGroupProperty() == null)
            return;

        string[] assetPaths = GetDraggedAssetPaths();
        if (assetPaths.Length == 0)
            return;

        DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
        evt.StopPropagation();
    }

    /// <summary>
    /// 接收拖拽资源并批量转为 Collector 条目。
    /// </summary>
    private void OnDragPerform(DragPerformEvent evt)
    {
        if (GetCurrentGroupProperty() == null)
            return;

        string[] assetPaths = GetDraggedAssetPaths();
        if (assetPaths.Length == 0)
            return;

        DragAndDrop.AcceptDrag();
        AddDraggedCollectors(assetPaths);
        SaveAndRebuild();
        evt.StopPropagation();
    }

    /// <summary>
    /// 在当前 Group 下新增一个空 Collector。
    /// </summary>
    private void AddCollector(bool isFile)
    {
        SerializedProperty collectorsProp = GetCurrentCollectorsProperty();
        if (collectorsProp == null)
            return;

        Undo.RecordObject(_setting, isFile ? "Add File Collector" : "Add Folder Collector");
        int index = collectorsProp.arraySize;
        collectorsProp.arraySize++;
        FillCollectorDefaults(collectorsProp.GetArrayElementAtIndex(index), string.Empty, isFile);
        _selectedCollectorIndex = index;
    }

    /// <summary>
    /// 将拖拽进来的资产路径批量追加为 Collector。
    /// </summary>
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
            FillCollectorDefaults(collectorsProp.GetArrayElementAtIndex(index), assetPath, isFile);
            _selectedCollectorIndex = index;
        }
    }

    /// <summary>
    /// 填充新建 Collector 的默认规则与模式。
    /// </summary>
    private static void FillCollectorDefaults(SerializedProperty collectorProp, string path, bool isFile)
    {
        collectorProp.FindPropertyRelative("CollectPath").stringValue = path;
        collectorProp.FindPropertyRelative("CollectPathType").enumValueIndex = isFile ? (int)ECollectPathType.File : (int)ECollectPathType.Folder;
        collectorProp.FindPropertyRelative("CollectorType").enumValueIndex = (int)ECollectorType.Main;
        collectorProp.FindPropertyRelative("ForcePayloadKind").enumValueIndex = (int)EForcePayloadKind.Auto;
        collectorProp.FindPropertyRelative("AddressRuleName").stringValue = FYAssetSettings.RULE_ADDRESS_BY_FILE_NAME;
        collectorProp.FindPropertyRelative("PackRuleName").stringValue = isFile ? FYAssetSettings.RULE_PACK_SEPARATELY : FYAssetSettings.RULE_PACK_BY_DIRECTORY;
        collectorProp.FindPropertyRelative("FilterRuleName").stringValue = FYAssetSettings.RULE_COLLECT_ALL;
        collectorProp.FindPropertyRelative("GroupRuleName").stringValue = FYAssetSettings.RULE_GROUP_ALL;
        collectorProp.FindPropertyRelative("Labels").arraySize = 0;
        collectorProp.FindPropertyRelative("IgnorePatterns").arraySize = 0;
    }

    /// <summary>
    /// 删除当前选中的 Collector。
    /// </summary>
    private void RemoveSelectedCollector()
    {
        SerializedProperty collectorsProp = GetCurrentCollectorsProperty();
        if (collectorsProp == null || _selectedCollectorIndex < 0 || _selectedCollectorIndex >= collectorsProp.arraySize)
            return;

        RemoveCollector(collectorsProp, _selectedCollectorIndex);
    }

    /// <summary>
    /// 从指定数组位置删除 Collector，并修正当前选中项。
    /// </summary>
    private void RemoveCollector(SerializedProperty collectorsProp, int collectorIndex)
    {
        Undo.RecordObject(_setting, "Remove Collector");
        collectorsProp.DeleteArrayElementAtIndex(collectorIndex);
        if (_selectedCollectorIndex >= collectorsProp.arraySize)
            _selectedCollectorIndex = collectorsProp.arraySize - 1;
    }

    /// <summary>
    /// 打开文件或文件夹选择器，并将绝对路径转换回项目内 Assets 相对路径。
    /// </summary>
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
        ApplyChanges();
    }

    /// <summary>
    /// 判断单个 Collector 是否匹配搜索关键字。
    /// </summary>
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

    /// <summary>
    /// 不区分大小写地检查字符串包含关系。
    /// </summary>
    private static bool Contains(string value, string token, StringComparison comparison)
    {
        return !string.IsNullOrEmpty(value) && value.IndexOf(token, comparison) >= 0;
    }

    /// <summary>
    /// 从当前拖拽对象中过滤出有效项目内资产路径。
    /// </summary>
    private string[] GetDraggedAssetPaths()
    {
        List<string> result = new List<string>();
        UnityEngine.Object[] objects = DragAndDrop.objectReferences;
        for (int i = 0; i < objects.Length; i++)
        {
            string assetPath = CollectorPathUtility.NormalizePath(AssetDatabase.GetAssetPath(objects[i]));
            if (string.IsNullOrEmpty(assetPath))
                continue;
            result.Add(assetPath);
        }

        return result.ToArray();
    }

    /// <summary>
    /// 将 SerializedObject 变更提交回资产，并刷新逆向索引。
    /// </summary>
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

    /// <summary>
    /// 保存当前改动、重算校验信息，并重建整个面板。
    /// </summary>
    private void SaveAndRebuild()
    {
        _so?.ApplyModifiedProperties();
        EditorUtility.SetDirty(_setting);
        CollectorReverseIndex.Instance.MarkDirty();
        _validationMessages = CollectorSettingValidator.Validate(_setting);
        Rebuild();
    }

    /// <summary>
    /// CollectorSetting 缺失时显示创建入口。
    /// </summary>
    private void DrawNoSetting()
    {
        VisualElement panel = BuildPipelineUIToolkitPanel.CreateCenteredPanel(_root, 420f);
        panel.Add(BuildPipelineUIToolkitPanel.CreateTitle("CollectorSetting not found"));
        panel.Add(BuildPipelineUIToolkitPanel.CreateBody(FYAssetSettings.Instance.CollectorSettingPath));
        panel.Add(new Button(CreateCollectorSetting) { text = "Create CollectorSetting" });
    }

    /// <summary>
    /// 创建新的 CollectorSetting 资产并立即重新加载。
    /// </summary>
    private void CreateCollectorSetting()
    {
        CollectorDataMigrator.EnsureDataFolder();
        CollectorSetting newSetting = ScriptableObject.CreateInstance<CollectorSetting>();
        AssetDatabase.CreateAsset(newSetting, FYAssetSettings.Instance.CollectorSettingPath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        CollectorReverseIndex.Instance.MarkDirty();
        LoadSetting();
        Rebuild();
    }

    /// <summary>
    /// 根据当前 Setting 数据修正 Package / Group / Collector 的选中索引。
    /// </summary>
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

    /// <summary>
    /// 获取 Package 下拉框显示名。
    /// </summary>
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

    /// <summary>
    /// 获取当前 Package 下的 Group 下拉框显示名。
    /// </summary>
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

    /// <summary>
    /// 获取当前选中的 Package 运行时对象。
    /// </summary>
    private CollectorPackage GetCurrentPackage()
    {
        if (_setting?.Packages == null || _selectedPackageIndex < 0 || _selectedPackageIndex >= _setting.Packages.Count)
            return null;
        return _setting.Packages[_selectedPackageIndex];
    }

    /// <summary>
    /// 获取 Packages 的 SerializedProperty 根节点。
    /// </summary>
    private SerializedProperty GetPackagesProperty()
    {
        if (_so == null)
            return null;
        _so.Update();
        return _so.FindProperty("Packages");
    }

    /// <summary>
    /// 获取当前选中 Package 的 SerializedProperty。
    /// </summary>
    private SerializedProperty GetCurrentPackageProperty()
    {
        SerializedProperty packagesProp = GetPackagesProperty();
        if (packagesProp == null || _selectedPackageIndex < 0 || _selectedPackageIndex >= packagesProp.arraySize)
            return null;
        return packagesProp.GetArrayElementAtIndex(_selectedPackageIndex);
    }

    /// <summary>
    /// 获取当前选中 Group 的 SerializedProperty。
    /// </summary>
    private SerializedProperty GetCurrentGroupProperty()
    {
        SerializedProperty packageProp = GetCurrentPackageProperty();
        SerializedProperty groupsProp = packageProp?.FindPropertyRelative("Groups");
        if (groupsProp == null || _selectedGroupIndex < 0 || _selectedGroupIndex >= groupsProp.arraySize)
            return null;
        return groupsProp.GetArrayElementAtIndex(_selectedGroupIndex);
    }

    /// <summary>
    /// 获取当前 Group 的 Collectors 数组属性。
    /// </summary>
    private SerializedProperty GetCurrentCollectorsProperty()
    {
        return GetCurrentGroupProperty()?.FindPropertyRelative("Collectors");
    }

    /// <summary>
    /// 获取当前选中 Collector 的 SerializedProperty。
    /// </summary>
    private SerializedProperty GetSelectedCollectorProperty()
    {
        SerializedProperty collectorsProp = GetCurrentCollectorsProperty();
        if (collectorsProp == null || _selectedCollectorIndex < 0 || _selectedCollectorIndex >= collectorsProp.arraySize)
            return null;
        return collectorsProp.GetArrayElementAtIndex(_selectedCollectorIndex);
    }

    /// <summary>
    /// 开始拖动分隔条；detail=true 表示右侧详情分隔条，否则表示底部结果区分隔条。
    /// </summary>
    private void BeginSplitter(PointerDownEvent evt, bool detail)
    {
        if (evt.button != 0)
            return;

        _draggingDetailSplitter = detail;
        _draggingBottomSplitter = !detail;
        _dragStartMouse = evt.position;
        _dragStartValue = detail ? _detailWidth : _bottomResultHeight;
        evt.target.CapturePointer(evt.pointerId);
        evt.StopPropagation();
    }

    /// <summary>
    /// 拖动时更新详情区宽度或底部结果区高度。
    /// </summary>
    private void OnSplitterMove(PointerMoveEvent evt)
    {
        if (_draggingDetailSplitter)
        {
            float delta = _dragStartMouse.x - evt.position.x;
            _detailWidth = Mathf.Clamp(_dragStartValue + delta, 180f, Mathf.Max(180f, _root.resolvedStyle.width * 0.6f));
            _detailPane.style.width = _detailWidth;
            evt.StopPropagation();
        }
        else if (_draggingBottomSplitter)
        {
            float delta = _dragStartMouse.y - evt.position.y;
            _bottomResultHeight = Mathf.Clamp(_dragStartValue + delta, 72f, Mathf.Max(72f, _root.resolvedStyle.height * 0.6f));
            _bottomPane.style.height = _bottomResultHeight;
            evt.StopPropagation();
        }
    }

    /// <summary>
    /// 结束分隔条拖拽。
    /// </summary>
    private void OnSplitterUp(PointerUpEvent evt)
    {
        if (!_draggingDetailSplitter && !_draggingBottomSplitter)
            return;

        _draggingDetailSplitter = false;
        _draggingBottomSplitter = false;
        evt.target.ReleasePointer(evt.pointerId);
        evt.StopPropagation();
    }
}
