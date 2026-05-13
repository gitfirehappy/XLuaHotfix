# Draft: Editor Feature Staged Verification

> **Source**: 已落地编辑器功能的阶段性验证计划
> **Status**: Draft — 2026-05-10, **Updated 2026-05-13**
> **Scope**: BuildPipelineWindow + Collector 配置/分配/扫描 + Settings/Pipeline/Version 面板
> **原则**: 每阶段可独立在 Editor 里点击验证，不依赖构建流程
>
> ## 2026-05-13 对齐审计结论
>
> E10 (IBuildBackend 双管线) + E11 (FYAssetSettings SO) + infrastructure-consistency 落地后重新审查：
> - **SettingsPanel**: ✅ 已对齐 FYAssetSettings 全部 10 个字段
> - **PipelinePanel**: ✅ 自动渲染 BuildPipelineConfig 4 字段
> - **VersionPanel**: ✅ 自动渲染 E9 新增的 Channel/Build 字段
> - **CollectorPropertyPanel**: ✅ GroupRuleName (E1-3) 已显式处理
> - **BuildArtifactOrganizer**: ✅ 执行时判定为不合理抽象，三处已直接迁移到 FileHelper（无缺口）
> - **BuilderPanel**: ⚠️ 仍为占位符，需设计 BuildGraph 可视化 + 构建触发功能（独立 plan）
>
> **结论**: 编辑器 UI 层已对齐所有已落地数据变更。唯一待补功能是 BuilderPanel 从占位符升级为真实构建入口。

---

## Landed Editor Features Map (Updated 2026-05-13)

```
BuildPipelineWindow
├── Sidebar (3 groups) — E11 重组后
│   ├── SETTINGS
│   │   └── SettingsPanel            ← FYAssetSettings SO 编辑（E11 新增）
│   ├── AB PIPELINE                  ← UseABBackend=false 时整组灰显
│   │   ├── CollectorSettingPanel    ← Package/Group 导航 + Collector CRUD
│   │   ├── CollectorPanel           ← 高密度表格 + 校验/扫描底部区域
│   │   ├── PipelinePanel            ← BuildPipelineConfig SO Inspector
│   │   └── BuilderPanel             ← 构建控制（⚠️ 仍为占位符）
│   └── MANAGE
│       └── VersionPanel             ← VersionDataBase SO Inspector（含 E9 新字段）
│
├── CollectorPropertyPanel           ← Package/Group/Collector 三级属性编辑器
├── CollectorTreeView                ← 三层次 TreeView + 拖拽排序
├── CollectorResultPanel             ← 校验消息 + 扫描预览
│
├── CollectorAssetInspectorGUI       ← Inspector 勾选（文件+文件夹）
├── CollectorContextMenu             ← Project 右键分配/移除
├── CollectorTargetPickerPopup       ← 选择目标 Group 弹窗
├── CollectorReverseIndex            ← 资产↔Collector 双向索引
├── CollectorAssetPostprocessor      ← 资产变动自动刷新索引
├── CollectorDataMigrator            ← SO 路径平滑迁移
│
├── RuleDropdownHelper               ← 规则下拉菜单
└── CollectorSettingValidator        ← 校验器集成
```

---

## Stage 1: Window Shell & Panel Lifecycle

**验证目标**: 窗口能打开、面板能切换、无 NullRef、无 GUILayout 错误。

| # | 验证项 | 操作 | 预期 |
|---|--------|------|------|
| 1.1 | 菜单打开 | `XLua/Build Pipeline Window` | 窗口打开，800×500，标题 "Build Pipeline" |
| 1.2 | 侧边栏渲染 | 观察左侧 | SETTINGS/AB PIPELINE/MANAGE 三组，6 个面板按钮 |
| 1.3 | 面板切换 | 点击各按钮 | 右侧内容区切换，无闪烁 |
| 1.4 | 分隔条拖拽 | 拖拽侧边栏右边线 | 100px~300px 范围内调整 |
| 1.5 | 关闭重开 | 关闭窗口 → 重新菜单打开 | 状态重置，无报错 |
| 1.6 | Domain Reload | 进入 Play Mode 再退出 | 窗口恢复，面板正常渲染 |
| 1.7 | SO 不存在 | 删除 `BuildPipelineConfig.asset` 后打开 PipelinePanel | 显示 "No Config found" + Create 按钮 |
| 1.8 | Create SO | 点击 Create 按钮 | SO 创建成功，Inspector 渲染 |

