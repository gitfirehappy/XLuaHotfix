# Draft: Version System Test Features and Read-Only Protection

**Date**: 2026-07-07  
**Status**: Promoted / Archived  
**Category**: Build Tools Enhancement

Promoted into `requirements/plan/plan-build-state-cleanup-tools-20260707.md`.

## Problem Statement

当前 `VersionDataBase` 在编辑器中存在两个问题：

1. **缺少测试用版本重置功能**：
   - 开发/测试时需要频繁重置版本号为 `1.0.0`
   - 当前只能手动修改 Major/Minor/Patch 字段，操作繁琐且易错
   - 无法同步重置 `LastBuildTime` 和 `DailyBuildCount`

2. **构建元数据可被意外修改**：
   - `LastBuildTime` 和 `DailyBuildCount` 在 Inspector 中可编辑
   - 这两个字段应由构建系统自动管理
   - 手动修改会破坏版本追踪逻辑，导致构建次数统计错误

**影响：**
- 测试流程效率低
- 版本号状态容易被人为污染
- 无法确保版本追踪数据的完整性

## Current Implementation Analysis

<thinking>
让我回顾一下当前的实现：

VersionDataBase.cs:
- CurrentVersion (VersionNumber) - 完全可编辑
- LastBuildTime (string) - 可编辑，应该是只读
- DailyBuildCount (int) - 可编辑，应该是只读

VersionPanel.cs:
- 使用通用的 PropertyField binding，自动显示所有可序列化字段
- 没有针对特定字段的只读保护
- 没有提供重置按钮

需要修改的点：
1. VersionPanel 中对 LastBuildTime 和 DailyBuildCount 使用只读显示
2. 添加 "Reset to 1.0.0 (Test)" 按钮
3. 重置逻辑需要清空 LastBuildTime，设置 DailyBuildCount=0
</thinking>

**VersionDataBase.cs (L10-17):**
```csharp
[Header("当前版本号")]
public VersionNumber CurrentVersion = new() { Major = 1, Minor = 0, Patch = 0 };

[Header("上次构建时间")]
public string LastBuildTime;  // ❌ 可编辑，应该只读

[Header("当日构建次数")]
public int DailyBuildCount;  // ❌ 可编辑，应该只读
```

**VersionPanel.cs (L66-82):**
```csharp
var scrollView = new ScrollView();
scrollView.Bind(_so);

SerializedProperty iterator = _so.GetIterator();
while (iterator.NextVisible(enterChildren))
{
    if (iterator.propertyPath == "m_Script")
        continue;
    
    scrollView.Add(new PropertyField(iterator.Copy()));  // ⚠️ 所有字段均可编辑
}
```

## Proposed Solution

### Part 1: Read-Only Display for Build Metadata

修改 `VersionPanel.cs`，为构建元数据字段添加只读显示：

```csharp
private void Rebuild()
{
    // ... existing toolbar code ...
    
    var scrollView = new ScrollView();
    scrollView.style.flexGrow = 1f;
    
    SerializedProperty iterator = _so.GetIterator();
    bool enterChildren = true;
    
    while (iterator.NextVisible(enterChildren))
    {
        enterChildren = false;
        if (iterator.propertyPath == "m_Script")
            continue;
        
        // 对构建元数据字段使用只读显示
        if (iterator.propertyPath == nameof(VersionDataBase.LastBuildTime) ||
            iterator.propertyPath == nameof(VersionDataBase.DailyBuildCount))
        {
            scrollView.Add(CreateReadOnlyField(iterator.Copy()));
        }
        else
        {
            scrollView.Add(new PropertyField(iterator.Copy()));
        }
    }
    
    _root.Add(scrollView);
}

/// <summary>
/// 创建只读字段显示（Label + Value）
/// </summary>
private VisualElement CreateReadOnlyField(SerializedProperty prop)
{
    var container = new VisualElement();
    container.style.flexDirection = FlexDirection.Row;
    container.style.marginBottom = 2f;
    
    var label = new Label(prop.displayName);
    label.style.width = 120f;
    label.style.unityFontStyleAndWeight = FontStyle.Bold;
    container.Add(label);
    
    var value = new Label(GetPropertyDisplayValue(prop));
    value.style.flexGrow = 1f;
    value.style.color = new Color(0.7f, 0.7f, 0.7f); // Gray to indicate read-only
    container.Add(value);
    
    return container;
}

private string GetPropertyDisplayValue(SerializedProperty prop)
{
    return prop.propertyType switch
    {
        SerializedPropertyType.String => prop.stringValue,
        SerializedPropertyType.Integer => prop.intValue.ToString(),
        _ => prop.displayName
    };
}
```

### Part 2: Test Reset Button

在 Toolbar 中添加 "Reset to 1.0.0" 按钮：

