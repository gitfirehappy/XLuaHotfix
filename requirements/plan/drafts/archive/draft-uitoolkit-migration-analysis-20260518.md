# Draft: FYAsset Editor UI Toolkit 迁移决策分析

> **Status**: Archived — 2026-05-19; extracted into `../../archive/plan-uitoolkit-mechanical-20260519.md` and `../../archive/plan-uitoolkit-complete-20260519.md`
> **Scope**: FYAsset 编辑器 IMGUI → UI Toolkit 迁移可行性、成本、收益评估
> **Trigger**: 开发者询问是否用 UI Toolkit 替换现有 IMGUI 编辑器代码

> **Extraction Note 2026-05-19**: 已提取低风险机械部分：Unity 2022.3.62f3 升级确认、UI Toolkit 新/简单面板基线、简单 reserved/placeholder panel shell 迁移。未提取部分仍停留在 draft：CollectorTreeView / PopupWindow / HeaderGUI / DragAndDrop / Collector 复杂布局 / Pipeline GraphView 混合宿主重设计。
> **Extraction Note 2026-05-19 #2**: 开发者确认继续全量替换，要求“布局完全对标，实现不同”。剩余 BuildPipelineWindow / Settings / Version / Legacy Config / Pipeline / Collector Config / Collector / Inspector / Popup 工作提升为 `../../archive/plan-uitoolkit-complete-20260519.md`。原 draft 的“不建议全量迁移”结论不再作为当前执行约束。
> **Extraction Note 2026-05-19 #3**: `plan-uitoolkit-complete-20260519.md` 已执行完成。`BuildPipelineWindow` 与 active FYAsset build editor panels 改为 UI Toolkit；旧 Collector IMGUI TreeView / property panel / result panel / popup 从 active 编译路径移除；`SOAddressableTagger` 也按 draft 清单补充迁移。`CollectorAssetInspectorGUI` 的 default Inspector header 注入仍保留 IMGUI，因为 Unity 仅提供 `Editor.finishedDefaultHeaderGUI` 回调。

---

## 一、FYAsset 编辑器现状

| 指标 | 数据 |
|------|------|
| 编辑器 UI 代码总量 | ~5,100 LOC |
| EditorWindow 子类 | 1 个 (BuildPipelineWindow, 392 行) |
| Custom Inspector | 1 个 (CollectorSettingInspector, 36 行) |
| PropertyDrawer | **0 个** |
| IMGUI 代码占比 | ~98% |
| 已有 UI Toolkit 代码 | 4 个文件，仅用于 BuildGraphView (Experimental.GraphView) |
| USS / UXML 文件 | **0 个** |

### 关键依赖

| 模块 | IMGUI 技术栈 |
|------|-------------|
| BuildPipelineWindow | EditorWindow.OnGUI, 自定义 splitter, GUIStyle 内联 |
| CollectorPanel | EditorGUILayout, DragAndDrop, 3 段 splitter, PropertyField, SerializedObject |
| CollectorSettingPanel | EditorGUILayout, GenericMenu, 手动滚动计算, SerializedObject |
| CollectorTreeView | **IMGUI.Controls.TreeView**, 3 级层级, 拖拽排序 |
| PipelinePanel | IMGUI + UI Toolkit 混合 (GUIToScreenRect 桥接) |
| CollectorAssetInspectorGUI | **Editor.finishedDefaultHeaderGUI** 全局钩子 |
| SOAddressableTagger | EditorGUILayout.Foldout, ReorderableList |
| CollectorTargetPickerPopup | PopupWindowContent |
| BuildGraphView | 已是 UI Toolkit (Experimental.GraphView) |

---

## 二、Unity 2022.3.6 UI Toolkit 编辑器支持度

### 稳定功能

- EditorWindow.CreateGUI()、Custom Inspector、PropertyDrawer
- TreeView / MultiColumnTreeView / ListView
- 数据绑定 (SerializedProperty, Editor-only)
- UI Builder 可视化编辑、UI Debugger

### 已知痛点（2022.3.6 特有关注）

