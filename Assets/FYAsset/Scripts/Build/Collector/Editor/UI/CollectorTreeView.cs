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
            return item.Type != CollectorTreeViewItem.NodeType.Package; // Package 固定在根层级，不需要排序
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
        if (_so.ApplyModifiedProperties())
            CollectorReverseIndex.Instance.MarkDirty();

        Reload();
        SetSelection(new[] { target.id });
    }

    #endregion

    #region Context Menu (T6)
    // 右键上下文菜单功能预留（Add/Delete/Duplicate 节点），当前 base.OnGUI 已处理基础交互
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
