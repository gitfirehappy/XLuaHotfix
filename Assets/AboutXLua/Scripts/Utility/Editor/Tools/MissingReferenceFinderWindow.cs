#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public class MissingReferenceFinderWindow : EditorWindow
{
    private struct MissingEntry
    {
        public string assetPath;
        public string objectName;
        public string componentType;
        public string propertyPath;
        public bool isComponent;
    }

    private readonly List<MissingEntry> _results = new List<MissingEntry>();
    private Vector2 _scrollPos;
    private bool _scanPrefabs = true;
    private bool _scanScenes = true;
    private string _searchDirectory = "Assets/";
    private string _filterKeyword = string.Empty;

    [MenuItem("XLua/Lua 工具/资源引用缺失检查", false, 20)]
    public static void ShowWindow()
    {
        GetWindow<MissingReferenceFinderWindow>("引用缺失检查");
    }

    private void OnGUI()
    {
        GUILayout.Label("资源引用缺失检查", EditorStyles.boldLabel);
        _searchDirectory = EditorGUILayout.TextField("扫描目录", _searchDirectory);
        _scanPrefabs = EditorGUILayout.Toggle("扫描 Prefab", _scanPrefabs);
        _scanScenes = EditorGUILayout.Toggle("扫描 Scene", _scanScenes);

        if (GUILayout.Button("开始扫描", GUILayout.Height(30)))
        {
            ScanAll();
        }

        _filterKeyword = EditorGUILayout.TextField("过滤关键字", _filterKeyword);
        EditorGUILayout.LabelField($"发现 {_results.Count} 处缺失引用");

        _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);
        foreach (var entry in _results)
        {
            if (!string.IsNullOrEmpty(_filterKeyword)
                && !entry.assetPath.Contains(_filterKeyword)
                && !entry.objectName.Contains(_filterKeyword))
            {
                continue;
            }

            EditorGUILayout.BeginHorizontal("box");
            EditorGUILayout.BeginVertical();
            EditorGUILayout.LabelField(entry.assetPath, EditorStyles.miniLabel);
            string detail = entry.isComponent
                ? $"[Missing Component] {entry.objectName}"
                : $"{entry.objectName} → {entry.componentType}.{entry.propertyPath}";
            EditorGUILayout.LabelField(detail);
            EditorGUILayout.EndVertical();

            if (GUILayout.Button("定位", GUILayout.Width(50)))
            {
                var obj = AssetDatabase.LoadAssetAtPath<Object>(entry.assetPath);
                Selection.activeObject = obj;
                EditorGUIUtility.PingObject(obj);
            }

            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.EndScrollView();
    }

    private void ScanAll()
    {
        _results.Clear();

        try
        {
            if (_scanPrefabs)
            {
                string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { _searchDirectory });
                for (int i = 0; i < guids.Length; i++)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                    float progress = guids.Length == 0 ? 1f : (float)i / guids.Length;
                    EditorUtility.DisplayProgressBar("扫描 Prefab", path, progress);
                    ScanPrefab(path);
                }
            }

            if (_scanScenes)
            {
                string[] guids = AssetDatabase.FindAssets("t:Scene", new[] { _searchDirectory });
                for (int i = 0; i < guids.Length; i++)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                    float progress = guids.Length == 0 ? 1f : (float)i / guids.Length;
                    EditorUtility.DisplayProgressBar("扫描 Scene", path, progress);
                    ScanScene(path);
                }
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        Debug.Log($"[MissingReferenceFinder] 扫描完成，发现 {_results.Count} 处缺失引用");
    }

    private void ScanPrefab(string assetPath)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
        if (prefab == null)
        {
            return;
        }

        ScanGameObject(prefab, assetPath, string.Empty);
    }

    private void ScanScene(string scenePath)
    {
        Scene scene = EditorSceneManager.GetSceneByPath(scenePath);
        bool openedByUs = false;
        if (!scene.isLoaded)
        {
            scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
            openedByUs = true;
        }

        try
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                ScanGameObject(root, scenePath, string.Empty);
            }
        }
        finally
        {
            if (openedByUs && scene.isLoaded)
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }
    }

    private void ScanGameObject(GameObject go, string assetPath, string parentPath)
    {
        string currentPath = string.IsNullOrEmpty(parentPath) ? go.name : $"{parentPath}/{go.name}";

        Component[] components = go.GetComponents<Component>();
        for (int i = 0; i < components.Length; i++)
        {
            if (components[i] == null)
            {
                _results.Add(new MissingEntry
                {
                    assetPath = assetPath,
                    objectName = currentPath,
                    componentType = "Unknown",
                    propertyPath = string.Empty,
                    isComponent = true
                });
                continue;
            }

            try
            {
                SerializedObject so = new SerializedObject(components[i]);
                SerializedProperty sp = so.GetIterator();
                while (sp.NextVisible(true))
                {
                    if (sp.propertyType == SerializedPropertyType.ObjectReference
                        && sp.objectReferenceValue == null
                        && sp.objectReferenceInstanceIDValue != 0)
                    {
                        _results.Add(new MissingEntry
                        {
                            assetPath = assetPath,
                            objectName = currentPath,
                            componentType = components[i].GetType().Name,
                            propertyPath = sp.propertyPath,
                            isComponent = false
                        });
                    }
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[MissingReferenceFinder] 跳过异常组件序列化: {assetPath} | {components[i].GetType().Name} | {ex.Message}");
            }
        }

        for (int i = 0; i < go.transform.childCount; i++)
        {
            ScanGameObject(go.transform.GetChild(i).gameObject, assetPath, currentPath);
        }
    }
}
#endif
