# 资源管理框架 Editor UI 布局参考 (Addressables & YooAsset)

本文档总结了业界主流资源管理框架（Unity Addressables 和 YooAsset）在编辑器 UI 上的布局结构与交互体验设计，为 `XLuaHotfix` 的 BuildPipelineWindow 及其它编辑器的 UX 优化提供设计参考。

## 一、 Unity Addressables (Addressables Groups Window)

### 1. 窗口布局结构
- **顶部工具栏 (Toolbar)**: 
  - 核心功能入口区。包含 `Create` (新建组)、`Profile` (预设切换)、`Tools` (诊断工具/检查更新)、`Build` (清理与构建)、`Play Mode Script` (运行模式切换)。
- **核心数据区 (Multi-column TreeView)**:
  - 以多列树状视图 (Multi-column Tree View) 呈现资源状态。
  - **列定义**: `Group Name \ Addressable Name` (层级缩进显示组与具体资源), `Path` (实际相对路径), `Labels` (标签)。
- **底部状态栏 (Bottom Status)**:
  - 简单反馈当前操作状态和警告信息。

### 2. 核心 UX 交互设计
- **选中与右侧面板解耦**: 
  - 选中任何 Group 或 Asset，详细的属性配置（如 Schema 设置、打包策略等）会直接在 Unity 默认的 `Inspector` 窗口中渲染，而不是在 Addressables 窗口内部分割（保持了核心数据区的最大化利用率）。
- **拖拽行为 (Drag & Drop)**: 
  - 支持从 Project 视图直接将文件夹或文件拖入树状图的某个 Group 下，自动生成 Addressable Entry。
  - 支持组内资源项跨组拖拽。
- **右键上下文菜单 (Context Menu)**:
  - 在组上右键：`Create New Group`, `Rename`, `Remove`, `Simplify Addressable Names` 等。
- **列排序与筛选**:
  - 支持点击表头进行字典序排序。
  - 窗口右上角有标准的 Search 搜索框，过滤特定的资源名或路径。

---

## 二、 YooAsset (AssetBundle Collector Window)

### 1. 窗口布局结构
- **顶部工具栏 (Toolbar)**:
  - `Save` (保存配置)、`Import/Export` (导入/导出 XML 配置)、全局设置入口。
- **左侧边栏 (Tree View 导航区)**:
  - 固定的树状层级：`Package (包裹)` -> `Group (组)` -> `Collector (收集器)`。
  - 用户通过树状视图理解完整的收集结构。
- **右侧主面板 (Property View 属性区)**:
  - 使用可拖拽分割线 (Splitter) 将左右切分。
  - 点击左侧的任何节点，右侧就会显示对应的字段面板（如选 Collector 时，显示收集路径、资源类型、Address Rule、Pack Rule、Filter Rule 等下拉框和自定义字段）。
  - 下方通常会有一个规则测试区或校验结果输出区。

### 2. 核心 UX 交互设计
- **规则下拉框 (Reflection Dropdown)**:
  - 不要求开发者手写规则类名，而是通过反射扫出所有 `IAddressRule`, `IPackRule`, `IFilterRule` 的实现类，以下拉框 (Popup) 形式供用户选择。
- **目录拖拽自动填充**:
  - `CollectPath` 字段旁提供目录选择按钮，同时支持用户直接将 Project 窗口的目录拖到该 TextField 内，自动转换为合法路径。
- **树节点拖拽与重排**:
  - 支持在左侧树结构中同层级拖拽以改变打包组的遍历优先级（Order）。
- **实时校验反馈 (Real-time Validation)**:
  - 自动检测无效路径（如配置了收集 `Assets/Arts/UI` 但文件夹已被删除）。一旦检测出路径缺失，TreeView 的该节点会显示 `Error` 或 `Warning` 红黄标，同时右侧详情区显示具体的报错原因。

---

## 三、 对 XLuaHotfix 的边界说明

上面两节是外部框架的观察记录，不定义本项目行为。以下只记录“本项目当前适用/不适用”的边界，当前实现以源码与 `docs/FYAsset/` 为准：

1. **层级**：AB 采集配置只有 `AssetCollectionSetting → Group → Collector` 三层，没有 YooAsset 的 `Package` 层，也没有独立 Project Labels 页面。照搬 YooAsset 的 `Package → Group → Collector` 树会引入本项目已删除的配置层。
2. **左右分栏与拖拽**：左右分栏、可拖拽分割线、`CollectPath` 接收 Project 目录拖入事件这类交互与本项目 Collection 面板一致，属于可继续沿用的模式。
3. **规则下拉框不适用**：本项目已删除 `IFilterRule` / `IGroupRule` / `RuleResolver` 反射规则体系与对应的反射下拉控件（`RuleDropdownHelper`），扫描行为是固定的可验证流程；Collector 只保存 `CollectPath` 与 `CollectPathType`。不要按 YooAsset 的 Rule/Filter/Pack 下拉设计新的采集字段。
4. **Address / Packing 归属**：Address 样式是项目级 `AssetAddressStyle`，打包粒度是 Group 级 `BundlePackingMode`，都在 Setting/Group 上编辑，不存在 Collector 级 Rule 名或逐资产 Role/Payload 下拉。
5. **实时校验**：路径缺失、命名非法、同路径冲突等以结构化 `BuildMessage` 输出，可在面板内展示并定位到配置项；这与 Addressables 的校验红标体验目标一致。
6. **窗口与面板**：AA/AB 各自一个构建窗口，不含统一的 Repository/Diff 管理页；发布面板只处理发布与目标选择。AB 选择写入 `CurrentABTargetId`，不提供 `Apply URL`；规则名单统一在 AB Collection 面板编辑。