```csharp
private void Rebuild()
{
    // ... existing code ...
    
    VisualElement toolbar = BuildPipelineUI.Toolbar();
    toolbar.Add(BuildPipelineUI.ToolbarButton("Refresh", () =>
    {
        LoadVersionDB();
        Rebuild();
    }, 60f));
    
    // 🆕 新增重置按钮
    toolbar.Add(BuildPipelineUI.ToolbarButton("Reset to 1.0.0 (Test)", () =>
    {
        if (EditorUtility.DisplayDialog(
            "Reset Version", 
            "确定要重置版本号为 1.0.0 吗？\n\n此操作将：\n- 版本号重置为 1.0.0\n- 清空构建时间\n- 重置构建次数为 0\n\n⚠️ 仅用于测试环境！", 
            "确定", "取消"))
        {
            ResetVersionToTest();
        }
    }, 120f));
    
    toolbar.Add(BuildPipelineUI.Spacer());
    _root.Add(toolbar);
    
    // ... rest of the panel ...
}

private void ResetVersionToTest()
{
    if (_versionDB == null)
        return;
    
    _versionDB.CurrentVersion = new VersionNumber 
    { 
        Major = 1, 
        Minor = 0, 
        Patch = 0,
        Build = 0,
        Channel = ""
    };
    _versionDB.LastBuildTime = "";
    _versionDB.DailyBuildCount = 0;
    
    EditorUtility.SetDirty(_versionDB);
    AssetDatabase.SaveAssets();
    
    Debug.Log("[VersionPanel] 版本号已重置为 1.0.0 (Test Mode)");
    
    LoadVersionDB();
    Rebuild();
}
```

### Part 3: Add Warning Indicator

为测试重置版本添加视觉提示（可选增强）：

```csharp
private void Rebuild()
{
    // ... toolbar and scrollView ...
    
    // 🆕 检测是否为测试版本（LastBuildTime 为空且版本为 1.0.0）
    if (_versionDB != null && 
        string.IsNullOrEmpty(_versionDB.LastBuildTime) &&
        _versionDB.CurrentVersion.Major == 1 &&
        _versionDB.CurrentVersion.Minor == 0 &&
        _versionDB.CurrentVersion.Patch == 0)
    {
        var warningBox = new HelpBox(
            "⚠️ 当前为测试版本状态（未经过正式构建）", 
            HelpBoxMessageType.Warning);
        warningBox.style.marginTop = 8f;
        _root.Add(warningBox);
    }
}
```

## Implementation Details

### File Changes

1. **VersionPanel.cs** (修改)
   - `Rebuild()` 方法：添加只读字段判断逻辑
   - `CreateReadOnlyField()` 方法：新增
   - `GetPropertyDisplayValue()` 方法：新增
   - `ResetVersionToTest()` 方法：新增
   - Toolbar：添加 Reset 按钮

2. **VersionDataBase.cs** (可选：添加注释)
   - 为 `LastBuildTime` 和 `DailyBuildCount` 添加 `[Tooltip]` 说明这些字段由系统管理

### Alternative: Use `[ReadOnly]` Attribute

如果项目有自定义 ReadOnly Attribute，可以直接标记字段：

```csharp
public class VersionDataBase : ScriptableObject
{
    [Header("当前版本号")]
    public VersionNumber CurrentVersion = new() { Major = 1, Minor = 0, Patch = 0 };
    
    [Header("上次构建时间")]
    [ReadOnly] // 🆕 需要自定义 PropertyDrawer
    [Tooltip("由构建系统自动更新，请勿手动修改")]
    public string LastBuildTime;
    
    [Header("当日构建次数")]
    [ReadOnly] // 🆕 需要自定义 PropertyDrawer
    [Tooltip("由构建系统自动更新，请勿手动修改")]
    public int DailyBuildCount;
}
```

但这需要实现 `ReadOnlyAttribute` 和对应的 PropertyDrawer，工作量较大。推荐使用 Part 1 的 UI Toolkit 方案。

## Testing Strategy

1. **手动测试 - 只读显示**：
   - 打开 Version Panel
   - 验证 `LastBuildTime` 和 `DailyBuildCount` 显示为灰色 Label（不可编辑）
   - 验证 `CurrentVersion` 仍可正常编辑

2. **手动测试 - 重置功能**：
   - 点击 "Reset to 1.0.0 (Test)" 按钮
   - 确认弹出确认对话框
   - 确认后验证：
     - CurrentVersion 变为 1.0.0
     - LastBuildTime 清空
     - DailyBuildCount 变为 0
   - 取消后验证数据未改变

3. **集成测试 - 构建后状态**：
   - 重置版本为 1.0.0
   - 执行一次完整构建
   - 验证 `LastBuildTime` 和 `DailyBuildCount` 正确更新

## UI Mockup

```
┌─────────────────────────────────────────┐
│ Version Panel                        [x]│
├─────────────────────────────────────────┤
│ [Refresh] [Reset to 1.0.0 (Test)]  ... │
├─────────────────────────────────────────┤
│ ┌───────────────────────────────────┐   │
│ │ 当前版本号                        │   │
│ │   Major:  [1]                     │   │
│ │   Minor:  [0]                     │   │
│ │   Patch:  [0]                     │   │
│ │   Build:  [0]                     │   │
│ │   Channel: [    ]                 │   │
│ │                                   │   │
│ │ 上次构建时间: 2026-07-07 06:30:15 │   │ <- 只读，灰色
│ │ 当日构建次数: 3                   │   │ <- 只读，灰色
│ └───────────────────────────────────┘   │
│                                         │
│ ⚠️ 当前为测试版本状态（未经过正式构建） │ <- 可选
└─────────────────────────────────────────┘
```

## Dependencies

- 无外部依赖
- 需要 Unity 2020.3+ UI Toolkit API

## Open Questions

1. 是否需要在 CLI 构建模式下也提供版本重置命令？
2. 是否需要记录版本重置历史到日志文件？
3. 是否需要限制 Reset 按钮仅在非生产环境可用？（通过宏或配置）

## Recommendation

**优先级：P1 (High)**  
**预估工作量：0.5 人日**

实现顺序：
1. Part 1（只读显示）- 防止意外修改
2. Part 2（重置按钮）- 提升测试效率
3. Part 3（警告提示）- 可选增强
