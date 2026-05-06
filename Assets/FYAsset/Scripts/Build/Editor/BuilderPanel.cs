using UnityEditor;
using UnityEngine;

/// <summary>
/// 构建控制面板（预留区）—— 后续集成热更构建和快照功能。
/// </summary>
public class BuilderPanel : IBuildPipelinePanel
{
    public string PanelName => "Builder";

    public void OnEnable(EditorWindow window)
    {
    }

    public void OnDisable()
    {
    }

    public void OnGUI(Rect windowRect)
    {
        GUILayout.BeginArea(windowRect);
        
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();

        GUILayout.FlexibleSpace();
        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        
        GUILayout.BeginVertical("box", GUILayout.Width(400));
        GUILayout.Space(10);
        GUILayout.Label("Builder Settings (Hotfix & Snapshot)", EditorStyles.boldLabel);
        GUILayout.Space(10);
        GUILayout.Label("These settings will be integrated here.", EditorStyles.wordWrappedLabel);
        GUILayout.Space(20);
        
        if (GUILayout.Button("Open Differential Processor Window", GUILayout.Height(30)))
        {
            EditorApplication.ExecuteMenuItem("XLua/Build Hotfix Patch"); // Placeholder or actual menu item
        }
        
        GUILayout.Space(10);
        GUILayout.EndVertical();
        
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();
        GUILayout.FlexibleSpace();
        
        GUILayout.EndArea();
    }
}
