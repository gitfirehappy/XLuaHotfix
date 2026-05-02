using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

/// <summary>
/// CollectorSetting 三层次 TreeView：Package（depth 0）→ Group（depth 1）→ Collector（depth 2）。
/// 支持拖拽排序（T5），右键菜单（T6）。
/// </summary>
public sealed class CollectorTreeView : TreeView
{
    #region Fields

    private CollectorSetting _setting;
    private SerializedObject _so;
    private List<CollectorTreeViewItem> _allItems = new();
    private TreeViewSelection _savedSelection;

    #endregion

    #region Constructor

    public CollectorTreeView(TreeViewState state, CollectorSetting setting)
        : base(state)
    {
        _setting = setting;
        if (setting != null)
            _so = new SerializedObject(setting);
        showAlternatingRowBackgrounds = true;
        Reload();
    }

    #endregion

    #region TreeView Overrides

    protected override TreeViewItem BuildRoot()
    {
        int id = 0;
        var root = new TreeViewItem
        {
            id = id++,
            depth = -1,
            displayName = "Root",
            children = new List<TreeViewItem>()
        };
        _allItems.Clear();

        if (_setting == null || _setting.Packages == null || _setting.Packages.Count == 0)
        {
            SetupDepthsFromParentsAndChildren(root);
            return root;
        }

        for (int pi = 0; pi < _setting.Packages.Count; pi++)
        {
            CollectorPackage pkg = _setting.Packages[pi];
            if (pkg == null)
                continue;

            string pkgDisplay = string.IsNullOrEmpty(pkg.PackageName) ? "(unnamed)" : pkg.PackageName;
            var pkgItem = new CollectorTreeViewItem
            {
                id = id++,
                depth = 0,
                displayName = string.Concat("\U0001F4E6 ", pkgDisplay),
                Type = CollectorTreeViewItem.NodeType.Package,
                PackageIndex = pi,
                children = new List<TreeViewItem>()
            };
            root.children.Add(pkgItem);
            _allItems.Add(pkgItem);

            if (pkg.Groups == null)
                continue;

            for (int gi = 0; gi < pkg.Groups.Count; gi++)
            {
                CollectorGroup grp = pkg.Groups[gi];
                if (grp == null)
                    continue;

                string grpDisplay = string.IsNullOrEmpty(grp.GroupName) ? "(unnamed)" : grp.GroupName;
                string disabledTag = grp.Enabled ? "" : " [Disabled]";
                var grpItem = new CollectorTreeViewItem
                {
                    id = id++,
                    depth = 1,
                    displayName = string.Concat("\U0001F4C1 ", grpDisplay, disabledTag),
                    Type = CollectorTreeViewItem.NodeType.Group,
                    PackageIndex = pi,
                    GroupIndex = gi,
                    children = new List<TreeViewItem>()
                };
                pkgItem.children.Add(grpItem);
                _allItems.Add(grpItem);

                if (grp.Collectors == null)
                    continue;

                for (int ci = 0; ci < grp.Collectors.Count; ci++)
                {
                    Collector col = grp.Collectors[ci];
                    if (col == null)
                        continue;

                    string lastSegment = "(empty)";
                    if (!string.IsNullOrEmpty(col.CollectPath))
                    {
                        int lastSlash = col.CollectPath.LastIndexOf('/');
                        lastSegment = lastSlash >= 0
                            ? col.CollectPath.Substring(lastSlash + 1)
                            : col.CollectPath;
                    }

                    var colItem = new CollectorTreeViewItem
                    {
                        id = id++,
                        depth = 2,
                        displayName = string.Concat("\U0001F4C4 ", lastSegment),
                        Type = CollectorTreeViewItem.NodeType.Collector,
                        PackageIndex = pi,
                        GroupIndex = gi,
                        CollectorIndex = ci
                    };
                    grpItem.children.Add(colItem);
                    _allItems.Add(colItem);
                }
            }
        }

        SetupDepthsFromParentsAndChildren(root);
        return root;
    }

    protected override bool CanMultiSelect(TreeViewItem item) => false;

    #endregion

    #region Drag & Drop (T5)

    protected override bool CanStartDrag(CanStartDragArgs args)
    {
        if (args.draggedItem is CollectorTreeViewItem item)
            return item.Type != CollectorTreeViewItem.NodeType.Package; // Packages stay at root — no reorder needed
        return false;
    }

