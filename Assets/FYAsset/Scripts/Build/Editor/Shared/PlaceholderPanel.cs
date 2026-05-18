using UnityEditor;
using UnityEngine;

/// <summary>
/// 占位面板，用于尚未实现的 Pipeline / Builder / Inspector / Settings 区域。
/// </summary>
public sealed class PlaceholderPanel : IBuildPipelinePanel
{
    private readonly string _panelName;
    private EditorWindow _window;

    public PlaceholderPanel(string panelName)
    {
        _panelName = panelName;
    }

    public string PanelName => _panelName;

    public void OnEnable(EditorWindow window)
    {
        _window = window;
    }

    public void OnGUI(Rect rect)
    {
        GUILayout.BeginArea(rect);
        GUILayout.FlexibleSpace();
        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        GUILayout.Label(_panelName + " — coming soon", EditorStyles.centeredGreyMiniLabel);
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();
        GUILayout.FlexibleSpace();
        GUILayout.EndArea();
    }

    public void OnDisable() { }
}
