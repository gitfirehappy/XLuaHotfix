using UnityEditor;
using UnityEngine;

/// <summary>
/// Legacy Pipeline 报告占位面板。
/// </summary>
public sealed class LegacyReportPanel : IBuildPipelinePanel
{
    public string PanelName => "Legacy Report";

    public void OnEnable(EditorWindow window)
    {
    }

    public void OnDisable()
    {
    }

    public void OnGUI(Rect rect)
    {
        GUILayout.BeginArea(rect);
        GUILayout.FlexibleSpace();
        GUILayout.BeginVertical("box", GUILayout.Width(Mathf.Min(420f, rect.width - 24f)));
        GUILayout.Label("Legacy report view is reserved.", EditorStyles.boldLabel);
        GUILayout.Space(6f);
        GUILayout.Label("Diff and report details will be added after E7 lands.", EditorStyles.wordWrappedMiniLabel);
        GUILayout.EndVertical();
        GUILayout.FlexibleSpace();
        GUILayout.EndArea();
    }
}
