using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

public class SOAddressableTagger : EditorWindow
{
    private ScriptObjectDataBse _soDatabase;
    private List<ScriptObjectContainer> _additionalContainers = new();
    private Vector2 _scrollPos;
    private Dictionary<ScriptObjectContainer, bool> _containerFoldouts = new();
    private Dictionary<ScriptObjectContainer, Vector2> _labelScrolls = new();
    private string _newLabel = "";
    private ScriptObjectContainer _newContainer;

    [MenuItem("Tools/Addressables/SO标签管理器", false, 101)]
    public static void ShowWindow()
    {
        GetWindow<SOAddressableTagger>("SO标签管理器");
    }

    private void OnGUI()
    {
        GUILayout.Label("ScriptableObject Addressable标签管理器", EditorStyles.boldLabel);
        GUILayout.Space(10);

        // 数据库选择
        EditorGUILayout.BeginHorizontal();
        _soDatabase = (ScriptObjectDataBse)EditorGUILayout.ObjectField(
            "SO数据库",
            _soDatabase,
            typeof(ScriptObjectDataBse),
            false
        );

        if (GUILayout.Button("创建新数据库", GUILayout.Width(120)))
        {
            CreateNewDatabase();
        }

        EditorGUILayout.EndHorizontal();

        GUILayout.Space(15);

        // 批量操作按钮
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("应用所有标签", GUILayout.Height(30)))
        {
            ApplyAllLabels();
        }

        if (GUILayout.Button("清除所有标签", GUILayout.Height(30)))
        {
            ClearAllLabels();
        }

        EditorGUILayout.EndHorizontal();

        GUILayout.Space(15);

        // 添加额外容器
        GUILayout.Label("额外容器", EditorStyles.boldLabel);
        EditorGUILayout.BeginHorizontal();
        _newContainer = (ScriptObjectContainer)EditorGUILayout.ObjectField(
            _newContainer,
            typeof(ScriptObjectContainer),
            false
        );

        if (GUILayout.Button("添加", GUILayout.Width(60)))
        {
            if (_newContainer != null && !_additionalContainers.Contains(_newContainer))
            {
                _additionalContainers.Add(_newContainer);
                _newContainer = null;
            }
        }

        EditorGUILayout.EndHorizontal();

        GUILayout.Space(10);

        // 获取所有容器
        List<ScriptObjectContainer> allContainers = GetAllContainers();

        if (allContainers.Count == 0)
        {
            EditorGUILayout.HelpBox("没有找到任何SO容器", MessageType.Info);
            return;
        }

        // 容器列表
        GUILayout.Label($"容器列表 ({allContainers.Count}个)", EditorStyles.boldLabel);

