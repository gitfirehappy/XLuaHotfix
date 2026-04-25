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

## 三、 对 XLuaHotfix (Plan E1-4) 的 UX 启示

结合上面两个框架的优势，我们正在实现的 E1-4 `CollectorPanel` 已融入了这些最佳实践：

1. **结构设计**: 采纳了 YooAsset 的左右分栏架构（左侧 TreeView 导航层级，右侧属性编辑面板），适合处理复杂的收集规则与配置。
2. **分割线 (Splitter)**: 采纳可拖拽分割线，允许开发者根据名字长度自由调整左右宽度比例。
3. **Inspector 解耦** vs **内联编辑**: E1-4 选择在**右侧面板内联编辑**（类似 YooAsset），以保证工具链的一致性，防止用户在 Inspector 和 Build 窗口之间来回切换视线。
4. **反射自动下拉 (Rule Dropdown)**: 与 YooAsset 一致，提供更好的防呆体验。
5. **快捷校验红标与双击跳转**: 吸纳了 Addressables 的直观校验体验，增加了双击下方 Validation Panel 的错误条目，自动定位到上方 TreeView 中对应 Collector 节点的能力。
6. **目录拖拽支持**: 允许在右侧面板的 CollectPath 接收 Project 文件夹拖入事件。