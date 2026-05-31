using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// AssetsCollection 面板。
/// 负责 Package / Group 导航、Collector 维护、扫描预览以及资产级元数据编辑。
/// </summary>
public class AssetsCollectionPanel : IBuildPipelinePanel
{
    /// <summary>
    /// 当前详情区选中的节点类型。
    /// </summary>
    private enum SelectionType
    {
        None,
        Package,
        Group
    }

    private EditorWindow _window;
    private AssetCollectionSetting _setting;
    private SerializedObject _so;
    private VisualElement _root;
    private VisualElement _sidebar;
    private VisualElement _detail;
    private VisualElement _splitter;
    private float _sidebarWidth = 220f;
    private bool _draggingSplitter;
    private Vector2 _dragStartMouse;
    private float _dragStartWidth;
    private ScanResult _lastScanResult;
    private bool _isScanning;
    private string _selectedAssetGuid;
    private readonly HashSet<string> _expandedScanNodes = new HashSet<string>();

    private SelectionType _selectionType = SelectionType.None;
    private int _selectedPackageIndex = -1;
    private int _selectedGroupIndex = -1;
    private int _selectedCollectorIndex = -1;

    public string PanelName => "AssetsCollection";

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
    /// 按当前 Setting 与选中状态重建整个面板。
    /// </summary>
    private void Rebuild()
    {
        if (_root == null)
            return;

        _root.Clear();
        _root.Unbind();

        DrawToolbar();

        if (_setting == null || _so == null)
        {
            DrawNoSetting();
            return;
        }

        var main = new VisualElement();
        main.style.flexGrow = 1f;
        main.style.flexDirection = FlexDirection.Row;
        _root.Add(main);

        _sidebar = new VisualElement();
        _sidebar.style.width = _sidebarWidth;
        _sidebar.style.flexShrink = 0f;
        _sidebar.style.backgroundColor = EditorGUIUtility.isProSkin ? new Color(0.18f, 0.18f, 0.18f) : new Color(0.82f, 0.82f, 0.82f);
        main.Add(_sidebar);

        _splitter = new VisualElement();
        _splitter.style.width = 6f;
        _splitter.style.flexShrink = 0f;
        _splitter.RegisterCallback<PointerDownEvent>(OnSplitterDown);
        _splitter.RegisterCallback<PointerMoveEvent>(OnSplitterMove);
        _splitter.RegisterCallback<PointerUpEvent>(OnSplitterUp);
        main.Add(_splitter);

        _detail = new VisualElement();
        _detail.style.flexGrow = 1f;
        _detail.style.flexDirection = FlexDirection.Column;
        main.Add(_detail);

        BuildSidebar();
        BuildDetail();
    }

    /// <summary>
    /// 绘制顶部工具栏。
    /// </summary>
    private void DrawToolbar()
    {
        VisualElement toolbar = BuildPipelineUI.Toolbar();
        toolbar.Add(BuildPipelineUI.ToolbarButton("刷新", () =>
        {
            LoadSetting();
            Rebuild();
        }, 60f));
        toolbar.Add(BuildPipelineUI.ToolbarButton("扫描", RunScan, 52f));
        toolbar.Add(BuildPipelineUI.Spacer());
        toolbar.Add(BuildPipelineUI.ToolbarLabel(FYAssetBuildSettingsProvider.Shared.AssetCollectionSettingPath));
        _root.Add(toolbar);
    }

    /// <summary>
    /// 构建左侧导航树，显示全部 Package 与 Group。
    /// </summary>
    private void BuildSidebar()
    {
        _sidebar.Clear();

        ScrollView scroll = new ScrollView();
        scroll.style.flexGrow = 1f;
        scroll.style.paddingTop = 8f;
        scroll.style.paddingLeft = 8f;
        scroll.style.paddingRight = 8f;
        _sidebar.Add(scroll);

        SerializedProperty packagesProp = GetPackagesProperty();
        if (packagesProp != null)
        {
            for (int i = 0; i < packagesProp.arraySize; i++)
            {
                SerializedProperty packageProp = packagesProp.GetArrayElementAtIndex(i);
                scroll.Add(CreatePackageEntry(packageProp, i));

                SerializedProperty groupsProp = packageProp.FindPropertyRelative("Groups");
                if (groupsProp == null)
                    continue;

                for (int j = 0; j < groupsProp.arraySize; j++)
                {
                    SerializedProperty groupProp = groupsProp.GetArrayElementAtIndex(j);
                    scroll.Add(CreateGroupEntry(groupProp, i, j));
                }
            }
        }

        _sidebar.AddManipulator(new ContextualMenuManipulator(evt =>
        {
            evt.menu.AppendAction("Add Package", _ =>
            {
                AddPackage();
                SaveAndRebuild();
            });
        }));
    }