    protected override void SetupDragAndDrop(SetupDragAndDropArgs args)
    {
        var draggedItems = new List<CollectorTreeViewItem>();
        for (int i = 0; i < args.draggedItemIDs.Count; i++)
        {
            int id = args.draggedItemIDs[i];
            var item = _allItems.Find(it => it.id == id);
            if (item != null)
                draggedItems.Add(item);
        }

        if (draggedItems.Count == 0)
            return;

        DragAndDrop.PrepareStartDrag();
        DragAndDrop.SetGenericData("CollectorTreeDrag", draggedItems);
        DragAndDrop.StartDrag(draggedItems[0].displayName);
    }

    protected override DragAndDropVisualMode HandleDragAndDrop(DragAndDropArgs args)
    {
        var draggedItems = DragAndDrop.GetGenericData("CollectorTreeDrag") as List<CollectorTreeViewItem>;
        if (draggedItems == null || draggedItems.Count == 0)
            return DragAndDropVisualMode.Rejected;

        var dragged = draggedItems[0];
        var target = args.parentItem as CollectorTreeViewItem;

        // Determine target parent based on drag type
        if (args.dragAndDropPosition == DragAndDropPosition.BetweenItems || args.dragAndDropPosition == DragAndDropPosition.UponItem)
        {
            if (!IsValidDropTarget(dragged, target))
                return DragAndDropVisualMode.Rejected;

            if (args.performDrop)
                PerformDrop(dragged, target);

            return DragAndDropVisualMode.Move;
        }

        return DragAndDropVisualMode.Rejected;
    }

    private bool IsValidDropTarget(CollectorTreeViewItem dragged, CollectorTreeViewItem target)
    {
        if (dragged == null || target == null)
            return false;

        // Must be same node type (same-level only)
        if (dragged.Type != target.Type)
            return false;

        // Groups must belong to the same Package
        if (dragged.Type == CollectorTreeViewItem.NodeType.Group)
            return dragged.PackageIndex == target.PackageIndex;

        // Collectors must belong to the same Group within the same Package
        if (dragged.Type == CollectorTreeViewItem.NodeType.Collector)
            return dragged.PackageIndex == target.PackageIndex && dragged.GroupIndex == target.GroupIndex;

        return false;
    }

    private void PerformDrop(CollectorTreeViewItem dragged, CollectorTreeViewItem target)
    {
        if (_so == null)
            return;

        _so.Update();

        SerializedProperty srcArray;
        int srcIndex;
        int dstIndex;

        switch (dragged.Type)
        {
            case CollectorTreeViewItem.NodeType.Group:
                srcArray = _so.FindProperty(
                    string.Concat("Packages.Array.data[", dragged.PackageIndex, "].Groups.Array.data"));
                dstIndex = target.GroupIndex;
                srcIndex = dragged.GroupIndex;
                break;
            case CollectorTreeViewItem.NodeType.Collector:
                srcArray = _so.FindProperty(
                    string.Concat("Packages.Array.data[", dragged.PackageIndex,
                        "].Groups.Array.data[", dragged.GroupIndex, "].Collectors.Array.data"));
                dstIndex = target.CollectorIndex;
                srcIndex = dragged.CollectorIndex;
                break;
            default:
                return;
        }

        if (srcArray == null || srcIndex < 0 || srcIndex >= srcArray.arraySize)
            return;

        Undo.RecordObject(_so.targetObject, "Reorder TreeView Node");
        srcArray.MoveArrayElement(srcIndex, dstIndex);
        _so.ApplyModifiedProperties();

        Reload();
        SetSelection(new[] { target.id });
    }

    #endregion

    #region Context Menu (T6)

    public override void OnGUI(Rect rect)
    {
        base.OnGUI(rect);

        Event evt = Event.current;
        if (evt != null && evt.type == EventType.ContextClick && rect.Contains(evt.mousePosition))
        {
            var selectedItem = GetSelectedItem();
            ShowContextMenu(selectedItem);
            evt.Use();
        }
    }

