using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// ScriptableObject Addressable 标签管理窗口。
/// 使用 UI Toolkit 管理容器、标签以及批量应用/清除标签操作。
/// </summary>
public class SOAddressableTagger : EditorWindow
{
    private ScriptObjectDataBase _soDatabase;
    private readonly List<ScriptObjectContainer> _additionalContainers = new();
    private readonly Dictionary<ScriptObjectContainer, bool> _containerFoldouts = new();
    private string _newLabel = "";
    private ScriptObjectContainer _newContainer;
    private VisualElement _root;
    private VisualElement _containerRoot;
    private Label _summaryLabel;

    [MenuItem("Tools/Addressables/SO标签管理器", false, 101)]
    public static void ShowWindow()
    {
        GetWindow<SOAddressableTagger>("SO标签管理器");
    }

    private void CreateGUI()
    {
        _root = rootVisualElement;
        _root.style.paddingLeft = 12f;
        _root.style.paddingRight = 12f;
        _root.style.paddingTop = 12f;
        _root.style.paddingBottom = 12f;
        _root.style.flexDirection = FlexDirection.Column;

        BuildWindow();
    }

    /// <summary>
    /// 重建整个窗口布局。
    /// </summary>
    private void BuildWindow()
    {
        _root.Clear();

        Label title = new Label("ScriptableObject Addressable标签管理器");
        title.style.unityFontStyleAndWeight = FontStyle.Bold;
        title.style.marginBottom = 10f;
        _root.Add(title);

        BuildDatabaseRow();
        BuildBatchRow();
        BuildAdditionalContainerRow();

        _containerRoot = new VisualElement();
        _containerRoot.style.flexGrow = 1f;
        _root.Add(_containerRoot);

        _summaryLabel = new Label();
        _summaryLabel.style.marginTop = 8f;
        _root.Add(_summaryLabel);

        RebuildContainers();
    }

    /// <summary>
    /// 绘制数据库选择与创建区。
    /// </summary>
    private void BuildDatabaseRow()
    {
        VisualElement row = CreateRow();
        ObjectField databaseField = new ObjectField("SO数据库")
        {
            objectType = typeof(ScriptObjectDataBase),
            allowSceneObjects = false,
            value = _soDatabase
        };
        databaseField.style.flexGrow = 1f;
        databaseField.RegisterValueChangedCallback(evt =>
        {
            _soDatabase = evt.newValue as ScriptObjectDataBase;
            RebuildContainers();
        });
        row.Add(databaseField);

        Button createButton = new Button(CreateNewDatabase) { text = "创建新数据库" };
        createButton.style.width = 120f;
        row.Add(createButton);
        _root.Add(row);
    }

    /// <summary>
    /// 绘制批量应用/清空标签的操作区。
    /// </summary>
    private void BuildBatchRow()
    {
        VisualElement row = CreateRow();
        Button applyButton = new Button(ApplyAllLabels) { text = "应用所有标签" };
        applyButton.style.height = 30f;
        applyButton.style.flexGrow = 1f;
        row.Add(applyButton);

        Button clearButton = new Button(ClearAllLabels) { text = "清除所有标签" };
        clearButton.style.height = 30f;
        clearButton.style.flexGrow = 1f;
        row.Add(clearButton);
        _root.Add(row);
    }

    /// <summary>
    /// 绘制额外容器添加区。
    /// </summary>
    private void BuildAdditionalContainerRow()
    {
        Label header = new Label("额外容器");
        header.style.unityFontStyleAndWeight = FontStyle.Bold;
        header.style.marginTop = 8f;
        _root.Add(header);

        VisualElement row = CreateRow();
        ObjectField containerField = new ObjectField
        {
            objectType = typeof(ScriptObjectContainer),
            allowSceneObjects = false,
            value = _newContainer
        };
        containerField.style.flexGrow = 1f;
        containerField.RegisterValueChangedCallback(evt => _newContainer = evt.newValue as ScriptObjectContainer);
        row.Add(containerField);

        Button addButton = new Button(() =>
        {
            if (_newContainer != null && !_additionalContainers.Contains(_newContainer))
            {
                _additionalContainers.Add(_newContainer);
                _newContainer = null;
                BuildWindow();
            }
        })
        { text = "添加" };
        addButton.style.width = 60f;
        row.Add(addButton);
        _root.Add(row);
    }

