using UnityEditor;
using UnityEngine;

/// <summary>
/// CollectorSetting SO 的自定义 Inspector —— 提供一键跳转到 BuildPipelineWindow 的快捷入口。
/// </summary>
[CustomEditor(typeof(CollectorSetting))]
public class CollectorSettingInspector : Editor
{
    private bool _showRawData = false;

    public override void OnInspectorGUI()
    {
        EditorGUILayout.Space(10);
        
        GUIStyle buttonStyle = new GUIStyle(GUI.skin.button);
        buttonStyle.fontSize = 14;
        buttonStyle.fontStyle = FontStyle.Bold;
        
        if (GUILayout.Button("Open Build Pipeline Window", buttonStyle, GUILayout.Height(40)))
        {
            EditorApplication.ExecuteMenuItem(FYAssetSettings.BUILD_PIPELINE_WINDOW_MENU_PATH);
        }
        
        EditorGUILayout.Space(10);
        
        _showRawData = EditorGUILayout.Foldout(_showRawData, "Show Raw Serialized Fields");
        if (_showRawData)
        {
            EditorGUI.BeginChangeCheck();
            base.OnInspectorGUI();
            if (EditorGUI.EndChangeCheck())
                CollectorReverseIndex.Instance.MarkDirty();
        }
    }
}