    private void ShowContextMenu(CollectorTreeViewItem selected)
    {
        GenericMenu menu = new GenericMenu();

        if (selected == null || selected.Type == CollectorTreeViewItem.NodeType.Package)
        {
            // Empty area or Package selected → Add Package (global)
            if (selected == null)
                menu.AddItem(new GUIContent("Add Package"), false, () => AddPackage());
            else
                menu.AddItem(new GUIContent("Add Group"), false, () => AddGroup(selected));
        }

        if (selected != null)
        {
            switch (selected.Type)
            {
                case CollectorTreeViewItem.NodeType.Package:
                    menu.AddSeparator("");
                    menu.AddItem(new GUIContent("Delete Package"), false, () => DeletePackage(selected));
                    menu.AddItem(new GUIContent("Duplicate Package"), false, () => DuplicatePackage(selected));
                    break;
                case CollectorTreeViewItem.NodeType.Group:
                    menu.AddItem(new GUIContent("Add Collector"), false, () => AddCollector(selected));
                    menu.AddSeparator("");
                    menu.AddItem(new GUIContent("Delete Group"), false, () => DeleteGroup(selected));
                    menu.AddItem(new GUIContent("Duplicate Group"), false, () => DuplicateGroup(selected));
                    break;
                case CollectorTreeViewItem.NodeType.Collector:
                    menu.AddItem(new GUIContent("Delete Collector"), false, () => DeleteCollector(selected));
                    menu.AddItem(new GUIContent("Duplicate Collector"), false, () => DuplicateCollector(selected));
                    break;
            }
        }

        menu.ShowAsContext();
    }

    #region Add Operations

    private void AddPackage()
    {
        if (_so == null) return;
        _so.Update();
        int idx = _so.FindProperty("Packages").arraySize;
        _so.FindProperty("Packages").InsertArrayElementAtIndex(idx);
        var pkg = _so.FindProperty(string.Concat("Packages.Array.data[", idx, "]"));
        pkg.FindPropertyRelative("PackageName").stringValue = "NewPackage";
        pkg.FindPropertyRelative("Groups").ClearArray();

        Undo.RecordObject(_so.targetObject, "Add Package");
        _so.ApplyModifiedProperties();
        Reload();
    }

    private void AddGroup(CollectorTreeViewItem pkgItem)
    {
        if (_so == null) return;
        _so.Update();
        var groupsProp = _so.FindProperty(
            string.Concat("Packages.Array.data[", pkgItem.PackageIndex, "].Groups"));
        int idx = groupsProp.arraySize;
        groupsProp.InsertArrayElementAtIndex(idx);
        var grp = groupsProp.GetArrayElementAtIndex(idx);
        grp.FindPropertyRelative("GroupName").stringValue = "NewGroup";
        grp.FindPropertyRelative("Enabled").boolValue = true;
        grp.FindPropertyRelative("Labels").ClearArray();
        grp.FindPropertyRelative("Collectors").ClearArray();

        Undo.RecordObject(_so.targetObject, "Add Group");
        _so.ApplyModifiedProperties();
        Reload();
    }

    private void AddCollector(CollectorTreeViewItem grpItem)
    {
        if (_so == null) return;
        _so.Update();
        var colsProp = _so.FindProperty(
            string.Concat("Packages.Array.data[", grpItem.PackageIndex,
                "].Groups.Array.data[", grpItem.GroupIndex, "].Collectors"));
        int idx = colsProp.arraySize;
        colsProp.InsertArrayElementAtIndex(idx);
        var col = colsProp.GetArrayElementAtIndex(idx);
        col.FindPropertyRelative("CollectPath").stringValue = "";
        col.FindPropertyRelative("CollectorType").enumValueIndex = 0; // Main
        col.FindPropertyRelative("ForcePayloadKind").enumValueIndex = 0; // Auto
        col.FindPropertyRelative("AddressRuleName").stringValue = FYAssetConstants.RULE_ADDRESS_BY_FILE_NAME;
        col.FindPropertyRelative("PackRuleName").stringValue = FYAssetConstants.RULE_PACK_BY_COLLECT_PATH;
        col.FindPropertyRelative("FilterRuleName").stringValue = FYAssetConstants.RULE_COLLECT_ALL;
        col.FindPropertyRelative("GroupRuleName").stringValue = FYAssetConstants.RULE_GROUP_ALL;
        col.FindPropertyRelative("Labels").ClearArray();
        col.FindPropertyRelative("IgnorePatterns").ClearArray();

        Undo.RecordObject(_so.targetObject, "Add Collector");
        _so.ApplyModifiedProperties();
        Reload();
    }

    #endregion

    #region Delete Operations

