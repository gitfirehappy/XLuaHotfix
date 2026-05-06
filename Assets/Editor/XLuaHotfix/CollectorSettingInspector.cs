using UnityEngine;
using UnityEditor;
using System;
using System.Linq;
using System.Reflection;

[CustomEditor(typeof(ScriptableObject), true)]
public class CollectorSettingInspector : Editor
{
    private bool showAdvanced = false;

    public override void OnInspectorGUI()
    {
        var so = target as ScriptableObject;
        if (so == null)
        {
            DrawDefaultInspector();
            return;
        }
        var typeName = so.GetType().Name;
        if (typeName != "CollectorSetting")
        {
            DrawDefaultInspector();
            return;
        }

        EditorGUILayout.Space();
        var style = new GUIStyle(GUI.skin.button);
        style.fontSize = 12;
        style.fixedHeight = 40;
        style.stretchWidth = true;
        if (GUILayout.Button("Open Build Pipeline Window", style, GUILayout.Height(40)))
        {
            OpenBuildPipelineWindow();
        }
        EditorGUILayout.Space();

        showAdvanced = EditorGUILayout.Foldout(showAdvanced, "Show Raw Serialized Fields");
        if (showAdvanced)
        {
            DrawDefaultInspector();
        }
    }

    private void OpenBuildPipelineWindow()
    {
        Type windowType = null;
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                windowType = asm.GetTypes().FirstOrDefault(t => t.Name == "BuildPipelineWindow");
                if (windowType != null) break;
            }
            catch { }
        }
        if (windowType == null)
        {
            EditorUtility.DisplayDialog("Open Build Pipeline Window", "BuildPipelineWindow 类型未找到，请确认项目中存在该 Editor 窗口。", "OK");
            return;
        }
        var win = EditorWindow.GetWindow(windowType, true, "Build Pipeline");
        win?.Show();
        try
        {
            var mi = windowType.GetMethod("SelectSidebar", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (mi != null)
            {
                mi.Invoke(win, new object[] { "Pipeline" });
                return;
            }
            mi = windowType.GetMethod("SelectPanel", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (mi != null)
            {
                mi.Invoke(win, new object[] { "Pipeline" });
                return;
            }

        }
        catch { }
    }

}
