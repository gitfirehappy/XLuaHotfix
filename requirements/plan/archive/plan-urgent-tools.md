# 临时紧急工具计划 — 五项独立 Editor 工具

> 状态：Archived — 2026-05-19；已执行，待签收
> 创建：2026-04-17
> 最后更新：2026-04-17
> 执行顺序：T1 → T3 → T2 → T4 → T5（按复杂度升序）
> 计划定位：与 E 系构建管线重构并行的独立工具集，互相无依赖

***

## 总览

| 编号 | 名称 | 类型 | 改造量 | 文件数 |
| --- | --- | --- | --- | --- |
| T1 | 通用文件后缀修改工具 | 现有改造 | 小 | 1 |
| T3 | CI/CD 命令行构建支持 | 新增+小改 | 小-中 | 1新+1改 |
| T2 | AssetPostProcessor 资源导入框架 | 新增 | 中 | 3新 |
| T4 | 资源引用缺失检查窗口 | 新增 | 中 | 1新 |
| T5 | 快速关联搜索工具 | 新增 | 中 | 1新 |

***

## T1: 通用文件后缀修改工具

### 目标

将 `LuaBatchConverterWindow` 从 `.lua.txt <-> .lua` 专用转换器升级为任意后缀转换工具，同时保留 LuaScriptContainer 引用更新作为可选模式。

### 文件

改造 1 个文件：
- `Assets/AboutXLua/Scripts/Utility/Editor/LuaBatchConverterWindow.cs`（原地改造）

### 改造清单

1. **新增字段**：
   ```csharp
   private string _sourceExt = ".lua.txt";
   private string _targetExt = ".lua";
   private string _targetDirectory = "Assets/";
   private ConvertMode _convertMode = ConvertMode.ContainerMode;
   ```

2. **新增枚举**：
   ```csharp
   private enum ConvertMode
   {
       ContainerMode,   // 遍历容器中的资产转换 + 引用更新（原有逻辑）
       DirectoryMode    // 扫描目录直接转换，不涉及容器引用
   }
   ```

3. **OnGUI 改造点**（按 UI 从上到下）：
   - 窗口标题改为 `"通用文件后缀转换器"`
   - 菜单路径改为 `[MenuItem("XLua/Lua 工具/通用后缀转换器", false, 2)]`
   - 顶部新增：`_convertMode` 枚举选择（EnumPopup）
   - 顶部新增：`_sourceExt` 和 `_targetExt` 文本输入框
   - 新增预设快捷按钮区域（一排小按钮）：
     - `".lua.txt → .lua"` 按钮：点击填充 `_sourceExt=".lua.txt"`, `_targetExt=".lua"`
     - `".lua → .lua.txt"` 按钮：点击填充反向
   - `DirectoryMode` 时：显示 `_targetDirectory` 文本框 + 文件夹选择按钮，隐藏容器相关 UI
   - `ContainerMode` 时：显示原有容器 UI（数据库 + 额外容器列表）
   - 底部转换按钮文字改为动态：`$"批量转换: {_sourceExt} → {_targetExt}"`，反向按钮 `$"批量转换: {_targetExt} → {_sourceExt}"`

4. **新增 DirectoryMode 转换方法**：
   ```csharp
   private void BatchConvertDirectory(string oldExt, string newExt)
   {
       // 1. 找到 _targetDirectory 对应的磁盘绝对路径
       // 2. Directory.GetFiles(fullPath, "*" + oldExt, SearchOption.AllDirectories)
       // 3. AssetDatabase.StartAssetEditing()
       // 4. 遍历匹配文件，计算新路径，AssetDatabase.MoveAsset
       // 5. AssetDatabase.StopAssetEditing() + Refresh()
       // 6. EditorUtility.DisplayDialog 报告结果
   }
   ```

5. **原有 BatchConvertAll 改造**：
   - 底部按钮点击时根据 `_convertMode` 调用不同方法
   - `ContainerMode` → 走原有 `BatchConvertAll(oldExt, newExt)` 逻辑不变
   - `DirectoryMode` → 走新增 `BatchConvertDirectory(oldExt, newExt)`

### 不做

- 不重构原有 ContainerMode 内部逻辑
- 不新增批量正则匹配功能
- 不处理跨项目文件

### 验收标准

- [x] `DirectoryMode` + 自定义后缀可正常转换
- [x] `ContainerMode` 行为与原工具完全一致（回归）
- [x] 预设按钮点击后自动填充后缀字段
- [x] 菜单路径和窗口标题已更新

