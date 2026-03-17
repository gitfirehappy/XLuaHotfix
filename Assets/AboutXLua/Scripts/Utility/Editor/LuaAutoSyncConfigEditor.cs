using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(LuaAutoSyncConfig))]
public class LuaAutoSyncConfigEditor : Editor
{
    private SerializedProperty _mappings;

    private void OnEnable()
    {
        _mappings = serializedObject.FindProperty("mappings");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.LabelField("Lua 目录同步配置", EditorStyles.boldLabel);
        EditorGUILayout.Space(5);

        int pendingDelete = -1;

        for (int i = 0; i < _mappings.arraySize; i++)
        {
            var mapping = _mappings.GetArrayElementAtIndex(i);
            var dirPath = mapping.FindPropertyRelative("directoryPath");
            var container = mapping.FindPropertyRelative("container");
            var outputDir = mapping.FindPropertyRelative("outputDirectory");
            var recursive = mapping.FindPropertyRelative("recursive");

            EditorGUILayout.BeginVertical("box");

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"映射 #{i + 1}", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("×", GUILayout.Width(25)))
            {
                pendingDelete = i;
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PropertyField(dirPath, new GUIContent("扫描目录"));
            if (GUILayout.Button("选择", GUILayout.Width(50)))
            {
                string selected = EditorUtility.OpenFolderPanel("选择扫描目录", Application.dataPath, "");
                if (!string.IsNullOrEmpty(selected) && selected.StartsWith(Application.dataPath))
                {
                    dirPath.stringValue = selected.Substring(Application.dataPath.Length + 1);
                }
                else if (!string.IsNullOrEmpty(selected))
                {
                    EditorUtility.DisplayDialog("错误", "请选择 Assets/ 下的目录", "确定");
                }
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.PropertyField(container, new GUIContent("容器 SO"));

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PropertyField(outputDir, new GUIContent("生成目录"));
            if (GUILayout.Button("选择", GUILayout.Width(50)))
            {
                string selected = EditorUtility.OpenFolderPanel("选择生成目录", Application.dataPath, "");
                if (!string.IsNullOrEmpty(selected) && selected.StartsWith(Application.dataPath))
                {
                    outputDir.stringValue = selected.Substring(Application.dataPath.Length + 1);
                }
                else if (!string.IsNullOrEmpty(selected))
                {
                    EditorUtility.DisplayDialog("错误", "请选择 Assets/ 下的目录", "确定");
                }
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.PropertyField(recursive, new GUIContent("递归扫描"));

            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(3);
        }

        if (pendingDelete >= 0)
        {
            _mappings.DeleteArrayElementAtIndex(pendingDelete);
        }

        EditorGUILayout.Space(5);
        if (GUILayout.Button("+ 添加映射"))
        {
            _mappings.InsertArrayElementAtIndex(_mappings.arraySize);
        }

        EditorGUILayout.Space(10);
        if (GUILayout.Button("扫描并同步", GUILayout.Height(30)))
        {
            LuaDirectoryScanner.ScanAndSync((LuaAutoSyncConfig)target);
        }

        serializedObject.ApplyModifiedProperties();
    }
}
