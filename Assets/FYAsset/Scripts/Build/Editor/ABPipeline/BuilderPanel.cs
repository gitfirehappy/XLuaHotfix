using UnityEditor;
using UnityEngine;

/// <summary>
/// 构建执行面板。DAG 可视化已归属 PipelinePanel，本面板预留给后续构建触发与状态展示。
/// </summary>
public class BuilderPanel : IBuildPipelinePanel
{
    public string PanelName => "Builder";

    public void OnEnable(EditorWindow window)
    {
    }

    public void OnGUI(Rect windowRect)
    {
        GUILayout.BeginArea(windowRect);
        GUILayout.FlexibleSpace();
        EditorGUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        GUILayout.Label("Builder is reserved for build execution.", EditorStyles.centeredGreyMiniLabel);
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();
        GUILayout.FlexibleSpace();
        GUILayout.EndArea();
    }

    public void OnDisable()
    {
    }
}