***

## T3: CI/CD 命令行构建支持

### 目标

编写 `-executeMethod` 入口，允许命令行触发 BuildFullPackage / BuildHotfix，无需打开 Unity Editor GUI。

### 文件

新增 1 个文件：
- `Assets/AboutXLua/Scripts/Core/Hotfix_AssetPackageManage/Build/BuildManage/Editor/BuildCommandLine.cs`

小改 1 个文件：
- `Assets/AboutXLua/Scripts/Core/Hotfix_AssetPackageManage/Build/BuildManage/Editor/BuildProjectManager.cs`

### 新文件：BuildCommandLine.cs

完整类结构：

```csharp
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// CI/CD 命令行构建入口
/// 用法示例：
///   Unity.exe -batchmode -quit -projectPath "E:/unity/project/XLuaHotfix"
///             -executeMethod BuildCommandLine.Build -buildType hotfix
///   Unity.exe -batchmode -quit -projectPath "E:/unity/project/XLuaHotfix"
///             -executeMethod BuildCommandLine.Build -buildType full
///   附加参数：
///     -confirmRelease    构建后自动执行 ConfirmRelease
///     -logFile build.log 日志输出到文件
/// </summary>
public static class BuildCommandLine
{
    /// <summary>
    /// 唯一入口 — Unity -executeMethod 调用此方法
    /// </summary>
    public static void Build()
    {
        var args = ParseCommandLineArgs();

        string buildType = GetArg(args, "-buildType", "hotfix");
        bool confirmRelease = HasFlag(args, "-confirmRelease");

        Debug.Log($"[BuildCommandLine] 启动 | buildType={buildType} confirmRelease={confirmRelease}");

        try
        {
            switch (buildType.ToLower())
            {
                case "full":
                    BuildProjectManager.BuildFullPackage();
                    break;
                case "hotfix":
                    BuildProjectManager.BuildHotfix();
                    break;
                default:
                    Debug.LogError($"[BuildCommandLine] 未知构建类型: {buildType}，支持: full / hotfix");
                    EditorApplication.Exit(1);
                    return;
            }

            if (confirmRelease)
            {
                BuildProjectManager.ConfirmReleaseHotfix();
                Debug.Log("[BuildCommandLine] ConfirmRelease 已执行");
            }

            Debug.Log("[BuildCommandLine] 构建完成，exit 0");
            EditorApplication.Exit(0);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[BuildCommandLine] 构建失败: {ex}");
            EditorApplication.Exit(1);
        }
    }

    /// <summary>解析命令行参数为 key-value 字典</summary>
    private static Dictionary<string, string> ParseCommandLineArgs()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i].StartsWith("-") && i + 1 < args.Length && !args[i + 1].StartsWith("-"))
            {
                result[args[i]] = args[i + 1];
                i++;
            }
            else if (args[i].StartsWith("-"))
            {
                result[args[i]] = "";
            }
        }
        return result;
    }

    private static string GetArg(Dictionary<string, string> args, string key, string defaultValue)
    {
        return args.TryGetValue(key, out string value) && !string.IsNullOrEmpty(value)
            ? value
            : defaultValue;
    }

    private static bool HasFlag(Dictionary<string, string> args, string key)
    {
        return args.ContainsKey(key);
    }
}
#endif
```

### 改动文件：BuildProjectManager.cs

需要 3 处 batchmode 兼容改动：

**改动 1**：`BuildFullPackage()` 末尾，`EditorApplication.ExecuteMenuItem` 和 `Debug.Log` 提示改为 batchmode 感知：
```csharp
// 原代码：
EditorApplication.ExecuteMenuItem("File/Build Settings...");
Debug.Log("...");

// 改为：
if (!Application.isBatchMode)
{
    EditorApplication.ExecuteMenuItem("File/Build Settings...");
    Debug.Log("[BuildProjectManager] 请在弹出的Build Settings中选择目标平台和场景...");
}
```

**改动 2**：`GenerateVersionStateFile()` 内的 `EditorUtility.DisplayDialog` 改为 batchmode 感知：
```csharp
// 原代码（约第 291 行）：
EditorUtility.DisplayDialog("热更包过大", $"...", "OK");
return;

// 改为：
if (Application.isBatchMode)
{
    Debug.LogError($"[BuildProjectManager] 热更包过大，构建中止");
    throw new Exception("热更包大小超过阈值");
}
else
{
    EditorUtility.DisplayDialog("热更包过大", $"...", "OK");
    return;
}
```