    /// <summary>
    /// 根据数据库与额外容器列表重建容器展示区。
    /// </summary>
    private void RebuildContainers()
    {
        if (_containerRoot == null)
            return;

        _containerRoot.Clear();
        List<ScriptObjectContainer> allContainers = GetAllContainers();

        if (allContainers.Count == 0)
        {
            HelpBox empty = new HelpBox("没有找到任何SO容器", HelpBoxMessageType.Info);
            empty.style.marginTop = 10f;
            _containerRoot.Add(empty);
            UpdateSummary(allContainers);
            return;
        }

        Label listHeader = new Label($"容器列表 ({allContainers.Count}个)");
        listHeader.style.unityFontStyleAndWeight = FontStyle.Bold;
        listHeader.style.marginTop = 10f;
        _containerRoot.Add(listHeader);

        ScrollView scroll = new ScrollView();
        scroll.style.flexGrow = 1f;
        scroll.style.minHeight = 120f;
        _containerRoot.Add(scroll);

        foreach (ScriptObjectContainer container in allContainers)
        {
            if (container != null)
                scroll.Add(CreateContainerElement(container));
        }

        UpdateSummary(allContainers);
    }

    /// <summary>
    /// 创建单个容器 Card，包含折叠区与快捷操作按钮。
    /// </summary>
    private VisualElement CreateContainerElement(ScriptObjectContainer container)
    {
        VisualElement card = new VisualElement();
        card.style.borderTopWidth = 1f;
        card.style.borderRightWidth = 1f;
        card.style.borderBottomWidth = 1f;
        card.style.borderLeftWidth = 1f;
        card.style.borderTopColor = new Color(0.35f, 0.35f, 0.35f);
        card.style.borderRightColor = new Color(0.35f, 0.35f, 0.35f);
        card.style.borderBottomColor = new Color(0.35f, 0.35f, 0.35f);
        card.style.borderLeftColor = new Color(0.35f, 0.35f, 0.35f);
        card.style.marginTop = 6f;
        card.style.paddingLeft = 8f;
        card.style.paddingRight = 8f;
        card.style.paddingTop = 6f;
        card.style.paddingBottom = 6f;

        VisualElement row = CreateRow();
        bool expanded = !_containerFoldouts.TryGetValue(container, out bool storedExpanded) || storedExpanded;
        Foldout foldout = new Foldout
        {
            text = $"{container.name} ({container.soAssets.Count}个SO)",
            value = expanded
        };
        foldout.style.flexGrow = 1f;
        foldout.RegisterValueChangedCallback(evt => _containerFoldouts[container] = evt.newValue);
        row.Add(foldout);

        Button applyButton = new Button(() => ApplyContainerLabels(container)) { text = "应用标签" };
        applyButton.style.width = 80f;
        row.Add(applyButton);

        Button removeButton = new Button(() =>
        {
            if (_additionalContainers.Remove(container))
            {
                _containerFoldouts.Remove(container);
                RebuildContainers();
            }
        })
        { text = "移除" };
        removeButton.style.width = 60f;
        removeButton.SetEnabled(_additionalContainers.Contains(container));
        row.Add(removeButton);

        card.Add(row);
        foldout.Add(CreateContainerContent(container));
        return card;
    }

