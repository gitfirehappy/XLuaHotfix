# Sub-Plan E13: Legacy Pipeline 侧边栏重组 + 面板骨架

> **Status**: ✅ Executed — 2026-05-15 代码已落地（T1-T4 完成，dotnet build 0 errors）
> **Risk**: Low (Editor-only UI restructure, no runtime impact)
> **Dependencies**: 无硬依赖，可并行 E7
> **Scope**: BuildPipelineWindow 侧边栏 4 组重组 + 互斥灰显 + LegacyConfigPanel（AA 配置摘要+按钮）+ 占位面板
> **Review**: review-E7-E13-plan-20260515.md（已归档 review/archive/）— 4 findings 已吸收至 E7 新草稿
> **不涉及**: 旧管线 Task 化（延后）、Diff 面板功能（等 E7）、产物查看（等 E7）

---

## Objective

将 BuildPipelineWindow 侧边栏从 3 组扩展为 4 组，新增 LEGACY PIPELINE 与 AB PIPELINE 对称结构，实现互斥灰显。为后续旧管线功能填充提供框架。

---

## Sidebar Final Structure

```
SETTINGS          → [SettingsPanel]                                              (1 panel)
LEGACY PIPELINE   → [LegacyConfigPanel, LegacyBuildPanel, LegacyReportPanel]    (3 panels)
AB PIPELINE       → [CollectorSettingPanel, CollectorPanel, PipelinePanel, BuilderPanel]  (4 panels)
MANAGE            → [VersionPanel]                                               (1 panel)
```

Total: 9 panels (原 6 + 新 3)

---

## Confirmed Design Decisions

### D1: Mutual Exclusion Gray-Out

| UseABBackend | LEGACY PIPELINE | AB PIPELINE |
|:---:|:---:|:---:|
| true | 灰显（不可交互） | 正常 |
| false | 正常 | 灰显（不可交互） |

SETTINGS 和 MANAGE 始终可用。灰显组点击时显示提示 banner（与现有 AB 灰显行为一致）。

### D2: AA Groups Embed Strategy

LegacyConfigPanel 采用**摘要+按钮**方案：
- 上半部分显示 Addressables 配置摘要（Group 数量、当前 Profile、Build/Load 路径）
- 下半部分放"Open Addressables Groups Window"按钮，点击调用 `EditorWindow.GetWindow<AddressableAssetsWindow>()`

不使用 `CreateEditor` 嵌入（AA Groups 是独立 EditorWindow，无法嵌入）。

### D3: Index Management Refactor

当前灰显逻辑 hardcode panel index range（1-4 = AB PIPELINE）。重组后改为基于 SidebarGroup 的 groupName 判断，消除 magic number。

### D4: Placeholder Panels

LegacyBuildPanel / LegacyReportPanel 作为占位面板，OnGUI 只渲染静态提示文本。不加载数据、不执行逻辑。后续独立 plan 填充。

### D5: Collapsible Sidebar Groups

侧边栏组标题可点击折叠/展开。9 个面板按钮全部展开会拥挤，折叠后只显示当前活跃组的按钮 + 其他组的组标题。

行为规则：
- 点击组标题 → 展开该组，折叠其他组
- 当前活跃面板所在组始终展开
- 组标题显示展开/折叠箭头图标

---

## New Files

| File | Path | Assembly | Lines (est.) | Description |
|------|------|----------|:---:|-------------|
| LegacyConfigPanel.cs | Build/Editor/ | Editor | ~80 | 嵌入 AA Groups Inspector |
| LegacyBuildPanel.cs | Build/Editor/ | Editor | ~30 | 占位：构建触发待实现 |
| LegacyReportPanel.cs | Build/Editor/ | Editor | ~30 | 占位：报告/Diff 待 E7 |

Total: 3 new files, ~140 lines estimated.

---

## Modified Files

| File | Change | Risk |
|------|--------|------|
| BuildPipelineWindow.cs | SidebarGroup[] 3→4 组；InitPanels() 6→9 面板；灰显逻辑改为互斥（基于 groupName）；面板索引调整 | Medium — 核心窗口，索引偏移需仔细 |

### Not Modified

- IBuildPipelinePanel.cs — 接口不变
- 所有现有面板 — 功能不变，仅索引偏移
- 运行时代码 — 零改动

---

## Task Breakdown

| Task | Content | Depends On |
|------|---------|:---:|
| E13-T1 | 创建 LegacyConfigPanel.cs — IBuildPipelinePanel 实现，AA 配置摘要（Group 数/Profile/路径）+ "Open Groups Window" 按钮 | — |
| E13-T2 | 创建 LegacyBuildPanel.cs — 占位面板（显示提示文本 + 上次构建时间 from VersionDataBase.LastBuildTime） | — |
| E13-T3 | 创建 LegacyReportPanel.cs — 占位面板（显示提示文本） | — |
| E13-T4 | 修改 BuildPipelineWindow.cs — SidebarGroup 4 组 + InitPanels 9 面板 + 互斥灰显 + 索引重构 + 折叠组逻辑（点击组标题展开/折叠，活跃面板所在组始终展开） | T1-T3 |
| E13-T5 | 编译验证 (`dotnet build XLuaHotfix.sln`) | T4 |

---

## Invariants (Must Hold After E13)

1. `UseABBackend=true` 时 LEGACY PIPELINE 组灰显，AB PIPELINE 正常
2. `UseABBackend=false` 时 AB PIPELINE 组灰显，LEGACY PIPELINE 正常
3. SETTINGS 和 MANAGE 始终可用，不受 UseABBackend 影响
4. LegacyConfigPanel 能渲染 Addressables Settings 相关信息
5. 现有 AB PIPELINE 4 个面板功能完全不受影响
6. `dotnet build XLuaHotfix.sln` 0 errors

---

## Not In Scope

- 旧管线 Task 化（IBuildTask 包装）— 延后
- Diff 面板功能实现 — 等 E7
- 产物查看面板 — 等 E7
- 旧管线构建触发逻辑 — LegacyBuildPanel 后续填充
- 旧管线优化回填（SerializationUtility/FileHelper）— 延后
- BuildMode.StandalonePackage 集成 — 独立 plan

---

## Change Log

| Date | Change |
|------|--------|
| 2026-05-15 | Initial version: 4 design decisions, 5 tasks, 3 new files, 1 modified file. Derived from draft-pipeline-coexistence.md layer 1 scope |
| 2026-05-15 | Review fixes: (1) D2 corrected — AA Groups is EditorWindow, cannot embed via CreateEditor; changed to summary+button approach. (2) D5 added — collapsible sidebar groups to handle 9-button UX. (3) T1 updated to reflect summary+button. (4) T4 updated to include collapse logic |