**改动 3**：`ExecuteBuildFlow()` 末尾的 `EditorUtility.RevealInFinder` 改为 batchmode 感知：
```csharp
// 原代码（约第 165 行）：
EditorUtility.RevealInFinder(hotfixOutputDir);

// 改为：
if (!Application.isBatchMode)
{
    EditorUtility.RevealInFinder(hotfixOutputDir);
}
```

### 不做

- 不做输出路径覆盖（当前 OutputRoot 已足够）
- 不做版本号命令行覆盖（版本号由 VersionDataBase 管理）
- 不做平台切换（依赖 Unity 启动参数 -buildTarget）
- 不做 CI 集成脚本（.yml/.sh），只做 Unity 侧入口

### 验收标准

- [ ] `Unity.exe -batchmode -quit -executeMethod BuildCommandLine.Build -buildType hotfix` 正常触发构建
- [ ] 构建成功 exit code 0，失败 exit code 1
- [ ] batchmode 下无 GUI 弹窗阻塞（DisplayDialog / ExecuteMenuItem / RevealInFinder 全部跳过）
- [ ] 原有 MenuItem 菜单调用不受影响（回归）

***

## T2: AssetPostProcessor 资源导入框架

### 目标

搭建可扩展的 `AssetPostprocessor` 框架，通过接口 + 反射自动发现规则，新增规则只需实现接口。先用一个 UI 纹理导入示例验证框架可用。

### 文件

新增 3 个文件：

```
Assets/AboutXLua/Scripts/Utility/Editor/AssetImportPipeline/
├── IAssetImportRule.cs              # 规则接口
├── AssetImportPipelineProcessor.cs  # 框架主体（AssetPostprocessor）
└── Rules/
    └── UITextureImportRule.cs       # 示例规则
```

### 文件 1：IAssetImportRule.cs

```csharp
using UnityEditor;

/// <summary>
/// 资源导入规则接口 — 每种特殊资源处理实现此接口
/// </summary>
public interface IAssetImportRule
{
    /// <summary>规则名称，用于日志输出</summary>
    string RuleName { get; }

    /// <summary>是否匹配该资源路径（返回 true 则执行此规则）</summary>
    bool Match(string assetPath);

    /// <summary>
    /// 在 Unity 导入资源前执行（可修改 Importer 设置）
    /// 典型用法：修改 TextureImporter / ModelImporter / AudioImporter 参数
    /// </summary>
    void OnPreprocess(AssetImporter importer);

    /// <summary>
    /// 在 Unity 导入资源后执行（可选，默认空实现）
    /// 典型用法：自动打标签、移动文件、生成附加数据
    /// </summary>
    void OnPostprocess(string assetPath);
}
```

注意：`OnPostprocess` 不使用 C# 8 默认接口方法（Unity 兼容性），改为让框架侧判空或用抽象基类。如果目标 Unity 版本支持 C# 8，可用默认实现 `void OnPostprocess(string assetPath) { }`。

**兼容方案**：提供可选抽象基类：
```csharp
public abstract class AssetImportRuleBase : IAssetImportRule
{
    public abstract string RuleName { get; }
    public abstract bool Match(string assetPath);
    public abstract void OnPreprocess(AssetImporter importer);
    public virtual void OnPostprocess(string assetPath) { }
}
```

### 文件 2：AssetImportPipelineProcessor.cs

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 资源导入流水线 — 继承 AssetPostprocessor，自动发现并执行所有 IAssetImportRule 实现
/// </summary>
public class AssetImportPipelineProcessor : AssetPostprocessor
{
    private static List<IAssetImportRule> _rules;

    private static List<IAssetImportRule> Rules
    {
        get
        {
            if (_rules == null) RebuildRuleCache();
            return _rules;
        }
    }

    /// <summary>通过反射扫描所有程序集，找到 IAssetImportRule 实现类并实例化</summary>
    private static void RebuildRuleCache()
    {
        _rules = new List<IAssetImportRule>();
        var ruleType = typeof(IAssetImportRule);
        var assemblies = AppDomain.CurrentDomain.GetAssemblies();
        foreach (var assembly in assemblies)
        {
            Type[] types;
            try { types = assembly.GetTypes(); }
            catch (System.Reflection.ReflectionTypeLoadException e) { types = e.Types; }

            if (types == null) continue;
            foreach (var type in types)
            {
                if (type == null || type.IsInterface || type.IsAbstract) continue;
                if (!ruleType.IsAssignableFrom(type)) continue;
                try
                {
                    _rules.Add((IAssetImportRule)Activator.CreateInstance(type));
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[AssetImportPipeline] 无法实例化规则 {type.Name}: {ex.Message}");
                }
            }
        }
        Debug.Log($"[AssetImportPipeline] 已加载 {_rules.Count} 条导入规则");
    }

