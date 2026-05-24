using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// 将 Project 中选中的资源批量加入目标 Collector Group 的 UI Toolkit 窗口。
/// </summary>
public sealed class CollectorTargetPickerWindow : EditorWindow
{
    private string[] _assetPaths = Array.Empty<string>();
    private Action _onApplied;
    private CollectorSetting _setting;
    private int _selectedPackageIndex;
    private int _selectedGroupIndex;
    private ECollectorType _collectorType = ECollectorType.Main;
    private EForcePayloadKind _forcePayloadKind = EForcePayloadKind.Auto;
    private static readonly List<string> ManualCollectorTypeNames = new List<string>
    {
        ECollectorType.Main.ToString(),
        ECollectorType.Static.ToString(),
        ECollectorType.Depend.ToString()
    };

    public static void Show(string[] assetPaths, Action onApplied)
    {
        CollectorTargetPickerWindow window = CreateInstance<CollectorTargetPickerWindow>();
        window.titleContent = new GUIContent("加入 Group");
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
    /// 按当前 Package / Group 选择状态重建整个弹窗内容。
    /// </summary>
    private void Build()
    {
        rootVisualElement.Clear();
        rootVisualElement.style.paddingLeft = 8f;
        rootVisualElement.style.paddingRight = 8f;
        rootVisualElement.style.paddingTop = 8f;
        rootVisualElement.style.paddingBottom = 8f;

        rootVisualElement.Add(BuildPipelineUI.Header("加入 Group"));

        if (_setting == null || _setting.Packages == null || _setting.Packages.Count == 0)
        {
            rootVisualElement.Add(BuildPipelineUI.SmallText("CollectorSetting 缺失或未配置 Package。"));
            return;
        }

        string[] packageNames = GetPackageNames();
        var packagePopup = new PopupField<string>(new List<string>(packageNames), Mathf.Clamp(_selectedPackageIndex, 0, packageNames.Length - 1));
        packagePopup.label = "Package";
        packagePopup.RegisterValueChangedCallback(evt =>
        {
            _selectedPackageIndex = Array.IndexOf(packageNames, evt.newValue);
            _selectedGroupIndex = 0;
            Build();
        });
        rootVisualElement.Add(packagePopup);

        string[] groupNames = GetGroupNames(_selectedPackageIndex);
        if (groupNames.Length == 0)
        {
            rootVisualElement.Add(BuildPipelineUI.SmallText("选中的 Package 没有 Group。先到 CollectorSettingPanel 新建。"));
            return;
        }

        var groupPopup = new PopupField<string>(new List<string>(groupNames), Mathf.Clamp(_selectedGroupIndex, 0, groupNames.Length - 1));
        groupPopup.label = "Group";
        groupPopup.RegisterValueChangedCallback(evt => _selectedGroupIndex = Array.IndexOf(groupNames, evt.newValue));
        rootVisualElement.Add(groupPopup);

        var collectorType = new PopupField<string>("Type", ManualCollectorTypeNames, _collectorType.ToString());
        collectorType.RegisterValueChangedCallback(evt =>
        {
            if (Enum.TryParse(evt.newValue, out ECollectorType parsed) && parsed != ECollectorType.Implicit)
                _collectorType = parsed;
        });
        rootVisualElement.Add(collectorType);

        var payload = new EnumField("Payload", _forcePayloadKind);
        payload.RegisterValueChangedCallback(evt => _forcePayloadKind = (EForcePayloadKind)evt.newValue);
        rootVisualElement.Add(payload);

        rootVisualElement.Add(BuildPipelineUI.Header("Assets"));
        for (int i = 0; i < _assetPaths.Length; i++)
            rootVisualElement.Add(BuildPipelineUI.SmallText(_assetPaths[i]));

        VisualElement footer = new VisualElement();
        footer.style.flexDirection = FlexDirection.Row;
        footer.style.justifyContent = Justify.FlexEnd;
        footer.style.marginTop = 8f;
        footer.Add(new Button(Close) { text = "取消" });
        footer.Add(new Button(ApplySelection) { text = "加" });
        rootVisualElement.Add(footer);
    }

    /// <summary>
    /// 加载当前 CollectorSetting，并初始化默认选择项。
    /// </summary>
    private void LoadSetting()
    {
        CollectorDataMigrator.EnsureDataFolder();
        CollectorDataMigrator.MigrateFromAAPath();
        _setting = AssetDatabase.LoadAssetAtPath<CollectorSetting>(FYAssetSettings.Instance.CollectorSettingPath);
        _selectedPackageIndex = 0;
        _selectedGroupIndex = 0;
    }

    /// <summary>
    /// 获取 Package 下拉框显示名列表。
    /// </summary>
    private string[] GetPackageNames()
    {
        List<string> names = new List<string>();
        for (int i = 0; i < _setting.Packages.Count; i++)
        {
            string packageName = _setting.Packages[i]?.PackageName;
            names.Add(string.IsNullOrEmpty(packageName) ? "(unnamed package)" : packageName);
        }
        return names.ToArray();
    }

    /// <summary>
    /// 获取指定 Package 下的 Group 下拉框显示名列表。
    /// </summary>
    private string[] GetGroupNames(int packageIndex)
    {
        if (packageIndex < 0 || packageIndex >= _setting.Packages.Count)
            return Array.Empty<string>();

        CollectorPackage package = _setting.Packages[packageIndex];
        if (package?.Groups == null || package.Groups.Count == 0)
            return Array.Empty<string>();

        List<string> names = new List<string>();
        for (int i = 0; i < package.Groups.Count; i++)
        {
            string groupName = package.Groups[i]?.GroupName;
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
        if (_setting == null || _selectedPackageIndex < 0 || _selectedPackageIndex >= _setting.Packages.Count)
            return;

        CollectorPackage package = _setting.Packages[_selectedPackageIndex];
        if (package?.Groups == null || _selectedGroupIndex < 0 || _selectedGroupIndex >= package.Groups.Count)
            return;

        CollectorGroup group = package.Groups[_selectedGroupIndex];
        group.Collectors ??= new List<Collector>();

        Undo.RecordObject(_setting, "Add Asset To Collector Group");

        for (int i = 0; i < _assetPaths.Length; i++)
        {
            string assetPath = CollectorPathUtility.NormalizePath(_assetPaths[i]);
            if (string.IsNullOrEmpty(assetPath))
                continue;
            if (string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(assetPath)))
                continue;
            if (CollectorReverseIndex.Instance.IsAssetCollected(assetPath))
                continue;

            bool isFolder = AssetDatabase.IsValidFolder(assetPath);
            group.Collectors.Add(new Collector
            {
                CollectPath = assetPath,
                CollectPathType = isFolder ? ECollectPathType.Folder : ECollectPathType.File,
                CollectorType = _collectorType,
                ForcePayloadKind = _forcePayloadKind,
                AddressRuleName = FYAssetSettings.RULE_ADDRESS_BY_FILE_NAME,
                PackRuleName = isFolder ? FYAssetSettings.RULE_PACK_BY_COLLECT_PATH : FYAssetSettings.RULE_PACK_SEPARATELY,
                FilterRuleName = FYAssetSettings.RULE_COLLECT_ALL,
                GroupRuleName = FYAssetSettings.RULE_GROUP_ALL,
                Labels = new List<string>(),
                IgnorePatterns = new List<string>()
            });
        }

        EditorUtility.SetDirty(_setting);
        AssetDatabase.SaveAssets();
        CollectorReverseIndex.Instance.MarkDirty();
        _onApplied?.Invoke();
        Close();
    }
}