        _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos, GUILayout.Height(500));

        foreach (var container in allContainers)
        {
            if (container == null) continue;

            EditorGUILayout.BeginVertical("box");

            // 容器头部
            EditorGUILayout.BeginHorizontal();

            // 初始化折叠状态
            if (!_containerFoldouts.ContainsKey(container))
            {
                _containerFoldouts[container] = true;
            }

            // 折叠箭头和容器名称
            _containerFoldouts[container] = EditorGUILayout.Foldout(
                _containerFoldouts[container],
                $"{container.name} ({container.soAssets.Count}个SO)",
                true
            );

            GUILayout.FlexibleSpace();

            // 容器操作按钮
            if (GUILayout.Button("应用标签", GUILayout.Width(80)))
            {
                ApplyContainerLabels(container);
            }

            if (GUILayout.Button("移除", GUILayout.Width(60)) && _additionalContainers.Contains(container))
            {
                _additionalContainers.Remove(container);
                _containerFoldouts.Remove(container);
                _labelScrolls.Remove(container);
            }

            EditorGUILayout.EndHorizontal();

            // 容器内容（折叠显示）
            if (_containerFoldouts[container])
            {
                EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true));

                // 标签管理
                GUILayout.Label("地址标签:", EditorStyles.miniBoldLabel);

                if (!_labelScrolls.ContainsKey(container))
                {
                    _labelScrolls[container] = Vector2.zero;
                }

                _labelScrolls[container] = EditorGUILayout.BeginScrollView(
                    _labelScrolls[container],
                    GUILayout.Height(100)
                );

                // 显示现有标签
                for (int i = 0; i < container.addressableLabels.Count; i++)
                {
                    EditorGUILayout.BeginHorizontal();
                    container.addressableLabels[i] = EditorGUILayout.TextField(container.addressableLabels[i]);

                    if (GUILayout.Button("×", GUILayout.Width(25)))
                    {
                        container.addressableLabels.RemoveAt(i);
                        i--;
                        EditorUtility.SetDirty(container);
                    }

                    EditorGUILayout.EndHorizontal();
                }

                EditorGUILayout.EndScrollView();

                // 添加新标签
                EditorGUILayout.BeginHorizontal();
                _newLabel = EditorGUILayout.TextField("新标签", _newLabel);

                if (GUILayout.Button("添加", GUILayout.Width(60)) && !string.IsNullOrEmpty(_newLabel))
                {
                    if (!container.addressableLabels.Contains(_newLabel))
                    {
                        container.addressableLabels.Add(_newLabel);
                        _newLabel = "";
                        EditorUtility.SetDirty(container);
                    }
                }

                EditorGUILayout.EndHorizontal();

                GUILayout.Space(10);

                // SO资源列表
                GUILayout.Label($"SO资源列表 ({container.soAssets.Count}个)", EditorStyles.miniBoldLabel);

                for (int i = 0; i < container.soAssets.Count; i++)
                {
                    EditorGUILayout.BeginHorizontal();
                    container.soAssets[i] = (ScriptableObject)EditorGUILayout.ObjectField(
                        container.soAssets[i],
                        typeof(ScriptableObject),
                        false
                    );

                    if (GUILayout.Button("×", GUILayout.Width(25)))
                    {
                        container.soAssets.RemoveAt(i);
                        i--;
                        EditorUtility.SetDirty(container);
                    }

                    EditorGUILayout.EndHorizontal();
                }

                // SO资源操作按钮
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("添加SO"))
                {
                    AddSOToContainer(container);
                }

                if (GUILayout.Button("清空SO"))
                {
                    container.soAssets.Clear();
                    EditorUtility.SetDirty(container);
                }

                EditorGUILayout.EndHorizontal();

                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.EndVertical();
            GUILayout.Space(5);
        }

        EditorGUILayout.EndScrollView();

        // 统计信息
        int totalSOs = allContainers.Sum(c => c.soAssets.Count);
        int totalLabels = allContainers.Sum(c => c.addressableLabels.Count);

        EditorGUILayout.HelpBox(
            $"总计: {allContainers.Count}个容器, {totalSOs}个SO资源, {totalLabels}个标签",
            MessageType.Info
        );
    }

    private List<ScriptObjectContainer> GetAllContainers()
    {
        List<ScriptObjectContainer> allContainers = new List<ScriptObjectContainer>();

        if (_soDatabase != null)
        {
            allContainers.AddRange(_soDatabase.groups.Where(c => c != null));
        }

        allContainers.AddRange(_additionalContainers.Where(c => c != null));

        return allContainers.Distinct().ToList();
    }

    private void CreateNewDatabase()
    {
        string path = EditorUtility.SaveFilePanelInProject(
            "创建SO数据库",
            "SODataBase",
            "asset",
            "选择保存SO数据库的位置"
        );

        if (!string.IsNullOrEmpty(path))
        {
            ScriptObjectDataBse newDatabase = CreateInstance<ScriptObjectDataBse>();
            AssetDatabase.CreateAsset(newDatabase, path);
            AssetDatabase.SaveAssets();
            _soDatabase = newDatabase;
            Selection.activeObject = newDatabase;
        }
    }

    private void AddSOToContainer(ScriptObjectContainer container)
    {
        string selectedPath = EditorUtility.OpenFilePanel(
            "选择ScriptableObject",
            Application.dataPath,
            "asset"
        );

        if (string.IsNullOrEmpty(selectedPath)) return;

        if (selectedPath.StartsWith(Application.dataPath))
        {
            string assetPath = "Assets" + selectedPath.Substring(Application.dataPath.Length);
            ScriptableObject soAsset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(assetPath);

            if (soAsset != null && !container.soAssets.Contains(soAsset))
            {
                container.soAssets.Add(soAsset);
                EditorUtility.SetDirty(container);
            }
        }
    }
    
    private void ApplyContainerLabels(ScriptObjectContainer container)
    {
        if (container.soAssets.Count == 0)
        {
            EditorUtility.DisplayDialog("提示", $"容器 '{container.name}' 中没有SO资源", "确定");
            return;
        }

        AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null)
        {
            EditorUtility.DisplayDialog("错误", "未找到Addressable设置，请先初始化Addressables", "确定");
            return;
        }

        int successCount = 0;
        int failCount = 0;

        foreach (ScriptableObject soAsset in container.soAssets)
        {
            if (soAsset == null) continue;

            string assetPath = AssetDatabase.GetAssetPath(soAsset);
            string guid = AssetDatabase.AssetPathToGUID(assetPath); 
            
            AddressableAssetEntry entry = settings.FindAssetEntry(guid);

            if (entry == null)
            {
                // 如果资源还没有Addressable条目，跳过
                Debug.LogError($"[TagError] 资源 '{soAsset.name}' ({assetPath}) 没有Addressable条目。请确保它已勾选Addressable。");
                failCount++;
                continue;
            }

            // 清除现有标签，然后应用新标签（覆盖逻辑）
            List<string> currentLabels = entry.labels.ToList();
            foreach (string label in currentLabels)
            {
                entry.SetLabel(label, false);
            }

            // 应用容器中定义的所有标签
            foreach (string label in container.addressableLabels)
            {
                if (!string.IsNullOrEmpty(label))
                {
                    if (!settings.GetLabels().Contains(label))
                    {
                        settings.AddLabel(label);
                    }

                    entry.SetLabel(label, true);
                }
            }
            successCount++;
        }

        settings.SetDirty(AddressableAssetSettings.ModificationEvent.LabelAdded, null, true);
        AssetDatabase.SaveAssets();

        string msg = $"成功: {successCount} 个\n失败/未找到条目: {failCount} 个\n（共 {container.soAssets.Count} 个）";
        if (failCount > 0) msg += "\n\n请查看Console获取失败详情。";
        
        EditorUtility.DisplayDialog("完成", msg, "确定");
    }
    
    private void ApplyAllLabels()
    {
        List<ScriptObjectContainer> allContainers = GetAllContainers();

        if (allContainers.Count == 0)
        {
            EditorUtility.DisplayDialog("提示", "没有找到任何容器", "确定");
            return;
        }

        AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null)
        {
            EditorUtility.DisplayDialog("错误", "未找到Addressable设置，请先初始化Addressables", "确定");
            return;
        }

        int totalSOsProcessed = 0;
        int containersProcessed = 0;

        foreach (var container in allContainers)
        {
            if (container == null || container.soAssets.Count == 0)
                continue;

            foreach (ScriptableObject soAsset in container.soAssets)
            {
                if (soAsset == null) continue;

                string assetPath = AssetDatabase.GetAssetPath(soAsset);
                string guid = AssetDatabase.AssetPathToGUID(assetPath); 
                
                // 使用GUID查找
                AddressableAssetEntry entry = settings.FindAssetEntry(guid);

                if (entry == null)
                {
                    Debug.LogWarning($"[TagWarning] 跳过资源 '{soAsset.name}'，因为它不在Addressable系统中。");
                    continue;
                }

                List<string> currentLabels = entry.labels.ToList();
                foreach (string label in currentLabels)
                {
                    entry.SetLabel(label, false);
                }

                foreach (string label in container.addressableLabels)
                {
                    if (!string.IsNullOrEmpty(label))
                    {
                        if (!settings.GetLabels().Contains(label))
                        {
                            settings.AddLabel(label);
                        }

                        entry.SetLabel(label, true);
                    }
                }

                totalSOsProcessed++;
            }

            containersProcessed++;
        }

        settings.SetDirty(AddressableAssetSettings.ModificationEvent.LabelAdded, null, true);
        AssetDatabase.SaveAssets();

        EditorUtility.DisplayDialog("完成",
            $"成功为 {containersProcessed} 个容器中的 {totalSOsProcessed} 个SO资源应用了标签", "确定");
    }

    private void ClearAllLabels()
    {
        if (!EditorUtility.DisplayDialog("确认",
                "确定要清除所有SO资源的Addressable标签吗？此操作不可撤销。", "确定", "取消"))
        {
            return;
        }

        List<ScriptObjectContainer> allContainers = GetAllContainers();
        AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;

        if (settings == null) return;

        int totalClearedCount = 0;

        foreach (var container in allContainers)
        {
            if (container == null || container.soAssets.Count == 0) continue;

            foreach (ScriptableObject soAsset in container.soAssets)
            {
                if (soAsset == null) continue;

                string assetPath = AssetDatabase.GetAssetPath(soAsset);
                string guid = AssetDatabase.AssetPathToGUID(assetPath);
                AddressableAssetEntry entry = settings.FindAssetEntry(guid);

                if (entry != null)
                {
                    List<string> labelsToRemove = entry.labels.ToList();
                    foreach (string label in labelsToRemove)
                    {
                        entry.SetLabel(label, false);
                        totalClearedCount++;
                    }
                }
            }
        }

        settings.SetDirty(AddressableAssetSettings.ModificationEvent.LabelRemoved, null, true);
        AssetDatabase.SaveAssets();

        EditorUtility.DisplayDialog("完成",
            $"成功清除了 {totalClearedCount} 个标签", "确定");
    }
}