| 痛点 | FYAsset 影响评估 |
|------|-----------------|
| Undo/Redo 视觉不同步 — ObjectField Undo 后界面不刷新 | **中风险** — 代码大量使用 Undo.RecordObject |
| Prefab Override 显示缺失 — 无蓝色覆盖条/加粗提示 | 不影响（内部工具，非 Prefab 编辑器） |
| 多窗口 viewDataKey 冲突 | 低风险（仅 1 个 EditorWindow） |
| GraphView 标记为 Experimental | **已在用** — API 冻结但无稳定性保证 |
| IMGUI ↔ UI Toolkit 单向嵌套 | 影响迁移策略（不能逐步混用 PropertyDrawer） |

---

## 三、1:1 翻译比例分析

逐文件分类后发现：**~70% 为纯机械 1:1 替换**，AI 可直接翻译。

| 类别 | LOC | 占比 |
|------|-----|------|
| 纯 1:1 机械翻译 | ~3,500 | 70% |
| API 不同但逻辑同 (splitter→TwoPaneSplitView, GUIStyle→USS, Event→Callback) | ~800 | 16% |
| 需重新设计 (TreeView, DragAndDrop, PopupWindow, Editor.finishedDefaultHeaderGUI) | ~700 | 14% |

### 1:1 示例

```csharp
// IMGUI (删除)
GUILayout.Label("hello", EditorStyles.boldLabel);

// UI Toolkit (新增)
new Label("hello") { style = { unityFontStyleAndWeight = FontStyle.Bold } }
```

### 非 1:1 清单

| 模块 | LOC | 原因 |
|------|-----|------|
| CollectorTreeView | 331 | IMGUI TreeView → MultiColumnTreeView API 完全不同 |
| CollectorAssetInspectorGUI | 156 | Editor.finishedDefaultHeaderGUI 无 UI Toolkit 等价钩子 |
| 3 处自定义 splitter | ~100 | → TwoPaneSplitView 或 Pointer 事件重写 |
| CollectorTargetPickerPopup | 160 | PopupWindowContent 无直接等价物 |
| HandleTableDragAndDrop | ~50 | IMGUI DragAndDrop → UI Toolkit DragUpdatedEvent |

---

## 四、2022.3 ↔ Unity 6 API 兼容性分析（核心发现）

### 关键结论：UI Toolkit 编辑器 API 几乎完全向后兼容

2022.3 和 Unity 6 的 **编辑器 UI Toolkit API 约 95% 相同**。以 2022.3.x API 为基线编写的代码可同时运行于：

```
2022.3.x (所有补丁)  →  团结引擎 (基于 2022.3)  →  Unity 6.x
         ←────────── 同一套代码，零改动 ──────────→
```

### 完全相同的核心 API（2022.3 ↔ Unity 6）

| EditorWindow | 布局 | 控件 | 数据 | 样式 |
|-------------|------|------|------|------|
| `CreateGUI()` | `VisualElement` | `Label`, `Button` | `PropertyField` | USS |
| `rootVisualElement` | `ScrollView` | `TextField`, `Toggle` | `SerializedObject.Bind()` | UI Builder |
| `titleContent` | `TwoPaneSplitView` | `DropdownField`, `EnumField` | `BindProperty()` | |
| `Show()` / `Repaint()` | `IMGUIContainer` | `Foldout` | `TrackPropertyValue()` | |
| `OnEnable/OnDisable` | flex-direction | `ListView` | `TrackSerializedObjectValue()` | |
| | | **`MultiColumnTreeView`** | `ObjectField` | |
| | | `ToolbarSearchField` | `InspectorElement` | |
| | | `TabView`, `ToggleButtonGroup` | `CurveField`, `GradientField` | |

### 仅有的 3 个破坏性变更（且 FYAsset 均不涉及）

| 变更 | 影响范围 | FYAsset 是否涉及 |
|------|---------|:---:|
| `ExecuteDefaultActionAtTarget` → `HandleEventBubbleUp` | 自定义 VisualElement 事件处理 | ✗ 无自定义控件 |
| `UxmlFactory`+`UxmlTraits` → `[UxmlElement]`+`[UxmlAttribute]` | 自定义 UXML 控件 | ✗ 零 UXML 文件 |
| `sorting-enabled` → `sorting-mode` (`ColumnSortingMode`) | MultiColumnTreeView 排序 | ✗ 尚未使用 |

**这三个变更都是 opt-in 的** — 旧 API 在 Unity 6 中标记为 `[Obsolete]` 但仍可编译运行（Unity 6.4 前不会移除）。

### 条件编译策略（如需，极少用）