    /// <summary>
    /// 创建容器详情内容，包括标签编辑与 SO 列表编辑。
    /// </summary>
    private VisualElement CreateContainerContent(ScriptObjectContainer container)
    {
        VisualElement content = new VisualElement();
        content.style.marginTop = 6f;

        Label labelHeader = new Label("地址标签:");
        labelHeader.style.unityFontStyleAndWeight = FontStyle.Bold;
        content.Add(labelHeader);

        for (int i = 0; i < container.addressableLabels.Count; i++)
        {
            int index = i;
            VisualElement row = CreateRow();
            TextField field = new TextField { value = container.addressableLabels[index] };
            field.style.flexGrow = 1f;
            field.RegisterValueChangedCallback(evt =>
            {
                container.addressableLabels[index] = evt.newValue;
                EditorUtility.SetDirty(container);
            });
            row.Add(field);

            Button remove = new Button(() =>
            {
                container.addressableLabels.RemoveAt(index);
                EditorUtility.SetDirty(container);
                RebuildContainers();
            })
            { text = "×" };
            remove.style.width = 25f;
            row.Add(remove);
            content.Add(row);
        }

        VisualElement addLabelRow = CreateRow();
        TextField newLabelField = new TextField("新标签") { value = _newLabel };
        newLabelField.style.flexGrow = 1f;
        newLabelField.RegisterValueChangedCallback(evt => _newLabel = evt.newValue);
        addLabelRow.Add(newLabelField);

        Button addLabel = new Button(() =>
        {
            if (!string.IsNullOrEmpty(_newLabel) && !container.addressableLabels.Contains(_newLabel))
            {
                container.addressableLabels.Add(_newLabel);
                _newLabel = "";
                EditorUtility.SetDirty(container);
                RebuildContainers();
            }
        })
        { text = "添加" };
        addLabel.style.width = 60f;
        addLabelRow.Add(addLabel);
        content.Add(addLabelRow);

        Label soHeader = new Label($"SO资源列表 ({container.soAssets.Count}个)");
        soHeader.style.unityFontStyleAndWeight = FontStyle.Bold;
        soHeader.style.marginTop = 10f;
        content.Add(soHeader);

        for (int i = 0; i < container.soAssets.Count; i++)
        {
            int index = i;
            VisualElement row = CreateRow();
            ObjectField soField = new ObjectField
            {
                objectType = typeof(ScriptableObject),
                allowSceneObjects = false,
                value = container.soAssets[index]
            };
            soField.style.flexGrow = 1f;
            soField.RegisterValueChangedCallback(evt =>
            {
                container.soAssets[index] = evt.newValue as ScriptableObject;
                EditorUtility.SetDirty(container);
            });
            row.Add(soField);

            Button remove = new Button(() =>
            {
                container.soAssets.RemoveAt(index);
                EditorUtility.SetDirty(container);
                RebuildContainers();
            })
            { text = "×" };
            remove.style.width = 25f;
            row.Add(remove);
            content.Add(row);
        }

        VisualElement soButtons = CreateRow();
        Button addSO = new Button(() => AddSOToContainer(container)) { text = "添加SO" };
        addSO.style.flexGrow = 1f;
        soButtons.Add(addSO);

        Button clearSO = new Button(() =>
        {
            container.soAssets.Clear();
            EditorUtility.SetDirty(container);
            RebuildContainers();
        })
        { text = "清空SO" };
        clearSO.style.flexGrow = 1f;
        soButtons.Add(clearSO);
        content.Add(soButtons);

        return content;
    }

    /// <summary>
    /// 创建横向行容器，供窗口各操作区复用。
    /// </summary>
    private static VisualElement CreateRow()
    {
        VisualElement row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        row.style.alignItems = Align.Center;
        row.style.marginBottom = 6f;
        return row;
    }