    /// <summary>手动刷新规则缓存（新增/修改规则后如未自动生效可点此）</summary>
    [MenuItem("XLua/Lua 工具/刷新导入规则缓存")]
    public static void RefreshRules() { _rules = null; }

    // ===== Unity AssetPostprocessor 回调 =====

    private void OnPreprocessTexture() { RunPreprocess(assetImporter); }
    private void OnPreprocessModel()   { RunPreprocess(assetImporter); }
    private void OnPreprocessAudio()   { RunPreprocess(assetImporter); }

    private static void OnPostprocessAllAssets(
        string[] importedAssets, string[] deletedAssets,
        string[] movedAssets, string[] movedFromAssetPaths)
    {
        foreach (string path in importedAssets)
        {
            foreach (var rule in Rules)
            {
                if (rule.Match(path))
                {
                    rule.OnPostprocess(path);
                }
            }
        }
    }

    // ===== 内部方法 =====

    private void RunPreprocess(AssetImporter importer)
    {
        foreach (var rule in Rules)
        {
            if (rule.Match(importer.assetPath))
            {
                Debug.Log($"[AssetImportPipeline] 应用规则 '{rule.RuleName}' → {importer.assetPath}");
                rule.OnPreprocess(importer);
            }
        }
    }
}
```

### 文件 3：Rules/UITextureImportRule.cs（示例）

```csharp
using UnityEditor;

/// <summary>
/// 示例规则：Assets/AboutXLua/Art/UI/ 目录下的图片自动设为 Sprite + 关闭 Mipmap
/// </summary>
public class UITextureImportRule : AssetImportRuleBase
{
    public override string RuleName => "UI纹理自动设置";

    public override bool Match(string assetPath)
    {
        return assetPath.StartsWith("Assets/AboutXLua/Art/UI/") &&
               (assetPath.EndsWith(".png") || assetPath.EndsWith(".jpg") || assetPath.EndsWith(".tga"));
    }

