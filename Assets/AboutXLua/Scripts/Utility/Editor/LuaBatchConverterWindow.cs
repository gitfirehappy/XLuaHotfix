using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public class LuaBatchConverterWindow : EditorWindow
{
    private LuaDataBase _luaDatabase;
    private List<LuaScriptContainer> _additionalContainers = new List<LuaScriptContainer>();
    private Vector2 _scrollPos;
    private Dictionary<LuaScriptContainer, bool> _containerFoldouts = new Dictionary<LuaScriptContainer, bool>();
    private Dictionary<LuaScriptContainer, Vector2> _containerScrolls = new Dictionary<LuaScriptContainer, Vector2>();
    private LuaScriptContainer _newContainer; // 用于添加新容器的临时字段

    [MenuItem("XLua/Lua 工具/批量转换容器", false, 2)]
    public static void ShowWindow()
    {
        GetWindow<LuaBatchConverterWindow>(".lua <-> .lua.txt批量转换器");
    }

    private void OnGUI()
    {
        GUILayout.Label(".lua <-> .lua.txt批量转换器", EditorStyles.boldLabel);
        GUILayout.Space(10);

        // 数据库选择
        EditorGUILayout.BeginHorizontal();
        _luaDatabase = (LuaDataBase)EditorGUILayout.ObjectField(
            "Lua数据库",
            _luaDatabase,
            typeof(LuaDataBase),
            false
        );

        if (GUILayout.Button("创建新数据库", GUILayout.Width(120)))
        {
            CreateNewDatabase();
        }

        EditorGUILayout.EndHorizontal();

        GUILayout.Space(15);

        // 批量转换按钮
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("批量转换为.lua", GUILayout.Height(30)))
        {
            BatchConvertAll(".lua.txt", ".lua");
        }

        if (GUILayout.Button("批量转换为.lua.txt", GUILayout.Height(30)))
        {
            BatchConvertAll(".lua", ".lua.txt");
        }

        EditorGUILayout.EndHorizontal();

        GUILayout.Space(15);

        // 添加额外容器
        GUILayout.Label("额外容器", EditorStyles.boldLabel);
        EditorGUILayout.BeginHorizontal();
        _newContainer = (LuaScriptContainer)EditorGUILayout.ObjectField(
            _newContainer,
            typeof(LuaScriptContainer),
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
        List<LuaScriptContainer> allContainers = GetAllContainers();

        if (allContainers.Count == 0)
        {
            EditorGUILayout.HelpBox("没有找到任何Lua容器", MessageType.Info);
            return;
        }

        // 容器列表
        GUILayout.Label($"容器列表 ({allContainers.Count}个)", EditorStyles.boldLabel);

        _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos, GUILayout.Height(400));

        foreach (var container in allContainers)
        {
            if (container == null) continue;

            EditorGUILayout.BeginVertical("box");

            // 容器头部
            EditorGUILayout.BeginHorizontal();

            // 初始化折叠状态
            if (!_containerFoldouts.ContainsKey(container))
            {
                _containerFoldouts[container] = false;
            }

            // 折叠箭头和容器名称
            _containerFoldouts[container] = EditorGUILayout.Foldout(
                _containerFoldouts[container],
                $"{container.groupName} ({container.luaAssets.Count}个脚本)",
                true
            );

            GUILayout.FlexibleSpace();

            // 容器操作按钮
            if (GUILayout.Button("移除", GUILayout.Width(60)) && _additionalContainers.Contains(container))
            {
                _additionalContainers.Remove(container);
                _containerFoldouts.Remove(container);
                _containerScrolls.Remove(container);
            }

            EditorGUILayout.EndHorizontal();

            // 容器内容（折叠显示）
            if (_containerFoldouts[container])
            {
                EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true));

                // 脚本列表
                if (!_containerScrolls.ContainsKey(container))
                {
                    _containerScrolls[container] = Vector2.zero;
                }

                _containerScrolls[container] = EditorGUILayout.BeginScrollView(
                    _containerScrolls[container],
                    GUILayout.Height(200)
                );

                for (int i = 0; i < container.luaAssets.Count; i++)
                {
                    EditorGUILayout.BeginHorizontal();
                    container.luaAssets[i] = (TextAsset)EditorGUILayout.ObjectField(
                        container.luaAssets[i],
                        typeof(TextAsset),
                        false
                    );

                    if (GUILayout.Button("×", GUILayout.Width(25)))
                    {
                        container.luaAssets.RemoveAt(i);
                        i--;
                        EditorUtility.SetDirty(container);
                    }

                    EditorGUILayout.EndHorizontal();
                }

                EditorGUILayout.EndScrollView();

                // 脚本操作按钮
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("添加脚本"))
                {
                    AddScriptsToContainer(container);
                }

                if (GUILayout.Button("清空脚本"))
                {
                    container.luaAssets.Clear();
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
        int totalScripts = allContainers.Sum(c => c.luaAssets.Count);

        EditorGUILayout.HelpBox(
            $"总计: {allContainers.Count}个容器, {totalScripts}个脚本",
            MessageType.Info
        );
    }

    private List<LuaScriptContainer> GetAllContainers()
    {
        List<LuaScriptContainer> allContainers = new List<LuaScriptContainer>();

        // 添加数据库中的容器
        if (_luaDatabase != null)
        {
            allContainers.AddRange(_luaDatabase.groups.Where(c => c != null));
        }

        // 添加额外容器
        allContainers.AddRange(_additionalContainers.Where(c => c != null));

        return allContainers.Distinct().ToList();
    }

    private void CreateNewDatabase()
    {
        string path = EditorUtility.SaveFilePanelInProject(
            "创建Lua数据库",
            "LuaDatabase",
            "asset",
            "选择保存Lua数据库的位置"
        );

        if (!string.IsNullOrEmpty(path))
        {
            LuaDataBase newDatabase = CreateInstance<LuaDataBase>();
            AssetDatabase.CreateAsset(newDatabase, path);
            AssetDatabase.SaveAssets();
            _luaDatabase = newDatabase;
            Selection.activeObject = newDatabase;
        }
    }

    private void AddScriptsToContainer(LuaScriptContainer container)
    {
        string selectedPath = EditorUtility.OpenFilePanel(
            "选择Lua脚本",
            Application.dataPath,
            "lua"
        );

        if (string.IsNullOrEmpty(selectedPath)) return;

        string[] paths = new string[] { selectedPath };

        foreach (string path in paths)
        {
            if (path.StartsWith(Application.dataPath))
            {
                string assetPath = "Assets" + path.Substring(Application.dataPath.Length);
                TextAsset luaAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(assetPath);

                if (luaAsset != null && !container.luaAssets.Contains(luaAsset))
                {
                    container.luaAssets.Add(luaAsset);
                }
            }
        }

        EditorUtility.SetDirty(container);
    }

    private void BatchConvertAll(string oldExt, string newExt)
    {
        List<LuaScriptContainer> allContainers = GetAllContainers();

        if (allContainers.Count == 0)
        {
            EditorUtility.DisplayDialog("提示", "没有找到任何容器", "确定");
            return;
        }

        int totalSuccessCount = 0;
        int totalContainersProcessed = 0;

        AssetDatabase.StartAssetEditing();

        foreach (var container in allContainers)
        {
            if (container == null || container.luaAssets.Count == 0) continue;

            int containerSuccessCount = 0;
            List<TextAsset> newAssets = new List<TextAsset>();
            List<string> successNewPaths = new List<string>();

            foreach (TextAsset asset in container.luaAssets)
            {
                if (asset == null) continue;

                string path = AssetDatabase.GetAssetPath(asset);
                // 处理不需要转换的文件（直接保留原引用）
                if (!path.EndsWith(oldExt, System.StringComparison.OrdinalIgnoreCase))
                {
                    newAssets.Add(asset);
                    continue;
                }

                // 计算新路径并移动文件
                string newPath = path.Substring(0, path.Length - oldExt.Length) + newExt;
                string moveResult = AssetDatabase.MoveAsset(path, newPath);

                if (string.IsNullOrEmpty(moveResult)) // 移动成功
                {
                    successNewPaths.Add(newPath);
                    containerSuccessCount++;
                }
                else // 移动失败
                {
                    newAssets.Add(asset); // 保留原引用
                    Debug.LogError($"转换失败 {asset.name}: {moveResult}");
                }
            }

            // 刷新后加载成功转换的文件
            AssetDatabase.StopAssetEditing();
            AssetDatabase.Refresh();
            AssetDatabase.StartAssetEditing();

            foreach (string newPath in successNewPaths)
            {
                TextAsset newAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(newPath);
                if (newAsset != null)
                {
                    newAssets.Add(newAsset);
                }
            }

            // 更新容器列表
            container.luaAssets.Clear();
            container.luaAssets.AddRange(newAssets);
            EditorUtility.SetDirty(container);

            totalSuccessCount += containerSuccessCount;
            totalContainersProcessed++;
        }

        AssetDatabase.StopAssetEditing();
        AssetDatabase.Refresh();

        EditorUtility.DisplayDialog(
            "完成",
            $"成功转换 {totalSuccessCount} 个文件，处理了 {totalContainersProcessed} 个容器",
            "确定"
        );
    }
}