    /// <summary>
    /// 更新底部汇总文本。
    /// </summary>
    private void UpdateSummary(List<ScriptObjectContainer> allContainers)
    {
        if (_summaryLabel == null)
            return;

        int totalSOs = allContainers.Sum(c => c.soAssets.Count);
        int totalLabels = allContainers.Sum(c => c.addressableLabels.Count);
        _summaryLabel.text = $"总计: {allContainers.Count}个容器, {totalSOs}个SO资源, {totalLabels}个标签";
    }

    /// <summary>
    /// 合并数据库中的容器与手动追加容器，并去重。
    /// </summary>
    private List<ScriptObjectContainer> GetAllContainers()
    {
        List<ScriptObjectContainer> allContainers = new List<ScriptObjectContainer>();

        if (_soDatabase != null)
            allContainers.AddRange(_soDatabase.groups.Where(c => c != null));

        allContainers.AddRange(_additionalContainers.Where(c => c != null));
        return allContainers.Distinct().ToList();
    }

    /// <summary>
    /// 创建新的 ScriptObjectDataBase 资产。
    /// </summary>
    private void CreateNewDatabase()
    {
        string path = EditorUtility.SaveFilePanelInProject(
            "创建SO数据库",
            "SODataBase",
            "asset",
            "选择保存SO数据库的位置"
        );

        if (!string.IsNullOrEmpty(path))
        {
            ScriptObjectDataBase newDatabase = CreateInstance<ScriptObjectDataBase>();
            AssetDatabase.CreateAsset(newDatabase, path);
            AssetDatabase.SaveAssets();
            _soDatabase = newDatabase;
            Selection.activeObject = newDatabase;
            BuildWindow();
        }
    }

    /// <summary>
    /// 通过文件选择器向容器追加 ScriptableObject 资产。
    /// </summary>
    private void AddSOToContainer(ScriptObjectContainer container)
    {
        string selectedPath = EditorUtility.OpenFilePanel(
            "选择ScriptableObject",
            Application.dataPath,
            "asset"
        );

        if (string.IsNullOrEmpty(selectedPath))
            return;

        if (FYAssetPathUtility.TryMakeAssetPath(selectedPath, Application.dataPath, out string assetPath))
        {
            ScriptableObject soAsset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(assetPath);

            if (soAsset != null && !container.soAssets.Contains(soAsset))
            {
                container.soAssets.Add(soAsset);
                EditorUtility.SetDirty(container);
                RebuildContainers();
            }
        }
    }

    /// <summary>
    /// 将单个容器中的 Addressable 标签批量同步到其所有 SO 资源。
    /// </summary>
    private void ApplyContainerLabels(ScriptObjectContainer container)
    {
        if (container.soAssets.Count == 0)
        {
            EditorUtility.DisplayDialog("提示", $"容器 '{container.name}' 中没有SO资源", "确定");
            return;
        }

        AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null)
        {
            EditorUtility.DisplayDialog("错误", "未找到Addressable设置，请先初始化Addressables", "确定");
            return;
        }

        int successCount = 0;
        int failCount = 0;

        foreach (ScriptableObject soAsset in container.soAssets)
        {
            if (soAsset == null)
                continue;

            string assetPath = AssetDatabase.GetAssetPath(soAsset);
            string guid = AssetDatabase.AssetPathToGUID(assetPath);
            AddressableAssetEntry entry = settings.FindAssetEntry(guid);

            if (entry == null)
            {
                Debug.LogError($"[TagError] 资源 '{soAsset.name}' ({assetPath}) 没有Addressable条目。请确保它已勾选Addressable。");
                failCount++;
                continue;
            }

            List<string> currentLabels = entry.labels.ToList();
            foreach (string label in currentLabels)
                entry.SetLabel(label, false);

            foreach (string label in container.addressableLabels)
            {
                if (string.IsNullOrEmpty(label))
                    continue;

                if (!settings.GetLabels().Contains(label))
                    settings.AddLabel(label);

                entry.SetLabel(label, true);
            }
            successCount++;
        }

        settings.SetDirty(AddressableAssetSettings.ModificationEvent.LabelAdded, null, true);
        AssetDatabase.SaveAssets();

