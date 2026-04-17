#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public class AssetAssociationSearchWindow : EditorWindow
{
    private enum SearchMode
    {
        ReverseReference,
        BridgeKeySearch
    }

    private struct SearchResult
    {
        public string assetPath;
        public string objectName;
        public string componentType;
        public string propertyInfo;
        public string detail;
    }

    private SearchMode _searchMode = SearchMode.ReverseReference;
    private Object _targetAsset;
    private string _bridgeKeyword = string.Empty;
    private string _searchDirectory = "Assets/";
    private readonly List<SearchResult> _results = new List<SearchResult>();
    private Vector2 _scrollPos;

    [MenuItem("XLua/Lua 工具/快速关联搜索", false, 21)]
    public static void ShowWindow()
    {
        GetWindow<AssetAssociationSearchWindow>("关联搜索");
    }

    private void OnGUI()
    {
        GUILayout.Label("快速关联搜索", EditorStyles.boldLabel);
        _searchMode = (SearchMode)EditorGUILayout.EnumPopup("搜索模式", _searchMode);
        _searchDirectory = EditorGUILayout.TextField("扫描目录", _searchDirectory);

        switch (_searchMode)
        {
            case SearchMode.ReverseReference:
                DrawReverseReferenceUI();
                break;
            case SearchMode.BridgeKeySearch:
                DrawBridgeKeySearchUI();
                break;
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField($"搜索结果: {_results.Count} 条");

        _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);
        foreach (var r in _results)
        {
            EditorGUILayout.BeginHorizontal("box");
            EditorGUILayout.BeginVertical();
            EditorGUILayout.LabelField(r.assetPath, EditorStyles.miniLabel);
            EditorGUILayout.LabelField($"{r.objectName} → {r.componentType}");
            if (!string.IsNullOrEmpty(r.propertyInfo))
            {
                EditorGUILayout.LabelField($"  属性: {r.propertyInfo}", EditorStyles.miniLabel);
            }

            if (!string.IsNullOrEmpty(r.detail))
            {
                EditorGUILayout.LabelField($"  关联: {r.detail}", EditorStyles.miniLabel);
            }

            EditorGUILayout.EndVertical();

            if (GUILayout.Button("定位", GUILayout.Width(50)))
            {
                var obj = AssetDatabase.LoadAssetAtPath<Object>(r.assetPath);
                Selection.activeObject = obj;
                EditorGUIUtility.PingObject(obj);
            }

            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.EndScrollView();
    }

    private void DrawReverseReferenceUI()
    {
        _targetAsset = EditorGUILayout.ObjectField("目标资产", _targetAsset, typeof(Object), false);
        if (GUILayout.Button("搜索引用者", GUILayout.Height(30)) && _targetAsset != null)
        {
            SearchReverseReferences();
        }
    }

    private void DrawBridgeKeySearchUI()
    {
        _bridgeKeyword = EditorGUILayout.TextField("configKey 关键字", _bridgeKeyword);
        if (GUILayout.Button("搜索 Bridge 关联", GUILayout.Height(30))
            && !string.IsNullOrEmpty(_bridgeKeyword))
        {
            SearchBridgeAssociations();
        }
    }

    private void SearchReverseReferences()
    {
        _results.Clear();
        string targetPath = AssetDatabase.GetAssetPath(_targetAsset);
        if (string.IsNullOrEmpty(targetPath))
        {
            return;
        }

        string[] guids = AssetDatabase.FindAssets("t:Prefab t:Scene t:ScriptableObject", new[] { _searchDirectory });

        try
        {
            for (int i = 0; i < guids.Length; i++)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guids[i]);
                float progress = guids.Length == 0 ? 1f : (float)i / guids.Length;
                EditorUtility.DisplayProgressBar("搜索引用", assetPath, progress);

                string[] deps = AssetDatabase.GetDependencies(assetPath, true);
                bool found = false;
                foreach (string dep in deps)
                {
                    if (dep == targetPath)
                    {
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    continue;
                }

                GameObject go = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
                if (go != null)
                {
                    FindReferencesInGameObject(go, assetPath, targetPath, string.Empty);
                }
                else
                {
                    _results.Add(new SearchResult
                    {
                        assetPath = assetPath,
                        objectName = "(非Prefab资产)",
                        componentType = AssetDatabase.GetMainAssetTypeAtPath(assetPath)?.Name ?? "Unknown",
                        propertyInfo = string.Empty,
                        detail = string.Empty
                    });
                }
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        Debug.Log($"[AssetAssociationSearch] 反向引用搜索完成，找到 {_results.Count} 条");
    }

    private void FindReferencesInGameObject(GameObject go, string assetPath, string targetPath, string parentPath)
    {
        string currentPath = string.IsNullOrEmpty(parentPath) ? go.name : $"{parentPath}/{go.name}";

        foreach (var comp in go.GetComponents<Component>())
        {
            if (comp == null)
            {
                continue;
            }

            SerializedObject so = new SerializedObject(comp);
            SerializedProperty sp = so.GetIterator();
            try
            {
                while (sp.NextVisible(true))
                {
                    if (sp.propertyType == SerializedPropertyType.ObjectReference && sp.objectReferenceValue != null)
                    {
                        string refPath = AssetDatabase.GetAssetPath(sp.objectReferenceValue);
                        if (refPath == targetPath)
                        {
                            _results.Add(new SearchResult
                            {
                                assetPath = assetPath,
                                objectName = currentPath,
                                componentType = comp.GetType().Name,
                                propertyInfo = sp.propertyPath,
                                detail = string.Empty
                            });
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[AssetAssociationSearch] 跳过异常组件序列化: {assetPath} | {comp.GetType().Name} | {ex.Message}");
            }
        }

        for (int i = 0; i < go.transform.childCount; i++)
        {
            FindReferencesInGameObject(go.transform.GetChild(i).gameObject, assetPath, targetPath, currentPath);
        }
    }

    private void SearchBridgeAssociations()
    {
        _results.Clear();

        string[] bridgeTypeNames = { "LuaBehaviourBridge", "ScriptObjectBridge", "AnimBridge" };
        string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { _searchDirectory });
        string[] sceneGuids = AssetDatabase.FindAssets("t:Scene", new[] { _searchDirectory });

        float total = prefabGuids.Length + sceneGuids.Length;
        if (total <= 0)
        {
            total = 1;
        }

        try
        {
            for (int i = 0; i < prefabGuids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(prefabGuids[i]);
                EditorUtility.DisplayProgressBar("搜索 Bridge", path, i / total);
                GameObject go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (go != null)
                {
                    ScanBridgeInGameObject(go, path, string.Empty, bridgeTypeNames);
                }
            }

            for (int i = 0; i < sceneGuids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(sceneGuids[i]);
                EditorUtility.DisplayProgressBar("搜索 Bridge", path, (prefabGuids.Length + i) / total);

                Scene scene = EditorSceneManager.GetSceneByPath(path);
                bool openedByUs = false;
                if (!scene.isLoaded)
                {
                    scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                    openedByUs = true;
                }

                try
                {
                    foreach (var root in scene.GetRootGameObjects())
                    {
                        ScanBridgeInGameObject(root, path, string.Empty, bridgeTypeNames);
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
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        Debug.Log($"[AssetAssociationSearch] Bridge 关联搜索完成，找到 {_results.Count} 条");
    }

    private void ScanBridgeInGameObject(GameObject go, string assetPath, string parentPath, string[] bridgeTypeNames)
    {
        string currentPath = string.IsNullOrEmpty(parentPath) ? go.name : $"{parentPath}/{go.name}";

        foreach (var comp in go.GetComponents<Component>())
        {
            if (comp == null)
            {
                continue;
            }

            string typeName = comp.GetType().Name;
            bool isBridge = false;
            foreach (string bridgeType in bridgeTypeNames)
            {
                if (typeName == bridgeType)
                {
                    isBridge = true;
                    break;
                }
            }

            if (!isBridge)
            {
                continue;
            }

            SerializedObject so = new SerializedObject(comp);
            SerializedProperty keyProp = so.FindProperty("configKey");
            if (keyProp == null || keyProp.propertyType != SerializedPropertyType.String)
            {
                continue;
            }

            string configKeyValue = keyProp.stringValue;
            if (string.IsNullOrEmpty(configKeyValue))
            {
                continue;
            }

            if (!configKeyValue.ToLowerInvariant().Contains(_bridgeKeyword.ToLowerInvariant()))
            {
                continue;
            }

            string associatedSOPath = TryFindSOByKey(configKeyValue);

            _results.Add(new SearchResult
            {
                assetPath = assetPath,
                objectName = currentPath,
                componentType = typeName,
                propertyInfo = $"configKey = \"{configKeyValue}\"",
                detail = string.IsNullOrEmpty(associatedSOPath)
                    ? "(未找到关联SO)"
                    : $"关联SO: {associatedSOPath}"
            });
        }

        for (int i = 0; i < go.transform.childCount; i++)
        {
            ScanBridgeInGameObject(go.transform.GetChild(i).gameObject, assetPath, currentPath, bridgeTypeNames);
        }
    }

    private string TryFindSOByKey(string configKey)
    {
        string[] guids = AssetDatabase.FindAssets(configKey + " t:ScriptableObject", new[] { "Assets/AboutXLua/SO" });

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            string fileName = Path.GetFileNameWithoutExtension(path);
            if (fileName == configKey)
            {
                return path;
            }
        }

        foreach (string guid in guids)
        {
            return AssetDatabase.GUIDToAssetPath(guid);
        }

        return null;
    }
}
#endif