    /// <summary>
    /// 创建单个 Package 导航项，并挂接右键菜单。
    /// </summary>
    private VisualElement CreatePackageEntry(SerializedProperty packageProp, int packageIndex)
    {
        string packageName = packageProp.FindPropertyRelative("PackageName")?.stringValue;
        if (string.IsNullOrEmpty(packageName))
            packageName = "(unnamed package)";

        bool selected = _selectionType == SelectionType.Package && _selectedPackageIndex == packageIndex;
        Label label = CreateNavLabel("□ " + packageName, selected, 30f, 10f);
        label.RegisterCallback<PointerDownEvent>(evt =>
        {
            if (evt.button == 1)
            {
                ShowPackageMenu(label, packageIndex);
                evt.StopPropagation();
                return;
            }

            SelectPackage(packageIndex);
            BuildSidebar();
            BuildDetail();
            evt.StopPropagation();
        });
        return label;
    }

    /// <summary>
    /// 创建单个 Group 导航项，并显示启用状态。
    /// </summary>
    private VisualElement CreateGroupEntry(SerializedProperty groupProp, int packageIndex, int groupIndex)
    {
        string groupName = groupProp.FindPropertyRelative("GroupName")?.stringValue;
        if (string.IsNullOrEmpty(groupName))
            groupName = "(unnamed group)";

        bool enabled = groupProp.FindPropertyRelative("Enabled")?.boolValue ?? true;
        string suffix = enabled ? string.Empty : "  [Disabled]";
        bool selected = _selectionType == SelectionType.Group && _selectedPackageIndex == packageIndex && _selectedGroupIndex == groupIndex;
        Label label = CreateNavLabel("  □ " + groupName + suffix, selected, 24f, 8f);
        label.RegisterCallback<PointerDownEvent>(evt =>
        {
            if (evt.button == 1)
            {
                ShowGroupMenu(label, packageIndex, groupIndex);
                evt.StopPropagation();
                return;
            }

            SelectGroup(packageIndex, groupIndex);
            BuildSidebar();
            BuildDetail();
            evt.StopPropagation();
        });
        return label;
    }

    /// <summary>
    /// 创建导航标签，并根据选中状态切换背景与字体样式。
    /// </summary>
    private static Label CreateNavLabel(string text, bool selected, float height, float leftPadding)
    {
        var label = new Label(text);
        label.style.height = height;
        label.style.unityTextAlign = TextAnchor.MiddleLeft;
        label.style.paddingLeft = leftPadding;
        label.style.marginBottom = 1f;
        label.style.whiteSpace = WhiteSpace.Normal;
        label.style.backgroundColor = selected ? BuildPipelineUI.ActiveColor : Color.clear;
        label.style.color = selected ? Color.white : (EditorGUIUtility.isProSkin ? new Color(0.9f, 0.9f, 0.9f) : Color.black);
        label.style.unityFontStyleAndWeight = selected ? FontStyle.Bold : FontStyle.Normal;
        return label;
    }

    /// <summary>
    /// 按当前选中节点刷新右侧详情区。
    /// </summary>
    private void BuildDetail()
    {
        _detail.Clear();
        _detail.Unbind();

        var scroll = new ScrollView();
        scroll.style.flexGrow = 1f;
        scroll.style.paddingLeft = 8f;
        scroll.style.paddingRight = 8f;
        scroll.Bind(_so);
        _detail.Add(scroll);

        switch (_selectionType)
        {
            case SelectionType.Package:
                DrawPackageDetail(scroll);
                break;
            case SelectionType.Group:
                DrawGroupDetail(scroll);
                DrawScanPreview(scroll);
                break;
            default:
                DrawEmptyDetail(scroll);
                break;
        }
    }

    /// <summary>
    /// 绘制 Package 级配置详情。
    /// </summary>
    private void DrawPackageDetail(VisualElement parent)
    {
        SerializedProperty packageProp = GetSelectedPackageProperty();
        if (packageProp == null)
        {
            DrawEmptyDetail(parent);
            return;
        }

        VisualElement card = BuildPipelineUI.Card();
        card.Add(BuildPipelineUI.Header("Package"));
        card.Add(new PropertyField(packageProp.FindPropertyRelative("PackageName"), "Package Name"));

        SerializedProperty sharePolicy = packageProp.FindPropertyRelative("SharePolicy");
        if (sharePolicy != null)
        {
            card.Add(BuildPipelineUI.Header("Share"));
            card.Add(new PropertyField(sharePolicy.FindPropertyRelative("MinReferenceCount"), "Min Reference Count"));
            card.Add(new PropertyField(sharePolicy.FindPropertyRelative("MinAssetSizeBytes"), "Min Asset Size Bytes"));
            card.Add(new PropertyField(sharePolicy.FindPropertyRelative("NoSharePatterns"), "No Share Patterns"));
            card.Add(new PropertyField(sharePolicy.FindPropertyRelative("ForceSharePatterns"), "Force Share Patterns"));
        }

        parent.Add(card);
    }