        string msg = $"成功: {successCount} 个\n失败/未找到条目: {failCount} 个\n（共 {container.soAssets.Count} 个）";
        if (failCount > 0)
            msg += "\n\n请查看Console获取失败详情。";

        EditorUtility.DisplayDialog("完成", msg, "确定");
    }

    /// <summary>
    /// 对全部容器执行批量标签应用。
    /// </summary>
    private void ApplyAllLabels()
    {
        List<ScriptObjectContainer> allContainers = GetAllContainers();

        if (allContainers.Count == 0)
        {
            EditorUtility.DisplayDialog("提示", "没有找到任何容器", "确定");
            return;
        }

        AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null)
        {
            EditorUtility.DisplayDialog("错误", "未找到Addressable设置，请先初始化Addressables", "确定");
            return;
        }

        int totalSOsProcessed = 0;
        int containersProcessed = 0;

        foreach (ScriptObjectContainer container in allContainers)
        {
            if (container == null || container.soAssets.Count == 0)
                continue;

            foreach (ScriptableObject soAsset in container.soAssets)
            {
                if (soAsset == null)
                    continue;

                string assetPath = AssetDatabase.GetAssetPath(soAsset);
                string guid = AssetDatabase.AssetPathToGUID(assetPath);
                AddressableAssetEntry entry = settings.FindAssetEntry(guid);

                if (entry == null)
                {
                    Debug.LogWarning($"[TagWarning] 跳过资源 '{soAsset.name}'，因为它不在Addressable系统中。");
                    continue;
                }

                List<string> currentLabels = entry.labels.ToList();
                foreach (string label in currentLabels)
                    entry.SetLabel(label, false);

                foreach (string label in container.addressableLabels)
                {
                    if (string.IsNullOrEmpty(label))
                        continue;

                    if (!settings.GetLabels().Contains(label))
                        settings.AddLabel(label);

                    entry.SetLabel(label, true);
                }

                totalSOsProcessed++;
            }

            containersProcessed++;
        }

        settings.SetDirty(AddressableAssetSettings.ModificationEvent.LabelAdded, null, true);
        AssetDatabase.SaveAssets();

        EditorUtility.DisplayDialog("完成",
            $"成功为 {containersProcessed} 个容器中的 {totalSOsProcessed} 个SO资源应用了标签", "确定");
    }

    /// <summary>
    /// 清除所有容器关联 SO 资源上的 Addressable 标签。
    /// </summary>
    private void ClearAllLabels()
    {
        if (!EditorUtility.DisplayDialog("确认",
                "确定要清除所有SO资源的Addressable标签吗？此操作不可撤销。", "确定", "取消"))
        {
            return;
        }

        List<ScriptObjectContainer> allContainers = GetAllContainers();
        AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;

        if (settings == null)
            return;

        int totalClearedCount = 0;

        foreach (ScriptObjectContainer container in allContainers)
        {
            if (container == null || container.soAssets.Count == 0)
                continue;

            foreach (ScriptableObject soAsset in container.soAssets)
            {
                if (soAsset == null)
                    continue;

                string assetPath = AssetDatabase.GetAssetPath(soAsset);
                string guid = AssetDatabase.AssetPathToGUID(assetPath);
                AddressableAssetEntry entry = settings.FindAssetEntry(guid);

                if (entry == null)
                    continue;

                List<string> labelsToRemove = entry.labels.ToList();
                foreach (string label in labelsToRemove)
                {
                    entry.SetLabel(label, false);
                    totalClearedCount++;
                }
            }
        }

        settings.SetDirty(AddressableAssetSettings.ModificationEvent.LabelRemoved, null, true);
        AssetDatabase.SaveAssets();

        EditorUtility.DisplayDialog("完成",
            $"成功清除了 {totalClearedCount} 个标签", "确定");
    }
}
