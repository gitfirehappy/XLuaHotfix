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

        CollectorMutationUtility.MembershipInfo membership = CollectorMutationUtility.GetMembership(assetPath);
        bool isCollected = membership.State == CollectorMutationUtility.CollectionState.DirectCollector ||
                           membership.State == CollectorMutationUtility.CollectionState.CoveredByFolderCollector;
        bool isIgnored = membership.State == CollectorMutationUtility.CollectionState.Ignored;
        bool canToggleOff = membership.State == CollectorMutationUtility.CollectionState.DirectCollector ||
                            (!isFolder && membership.State == CollectorMutationUtility.CollectionState.CoveredByFolderCollector);

        GUILayout.Space(4f);
        GUILayout.BeginVertical(EditorStyles.helpBox);

        Rect rowRect = EditorGUILayout.GetControlRect(false, 20f);
        EditorGUI.BeginDisabledGroup(isCollected && !canToggleOff);
        bool newState = EditorGUI.ToggleLeft(rowRect, isIgnored ? "Collected (Ignored)" : "Collected", isCollected, EditorStyles.boldLabel);
        EditorGUI.EndDisabledGroup();

        if (isCollected)
        {
            string groupName = GetGroupName(membership);
            EditorGUILayout.LabelField("Group: " + groupName, EditorStyles.miniLabel);

            if (membership.State == CollectorMutationUtility.CollectionState.CoveredByFolderCollector)
            {
                    string hint = isFolder
                    ? "Covered by a parent Folder collector."
                    : "Covered by a parent Folder collector. Toggle off to add this asset path to Ignore.";
                EditorGUILayout.LabelField(hint, EditorStyles.wordWrappedMiniLabel);
            }
        }
        else if (isIgnored)
        {
            EditorGUILayout.LabelField("Ignored by a Collection rule. Toggle on to remove the exact path rule.", EditorStyles.wordWrappedMiniLabel);
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
                if (isIgnored)
                {
                    CollectorMutationUtility.RestoreIgnored(assetPath);
                }
                else
                {
                    CollectorTargetPickerWindow.Show(new[] { assetPath }, CollectorMutationUtility.NotifyChanged);
                }
            }
            else if (canToggleOff)
            {
                CollectorMutationUtility.RemoveOrIgnore(assetPath);
            }
        }

        GUILayout.EndVertical();
    }

    private static string GetGroupName(CollectorMutationUtility.MembershipInfo membership)
    {
        string groupName = membership?.Group?.GroupName;
        return string.IsNullOrEmpty(groupName) ? "(unnamed group)" : groupName;
    }

}
