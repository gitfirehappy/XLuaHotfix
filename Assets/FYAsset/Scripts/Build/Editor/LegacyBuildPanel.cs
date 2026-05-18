using System;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Legacy Pipeline 构建占位面板。
/// </summary>
public sealed class LegacyBuildPanel : IBuildPipelinePanel
{
    private string _lastBuildTime = string.Empty;

    public string PanelName => "Legacy Build";

    public void OnEnable(EditorWindow window)
    {
        LoadLastBuildTime();
    }

    public void OnDisable()
    {
    }

    public void OnGUI(Rect rect)
    {
        GUILayout.BeginArea(rect);
        GUILayout.FlexibleSpace();

        GUILayout.BeginVertical("box", GUILayout.Width(Mathf.Min(420f, rect.width - 24f)));
        GUILayout.Label("Legacy build entry is reserved.", EditorStyles.boldLabel);
        GUILayout.Space(6f);
        GUILayout.Label(string.IsNullOrEmpty(_lastBuildTime)
            ? "Last build time: (not available)"
            : "Last build time: " + _lastBuildTime, EditorStyles.wordWrappedMiniLabel);
        GUILayout.Space(6f);
        GUILayout.Label("Build trigger will be added in a later sub-plan.", EditorStyles.wordWrappedMiniLabel);
        GUILayout.EndVertical();

        GUILayout.FlexibleSpace();
        GUILayout.EndArea();
    }

    private void LoadLastBuildTime()
    {
        VersionDataBase versionData = AssetDatabase.LoadAssetAtPath<VersionDataBase>(FYAssetSettings.Instance.VersionDataBasePath);
        _lastBuildTime = versionData != null ? versionData.LastBuildTime : string.Empty;
    }
}