    public override void OnPreprocess(AssetImporter importer)
    {
        if (importer is TextureImporter textureImporter)
        {
            textureImporter.textureType = TextureImporterType.Sprite;
            textureImporter.mipmapEnabled = false;
            textureImporter.spritePixelsPerUnit = 100;
        }
    }
}
```

### 设计决策

1. **规则发现**：反射自动扫描，不需要手动注册 — 新增规则只需实现接口
2. **缓存策略**：首次调用时缓存规则列表，提供菜单手动刷新
3. **与 LuaAssetImporter 的关系**：`LuaAssetImporter` 是 `ScriptedImporter`（处理 .lua 文件格式识别），与本框架的 `AssetPostprocessor`（处理导入参数设置）职责不同，互不冲突
4. **AssetImportRuleBase**：提供抽象基类避免 C# 8 默认接口方法的兼容性问题

### 不做

- 不做规则优先级/排序（V1 规则无序执行，冲突概率低）
- 不做规则开关 UI（V1 通过删除/注释规则类控制）
- 不做 ScriptedImporter 集成（那是另一套机制）

### 验收标准

- [ ] 拖入一张 png 到 `Assets/AboutXLua/Art/UI/`，自动被设为 Sprite + 关闭 Mipmap
- [ ] 拖入同一张 png 到其他目录，不触发 UI 规则
- [ ] `XLua/Lua 工具/刷新导入规则缓存` 菜单可用
- [ ] 新增一个空规则类实现 `IAssetImportRule`，编译后自动被框架发现

***

## T4: 资源引用缺失检查窗口

### 目标

EditorWindow 扫描项目中 Prefab 和 Scene 的序列化引用，找出所有 Missing Reference（脚本丢失 / 引用断裂），以列表展示，支持一键定位和关键字过滤。

### 文件

新增 1 个文件：
- `Assets/AboutXLua/Scripts/Utility/Editor/MissingReferenceFinderWindow.cs`

### 完整类结构

```csharp
#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public class MissingReferenceFinderWindow : EditorWindow
{
    // ===== 数据结构 =====
    private struct MissingEntry
    {
        public string assetPath;       // Prefab/Scene 路径
        public string objectName;      // GameObject 层级路径（如 "Canvas/Panel/Button"）
        public string componentType;   // 组件类型名（Missing Component 时为 "Unknown"）
        public string propertyPath;    // 序列化属性路径（Missing Component 时为空）
        public bool isComponent;       // true = 整个组件丢失, false = 组件上某属性引用丢失
    }

    // ===== 字段 =====
    private List<MissingEntry> _results = new List<MissingEntry>();
    private Vector2 _scrollPos;
    private bool _scanPrefabs = true;
    private bool _scanScenes = true;
    private string _searchDirectory = "Assets/";
    private string _filterKeyword = "";

    [MenuItem("XLua/Lua 工具/资源引用缺失检查", false, 20)]
    public static void ShowWindow()
    {
        GetWindow<MissingReferenceFinderWindow>("引用缺失检查");
    }

    // ===== OnGUI =====
    private void OnGUI()
    {
        // 顶部控制区
        GUILayout.Label("资源引用缺失检查", EditorStyles.boldLabel);
        _searchDirectory = EditorGUILayout.TextField("扫描目录", _searchDirectory);
        _scanPrefabs = EditorGUILayout.Toggle("扫描 Prefab", _scanPrefabs);
        _scanScenes = EditorGUILayout.Toggle("扫描 Scene", _scanScenes);

        if (GUILayout.Button("开始扫描", GUILayout.Height(30)))
            ScanAll();

        // 过滤
        _filterKeyword = EditorGUILayout.TextField("过滤关键字", _filterKeyword);
        EditorGUILayout.LabelField($"发现 {_results.Count} 处缺失引用");

        // 结果列表
        _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);
        foreach (var entry in _results)
        {
            // 关键字过滤
            if (!string.IsNullOrEmpty(_filterKeyword) &&
                !entry.assetPath.Contains(_filterKeyword) &&
                !entry.objectName.Contains(_filterKeyword))
                continue;

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

    // ===== 扫描主入口 =====
    private void ScanAll()
    {
        _results.Clear();

        if (_scanPrefabs)
        {
            string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { _searchDirectory });
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                EditorUtility.DisplayProgressBar("扫描 Prefab", path, (float)i / guids.Length);
                ScanPrefab(path);
            }
        }

        if (_scanScenes)
        {
            string[] guids = AssetDatabase.FindAssets("t:Scene", new[] { _searchDirectory });
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                EditorUtility.DisplayProgressBar("扫描 Scene", path, (float)i / guids.Length);
                ScanScene(path);
            }
        }

        EditorUtility.ClearProgressBar();
        Debug.Log($"[MissingReferenceFinder] 扫描完成，发现 {_results.Count} 处缺失引用");
    }

    // ===== Prefab 扫描 =====
    private void ScanPrefab(string assetPath)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
        if (prefab == null) return;
        ScanGameObject(prefab, assetPath, "");
    }

    // ===== Scene 扫描 =====
    private void ScanScene(string scenePath)
    {
        var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
        foreach (var root in scene.GetRootGameObjects())
            ScanGameObject(root, scenePath, "");
        EditorSceneManager.CloseScene(scene, true);
    }

    // ===== 递归扫描 GameObject =====
    private void ScanGameObject(GameObject go, string assetPath, string parentPath)
    {
        string currentPath = string.IsNullOrEmpty(parentPath) ? go.name : $"{parentPath}/{go.name}";

        Component[] components = go.GetComponents<Component>();
        for (int i = 0; i < components.Length; i++)
        {
            // 检测 Missing Component（脚本丢失时 Component 为 null）
            if (components[i] == null)
            {
                _results.Add(new MissingEntry
                {
                    assetPath = assetPath,
                    objectName = currentPath,
                    componentType = "Unknown",
                    propertyPath = "",
                    isComponent = true
                });
                continue;
            }

            // 检测 Missing Reference（遍历序列化属性）
            SerializedObject so = new SerializedObject(components[i]);
            SerializedProperty sp = so.GetIterator();
            while (sp.NextVisible(true))
            {
                if (sp.propertyType == SerializedPropertyType.ObjectReference &&
                    sp.objectReferenceValue == null &&
                    sp.objectReferenceInstanceIDValue != 0)
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

        // 递归子物体
        for (int i = 0; i < go.transform.childCount; i++)
            ScanGameObject(go.transform.GetChild(i).gameObject, assetPath, currentPath);
    }
}
#endif
```

### 核心检测逻辑说明

- **Missing Component**：`go.GetComponents<Component>()` 返回的数组中，如果某元素为 `null`，说明挂载的 MonoBehaviour 脚本已丢失
- **Missing Reference**：`SerializedProperty.propertyType == ObjectReference` 且 `objectReferenceValue == null` 且 `objectReferenceInstanceIDValue != 0` — 这表示该属性曾经指向一个对象但现在找不到了

### 不做

- 不做自动修复（只做诊断）
- 不做 ScriptableObject 内部引用检查（V1 只查 Prefab/Scene）
- 不做材质/Shader 引用检查（可作为 V2 扩展）

### 验收标准

- [ ] 扫描后列表正确显示 Missing Component 和 Missing Reference
- [ ] 点击"定位"按钮在 Project 窗口高亮对应 Prefab/Scene
- [ ] 进度条正常显示
- [ ] 过滤关键字可缩小结果范围
- [ ] 扫描大目录不报错（有 ProgressBar 防止 Editor 假死）

***

## T5: 快速关联搜索工具

### 目标

EditorWindow 提供两种搜索模式：
1. **反向引用搜索**：输入任意资产（LuaScriptContainer、SO、Texture 等），找出所有引用它的 Prefab / Scene / 其他资产
2. **Bridge configKey 关联搜索**：输入 configKey 字符串关键字，找出哪些 Prefab/Scene 上的 Bridge 组件使用了该 key，并关联到对应的实际 SO 资产

### 背景：Bridge 引用模式

项目中 Bridge 组件的引用链路为：
```
Prefab/Scene 上的 Bridge MonoBehaviour
  ↓ string configKey（Addressable Key）
对应的 Config SO 资产（如 LuaBehaviourConfigSO / ScriptObjectBridgeConfig / StateAnimationConfigSO）
  ↓ 内部字段
实际脚本名/资产 Key
```

涉及的 Bridge 类型：
| Bridge 类 | configKey 指向 | 位置 |
| --- | --- | --- |
| LuaBehaviourBridge | LuaBehaviourConfigSO | Scripts/Framework/Bridge/Utils/ |
| ScriptObjectBridge | ScriptObjectBridgeConfig | Scripts/Framework/Bridge/ |
| AnimBridge | StateAnimationConfigSO | Scripts/Framework/Bridge/Anime/ |

### 文件

新增 1 个文件：
- `Assets/AboutXLua/Scripts/Utility/Editor/AssetAssociationSearchWindow.cs`

### 完整类结构

```csharp
#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public class AssetAssociationSearchWindow : EditorWindow
{
    // ===== 搜索模式 =====
    private enum SearchMode
    {
        ReverseReference,   // 反向引用：谁引用了这个资产
        BridgeKeySearch     // Bridge 关联：configKey → SO → 引用者
    }

    // ===== 结果条目 =====
    private struct SearchResult
    {
        public string assetPath;       // 引用者路径（Prefab/Scene/SO）
        public string objectName;      // GameObject 名称或层级路径
        public string componentType;   // 组件类型名
        public string propertyInfo;    // 属性路径或关联信息
        public string detail;          // 额外信息（如 configKey 值、关联 SO 路径）
    }

    // ===== 字段 =====
    private SearchMode _searchMode = SearchMode.ReverseReference;
    private Object _targetAsset;
    private string _bridgeKeyword = "";
    private string _searchDirectory = "Assets/";
    private List<SearchResult> _results = new List<SearchResult>();
    private Vector2 _scrollPos;

    [MenuItem("XLua/Lua 工具/快速关联搜索", false, 21)]
    public static void ShowWindow()
    {
        GetWindow<AssetAssociationSearchWindow>("关联搜索");
    }

    // ===== OnGUI =====
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

        // 结果列表
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
                EditorGUILayout.LabelField($"  属性: {r.propertyInfo}", EditorStyles.miniLabel);
            if (!string.IsNullOrEmpty(r.detail))
                EditorGUILayout.LabelField($"  关联: {r.detail}", EditorStyles.miniLabel);
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

    // ===== 反向引用 UI =====
    private void DrawReverseReferenceUI()
    {
        _targetAsset = EditorGUILayout.ObjectField("目标资产", _targetAsset, typeof(Object), false);
        if (GUILayout.Button("搜索引用者", GUILayout.Height(30)) && _targetAsset != null)
            SearchReverseReferences();
    }

    // ===== Bridge 关联 UI =====
    private void DrawBridgeKeySearchUI()
    {
        _bridgeKeyword = EditorGUILayout.TextField("configKey 关键字", _bridgeKeyword);
        if (GUILayout.Button("搜索 Bridge 关联", GUILayout.Height(30))
            && !string.IsNullOrEmpty(_bridgeKeyword))
            SearchBridgeAssociations();
    }

    // ===== 反向引用搜索实现 =====
    private void SearchReverseReferences()
    {
        _results.Clear();
        string targetPath = AssetDatabase.GetAssetPath(_targetAsset);

        // 扫描 Prefab + Scene + ScriptableObject
        string[] guids = AssetDatabase.FindAssets(
            "t:Prefab t:Scene t:ScriptableObject",
            new[] { _searchDirectory });

        for (int i = 0; i < guids.Length; i++)
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(guids[i]);
            EditorUtility.DisplayProgressBar("搜索引用", assetPath, (float)i / guids.Length);

            // 用 GetDependencies 做快速筛选（非递归，只看直接依赖）
            string[] deps = AssetDatabase.GetDependencies(assetPath, false);
            bool found = false;
            foreach (string dep in deps)
            {
                if (dep == targetPath) { found = true; break; }
            }

            if (!found) continue;

            // 找到引用 — 尝试定位具体组件
            GameObject go = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (go != null)
            {
                FindReferencesInGameObject(go, assetPath, targetPath, "");
            }
            else
            {
                // Scene 或 SO — 记录路径级结果
                _results.Add(new SearchResult
                {
                    assetPath = assetPath,
                    objectName = "(非Prefab资产)",
                    componentType = AssetDatabase.GetMainAssetTypeAtPath(assetPath)?.Name ?? "Unknown",
                    propertyInfo = "",
                    detail = ""
                });
            }
        }

        EditorUtility.ClearProgressBar();
        Debug.Log($"[AssetAssociationSearch] 反向引用搜索完成，找到 {_results.Count} 条");
    }

    /// <summary>在 GameObject 树中定位具体引用了 targetPath 的组件和属性</summary>
    private void FindReferencesInGameObject(GameObject go, string assetPath,
        string targetPath, string parentPath)
    {
        string currentPath = string.IsNullOrEmpty(parentPath) ? go.name : $"{parentPath}/{go.name}";

        foreach (var comp in go.GetComponents<Component>())
        {
            if (comp == null) continue;
            SerializedObject so = new SerializedObject(comp);
            SerializedProperty sp = so.GetIterator();
            while (sp.NextVisible(true))
            {
                if (sp.propertyType == SerializedPropertyType.ObjectReference
                    && sp.objectReferenceValue != null)
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
                            detail = ""
                        });
                    }
                }
            }
        }

        for (int i = 0; i < go.transform.childCount; i++)
            FindReferencesInGameObject(go.transform.GetChild(i).gameObject,
                assetPath, targetPath, currentPath);
    }

    // ===== Bridge configKey 关联搜索实现 =====
    private void SearchBridgeAssociations()
    {
        _results.Clear();

        // Bridge 类型列表 — 每种 Bridge 的 configKey 字段名
        // LuaBehaviourBridge.configKey / ScriptObjectBridge.configKey / AnimBridge.configKey
        string[] bridgeTypeNames = { "LuaBehaviourBridge", "ScriptObjectBridge", "AnimBridge" };

        string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { _searchDirectory });
        string[] sceneGuids = AssetDatabase.FindAssets("t:Scene", new[] { _searchDirectory });

        // 搜索 Prefab
        for (int i = 0; i < prefabGuids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(prefabGuids[i]);
            EditorUtility.DisplayProgressBar("搜索 Bridge", path,
                (float)i / (prefabGuids.Length + sceneGuids.Length));
            GameObject go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (go != null) ScanBridgeInGameObject(go, path, "", bridgeTypeNames);
        }

        // 搜索 Scene
        for (int i = 0; i < sceneGuids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(sceneGuids[i]);
            EditorUtility.DisplayProgressBar("搜索 Bridge", path,
                (float)(prefabGuids.Length + i) / (prefabGuids.Length + sceneGuids.Length));
            var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            foreach (var root in scene.GetRootGameObjects())
                ScanBridgeInGameObject(root, path, "", bridgeTypeNames);
            EditorSceneManager.CloseScene(scene, true);
        }

        EditorUtility.ClearProgressBar();
        Debug.Log($"[AssetAssociationSearch] Bridge 关联搜索完成，找到 {_results.Count} 条");
    }

    /// <summary>递归扫描 GameObject 上的 Bridge 组件，匹配 configKey</summary>
    private void ScanBridgeInGameObject(GameObject go, string assetPath,
        string parentPath, string[] bridgeTypeNames)
    {
        string currentPath = string.IsNullOrEmpty(parentPath) ? go.name : $"{parentPath}/{go.name}";

        foreach (var comp in go.GetComponents<Component>())
        {
            if (comp == null) continue;
            string typeName = comp.GetType().Name;

            // 检查是否是 Bridge 类型
            bool isBridge = false;
            foreach (string bt in bridgeTypeNames)
            {
                if (typeName == bt) { isBridge = true; break; }
            }
            if (!isBridge) continue;

            // 读取 configKey 字段
            SerializedObject so = new SerializedObject(comp);
            SerializedProperty keyProp = so.FindProperty("configKey");
            if (keyProp == null || keyProp.propertyType != SerializedPropertyType.String) continue;

            string configKeyValue = keyProp.stringValue;
            if (string.IsNullOrEmpty(configKeyValue)) continue;

            // 匹配关键字（不区分大小写）
            if (!configKeyValue.ToLower().Contains(_bridgeKeyword.ToLower())) continue;

            // 尝试关联到实际 SO 资产（通过 Addressable Key 或文件名搜索）
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
            ScanBridgeInGameObject(go.transform.GetChild(i).gameObject,
                assetPath, currentPath, bridgeTypeNames);
    }

    /// <summary>
    /// 通过 configKey 值尝试找到对应的 SO 资产
    /// 搜索策略：在 SO 目录下按文件名匹配（configKey 通常就是 SO 的 Addressable Address）
    /// </summary>
    private string TryFindSOByKey(string configKey)
    {
        // 策略 1：在 Assets/AboutXLua/SO/ 下搜索同名 .asset 文件
        string[] guids = AssetDatabase.FindAssets(configKey + " t:ScriptableObject",
            new[] { "Assets/AboutXLua/SO" });

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            string fileName = System.IO.Path.GetFileNameWithoutExtension(path);
            if (fileName == configKey) return path;
        }

        // 策略 2：模糊搜索（文件名包含 configKey）
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            return path; // 取第一个模糊匹配
        }

        return null;
    }
}
#endif
```

### 设计决策

1. **反向引用方法**：使用 `AssetDatabase.GetDependencies(path, false)` 做快速筛选（O(1) per asset），匹配后再遍历序列化属性定位具体引用位置
2. **Bridge 关联策略**：通过 `SerializedObject.FindProperty("configKey")` 读取字符串值，再在 `Assets/AboutXLua/SO/` 下按文件名匹配关联的 SO
3. **Scene 扫描**：使用 `OpenScene(Additive)` + `CloseScene(true)` 避免影响当前打开的场景
4. **Bridge 类型硬编码**：V1 硬编码三种 Bridge 类型名，可通过 bridgeTypeNames 数组轻松扩展

### 不做

- 不做 Addressable 地址反查（需要 AA Settings 接口，复杂度高）
- 不做全资产类型反向引用（V1 只查 Prefab/Scene/SO）
- 不做引用图可视化（V1 只做列表）
- 不做正则匹配（V1 用 Contains 模糊匹配）

### 验收标准

- [ ] 反向引用模式：拖入一个 LuaScriptContainer，找到所有引用它的 Prefab
- [ ] 反向引用模式：拖入一个 Texture，找到所有引用它的 Prefab
- [ ] Bridge 模式：输入 "Player"，找到所有 configKey 包含 "Player" 的 Bridge 组件
- [ ] Bridge 模式：搜索结果显示关联的 SO 路径
- [ ] 点击"定位"按钮能高亮对应 Prefab
- [ ] 进度条正常显示

***

## Change Log

| 日期 | 版本 | 描述 | 作者 |
| --- | --- | --- | --- |
| 2026-04-17 | 1.0.0 | 初始五项工具计划草案 | Claude + 开发者 |
| 2026-04-17 | 1.1.0 | 按计划顺序完成 T1/T3/T2/T4/T5 代码落地；通过 dotnet build 编译验证（0 error） | Hephaestus |
