using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Collector 目标选择弹窗 —— 选择 Package / Group，并为指定资产创建 File Collector。
/// </summary>
public sealed class CollectorTargetPickerPopup : PopupWindowContent
{
    private readonly string[] _assetPaths;
    private readonly Action _onApplied;

    private CollectorSetting _setting;
    private int _selectedPackageIndex;
    private int _selectedGroupIndex;
    private ECollectorType _collectorType = ECollectorType.Main;
    private EForcePayloadKind _forcePayloadKind = EForcePayloadKind.Auto;

    public CollectorTargetPickerPopup(string[] assetPaths, Action onApplied)
    {
        _assetPaths = assetPaths ?? Array.Empty<string>();
        _onApplied = onApplied;
        LoadSetting();
    }

    public override Vector2 GetWindowSize()
    {
        return new Vector2(320f, 220f);
    }

    public override void OnGUI(Rect rect)
    {
        EditorGUILayout.LabelField("Add To Collector Group", EditorStyles.boldLabel);
        EditorGUILayout.Space(4f);

        if (_setting == null || _setting.Packages == null || _setting.Packages.Count == 0)
        {
            EditorGUILayout.HelpBox("CollectorSetting is missing or has no Package configured.", MessageType.Warning);
            return;
        }

        string[] packageNames = GetPackageNames();
        _selectedPackageIndex = EditorGUILayout.Popup("Package", Mathf.Clamp(_selectedPackageIndex, 0, packageNames.Length - 1), packageNames);

        string[] groupNames = GetGroupNames(_selectedPackageIndex);
        if (groupNames.Length == 0)
        {
            EditorGUILayout.HelpBox("Selected Package has no Group. Create one in CollectorSettingPanel first.", MessageType.Warning);
            return;
        }

        _selectedGroupIndex = EditorGUILayout.Popup("Group", Mathf.Clamp(_selectedGroupIndex, 0, groupNames.Length - 1), groupNames);
        _collectorType = (ECollectorType)EditorGUILayout.EnumPopup("Collector Type", _collectorType);
        _forcePayloadKind = (EForcePayloadKind)EditorGUILayout.EnumPopup("Payload", _forcePayloadKind);

        EditorGUILayout.Space(6f);
        EditorGUILayout.LabelField("Assets", EditorStyles.boldLabel);
        for (int i = 0; i < _assetPaths.Length; i++)
            EditorGUILayout.LabelField("• " + _assetPaths[i], EditorStyles.miniLabel);

        EditorGUILayout.Space(8f);
        EditorGUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("Cancel", GUILayout.Width(72f)))
            editorWindow.Close();
        if (GUILayout.Button("Add", GUILayout.Width(72f)))
            ApplySelection();
        EditorGUILayout.EndHorizontal();
    }

    private void LoadSetting()
    {
        CollectorDataMigrator.EnsureDataFolder();
        CollectorDataMigrator.MigrateFromLegacyPath();
        _setting = AssetDatabase.LoadAssetAtPath<CollectorSetting>(FYAssetConstants.COLLECTOR_SETTING_ASSET_PATH);
        _selectedPackageIndex = 0;
        _selectedGroupIndex = 0;
    }

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

    private void ApplySelection()
    {
        if (_setting == null || _selectedPackageIndex < 0 || _selectedPackageIndex >= _setting.Packages.Count)
            return;

        CollectorPackage package = _setting.Packages[_selectedPackageIndex];
        if (package?.Groups == null || _selectedGroupIndex < 0 || _selectedGroupIndex >= package.Groups.Count)
            return;

        CollectorGroup group = package.Groups[_selectedGroupIndex];
        if (group.Collectors == null)
            group.Collectors = new List<Collector>();

        Undo.RecordObject(_setting, "Add Asset To Collector Group");

        for (int i = 0; i < _assetPaths.Length; i++)
        {
            string assetPath = NormalizePath(_assetPaths[i]);
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
                AddressRuleName = FYAssetConstants.RULE_ADDRESS_BY_FILE_NAME,
                PackRuleName = isFolder ? FYAssetConstants.RULE_PACK_BY_COLLECT_PATH : FYAssetConstants.RULE_PACK_SEPARATELY,
                FilterRuleName = FYAssetConstants.RULE_COLLECT_ALL,
                GroupRuleName = FYAssetConstants.RULE_GROUP_ALL,
                Labels = new List<string>(),
                IgnorePatterns = new List<string>()
            });
        }

        EditorUtility.SetDirty(_setting);
        AssetDatabase.SaveAssets();
        CollectorReverseIndex.Instance.MarkDirty();
        _onApplied?.Invoke();
        editorWindow.Close();
    }

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrEmpty(path))
            return string.Empty;

        return path.Replace('\\', '/').TrimEnd('/');
    }
}
