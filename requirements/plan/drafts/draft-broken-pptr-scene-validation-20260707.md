# Draft: Broken PPtr Scene Validation

**Date**: 2026-07-07  
**Status**: Resolved / Archived  
**Category**: Build Pipeline Enhancement

## Problem Statement

构建日志中频繁出现 `Broken text PPtr` 警告：

```
Broken text PPtr in file(Assets/Scenes/Xlua/Test.unity). 
Local file identifier (1652924322) doesn't exist!
```

**影响：**
- 日志污染，增加排查成本
- 潜在的场景完整性问题被掩盖
- Repository health check 通过但存在陈旧引用

**根本原因：**
- `Test.unity` 中存在指向已删除对象的引用（dangling reference）
- Unity 场景序列化时保留了 localFileID，但目标对象已不存在
- 当前构建流程无场景完整性验证环节

## Proposed Solutions

### Solution 1: Manual Scene Cleanup (Immediate)

**操作步骤：**
1. 在 Unity Editor 中打开 `Assets/Scenes/Xlua/Test.unity`
2. 检查 Hierarchy 中所有 GameObject，查找带 Missing Script/Missing Reference 的组件
3. 清理损坏引用：
   - 删除 Missing Script 组件
   - 重新分配 Missing Reference（或删除引用该资源的组件）
4. 保存场景并验证构建日志

**优点：**
- 立即可执行
- 无需修改构建系统

**缺点：**
- 手动操作，无法预防未来重现
- 其他场景可能存在相同问题

### Solution 2: Pre-Build Scene Validation Task (Recommended)

**实现方案：**

在 DAG Pipeline 中添加 `TaskValidateScenes`，作为构建前置检查：

```csharp
public class TaskValidateScenes : IBuildTask
{
    public string Name => "ValidateScenes";
    
    public BuildTaskResult Execute(BuildContext context)
    {
        var sceneGuids = AssetDatabase.FindAssets("t:Scene");
        var brokenScenes = new List<string>();
        
        foreach (var guid in sceneGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (HasBrokenReferences(path))
            {
                brokenScenes.Add(path);
            }
        }
        
        if (brokenScenes.Count > 0)
        {
            string msg = $"Found {brokenScenes.Count} scene(s) with broken references:\n" +
                         string.Join("\n", brokenScenes);
            
            // Option 1: Error -> block build
            // return BuildTaskResult.Fatal(msg);
            
            // Option 2: Warning -> log but continue
            Debug.LogWarning($"[TaskValidateScenes] {msg}");
            return BuildTaskResult.Success();
        }
        
        return BuildTaskResult.Success();
    }
    
    private bool HasBrokenReferences(string scenePath)
    {
        // Read scene file as text and search for:
        // - "m_Script: {fileID: 0}"
        // - References with fileID pointing to non-existent objects
        string content = File.ReadAllText(scenePath);
        return content.Contains("m_Script: {fileID: 0}") ||
               content.Contains("fileID: 11500000, guid: 00000000"); // Missing script pattern
    }
}
```

**集成位置：**
- 在 `TaskPrepareContext` 之后、`TaskCollectAssets` 之前执行
- 归类为 BuildType=All（Full + Hotfix 均执行）

**配置选项：**
```csharp
[Tooltip("场景验证失败时的行为")]
public SceneValidationBehavior OnSceneValidationFail = SceneValidationBehavior.Warning;

public enum SceneValidationBehavior
{
    Ignore,   // 跳过验证
    Warning,  // 记录警告但继续构建
    Error     // 阻止构建
}
```

**优点：**
- 自动化预防，避免人工遗漏
- 提前发现问题，减少构建失败风险
- 可扩展到其他资源类型（Prefab/ScriptableObject）

**缺点：**
- 需要修改 Build Pipeline
- 文本解析可能存在误判（需充分测试）

### Solution 3: Editor Utility for Batch Scene Cleanup

提供独立的编辑器工具，用于批量扫描和修复场景：

```csharp
// Menu: Tools/FYAsset/Cleanup Scenes
public static void CleanupAllScenes()
{
    var sceneGuids = AssetDatabase.FindAssets("t:Scene");
    int fixedCount = 0;
    
    foreach (var guid in sceneGuids)
    {
        string path = AssetDatabase.GUIDToAssetPath(guid);
        var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
        
        bool modified = false;
        foreach (var go in scene.GetRootGameObjects())
        {
            modified |= CleanupGameObject(go);
        }
        
        if (modified)
        {
            EditorSceneManager.SaveScene(scene);
            fixedCount++;
        }
    }
    
    Debug.Log($"[SceneCleanup] Fixed {fixedCount} scene(s)");
}

private static bool CleanupGameObject(GameObject go)
{
    bool modified = false;
    var components = go.GetComponents<Component>();
    
    for (int i = components.Length - 1; i >= 0; i--)
    {
        if (components[i] == null) // Missing component
        {
            GameObjectUtility.RemoveMonoBehavioursWithMissingScript(go);
            modified = true;
        }
    }
    
    foreach (Transform child in go.transform)
    {
        modified |= CleanupGameObject(child.gameObject);
    }
    
    return modified;
}
```

## Recommendation

**短期（已处理）：**
1. `Test.unity` 的坏 `SceneRoots` 引用已直接删除。
2. 暂不增加 Scene Cleanup Utility。

**中长期（纳入 Plan）：**
- 实现 `TaskValidateScenes` 并集成到 Build Pipeline（Solution 2）
- 配置为 Warning 模式，避免阻断现有构建流程
- 逐步提升为 Error 模式（待验证稳定后）

## Dependencies

- 无外部依赖
- 需要 Unity 2020.3+ API（EditorSceneManager）

## Testing Strategy

1. **Unit Test**: 模拟损坏场景文件，验证检测逻辑
2. **Integration Test**: 在测试项目中构建，确认 Task 正确执行
3. **Manual Test**: 故意创建 Missing Script，验证工具能否检测和清理

## Open Questions

1. 是否需要检测 Prefab 中的 Broken PPtr？
2. 检测到损坏引用后，是否需要自动修复（而非仅报告）？
3. 是否需要将验证结果写入构建报告供 CI/CD 追踪？
