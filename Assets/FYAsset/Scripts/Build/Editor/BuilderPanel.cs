using UnityEditor;
using UnityEngine;

/// <summary>
/// Builder 面板：Hotfix / Snapshot 配置入口的占位实现。
/// </summary>
public sealed class BuilderPanel : IBuildPipelinePanel
{
    private EditorWindow _window;
    public string PanelName => "Builder";

    public void OnEnable(EditorWindow window)
    {
        _window = window;
    }

    public void OnGUI(Rect rect)
    {
        GUILayout.BeginArea(rect);
        GUILayout.Label("Builder - Hotfix & Snapshot", EditorStyles.boldLabel);
        GUILayout.Space(8);

        GUILayout.Label("Hotfix / Snapshot 配置尚为占位实现。", EditorStyles.helpBox);
        GUILayout.Space(6);

        if (GUILayout.Button("Open Hotfix Config"))
        {
            // 试图打开名为 HotfixConfig 的 asset（如果存在）
            string[] guids = AssetDatabase.FindAssets("t:HotfixConfig");
            if (guids != null && guids.Length > 0)
            {
                var path = AssetDatabase.GUIDToAssetPath(guids[0]);
                var asset = AssetDatabase.LoadMainAssetAtPath(path);
                Selection.activeObject = asset;
                EditorGUIUtility.PingObject(asset);
            }
            else
            {
                EditorUtility.DisplayDialog("Hotfix Config", "未发现 HotfixConfig 类型的资源。", "OK");
            }
        }

        GUILayout.FlexibleSpace();
        GUILayout.EndArea();
    }

    public void OnDisable() { }
}