    private bool ConfirmDelete(string nodeType, string nodeName)
    {
        return EditorUtility.DisplayDialog(
            string.Concat("Delete ", nodeType),
            string.Concat("Delete ", nodeType, " '", nodeName, "'?"),
            "Delete", "Cancel");
    }

    private void DeletePackage(CollectorTreeViewItem item)
    {
        if (_so == null) return;
        if (!ConfirmDelete("Package", item.displayName)) return;
        _so.Update();
        _so.FindProperty("Packages").DeleteArrayElementAtIndex(item.PackageIndex);
        Undo.RecordObject(_so.targetObject, "Delete Package");
        _so.ApplyModifiedProperties();
        Reload();
    }

    private void DeleteGroup(CollectorTreeViewItem item)
    {
        if (_so == null) return;
        if (!ConfirmDelete("Group", item.displayName)) return;
        _so.Update();
        _so.FindProperty(
            string.Concat("Packages.Array.data[", item.PackageIndex, "].Groups"))
            .DeleteArrayElementAtIndex(item.GroupIndex);
        Undo.RecordObject(_so.targetObject, "Delete Group");
        _so.ApplyModifiedProperties();
        Reload();
    }

    private void DeleteCollector(CollectorTreeViewItem item)
    {
        if (_so == null) return;
        if (!ConfirmDelete("Collector", item.displayName)) return;
        _so.Update();
        _so.FindProperty(
            string.Concat("Packages.Array.data[", item.PackageIndex,
                "].Groups.Array.data[", item.GroupIndex, "].Collectors"))
            .DeleteArrayElementAtIndex(item.CollectorIndex);
        Undo.RecordObject(_so.targetObject, "Delete Collector");
        _so.ApplyModifiedProperties();
        Reload();
    }

    #endregion

    #region Duplicate Operations

    private void DuplicatePackage(CollectorTreeViewItem item)
    {
        if (_so == null) return;
        _so.Update();
        var packages = _so.FindProperty("Packages");
        packages.InsertArrayElementAtIndex(packages.arraySize);
        var dest = packages.GetArrayElementAtIndex(packages.arraySize - 1);
        CopySerializedProperty(
            packages.GetArrayElementAtIndex(item.PackageIndex), dest,
            new[] { "PackageName", "SharePolicy", "Groups" });

        Undo.RecordObject(_so.targetObject, "Duplicate Package");
        _so.ApplyModifiedProperties();
        Reload();
    }

    private void DuplicateGroup(CollectorTreeViewItem item)
    {
        if (_so == null) return;
        _so.Update();
        var groups = _so.FindProperty(
            string.Concat("Packages.Array.data[", item.PackageIndex, "].Groups"));
        groups.InsertArrayElementAtIndex(groups.arraySize);
        var dest = groups.GetArrayElementAtIndex(groups.arraySize - 1);
        CopySerializedProperty(
            groups.GetArrayElementAtIndex(item.GroupIndex), dest,
            new[] { "GroupName", "Enabled", "Labels", "Collectors" });

        Undo.RecordObject(_so.targetObject, "Duplicate Group");
        _so.ApplyModifiedProperties();
        Reload();
    }

    private void DuplicateCollector(CollectorTreeViewItem item)
    {
        if (_so == null) return;
        _so.Update();
        var cols = _so.FindProperty(
            string.Concat("Packages.Array.data[", item.PackageIndex,
                "].Groups.Array.data[", item.GroupIndex, "].Collectors"));
        cols.InsertArrayElementAtIndex(cols.arraySize);
        var dest = cols.GetArrayElementAtIndex(cols.arraySize - 1);
        CopySerializedProperty(
            cols.GetArrayElementAtIndex(item.CollectorIndex), dest,
            new[] { "CollectPath", "CollectorType", "ForcePayloadKind", "AddressRuleName",
                    "PackRuleName", "FilterRuleName", "GroupRuleName", "Labels", "IgnorePatterns" });

        Undo.RecordObject(_so.targetObject, "Duplicate Collector");
        _so.ApplyModifiedProperties();
        Reload();
    }