    /// <summary>
    /// 绘制 Group 级配置详情，并附带其 Collector 列表。
    /// </summary>
    private void DrawGroupDetail(VisualElement parent)
    {
        SerializedProperty groupProp = GetSelectedGroupProperty();
        if (groupProp == null)
        {
            DrawEmptyDetail(parent);
            return;
        }

        VisualElement card = BuildPipelineUI.Card();
        card.Add(BuildPipelineUI.Header("Group"));
        card.Add(new PropertyField(groupProp.FindPropertyRelative("GroupName"), "Group Name"));
        card.Add(new PropertyField(groupProp.FindPropertyRelative("Enabled"), "Enabled"));
        card.Add(new PropertyField(groupProp.FindPropertyRelative("BundlePackingMode"), "Bundle Packing"));
        card.Add(new PropertyField(groupProp.FindPropertyRelative("Labels"), "Group Labels"));
        parent.Add(card);

        DrawCollectorTable(parent, groupProp.FindPropertyRelative("Collectors"));
    }

    /// <summary>
    /// 绘制当前 Group 下的简化 Collector 表。
    /// </summary>
    private void DrawCollectorTable(VisualElement parent, SerializedProperty collectorsProp)
    {
        VisualElement card = BuildPipelineUI.Card();
        VisualElement header = new VisualElement();
        header.style.flexDirection = FlexDirection.Row;
        header.style.alignItems = Align.Center;
        header.Add(BuildPipelineUI.Header("Collectors"));
        header.Add(BuildPipelineUI.Spacer());
        header.Add(new Button(() =>
        {
            AddCollector(collectorsProp, false);
            SaveAndRebuild();
        }) { text = "+ 目录" });
        header.Add(new Button(() =>
        {
            AddCollector(collectorsProp, true);
            SaveAndRebuild();
        }) { text = "+ 文件" });
        card.Add(header);

        if (collectorsProp == null || collectorsProp.arraySize == 0)
        {
            card.Add(BuildPipelineUI.SmallText("当前 Group 没有 Collector。"));
            parent.Add(card);
            return;
        }

        for (int i = 0; i < collectorsProp.arraySize; i++)
        {
            SerializedProperty collectorProp = collectorsProp.GetArrayElementAtIndex(i);
            card.Add(CreateCollectorRow(collectorProp, collectorsProp, i));
        }

        parent.Add(card);
    }

    /// <summary>
    /// 创建单个 Collector 行；选中时额外展开 Labels 与 IgnorePatterns。
    /// </summary>
    private VisualElement CreateCollectorRow(SerializedProperty collectorProp, SerializedProperty collectorsProp, int collectorIndex)
    {
        bool selected = _selectedCollectorIndex == collectorIndex;
        VisualElement row = BuildPipelineUI.Card();
        row.style.marginBottom = 4f;
        if (selected)
            row.style.backgroundColor = new Color(0.17f, 0.36f, 0.53f, 0.18f);

        VisualElement top = new VisualElement { style = { flexDirection = FlexDirection.Row } };
        PropertyField pathType = new PropertyField(collectorProp.FindPropertyRelative("CollectPathType"));
        pathType.label = string.Empty;
        pathType.style.width = 92f;
        top.Add(pathType);
        PropertyField path = new PropertyField(collectorProp.FindPropertyRelative("CollectPath"));
        path.label = string.Empty;
        path.style.flexGrow = 1f;
        top.Add(path);
        top.Add(new Button(() => PickCollectPath(collectorProp.FindPropertyRelative("CollectPath"), collectorProp.FindPropertyRelative("CollectPathType").enumValueIndex == (int)ECollectPathType.File)) { text = "..." });
        top.Add(new Button(() =>
        {
            RemoveCollector(collectorsProp, collectorIndex);
            SaveAndRebuild();
        }) { text = "x" });
        row.Add(top);

        VisualElement rules = new VisualElement { style = { flexDirection = FlexDirection.Row } };
        AddCollectorTypePopup(rules, collectorProp.FindPropertyRelative("CollectorType"), 92f);
        AddCompactProperty(rules, collectorProp.FindPropertyRelative("ForcePayloadKind"), 108f);
        AddRulePopup(rules, "Filter", collectorProp.FindPropertyRelative("FilterRuleName"), RuleDropdownHelper.GetFilterRuleNames());
        AddRulePopup(rules, "Group", collectorProp.FindPropertyRelative("GroupRuleName"), RuleDropdownHelper.GetGroupRuleNames());
        row.Add(rules);

        if (selected)
        {
            row.Add(new PropertyField(collectorProp.FindPropertyRelative("IgnorePatterns"), "Ignore Patterns"));
        }

        row.RegisterCallback<PointerDownEvent>(evt =>
        {
            _selectedAssetGuid = null;
            _selectedCollectorIndex = collectorIndex;
            BuildDetail();
            evt.StopPropagation();
        });
        return row;
    }

    /// <summary>
    /// 绘制扫描预览树和当前选中资产的 AssetEntry 编辑区。
    /// </summary>
    private void DrawScanPreview(VisualElement parent)
    {
        VisualElement card = BuildPipelineUI.Card();
        VisualElement header = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };
        header.Add(BuildPipelineUI.Header("Scan Preview"));
        header.Add(BuildPipelineUI.Spacer());
        header.Add(new Button(RunScan) { text = "扫描" });
        card.Add(header);

