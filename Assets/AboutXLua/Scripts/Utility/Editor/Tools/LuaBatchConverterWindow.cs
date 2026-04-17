using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public class LuaBatchConverterWindow : EditorWindow
{
    private enum ConvertMode
    {
        ContainerMode,
        DirectoryMode
    }

    private LuaDataBase _luaDatabase;
    private List<LuaScriptContainer> _additionalContainers = new List<LuaScriptContainer>();
    private Vector2 _scrollPos;
    private Dictionary<LuaScriptContainer, bool> _containerFoldouts = new Dictionary<LuaScriptContainer, bool>();
    private Dictionary<LuaScriptContainer, Vector2> _containerScrolls = new Dictionary<LuaScriptContainer, Vector2>();
    private LuaScriptContainer _newContainer;

    private string _sourceExt = ".lua.txt";
    private string _targetExt = ".lua";
    private string _targetDirectory = "Assets/";
    private ConvertMode _convertMode = ConvertMode.ContainerMode;

    [MenuItem("XLua/Lua 工具/通用后缀转换器", false, 2)]
    public static void ShowWindow()
    {
        GetWindow<LuaBatchConverterWindow>("通用文件后缀转换器");
    }

    private void OnGUI()
    {
        GUILayout.Label("通用文件后缀转换器", EditorStyles.boldLabel);
        GUILayout.Space(8);

        _convertMode = (ConvertMode)EditorGUILayout.EnumPopup("转换模式", _convertMode);
        _sourceExt = EditorGUILayout.TextField("源后缀", _sourceExt);
        _targetExt = EditorGUILayout.TextField("目标后缀", _targetExt);

        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("预设", GUILayout.Width(40));
        if (GUILayout.Button(".lua.txt → .lua", GUILayout.Height(22)))
        {
            _sourceExt = ".lua.txt";
            _targetExt = ".lua";
        }

        if (GUILayout.Button(".lua → .lua.txt", GUILayout.Height(22)))
        {
            _sourceExt = ".lua";
            _targetExt = ".lua.txt";
        }

        EditorGUILayout.EndHorizontal();
        GUILayout.Space(10);

        if (_convertMode == ConvertMode.DirectoryMode)
        {
            DrawDirectoryModeUI();
        }
        else
        {
            DrawContainerModeUI();
        }

        GUILayout.Space(15);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button($"批量转换: {_sourceExt} → {_targetExt}", GUILayout.Height(30)))
        {
            ExecuteConvert(_sourceExt, _targetExt);
        }

        if (GUILayout.Button($"批量转换: {_targetExt} → {_sourceExt}", GUILayout.Height(30)))
        {
            ExecuteConvert(_targetExt, _sourceExt);
        }

        EditorGUILayout.EndHorizontal();
    }

    private void DrawDirectoryModeUI()
    {
        GUILayout.Label("目录模式", EditorStyles.boldLabel);
        EditorGUILayout.BeginHorizontal();
        _targetDirectory = EditorGUILayout.TextField("目标目录", _targetDirectory);
        if (GUILayout.Button("选择", GUILayout.Width(60)))
        {
            string selected = EditorUtility.OpenFolderPanel("选择扫描目录", Application.dataPath, string.Empty);
            if (!string.IsNullOrEmpty(selected))
            {
                string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? string.Empty;
                string normalizedSelected = selected.Replace('\\', '/');
                string normalizedRoot = projectRoot.Replace('\\', '/');
                if (normalizedSelected.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
                {
                    string relative = normalizedSelected.Substring(normalizedRoot.Length).TrimStart('/');
                    _targetDirectory = string.IsNullOrEmpty(relative) ? "Assets/" : relative;
                }
                else
                {
                    EditorUtility.DisplayDialog("目录无效", "请选择当前项目目录下的文件夹。", "确定");
                }
            }
        }

        EditorGUILayout.EndHorizontal();
    }

    private void DrawContainerModeUI()
    {
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

        List<LuaScriptContainer> allContainers = GetAllContainers();

        if (allContainers.Count == 0)
        {
            EditorGUILayout.HelpBox("没有找到任何Lua容器", MessageType.Info);
            return;
        }

        GUILayout.Label($"容器列表 ({allContainers.Count}个)", EditorStyles.boldLabel);

        _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos, GUILayout.Height(400));

        foreach (var container in allContainers)
        {
            if (container == null) continue;

            EditorGUILayout.BeginVertical("box");

            EditorGUILayout.BeginHorizontal();

            if (!_containerFoldouts.ContainsKey(container))
            {
                _containerFoldouts[container] = false;
            }

            _containerFoldouts[container] = EditorGUILayout.Foldout(
                _containerFoldouts[container],
                $"{container.groupName} ({container.luaAssets.Count}个脚本)",
                true
            );

            GUILayout.FlexibleSpace();

            if (GUILayout.Button("移除", GUILayout.Width(60)) && _additionalContainers.Contains(container))
            {
                _additionalContainers.Remove(container);
                _containerFoldouts.Remove(container);
                _containerScrolls.Remove(container);
            }

            EditorGUILayout.EndHorizontal();

            if (_containerFoldouts[container])
            {
                EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true));

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

        int totalScripts = allContainers.Sum(c => c.luaAssets.Count);

        EditorGUILayout.HelpBox(
            $"总计: {allContainers.Count}个容器, {totalScripts}个脚本",
            MessageType.Info
        );
    }

    private void ExecuteConvert(string oldExt, string newExt)
    {
        oldExt = NormalizeExtension(oldExt);
        newExt = NormalizeExtension(newExt);

        if (string.IsNullOrEmpty(oldExt) || string.IsNullOrEmpty(newExt))
        {
            EditorUtility.DisplayDialog("参数错误", "源后缀和目标后缀不能为空。", "确定");
            return;
        }

        if (string.Equals(oldExt, newExt, StringComparison.OrdinalIgnoreCase))
        {
            EditorUtility.DisplayDialog("参数错误", "源后缀与目标后缀相同，无需转换。", "确定");
            return;
        }

        if (_convertMode == ConvertMode.DirectoryMode)
        {
            BatchConvertDirectory(oldExt, newExt);
        }
        else
        {
            BatchConvertAll(oldExt, newExt);
        }
    }

    private string NormalizeExtension(string ext)
    {
        if (string.IsNullOrWhiteSpace(ext))
        {
            return string.Empty;
        }

        string trimmed = ext.Trim();
        return trimmed.StartsWith(".") ? trimmed : $".{trimmed}";
    }

    private List<LuaScriptContainer> GetAllContainers()
    {
        List<LuaScriptContainer> allContainers = new List<LuaScriptContainer>();

        if (_luaDatabase != null)
        {
            allContainers.AddRange(_luaDatabase.groups.Where(c => c != null));
        }

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

    private void BatchConvertDirectory(string oldExt, string newExt)
    {
        string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? string.Empty;
        string normalizedDir = (_targetDirectory ?? string.Empty).Replace('\\', '/').Trim();

        string fullPath;
        if (Path.IsPathRooted(normalizedDir))
        {
            fullPath = normalizedDir;
        }
        else
        {
            fullPath = Path.GetFullPath(Path.Combine(projectRoot, normalizedDir));
        }

        if (!Directory.Exists(fullPath))
        {
            EditorUtility.DisplayDialog("目录不存在", $"未找到目录: {fullPath}", "确定");
            return;
        }

        int successCount = 0;
        int failCount = 0;
        string[] files = Directory.GetFiles(fullPath, $"*{oldExt}", SearchOption.AllDirectories);

        AssetDatabase.StartAssetEditing();
        try
        {
            foreach (string file in files)
            {
                string normalizedFile = file.Replace('\\', '/');
                string normalizedRoot = projectRoot.Replace('\\', '/');
                if (!normalizedFile.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
                {
                    failCount++;
                    continue;
                }

                string relativePath = normalizedFile.Substring(normalizedRoot.Length).TrimStart('/');
                string assetPath = relativePath.Replace('\\', '/');
                if (!assetPath.EndsWith(oldExt, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string newPath = assetPath.Substring(0, assetPath.Length - oldExt.Length) + newExt;
                string moveResult = AssetDatabase.MoveAsset(assetPath, newPath);
                if (string.IsNullOrEmpty(moveResult))
                {
                    successCount++;
                }
                else
                {
                    failCount++;
                    Debug.LogError($"[LuaBatchConverterWindow] 转换失败: {assetPath} -> {newPath} | {moveResult}");
                }
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
            AssetDatabase.Refresh();
        }

        EditorUtility.DisplayDialog(
            "目录转换完成",
            $"扫描目录: {_targetDirectory}\n匹配文件: {files.Length}\n成功: {successCount}\n失败: {failCount}",
            "确定"
        );
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
                if (!path.EndsWith(oldExt, System.StringComparison.OrdinalIgnoreCase))
                {
                    newAssets.Add(asset);
                    continue;
                }

                string newPath = path.Substring(0, path.Length - oldExt.Length) + newExt;
                string moveResult = AssetDatabase.MoveAsset(path, newPath);

                if (string.IsNullOrEmpty(moveResult))
                {
                    successNewPaths.Add(newPath);
                    containerSuccessCount++;
                }
                else
                {
                    newAssets.Add(asset);
                    Debug.LogError($"转换失败 {asset.name}: {moveResult}");
                }
            }

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