    private static void CopySerializedProperty(SerializedProperty src, SerializedProperty dst, string[] fields)
    {
        for (int i = 0; i < fields.Length; i++)
        {
            SerializedProperty srcField = src.FindPropertyRelative(fields[i]);
            SerializedProperty dstField = dst.FindPropertyRelative(fields[i]);
            if (srcField != null && dstField != null)
            {
                switch (srcField.propertyType)
                {
                    case SerializedPropertyType.String:
                        dstField.stringValue = srcField.stringValue;
                        break;
                    case SerializedPropertyType.Integer:
                    case SerializedPropertyType.Enum:
                        dstField.intValue = srcField.intValue;
                        break;
                    case SerializedPropertyType.Boolean:
                        dstField.boolValue = srcField.boolValue;
                        break;
                    case SerializedPropertyType.Generic:
                        // Deep copy arrays/lists via CopySerializedValue
                        if (srcField.isArray)
                        {
                            dstField.ClearArray();
                            for (int j = 0; j < srcField.arraySize; j++)
                            {
                                dstField.InsertArrayElementAtIndex(j);
                                // Copy child fields of list elements (value types)
                                SerializedProperty srcElem = srcField.GetArrayElementAtIndex(j);
                                SerializedProperty dstElem = dstField.GetArrayElementAtIndex(j);
                                if (srcElem.propertyType == SerializedPropertyType.String)
                                    dstElem.stringValue = srcElem.stringValue;
                            }
                        }
                        else
                        {
                            // Nested object — copy child properties by iterating visible children
                            CopyAllVisibleChildren(srcField, dstField);
                        }
                        break;
                    default:
                        // Unhandled property type — skip (none of our data uses this)
                        break;
                }
            }
        }
    }

    private static void CopyAllVisibleChildren(SerializedProperty src, SerializedProperty dst)
    {
        SerializedProperty srcChild = src.Copy();
        SerializedProperty dstChild = dst.Copy();
        SerializedProperty srcEnd = src.GetEndProperty();
        dstChild.NextVisible(true);

        bool entered = srcChild.NextVisible(true);
        while (entered && !SerializedProperty.EqualContents(srcChild, srcEnd))
        {
            switch (srcChild.propertyType)
            {
                case SerializedPropertyType.String:
                    dstChild.stringValue = srcChild.stringValue;
                    break;
                case SerializedPropertyType.Integer:
                case SerializedPropertyType.Enum:
                    dstChild.intValue = srcChild.intValue;
                    break;
                case SerializedPropertyType.Boolean:
                    dstChild.boolValue = srcChild.boolValue;
                    break;
                case SerializedPropertyType.Float:
                    dstChild.floatValue = srcChild.floatValue;
                    break;
            }

            bool hasNext = srcChild.NextVisible(false);
            if (hasNext && !SerializedProperty.EqualContents(srcChild, srcEnd))
                dstChild.NextVisible(false);
            else
                break;
        }
    }

    #endregion

    #endregion

    #region Public API

    /// <summary>返回当前选中的 TreeViewItem，null 表示无选中或选中 root</summary>
    public CollectorTreeViewItem GetSelectedItem()
    {
        if (state.selectedIDs.Count == 0)
            return null;

        int selectedId = -1;
        using (var enumerator = state.selectedIDs.GetEnumerator())
        {
            if (enumerator.MoveNext())
                selectedId = enumerator.Current;
        }

        for (int i = 0; i < _allItems.Count; i++)
        {
            if (_allItems[i].id == selectedId)
                return _allItems[i];
        }

        return null;
    }

    /// <summary>从 SO 全量刷新 TreeView</summary>
    public void RefreshData(CollectorSetting setting)
    {
        _setting = setting;
        _savedSelection = new TreeViewSelection(state);
        Reload();
        _savedSelection?.Restore(state);
    }

    public IReadOnlyList<CollectorTreeViewItem> GetAllItems() => _allItems;

    #endregion

    #region Nested Types

    /// <summary>保存/恢复 TreeView 选中状态</summary>
    private class TreeViewSelection
    {
        private readonly List<int> _ids;

        public TreeViewSelection(TreeViewState state)
        {
            _ids = new List<int>(state.selectedIDs);
        }

        public void Restore(TreeViewState state)
        {
            state.selectedIDs = new List<int>(_ids);
        }
    }

    #endregion
}

/// <summary>
/// TreeView 节点数据模型 —— 携带三层索引，支持拖拽和右键菜单。
/// </summary>
public class CollectorTreeViewItem : TreeViewItem
{
    public enum NodeType { Package, Group, Collector }

    public NodeType Type;
    public int PackageIndex;
    public int GroupIndex = -1;
    public int CollectorIndex = -1;
}
