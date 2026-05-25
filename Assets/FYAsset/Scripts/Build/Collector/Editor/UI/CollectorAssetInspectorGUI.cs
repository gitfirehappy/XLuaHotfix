using UnityEditor;
using UnityEngine;

/// <summary>
/// 资产 Inspector Header 扩展 —— 为单个资产提供 Collector 勾选入口。
/// </summary>
[InitializeOnLoad]
public static class CollectorAssetInspectorGUI
{
    static CollectorAssetInspectorGUI()
    {
        Editor.finishedDefaultHeaderGUI -= OnHeaderGUI;
        Editor.finishedDefaultHeaderGUI += OnHeaderGUI;
    }

    private static void OnHeaderGUI(Editor editor)
    {
        if (editor == null || editor.targets == null || editor.targets.Length != 1)
            return;

        Object target = editor.target;
        if (target == null)
            return;

        string assetPath = CollectorPathUtility.NormalizePath(AssetDatabase.GetAssetPath(target));
        if (string.IsNullOrEmpty(assetPath))
            return;
        if (string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(assetPath)))
            return;

        bool isFolder = AssetDatabase.IsValidFolder(assetPath);

        CollectorReverseIndex.Instance.TryGetCollector(assetPath, out CollectorReverseIndex.CollectorRef collectorRef);
        bool isCollected = CollectorReverseIndex.Instance.IsAssetCollected(assetPath);
        Collector collector = isCollected ? GetCollector(collectorRef) : null;
        bool canToggleOff = collector != null
            && ((isFolder && collector.CollectPathType == ECollectPathType.Folder) || (!isFolder && collector.CollectPathType == ECollectPathType.File))
            && string.Equals(CollectorPathUtility.NormalizePath(collector.CollectPath), assetPath, System.StringComparison.OrdinalIgnoreCase);

        GUILayout.Space(4f);
        GUILayout.BeginVertical(EditorStyles.helpBox);

        Rect rowRect = EditorGUILayout.GetControlRect(false, 20f);
        EditorGUI.BeginDisabledGroup(isCollected && !canToggleOff);
        bool newState = EditorGUI.ToggleLeft(rowRect, "Collected", isCollected, EditorStyles.boldLabel);
        EditorGUI.EndDisabledGroup();

        if (isCollected)
        {
            string packageName = GetPackageName(collectorRef);
            string groupName = GetGroupName(collectorRef);
            EditorGUILayout.LabelField("Package: " + packageName + "    Group: " + groupName, EditorStyles.miniLabel);

            if (!canToggleOff)
                EditorGUILayout.LabelField("Collected via a parent Folder collector. Remove it from the collector panel.", EditorStyles.wordWrappedMiniLabel);
        }
        else
        {
            string hint = isFolder
                ? "Not collected. Toggle on to add a Folder collector."
                : "Not collected. Toggle on to add a File collector.";
            EditorGUILayout.LabelField(hint, EditorStyles.miniLabel);
        }

        if (newState != isCollected)
        {
            if (newState)
            {
                CollectorTargetPickerWindow.Show(new[] { assetPath }, () =>
                {
                    CollectorReverseIndex.Instance.MarkDirty();
                    UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
                });
            }
            else if (canToggleOff)
            {
                RemoveCollector(collectorRef);
                CollectorReverseIndex.Instance.MarkDirty();
                UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
            }
        }

        GUILayout.EndVertical();
    }

    private static CollectorSetting LoadSetting()
    {
        return AssetDatabase.LoadAssetAtPath<CollectorSetting>(FYAssetBuildSettingsProvider.Shared.CollectorSettingPath);
    }

    private static Collector GetCollector(CollectorReverseIndex.CollectorRef collectorRef)
    {
        CollectorSetting setting = LoadSetting();
        if (setting?.Packages == null)
            return null;
        if (collectorRef.PackageIndex < 0 || collectorRef.PackageIndex >= setting.Packages.Count)
            return null;

        CollectorPackage package = setting.Packages[collectorRef.PackageIndex];
        if (package?.Groups == null || collectorRef.GroupIndex < 0 || collectorRef.GroupIndex >= package.Groups.Count)
            return null;

        CollectorGroup group = package.Groups[collectorRef.GroupIndex];
        if (group?.Collectors == null || collectorRef.CollectorIndex < 0 || collectorRef.CollectorIndex >= group.Collectors.Count)
            return null;

        return group.Collectors[collectorRef.CollectorIndex];
    }

    private static string GetPackageName(CollectorReverseIndex.CollectorRef collectorRef)
    {
        CollectorSetting setting = LoadSetting();
        if (setting?.Packages == null || collectorRef.PackageIndex < 0 || collectorRef.PackageIndex >= setting.Packages.Count)
            return "(unknown package)";

        string packageName = setting.Packages[collectorRef.PackageIndex]?.PackageName;
        return string.IsNullOrEmpty(packageName) ? "(unnamed package)" : packageName;
    }

    private static string GetGroupName(CollectorReverseIndex.CollectorRef collectorRef)
    {
        CollectorSetting setting = LoadSetting();
        if (setting?.Packages == null || collectorRef.PackageIndex < 0 || collectorRef.PackageIndex >= setting.Packages.Count)
            return "(unknown group)";

        CollectorPackage package = setting.Packages[collectorRef.PackageIndex];
        if (package?.Groups == null || collectorRef.GroupIndex < 0 || collectorRef.GroupIndex >= package.Groups.Count)
            return "(unknown group)";

        string groupName = package.Groups[collectorRef.GroupIndex]?.GroupName;
        return string.IsNullOrEmpty(groupName) ? "(unnamed group)" : groupName;
    }

    private static void RemoveCollector(CollectorReverseIndex.CollectorRef collectorRef)
    {
        CollectorSetting setting = LoadSetting();
        if (setting?.Packages == null || collectorRef.PackageIndex < 0 || collectorRef.PackageIndex >= setting.Packages.Count)
            return;

        CollectorPackage package = setting.Packages[collectorRef.PackageIndex];
        if (package?.Groups == null || collectorRef.GroupIndex < 0 || collectorRef.GroupIndex >= package.Groups.Count)
            return;

        CollectorGroup group = package.Groups[collectorRef.GroupIndex];
        if (group?.Collectors == null || collectorRef.CollectorIndex < 0 || collectorRef.CollectorIndex >= group.Collectors.Count)
            return;

        Undo.RecordObject(setting, "Remove Asset From Collector Group");
        group.Collectors.RemoveAt(collectorRef.CollectorIndex);
        EditorUtility.SetDirty(setting);
        AssetDatabase.SaveAssets();
    }

}