**涉及文件**: `BuildPipelineWindow.cs`, `IBuildPipelinePanel.cs`, `PlaceholderPanel.cs`, `PipelinePanel.cs`, `VersionPanel.cs`, `BuilderPanel.cs`

---

## Stage 2: Collector Configuration CRUD

**验证目标**: 通过 GUI 完整操作 Collector 数据的增删改查。

| # | 验证项 | 操作 | 预期 |
|---|--------|------|------|
| 2.1 | LoadSetting | 打开 Collect Config 面板 | `CollectorSetting.asset` 加载，无报错 |
| 2.2 | SO 不存在自动创建 | 删除 SO → 重新打开 | 自动创建空 SO |
| 2.3 | Package 添加 | 右侧工具栏 `+Package` | 新 Package 出现在左侧树 |
| 2.4 | Group 添加 | 选中 Package → 右键 `Add Group` | 新 Group 创建 |
| 2.5 | Collector 添加 | 选中 Group → 右键 `Add Collector` | 新 Collector 创建，默认 Folder 类型 |
| 2.6 | Collector 删除 | 选中 Collector → 右键 `Remove` | 删除成功，ReverseIndex 同步 |
| 2.7 | Collect Path 选择 | 点击 Collector 的路径字段 | 文件夹选择器弹出，可选择 |
| 2.8 | Rule 下拉 | 点击 AddressRule/PackRule/FilterRule 下拉 | 扫描到的规则列表显示正确 |
| 2.9 | 数据持久化 | 修改 → 关闭窗口 → 重开 | 所有修改保留 |
| 2.10 | Undo/Redo | 修改 → Ctrl+Z → Ctrl+Y | Undo/Redo 正常工作 |

**涉及文件**: `CollectorSettingPanel.cs`, `CollectorPanel.cs`, `CollectorPropertyPanel.cs`, `CollectorTreeView.cs`, `RuleDropdownHelper.cs`, `CollectorReverseIndex.cs`

---

## Stage 3: Asset Assignment (3 Entry Points)

**验证目标**: Inspector 勾选、右键菜单、拖拽三种分配方式都正确更新数据。

| # | 验证项 | 操作 | 预期 |
|---|--------|------|------|
| **Inspector** ||||
| 3.1 | 文件勾选 | Project 选中单文件 → Inspector 勾选 Collector 复选框 | 复选框显示，勾选后文件加入 Collector |
| 3.2 | 文件夹勾选 | Project 选中文件夹 → Inspector | 文件夹显示 Collected 状态，勾选有效 |
| 3.3 | 取消勾选 | 已分配资产 → 取消复选框 | 资产从 Collector 移除 |
| 3.4 | 非资产路径 | 选中 Packages 下资源 | 不显示 Collector 复选框（非 Assets 路径） |
| 3.5 | 多选不显示 | Ctrl+多选多个文件 | 不显示复选框（仅单选） |
| **右键菜单** ||||
| 3.6 | Add to Group | Project 右键 `Assets/FYAsset/Add to Collector Group` | TargetPickerPopup 弹出，可选 Group |
| 3.7 | Remove from Collector | 已分配资产右键 `Assets/FYAsset/Remove from Collector` | 直接从 Collector 移除 |
| 3.8 | 菜单启用条件 | 未选中任何资产 → 右键 | 菜单项灰色不可用 |
| **反向索引** ||||
| 3.9 | 索引一致性 | 添加/移除资产 → 检查 ReverseIndex | `IsAssetCollected` 返回正确值 |
| 3.10 | 资产删除自动更新 | 在 Project 中删除已分配资产 | `CollectorAssetPostprocessor` 自动清理索引 |
| 3.11 | 资产移动自动更新 | 移动已分配资产到新目录 | 索引中的路径更新 |

**涉及文件**: `CollectorAssetInspectorGUI.cs`, `CollectorContextMenu.cs`, `CollectorTargetPickerPopup.cs`, `CollectorReverseIndex.cs`, `CollectorAssetPostprocessor.cs`, `CollectorDataMigrator.cs`

---

## Stage 4: Validation & Scan Preview

