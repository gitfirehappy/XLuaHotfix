using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(ConfigConvertChannel))]
public class ConfigConvertChannelEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        
        ConfigConvertChannel channel = (ConfigConvertChannel)target;
        
        // 输入文件夹选择
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.PropertyField(serializedObject.FindProperty("inputFolder"));
        if (GUILayout.Button("浏览", GUILayout.Width(60)))
        {
            string path = EditorUtility.OpenFolderPanel("选择输入文件夹", Application.dataPath, "");
            if (!string.IsNullOrEmpty(path))
            {
                serializedObject.FindProperty("inputFolder").stringValue = path;
            }
        }
        EditorGUILayout.EndHorizontal();
        
        // 输出文件夹选择
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.PropertyField(serializedObject.FindProperty("outputFolder"));
        if (GUILayout.Button("浏览", GUILayout.Width(60)))
        {
            string path = EditorUtility.OpenFolderPanel("选择输出文件夹", Application.dataPath, "");
            if (!string.IsNullOrEmpty(path))
            {
                serializedObject.FindProperty("outputFolder").stringValue = path;
            }
        }
        EditorGUILayout.EndHorizontal();
        
        // 显示格式选择字段
        EditorGUILayout.PropertyField(serializedObject.FindProperty("inputFormat"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("outputFormat"));
        
        serializedObject.ApplyModifiedProperties();
        
        // 添加一些辅助信息
        EditorGUILayout.Space();
        EditorGUILayout.HelpBox($"输入格式: {channel.inputFormat}\n输出格式: {channel.outputFormat}", MessageType.Info);
    }
}