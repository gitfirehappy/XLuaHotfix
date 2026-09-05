using System.IO;
using UnityEditor;
using UnityEngine;

public class LuaFileCreatorWindow : EditorWindow
{
    private const string LuaTemplate = "-- Lua script\n\nfunction Start()\n    print(\"Hello Lua\")\nend";

    private LuaDataBase _luaDatabase;
    private LuaScriptContainer _selectedContainer;
    private string _newContainerName = "NewContainer";
    private string _fileName = "NewLuaScript.lua";
    private string _selectedPath = "Assets";
    private Vector2 _scrollPos;

    [MenuItem("XLua/Lua 工具/创建 Lua 文件", false, 1)]
    public static void ShowWindow()
    {
        GetWindow<LuaFileCreatorWindow>("创建Lua文件");
    }

    private void OnGUI()
    {
        GUILayout.Label("创建Lua文件", EditorStyles.boldLabel);
        GUILayout.Space(10);

        // 数据库选择区域
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

        GUILayout.Space(10);

        // 容器选择区域
        EditorGUILayout.BeginHorizontal();
        _selectedContainer = (LuaScriptContainer)EditorGUILayout.ObjectField(
            "选择容器",
            _selectedContainer,
            typeof(LuaScriptContainer),
            false
        );

        if (GUILayout.Button("创建新容器", GUILayout.Width(120)))
        {
            if (_luaDatabase != null)
            {
                CreateNewContainer();
            }
            else
            {
                EditorUtility.DisplayDialog("提示", "请先选择或创建一个数据库", "确定");
            }
        }

        EditorGUILayout.EndHorizontal();

        // 仅当存在数据库时才显示新容器名称输入（因为创建容器需要数据库）
        if (_luaDatabase != null)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("容器名称:", GUILayout.Width(70));
            _newContainerName = EditorGUILayout.TextField(_newContainerName);
            EditorGUILayout.EndHorizontal();
        }

        GUILayout.Space(15);

        // 文件设置区域
        GUILayout.Label("文件设置", EditorStyles.boldLabel);

        // 路径选择
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("保存路径:", GUILayout.Width(70));
        EditorGUILayout.LabelField(_selectedPath);
        if (GUILayout.Button("浏览...", GUILayout.Width(60)))
        {
            string newPath = EditorUtility.OpenFolderPanel("选择保存路径", _selectedPath, "");
            if (!string.IsNullOrEmpty(newPath) && newPath.Contains(Application.dataPath))
            {
                _selectedPath = "Assets" + newPath.Substring(Application.dataPath.Length);
            }
        }

        EditorGUILayout.EndHorizontal();

        // 文件名输入
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("文件名称:", GUILayout.Width(70));
        _fileName = EditorGUILayout.TextField(_fileName);
        // 自动添加.lua扩展名
        if (!string.IsNullOrEmpty(_fileName) && !_fileName.EndsWith(".lua"))
        {
            _fileName += ".lua";
        }

        EditorGUILayout.EndHorizontal();

        GUILayout.Space(20);

        if (GUILayout.Button("创建Lua文件", GUILayout.Height(30)))
        {
            CreateLuaFile();
        }

        GUILayout.Space(10);

        EditorGUILayout.HelpBox(
            "操作步骤:\n1. 选择或创建数据库\n2. 选择或创建容器\n3. 设置保存路径和文件名\n4. 点击创建按钮",
            MessageType.Info
        );

        // 显示当前选择的数据库和容器信息
        if (_luaDatabase != null)
        {
            EditorGUILayout.HelpBox(
                $"当前数据库: {_luaDatabase.name}\n包含容器: {_luaDatabase.groups.Count}个",
                MessageType.None
            );
        }

        if (_selectedContainer != null)
        {
            EditorGUILayout.HelpBox(
                $"当前容器: {_selectedContainer.name}\n包含脚本: {_selectedContainer.luaAssets.Count}个",
                MessageType.None
            );
        }
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

    private void CreateNewContainer()
    {
        if (string.IsNullOrEmpty(_newContainerName))
        {
            EditorUtility.DisplayDialog("提示", "请输入容器名称", "确定");
            return;
        }

        // 确保容器名称唯一
        string uniqueName = _newContainerName;
        int counter = 1;
        while (_luaDatabase.groups.Exists(c => c != null && c.name == uniqueName))
        {
            uniqueName = $"{_newContainerName}_{counter}";
            counter++;
        }

        // 创建新容器
        LuaScriptContainer newContainer = CreateInstance<LuaScriptContainer>();
        newContainer.name = uniqueName;

        // 保存容器到数据库所在目录
        string databasePath = AssetDatabase.GetAssetPath(_luaDatabase);
        string containerPath = Path.Combine(Path.GetDirectoryName(databasePath), $"{uniqueName}.asset");

        AssetDatabase.CreateAsset(newContainer, containerPath);
        AssetDatabase.SaveAssets();

        // 添加到数据库
        _luaDatabase.groups.Add(newContainer);
        EditorUtility.SetDirty(_luaDatabase);

        // 选中新容器
        _selectedContainer = newContainer;
        Selection.activeObject = newContainer;
    }

    private void CreateLuaFile()
    {
        // 验证必要条件
        if (_luaDatabase == null)
        {
            EditorUtility.DisplayDialog("错误", "请选择或创建一个数据库", "确定");
            return;
        }

        if (_selectedContainer == null)
        {
            EditorUtility.DisplayDialog("错误", "请选择或创建一个容器", "确定");
            return;
        }

        if (string.IsNullOrEmpty(_fileName) || !_fileName.EndsWith(".lua"))
        {
            EditorUtility.DisplayDialog("错误", "请输入有效的文件名（必须以.lua结尾）", "确定");
            return;
        }

        if (string.IsNullOrEmpty(_selectedPath) || !_selectedPath.StartsWith("Assets"))
        {
            EditorUtility.DisplayDialog("错误", "请选择有效的保存路径", "确定");
            return;
        }

        // 构建完整路径
        string fullPath = Path.Combine(_selectedPath, _fileName);

        // 检查文件是否已存在
        if (File.Exists(fullPath))
        {
            if (!EditorUtility.DisplayDialog("确认", "文件已存在，是否覆盖？", "是", "否"))
            {
                return;
            }
        }

        try
        {
            // 确保目录存在
            Directory.CreateDirectory(_selectedPath);

            // 写入文件内容
            File.WriteAllText(fullPath, LuaTemplate);

            // 刷新资源数据库
            AssetDatabase.Refresh();

            // 加载新创建的资源
            TextAsset luaAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(fullPath);

            if (luaAsset != null)
            {
                // 添加到容器
                if (!_selectedContainer.luaAssets.Contains(luaAsset))
                {
                    _selectedContainer.luaAssets.Add(luaAsset);
                    EditorUtility.SetDirty(_selectedContainer);
                }

                // 选中新创建的文件
                Selection.activeObject = luaAsset;
                EditorUtility.FocusProjectWindow();

                EditorUtility.DisplayDialog("成功", $"Lua文件已创建：{_fileName}\n并已添加到容器：{_selectedContainer.name}",
                    "确定");
            }
            else
            {
                EditorUtility.DisplayDialog("错误", "创建文件失败，无法加载资源", "确定");
            }
        }
        catch (System.Exception ex)
        {
            EditorUtility.DisplayDialog("错误", $"创建文件时发生错误：{ex.Message}", "确定");
        }
    }
}