**验证目标**: 校验器能检测配置错误，扫描预览能显示资产分配结果。

| # | 验证项 | 操作 | 预期 |
|---|--------|------|------|
| 4.1 | 基础校验 | 配置有效规则 → 打开 Collector 面板 | 底部无 Error 消息 |
| 4.2 | 空配置校验 | 创建空 Collector（无路径） | 显示 Warning: "CollectPath is empty" |
| 4.3 | 路径不存在校验 | Collector 路径设为不存在目录 | 显示 Error: "CollectPath not found" |
| 4.4 | 交叉包含校验 | 两个 Package 路径有包含关系 | 显示 CrossPackageContainment 消息 |
| 4.5 | 规则解析校验 | 填写不存在的 Rule 名称 | 显示 Rule 无法解析的 Error |
| 4.6 | 扫描预览 | 配置合法规则 → 点击 Scan | 底部切换到 Scan Preview，显示资产数量 |
| 4.7 | 扫描进度 | 大量资产 → 点击 Scan | 显示 "Scanning..." 后显示结果 |
| 4.8 | 空扫描 | 路径下无匹配资产 → Scan | 显示 "0 collected assets" |
| 4.9 | 切换底部模式 | 点击 Validation/Scan Preview 切换 | 底部内容正确切换 |

**涉及文件**: `CollectorResultPanel.cs`, `CollectorSettingValidator.cs`, `CollectionScanner.cs`, `CollectorPanel.cs` (bottom area)

---

## Stage 5: Settings, Pipeline & Version Panel Integration

**验证目标**: FYAssetSettings SO、Build Pipeline SO 和 Version SO 的编辑功能正常；灰显逻辑正确。

| # | 验证项 | 操作 | 预期 |
|---|--------|------|------|
| 5.0 | SettingsPanel 显示 | 点击 Settings 面板 | `FYAssetSettings.asset` 渲染 10 个字段（3 组） |
| 5.0a | UseABBackend 切换 | 切换 UseABBackend 开关 | AB PIPELINE 组灰显/激活切换 |
| 5.0b | SO 不存在 | 删除 `FYAssetSettings.asset` 后打开 | LoadOrCreate 自动创建 |
| 5.1 | PipelinePanel 显示 | 点击 Pipeline 面板 | `BuildPipelineConfig.asset` 的 Inspector 渲染 |
| 5.2 | PipelinePanel 编辑 | 修改 TaskEntry 字段 | 与直接选中 SO 查看行为一致 |
| 5.3 | PipelinePanel Reload | 修改 → Reload | 重新加载 SO，未保存修改丢失（预期行为） |
| 5.4 | VersionPanel 显示 | 点击 Version 面板 | `VersionDataBase.asset` 的 Inspector 渲染，含 Channel/Build 新字段 |
| 5.5 | VersionPanel 编辑 | 修改 Major/Minor/Patch/Channel/Build | 修改生效 |
| 5.6 | VersionPanel Create | 删除 SO → 点击 Version 面板 | 显示 "No VersionDataBase" + Create 按钮 |
| 5.7 | BuilderPanel 占位 | 点击 Builder 面板 | 显示 placeholder 内容 + 按钮（⚠️ 待升级为真实功能） |

**涉及文件**: `SettingsPanel.cs`, `PipelinePanel.cs`, `VersionPanel.cs`, `BuilderPanel.cs`

---

## Execution Order (Suggested)

```
Stage 1 (Window Shell)         ← 优先：整个编辑器入口必须稳
Stage 2 (Collector CRUD)       ← 核心数据配置面
Stage 3 (Asset Assignment)     ← 最常用的日常入口
Stage 4 (Validation & Scan)    ← 反馈闭环
Stage 5 (Pipeline & Version)   ← 管线配置面
```

每阶段独立验证，前一阶段通过再进下一阶段。全阶段通过 = 编辑器功能面毕业。

---

## Change Log

| Date | Change |
|------|--------|
| 2026-05-10 | Initial draft — 5 stages covering all landed editor features |
| 2026-05-13 | 对齐审计：侧栏结构更新为 SETTINGS/AB PIPELINE/MANAGE；Stage 5 新增 SettingsPanel + 灰显验证；确认 BuildArtifactOrganizer 无缺口；标记 BuilderPanel 为唯一待补功能点 |