        if (_isScanning)
        {
            card.Add(BuildPipelineUI.SmallText("扫描中..."));
            parent.Add(card);
            return;
        }

        if (_lastScanResult == null)
        {
            card.Add(BuildPipelineUI.SmallText("尚未扫描。"));
            parent.Add(card);
            return;
        }

        int assetCount = _lastScanResult.Assets != null ? _lastScanResult.Assets.Count : 0;
        card.Add(BuildPipelineUI.SmallText($"Assets: {assetCount}"));

        if (assetCount == 0)
        {
            card.Add(BuildPipelineUI.SmallText("没有收集到资源。"));
            parent.Add(card);
            return;
        }

        DrawScanTree(card, _lastScanResult.Assets);

        parent.Add(card);

        if (!string.IsNullOrEmpty(_selectedAssetGuid))
            DrawAssetDetail(parent, _selectedAssetGuid);
    }

    private void DrawScanTree(VisualElement parent, List<CollectedAssetInfo> assets)
    {
        SortedDictionary<string, SortedDictionary<string, List<CollectedAssetInfo>>> tree = BuildScanTree(assets);
        foreach (KeyValuePair<string, SortedDictionary<string, List<CollectedAssetInfo>>> packagePair in tree)
        {
            string packageKey = GetScanNodeKey(packagePair.Key, null);
            bool packageExpanded = IsScanNodeExpanded(packageKey);
            int packageAssetCount = CountPackageAssets(packagePair.Value);
            parent.Add(CreateScanNodeRow(
                packageExpanded,
                string.Format("{0} ({1})", packagePair.Key, packageAssetCount),
                0f,
                () => ToggleScanNode(packageKey)));

            if (!packageExpanded)
                continue;

            foreach (KeyValuePair<string, List<CollectedAssetInfo>> groupPair in packagePair.Value)
            {
                string groupKey = GetScanNodeKey(packagePair.Key, groupPair.Key);
                bool groupExpanded = IsScanNodeExpanded(groupKey);
                parent.Add(CreateScanNodeRow(
                    groupExpanded,
                    string.Format("{0} ({1})", groupPair.Key, groupPair.Value.Count),
                    14f,
                    () => ToggleScanNode(groupKey)));

                if (!groupExpanded)
                    continue;

                for (int i = 0; i < groupPair.Value.Count; i++)
                    parent.Add(CreateAssetPreviewRow(groupPair.Value[i], 28f));
            }
        }
    }

    private static SortedDictionary<string, SortedDictionary<string, List<CollectedAssetInfo>>> BuildScanTree(List<CollectedAssetInfo> assets)
    {
        var tree = new SortedDictionary<string, SortedDictionary<string, List<CollectedAssetInfo>>>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < assets.Count; i++)
        {
            CollectedAssetInfo asset = assets[i];
            if (asset == null)
                continue;

            string packageName = string.IsNullOrEmpty(asset.PackageName) ? "(unnamed package)" : asset.PackageName;
            string groupName = string.IsNullOrEmpty(asset.GroupName) ? "(unnamed group)" : asset.GroupName;

            if (!tree.TryGetValue(packageName, out SortedDictionary<string, List<CollectedAssetInfo>> groups))
            {
                groups = new SortedDictionary<string, List<CollectedAssetInfo>>(StringComparer.OrdinalIgnoreCase);
                tree.Add(packageName, groups);
            }

            if (!groups.TryGetValue(groupName, out List<CollectedAssetInfo> groupAssets))
            {
                groupAssets = new List<CollectedAssetInfo>();
                groups.Add(groupName, groupAssets);
            }

            groupAssets.Add(asset);
        }

        foreach (KeyValuePair<string, SortedDictionary<string, List<CollectedAssetInfo>>> packagePair in tree)
        {
            foreach (KeyValuePair<string, List<CollectedAssetInfo>> groupPair in packagePair.Value)
                groupPair.Value.Sort((left, right) => string.Compare(left.AssetPath, right.AssetPath, StringComparison.OrdinalIgnoreCase));
        }

        return tree;
    }

    private static int CountPackageAssets(SortedDictionary<string, List<CollectedAssetInfo>> groups)
    {
        int count = 0;
        foreach (KeyValuePair<string, List<CollectedAssetInfo>> groupPair in groups)
            count += groupPair.Value.Count;
        return count;
    }

    private VisualElement CreateScanNodeRow(bool expanded, string text, float indent, Action onClick)
    {
        Label row = BuildPipelineUI.SmallText((expanded ? "▼ " : "▶ ") + text);
        row.style.marginLeft = indent;
        row.style.height = 20f;
        row.style.unityTextAlign = TextAnchor.MiddleLeft;
        row.style.unityFontStyleAndWeight = FontStyle.Bold;
        row.RegisterCallback<PointerDownEvent>(evt =>
        {
            if (evt.button != 0)
                return;

            onClick?.Invoke();
            evt.StopPropagation();
        });
        return row;
    }

    private VisualElement CreateAssetPreviewRow(CollectedAssetInfo asset, float indent)
    {
        bool selected = string.Equals(_selectedAssetGuid, asset.AssetGUID, StringComparison.Ordinal);
        VisualElement row = BuildPipelineUI.Card();
        row.style.marginBottom = 3f;
        row.style.marginLeft = indent;
        if (selected)
            row.style.backgroundColor = new Color(0.17f, 0.36f, 0.53f, 0.18f);

        row.Add(BuildPipelineUI.SmallText(asset.AssetPath));
        row.Add(BuildPipelineUI.SmallText($"{asset.BundlePackingMode}  {asset.Address}  ->  {asset.BundleName}"));
        row.Add(BuildPipelineUI.SmallText($"Role={asset.Classification.Role}  Payload={asset.Classification.PayloadKind}  Labels={string.Join(",", asset.Labels)}"));
        row.RegisterCallback<PointerDownEvent>(evt =>
        {
            _selectedAssetGuid = asset.AssetGUID;
            BuildDetail();
            evt.StopPropagation();
        });
        return row;
    }

    private void ToggleScanNode(string nodeKey)
    {
        if (!_expandedScanNodes.Remove(nodeKey))
            _expandedScanNodes.Add(nodeKey);
        BuildDetail();
    }

    private bool IsScanNodeExpanded(string nodeKey)
    {
        return _expandedScanNodes.Contains(nodeKey);
    }

    private static string GetScanNodeKey(string packageName, string groupName)
    {
        return groupName == null
            ? "pkg:" + packageName
            : "grp:" + packageName + "/" + groupName;
    }

    private void ExpandCurrentScanGroup()
    {
        AssetCollectionPackage package = GetSelectedPackage();
        AssetCollectionGroup group = GetSelectedGroup();
        if (package == null || group == null)
            return;

        string packageName = string.IsNullOrEmpty(package.PackageName) ? "(unnamed package)" : package.PackageName;
        string groupName = string.IsNullOrEmpty(group.GroupName) ? "(unnamed group)" : group.GroupName;
        _expandedScanNodes.Add(GetScanNodeKey(packageName, null));
        _expandedScanNodes.Add(GetScanNodeKey(packageName, groupName));
    }

    private void DrawAssetDetail(VisualElement parent, string assetGuid)
    {
        SerializedProperty entryProp = FindAssetEntryProperty(assetGuid);
        CollectedAssetInfo preview = FindPreviewAsset(assetGuid);

        VisualElement card = BuildPipelineUI.Card();
        card.Add(BuildPipelineUI.Header("Asset"));
        if (preview != null)
        {
            card.Add(BuildPipelineUI.SmallText(preview.AssetPath));
            card.Add(BuildPipelineUI.SmallText($"Bundle: {preview.BundleName}"));
            card.Add(BuildPipelineUI.SmallText($"Inherited Labels: {string.Join(",", preview.GroupLabels)}"));
        }

        if (entryProp == null)
        {
            card.Add(BuildPipelineUI.SmallText("AssetEntry 尚未生成。先执行扫描。"));
            parent.Add(card);
            return;
        }

        card.Add(new PropertyField(entryProp.FindPropertyRelative("AssetGUID"), "GUID"));
        AddResettableAutoField(
            card,
            "Address",
            entryProp.FindPropertyRelative("AutoAddress"),
            entryProp.FindPropertyRelative("Address"),
            () => ResetAddress(entryProp, preview));
        card.Add(new PropertyField(entryProp.FindPropertyRelative("Labels"), "Asset Labels"));
        AddResettableAutoField(
            card,
            "Role",
            entryProp.FindPropertyRelative("AutoRole"),
            entryProp.FindPropertyRelative("Role"),
            () => ResetRole(entryProp, preview));
        AddResettableAutoField(
            card,
            "Payload",
            entryProp.FindPropertyRelative("AutoPayload"),
            entryProp.FindPropertyRelative("PayloadKind"),
            () => ResetPayload(entryProp, preview));
        parent.Add(card);
    }

    private void AddResettableAutoField(
        VisualElement parent,
        string label,
        SerializedProperty autoProp,
        SerializedProperty valueProp,
        Action reset)
    {
        VisualElement row = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };
        PropertyField autoField = new PropertyField(autoProp, "Auto " + label);
        autoField.style.flexGrow = 1f;
        row.Add(autoField);
        row.Add(new Button(() =>
        {
            reset?.Invoke();
            ApplyChanges();
            RunScan();
        }) { text = "Reset Auto" });
        parent.Add(row);
        parent.Add(new PropertyField(valueProp, label));
    }

    private SerializedProperty FindAssetEntryProperty(string assetGuid)
    {
        if (_so == null || string.IsNullOrEmpty(assetGuid))
            return null;

        _so.Update();
        SerializedProperty entries = _so.FindProperty("AssetEntries");
        if (entries == null)
            return null;

        for (int i = 0; i < entries.arraySize; i++)
        {
            SerializedProperty entry = entries.GetArrayElementAtIndex(i);
            if (string.Equals(entry.FindPropertyRelative("AssetGUID")?.stringValue, assetGuid, StringComparison.Ordinal))
                return entry;
        }

        return null;
    }

    private CollectedAssetInfo FindPreviewAsset(string assetGuid)
    {
        if (_lastScanResult?.Assets == null || string.IsNullOrEmpty(assetGuid))
            return null;

        for (int i = 0; i < _lastScanResult.Assets.Count; i++)
        {
            CollectedAssetInfo asset = _lastScanResult.Assets[i];
            if (asset != null && string.Equals(asset.AssetGUID, assetGuid, StringComparison.Ordinal))
                return asset;
        }

        return null;
    }

    private static void ResetAddress(SerializedProperty entryProp, CollectedAssetInfo preview)
    {
        if (entryProp == null || preview == null)
            return;

        entryProp.FindPropertyRelative("AutoAddress").boolValue = true;
        entryProp.FindPropertyRelative("Address").stringValue =
            AssetAddressGenerator.GenerateShortAddress(preview.AssetPath, preview.PrimaryType, true);
    }

    private static void ResetRole(SerializedProperty entryProp, CollectedAssetInfo preview)
    {
        if (entryProp == null || preview == null)
            return;

        entryProp.FindPropertyRelative("AutoRole").boolValue = true;
        entryProp.FindPropertyRelative("Role").enumValueIndex = (int)preview.Classification.Role;
    }

    private static void ResetPayload(SerializedProperty entryProp, CollectedAssetInfo preview)
    {
        if (entryProp == null || preview == null)
            return;

        entryProp.FindPropertyRelative("AutoPayload").boolValue = true;
        entryProp.FindPropertyRelative("PayloadKind").enumValueIndex = (int)preview.Classification.PayloadKind;
    }

    /// <summary>
    /// 添加定宽字段，用于紧凑布局中的短枚举属性。
    /// </summary>
    private static void AddCompactProperty(VisualElement parent, SerializedProperty property, float width)
    {
        PropertyField field = new PropertyField(property);
        field.label = string.Empty;
        field.style.width = width;
        parent.Add(field);
    }

    private static readonly ECollectorType[] ManualCollectorTypes =
    {
        ECollectorType.Main,
        ECollectorType.Static,
        ECollectorType.Depend
    };

    private static readonly string[] ManualCollectorTypeNames =
    {
        ECollectorType.Main.ToString(),
        ECollectorType.Static.ToString(),
        ECollectorType.Depend.ToString()
    };

    private void AddCollectorTypePopup(VisualElement parent, SerializedProperty property, float width)
    {
        int enumValue = property.enumValueIndex;
        string current = IsManualCollectorType(enumValue)
            ? ((ECollectorType)enumValue).ToString()
            : string.Concat("Invalid: ", ((ECollectorType)enumValue).ToString());

        List<string> choices = new List<string>(ManualCollectorTypeNames);
        if (!choices.Contains(current))
            choices.Insert(0, current);

        var popup = new PopupField<string>(choices, current);
        popup.style.width = width;
        popup.RegisterValueChangedCallback(evt =>
        {
            if (TryParseManualCollectorType(evt.newValue, out ECollectorType collectorType))
            {
                property.enumValueIndex = (int)collectorType;
                ApplyChanges();
            }
        });
        parent.Add(popup);
    }

    private static bool IsManualCollectorType(int enumValue)
    {
        for (int i = 0; i < ManualCollectorTypes.Length; i++)
        {
            if ((int)ManualCollectorTypes[i] == enumValue)
                return true;
        }

        return false;
    }

    private static bool TryParseManualCollectorType(string value, out ECollectorType collectorType)
    {
        for (int i = 0; i < ManualCollectorTypes.Length; i++)
        {
            if (string.Equals(value, ManualCollectorTypeNames[i], StringComparison.Ordinal))
            {
                collectorType = ManualCollectorTypes[i];
                return true;
            }
        }

        collectorType = ECollectorType.Main;
        return false;
    }

    /// <summary>
    /// 绘制规则名下拉框，并在变更时立即提交 SerializedProperty。
    /// </summary>
    private void AddRulePopup(VisualElement parent, string shortLabel, SerializedProperty property, string[] choices)
    {
        parent.Add(BuildPipelineUI.SmallText(shortLabel));
        List<string> list = new List<string>(choices ?? Array.Empty<string>());
        if (list.Count == 0)
            list.Add(property.stringValue);
        if (!list.Contains(property.stringValue))
            list.Insert(0, property.stringValue);

        var popup = new PopupField<string>(list, property.stringValue);
        popup.style.width = 92f;
        popup.RegisterValueChangedCallback(evt =>
        {
            property.stringValue = evt.newValue;
            ApplyChanges();
        });
        parent.Add(popup);
    }

    /// <summary>
    /// 在未选中任何节点时显示引导说明。
    /// </summary>
    private void DrawEmptyDetail(VisualElement parent)
    {
        VisualElement panel = BuildPipelineUIToolkitPanel.CreateCenteredPanel(parent, 320f);
        panel.Add(BuildPipelineUIToolkitPanel.CreateTitle("选中 Package 或 Group"));
        panel.Add(BuildPipelineUIToolkitPanel.CreateBody("左侧看层级，右侧改 Package、Group 和 Collector。"));
    }

    /// <summary>
    /// AssetCollectionSetting 缺失时显示创建入口。
    /// </summary>
    private void DrawNoSetting()
    {
        VisualElement panel = BuildPipelineUIToolkitPanel.CreateCenteredPanel(_root, 420f);
        panel.Add(BuildPipelineUIToolkitPanel.CreateTitle("未找到 AssetCollectionSetting"));
        panel.Add(BuildPipelineUIToolkitPanel.CreateBody(FYAssetBuildSettingsProvider.Shared.AssetCollectionSettingPath));
        panel.Add(new Button(CreateSetting) { text = "创建" });
    }

    /// <summary>
    /// 加载 AssetCollectionSetting 并修正当前选中状态。
    /// </summary>
    private void LoadSetting()
    {
        _setting = AssetDatabase.LoadAssetAtPath<AssetCollectionSetting>(FYAssetBuildSettingsProvider.Shared.AssetCollectionSettingPath);
        _so = _setting != null ? new SerializedObject(_setting) : null;
        _selectedAssetGuid = null;
        EnsureSelection();
    }

    /// <summary>
    /// 创建新的 AssetCollectionSetting 资产。
    /// </summary>
    private void CreateSetting()
    {
        BuildPipelineUI.EnsureAssetParentFolder(FYAssetBuildSettingsProvider.Shared.AssetCollectionSettingPath);
        AssetCollectionSetting asset = ScriptableObject.CreateInstance<AssetCollectionSetting>();
        AssetDatabase.CreateAsset(asset, FYAssetBuildSettingsProvider.Shared.AssetCollectionSettingPath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        CollectorReverseIndex.Instance.MarkDirty();
        LoadSetting();
        Rebuild();
    }

    /// <summary>
    /// 追加一个新的 Package，并填入默认 SharePolicy。
    /// </summary>
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

    /// <summary>
    /// 向指定 Package 追加一个新的 Group。
    /// </summary>
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
        groupProp.FindPropertyRelative("BundlePackingMode").enumValueIndex = (int)BundlePackingMode.PackTogetherByLabel;
        groupProp.FindPropertyRelative("Labels").arraySize = 0;
        groupProp.FindPropertyRelative("Collectors").arraySize = 0;
        SelectGroup(packageIndex, index);
    }

    /// <summary>
    /// 在当前 Group 下追加一个简化默认 Collector。
    /// </summary>
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
        collectorProp.FindPropertyRelative("FilterRuleName").stringValue = FYAssetSettings.RULE_COLLECT_ALL;
        collectorProp.FindPropertyRelative("GroupRuleName").stringValue = FYAssetSettings.RULE_GROUP_ALL;
        collectorProp.FindPropertyRelative("IgnorePatterns").arraySize = 0;
        _selectedCollectorIndex = index;
    }

    /// <summary>
    /// 删除指定位置的 Collector，并修正选中项。
    /// </summary>
    private void RemoveCollector(SerializedProperty collectorsProp, int collectorIndex)
    {
        Undo.RecordObject(_setting, "Remove Collector");
        collectorsProp.DeleteArrayElementAtIndex(collectorIndex);
        if (_selectedCollectorIndex >= collectorsProp.arraySize)
            _selectedCollectorIndex = collectorsProp.arraySize - 1;
    }

    /// <summary>
    /// 删除当前选中的 Package 或 Group。
    /// </summary>
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

    /// <summary>
    /// 显示 Package 的右键菜单。
    /// </summary>
    private void ShowPackageMenu(VisualElement target, int packageIndex)
    {
        var menu = new GenericMenu();
        menu.AddItem(new GUIContent("加 Group"), false, () =>
        {
            AddGroup(packageIndex);
            SaveAndRebuild();
        });
        menu.AddSeparator("");
        menu.AddItem(new GUIContent("删 Package"), false, () =>
        {
            SelectPackage(packageIndex);
            DeleteCurrentSelection();
            SaveAndRebuild();
        });
        menu.DropDown(target.worldBound);
    }

    /// <summary>
    /// 显示 Group 的右键菜单。
    /// </summary>
    private void ShowGroupMenu(VisualElement target, int packageIndex, int groupIndex)
    {
        var menu = new GenericMenu();
        menu.AddItem(new GUIContent("删 Group"), false, () =>
        {
            SelectGroup(packageIndex, groupIndex);
            DeleteCurrentSelection();
            SaveAndRebuild();
        });
        menu.DropDown(target.worldBound);
    }

    /// <summary>
    /// 打开文件/文件夹选择器并回填为项目内 Assets 路径。
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
    /// 提交 SerializedObject 修改，并刷新逆向索引。
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
    /// 保存当前改动并重建整个面板。
    /// </summary>
    private void SaveAndRebuild()
    {
        _so?.ApplyModifiedProperties();
        EditorUtility.SetDirty(_setting);
        CollectorReverseIndex.Instance.MarkDirty();
        Rebuild();
    }

    /// <summary>
    /// 运行 Collector 扫描并刷新右侧扫描预览。
    /// </summary>
    private void RunScan()
    {
        if (_setting == null || _isScanning)
            return;

        try
        {
            _isScanning = true;
            _lastScanResult = CollectionScanner.Scan(_setting);
            _expandedScanNodes.Clear();
            ExpandCurrentScanGroup();
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
            _lastScanResult = null;
        }
        finally
        {
            _isScanning = false;
            BuildDetail();
        }
    }

    /// <summary>
    /// 依据当前 Setting 修正导航与 Collector 选中状态。
    /// </summary>
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

        AssetCollectionPackage package = _setting.Packages[_selectedPackageIndex];
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

    /// <summary>
    /// 选中一个 Package，并清空更细粒度的选择。
    /// </summary>
    private void SelectPackage(int packageIndex)
    {
        _selectionType = SelectionType.Package;
        _selectedPackageIndex = packageIndex;
        _selectedGroupIndex = -1;
        _selectedCollectorIndex = -1;
        _selectedAssetGuid = null;
    }

    /// <summary>
    /// 选中一个 Group，并默认将 Collector 选择移动到第一项。
    /// </summary>
    private void SelectGroup(int packageIndex, int groupIndex)
    {
        _selectionType = SelectionType.Group;
        _selectedPackageIndex = packageIndex;
        _selectedGroupIndex = groupIndex;
        _selectedCollectorIndex = 0;
        _selectedAssetGuid = null;
    }

    /// <summary>
    /// 获取 Packages 根属性。
    /// </summary>
    private SerializedProperty GetPackagesProperty()
    {
        if (_so == null)
            return null;

        _so.Update();
        return _so.FindProperty("Packages");
    }

    /// <summary>
    /// 获取指定下标的 Package 属性。
    /// </summary>
    private SerializedProperty GetPackageProperty(int packageIndex)
    {
        SerializedProperty packagesProp = GetPackagesProperty();
        if (packagesProp == null || packageIndex < 0 || packageIndex >= packagesProp.arraySize)
            return null;

        return packagesProp.GetArrayElementAtIndex(packageIndex);
    }

    /// <summary>
    /// 获取当前选中的 Package 属性。
    /// </summary>
    private SerializedProperty GetSelectedPackageProperty()
    {
        return GetPackageProperty(_selectedPackageIndex);
    }

    /// <summary>
    /// 获取当前选中的 Package 运行时对象。
    /// </summary>
    private AssetCollectionPackage GetSelectedPackage()
    {
        if (_setting?.Packages == null || _selectedPackageIndex < 0 || _selectedPackageIndex >= _setting.Packages.Count)
            return null;

        return _setting.Packages[_selectedPackageIndex];
    }

    /// <summary>
    /// 获取当前选中的 Group 属性。
    /// </summary>
    private SerializedProperty GetSelectedGroupProperty()
    {
        SerializedProperty packageProp = GetSelectedPackageProperty();
        SerializedProperty groupsProp = packageProp?.FindPropertyRelative("Groups");
        if (groupsProp == null || _selectedGroupIndex < 0 || _selectedGroupIndex >= groupsProp.arraySize)
            return null;

        return groupsProp.GetArrayElementAtIndex(_selectedGroupIndex);
    }

    /// <summary>
    /// 获取当前选中的 Group 运行时对象。
    /// </summary>
    private AssetCollectionGroup GetSelectedGroup()
    {
        AssetCollectionPackage package = GetSelectedPackage();
        if (package?.Groups == null || _selectedGroupIndex < 0 || _selectedGroupIndex >= package.Groups.Count)
            return null;

        return package.Groups[_selectedGroupIndex];
    }

    /// <summary>
    /// 开始拖动左右分隔条。
    /// </summary>
    private void OnSplitterDown(PointerDownEvent evt)
    {
        if (evt.button != 0)
            return;

        _draggingSplitter = true;
        _dragStartMouse = evt.position;
        _dragStartWidth = _sidebarWidth;
        evt.target.CapturePointer(evt.pointerId);
        evt.StopPropagation();
    }

    /// <summary>
    /// 拖动时更新左侧导航区宽度。
    /// </summary>
    private void OnSplitterMove(PointerMoveEvent evt)
    {
        if (!_draggingSplitter)
            return;

        float delta = evt.position.x - _dragStartMouse.x;
        _sidebarWidth = Mathf.Clamp(_dragStartWidth + delta, 140f, Mathf.Max(140f, _root.resolvedStyle.width * 0.5f));
        _sidebar.style.width = _sidebarWidth;
        evt.StopPropagation();
    }

    /// <summary>
    /// 结束左右分隔条拖拽。
    /// </summary>
    private void OnSplitterUp(PointerUpEvent evt)
    {
        if (!_draggingSplitter)
            return;

        _draggingSplitter = false;
        evt.target.ReleasePointer(evt.pointerId);
        evt.StopPropagation();
    }
}
