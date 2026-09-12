using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// 将 Project 中选中的资源批量加入目标 Collector Group 的 UI Toolkit 窗口。
/// </summary>
/// <remarks>
/// 采集类型由路径自身推断：目录生成 Folder Collector，单个文件生成 File Collector。
/// </remarks>
public sealed class CollectorTargetPickerWindow : EditorWindow
{
    private string[] _assetPaths = Array.Empty<string>();
    private Action _onApplied;
    private AssetCollectionSetting _setting;
    private int _selectedGroupIndex;

    public static void Show(string[] assetPaths, Action onApplied)
    {
        CollectorTargetPickerWindow window = CreateInstance<CollectorTargetPickerWindow>();
        window.titleContent = new GUIContent("Add to Group");
        window.minSize = new Vector2(320f, 220f);
        window._assetPaths = assetPaths ?? Array.Empty<string>();
        window._onApplied = onApplied;
        window.LoadSetting();
        window.ShowUtility();
    }

    public void CreateGUI()
    {
        Build();
    }

    /// <summary>
    /// 按当前 Group 选择状态重建整个弹窗内容。
    /// </summary>
    private void Build()
    {
        rootVisualElement.Clear();
        rootVisualElement.style.paddingLeft = 8f;
        rootVisualElement.style.paddingRight = 8f;
        rootVisualElement.style.paddingTop = 8f;
        rootVisualElement.style.paddingBottom = 8f;

        rootVisualElement.Add(BuildPipelineUI.Header("Add to Group"));

        if (_setting == null || _setting.Groups == null || _setting.Groups.Count == 0)
        {
            rootVisualElement.Add(BuildPipelineUI.SmallText("AssetCollectionSetting 缺失或未配置 Group。"));
            return;
        }

        string[] groupNames = GetGroupNames();
        var groupPopup = new PopupField<string>(new List<string>(groupNames), Mathf.Clamp(_selectedGroupIndex, 0, groupNames.Length - 1));
        groupPopup.label = "Group";
        groupPopup.RegisterValueChangedCallback(evt => _selectedGroupIndex = Array.IndexOf(groupNames, evt.newValue));
        rootVisualElement.Add(groupPopup);

        rootVisualElement.Add(BuildPipelineUI.Header("Assets"));
        for (int i = 0; i < _assetPaths.Length; i++)
            rootVisualElement.Add(BuildPipelineUI.SmallText(_assetPaths[i]));

        VisualElement footer = new VisualElement();
        footer.style.flexDirection = FlexDirection.Row;
        footer.style.justifyContent = Justify.FlexEnd;
        footer.style.marginTop = 8f;
        footer.Add(new Button(Close) { text = "Cancel" });
        footer.Add(new Button(ApplySelection) { text = "Add" });
        rootVisualElement.Add(footer);
    }

    private void LoadSetting()
    {
        _setting = AssetDatabase.LoadAssetAtPath<AssetCollectionSetting>(FYAssetABSettings.Instance.AssetCollectionSettingPath);
        _selectedGroupIndex = 0;
    }

    private string[] GetGroupNames()
    {
        List<string> names = new List<string>();
        for (int i = 0; i < _setting.Groups.Count; i++)
        {
            string groupName = _setting.Groups[i]?.GroupName;
            names.Add(string.IsNullOrEmpty(groupName) ? "(unnamed group)" : groupName);
        }
        return names.ToArray();
    }

    /// <summary>
    /// 将当前弹窗中的资源选择写入目标 Group。
    /// 已被其他 Collector 收录的资源会被跳过，避免重复收录。
    /// </summary>
    private void ApplySelection()
    {
        if (_setting?.Groups == null || _selectedGroupIndex < 0 || _selectedGroupIndex >= _setting.Groups.Count)
            return;

        AssetCollectionGroup group = _setting.Groups[_selectedGroupIndex];
        group.Collectors ??= new List<Collector>();

        for (int i = 0; i < _assetPaths.Length; i++)
        {
            CollectorMutationUtility.AddToGroup(_setting, group, _assetPaths[i]);
        }

        EditorUtility.SetDirty(_setting);
        AssetDatabase.SaveAssets();
        _onApplied?.Invoke();
        Close();
    }
}