```csharp
#if UNITY_6000_0_OR_NEWER
    treeView.sortingMode = ColumnSortingMode.Default;
#else
    treeView.sortingEnabled = true;
#endif
```

### Unity 6 独占功能（不影响编辑器兼容性）

以下为 Unity 6 新增，编辑器 UI Toolkit 迁移不需要：
- Runtime 数据绑定系统
- World-space UI
- SVG 导入 / USS 后处理滤镜
- TextCore/ATG 高级文本引擎

### 结论

**以 2022.3.x API 为基线编写 UI Toolkit 代码，天然兼容 Unity 6。版本隔离不是迁移障碍。**

---

## 五、版本升级路径

```
当前: Unity 2022.3.6
         │
         ├─→ 2022.3.62f3（最后公开 LTS 补丁）
         │     IMGUI 零改动，UI Toolkit Bug 修复积累
         │
         ├─→ 团结引擎（基于 2022.3 LTS，持续维护）
         │     IMGUI 零改动，Unity 6 功能逐步回植
         │     本土能力：微信小游戏、鸿蒙等
         │
         └─→ Unity 6.x（个人学习可用）
                IMGUI 零改动，UI Toolkit 最完善
                注意：中国区正式渠道不可用
```

### UI Toolkit 修复积累（2022.3.6 → 2022.3.62）

| 修复项 | 关联的 FYAsset 痛点 |
|--------|-------------------|
| ListView 滚动到错误项 | CollectorPanel 列表滚动 |
| EnumField 显示文本不更新 | 构建模式 EnumPopup |
| TreeView 展开异常 | CollectorTreeView |
| ObjectField Undo 回退无法清空 | Undo.RecordObject 场景 |
| IMGUI-UI Toolkit 混合渲染深度排序 | PipelinePanel 混合区域 |

**GraphView 在所有版本中均为 Experimental** — 不受版本选择影响。

---

## 六、决策

### 迁移决策：**不建议全量迁移。采用增量策略。**

| 场景 | 策略 |
|------|------|
| **现有 IMGUI 面板** | 不动 — 代码成熟稳定，无功能缺陷 |
| **新面板 / 新窗口** | 优先用 UI Toolkit (CreateGUI)，以 2022.3.x API 为基线 |
| **新 ScriptableObject Inspector** | 用 CreateInspectorGUI() |
| **BuildGraphView** | 保持现状，等待 Graph Toolkit 稳定后再评估 |
| **版本升级** | 可升 2022.3.62f3 / 团结引擎 / Unity 6，IMGUI 均零改动 |

### 版本策略：**自由选择，API 层面无锁定**

- 2022.3.x API 基线编写的 UI Toolkit 代码同时兼容 2022.3.62f3、团结引擎、Unity 6.x
- IMGUI 代码在三者中**完全相同**，无任何修改必要
- 三个破坏性变更（事件处理 / UxmlTraits / sorting-mode）FYAsset 均不涉及

### 核心理由

1. ~5,100 LOC 中 30% (~1,500 LOC) 不是纯机械翻译，需人工设计
2. AI 可 1 天完成 70% 机械翻译，但测试验证仍需 1-2 天
3. 收益（USS 主题化、UI Builder）对内部构建管线工具非刚需
4. 现有 IMGUI 代码零维护负担，无已知 Bug
5. 迁移引入的回归风险 > 当前痛点
6. **不为了迁移而迁移** — 但版本隔离已被排除，不构成阻力

---

## 七、触发条件（何时重新评估）

满足以下任一条件时，重新评估全量迁移：
- Graph Toolkit 达到稳定（当前仍 Experimental）
- IMGUI 编辑器出现无法修复的性能瓶颈
- 需要运行时 UI 且 UI Toolkit runtime 达到生产级
- 团队有新人需要 UI Builder 可视化编辑降低上手成本

---

## Change Log

| Date | Change |
|------|--------|
| 2026-05-18 | 初始草稿：UI Toolkit 支持度调研 + FYAsset 代码分析 + 1:1 翻译比例量化 + 版本升级路径 + 决策 |
| 2026-05-18 | 修正：Unity 6 可用性（个人渠道），补充 2022.3 ↔ Unity 6 API 兼容性分析（~95% 相同，3 个破坏性变更均不涉及 FYAsset），版本隔离被排除 |
