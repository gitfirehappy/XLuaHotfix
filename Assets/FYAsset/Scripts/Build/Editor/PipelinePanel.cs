using UnityEditor;
using UnityEngine;
using System.Linq;

/// <summary>
/// Pipeline 面板：显示 BuildPipelineConfig 的简单入口与预览（占位实现）。
/// </summary>
public sealed class PipelinePanel : IBuildPipelinePanel
{
    private EditorWindow _window;
    public string PanelName => "Pipeline";

    public void OnEnable(EditorWindow window)
    {
        _window = window;
    }

    public void OnGUI(Rect rect)
    {
        GUILayout.BeginArea(rect);
        GUILayout.Label("Build Pipeline Configuration", EditorStyles.boldLabel);
        GUILayout.Space(8);

        // 尝试查找名为 BuildPipelineConfig 的 ScriptableObject（占位逻辑）
        string[] guids = AssetDatabase.FindAssets("t:BuildPipelineConfig");
        if (guids != null && guids.Length > 0)
        {
            var path = AssetDatabase.GUIDToAssetPath(guids[0]);
            var asset = AssetDatabase.LoadMainAssetAtPath(path);
            GUILayout.Label($"Found: {asset.name}", EditorStyles.label);
            if (GUILayout.Button("Open Config"))
            {
                Selection.activeObject = asset;
                EditorGUIUtility.PingObject(asset);
            }
        }
        else
        {
            GUILayout.Label("No BuildPipelineConfig asset found.", EditorStyles.helpBox);
            if (GUILayout.Button("Create Placeholder Config"))
            {
                // Create a placeholder ScriptableObject asset to guide the user.
                var so = ScriptableObject.CreateInstance("BuildPipelineConfig");
                if (so != null)
                {
                    string path = "Assets/BuildPipelineConfig.asset";
                    AssetDatabase.CreateAsset(so, path);
                    AssetDatabase.SaveAssets();
                    Selection.activeObject = so;
                }
                else
                {
                    EditorUtility.DisplayDialog("Create Config", "无法创建 BuildPipelineConfig 类型的占位资源，请先在项目中定义该类型。", "OK");
                }
            }
        }

        GUILayout.FlexibleSpace();
        GUILayout.EndArea();
    }

    public void OnDisable() { }
}
