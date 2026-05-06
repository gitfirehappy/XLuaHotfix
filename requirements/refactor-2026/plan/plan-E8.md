# Sub-Plan E8: Collector Editor UX Optimization

> **Risk**: Medium (UI 重写涉及多个面板，但数据模型改动最小)
> **Dependencies**: E4 (CollectorPanel/TreeView/PropertyPanel), E5-1 (CollectionScanner)
> **Status**: Draft — 2026-05-06
> **Scope**: 三阶段交付，Phase 1 基础设施+面板重做，Phase 2 Inspector 勾选，Phase 3 拖拽+右键

---

## Objective

优化构建编辑器的资产分配体验。当前唯一入口是 Collector 节点的文件夹选择器，不支持单资产、拖拽、批量操作。目标：

1. 参考 Addressables Groups 窗口重做 CollectorSettingPanel（横向列式布局）
2. 参考 YooAsset Collector 重做 CollectorPanel（纯表格，高信息密度）
3. 新增三种资产分配入口：Inspector 勾选 / 拖拽进表格 / Project 右键菜单
4. 支持单文件级别的 Collector 指定

---

## Phase 1: 核心基础设施 + 面板重做

### 1A. 数据模型扩展

**修改** `Assets/FYAsset/Scripts/Build/Collector/CollectorEnums.cs`

```csharp
public enum ECollectPathType { Folder = 0, File = 1 }
```

**修改** `Assets/FYAsset/Scripts/Build/Collector/CollectorSetting.cs`

Collector 类新增：
```csharp
public ECollectPathType CollectPathType = ECollectPathType.Folder;
```

向后兼容：默认值 0 = Folder，现有序列化数据无需迁移。

### 1B. 配置文件夹统一管理

**修改** `Assets/FYAsset/Scripts/FYAssetConstants.cs`

```csharp
public const string COLLECTOR_DATA_FOLDER = "Assets/FYAsset/CollectorData";
public const string COLLECTOR_SETTING_ASSET_PATH = "Assets/FYAsset/CollectorData/CollectorSetting.asset";
```

**新建** `Assets/FYAsset/Scripts/Build/Collector/Editor/CollectorDataMigrator.cs`

| 方法 | 职责 |
|------|------|
| `EnsureDataFolder()` | 创建 `Assets/FYAsset/CollectorData/` |
| `MigrateFromLegacyPath()` | 从 `Assets/Build/CollectorSetting.asset` 迁移到新路径 |

面板 LoadSetting 时自动调用，一次性迁移。

### 1C. CollectionScanner 支持文件路径

**修改** `Assets/FYAsset/Scripts/Build/Collector/Editor/CollectionScanner.cs`

在 ScanCollector 逻辑中新增分支：

```
if (collector.CollectPathType == ECollectPathType.File)
    → 验证文件存在（AssetDatabase.AssetPathToGUID）
    → 直接构建单个 CollectedAssetInfo
    → 跳过 FindAssets 目录扫描
```

现有文件夹逻辑不变。

### 1D. 反向索引（资产→Collector 快速查询）

**新建** `Assets/FYAsset/Scripts/Build/Collector/Editor/CollectorReverseIndex.cs`

```csharp
public sealed class CollectorReverseIndex
{
    public static CollectorReverseIndex Instance { get; }

    public struct CollectorRef { int PackageIndex; int GroupIndex; int CollectorIndex; }

    private Dictionary<string, CollectorRef> _map;  // key = asset path
    private bool _dirty = true;

    public void MarkDirty();
    public void RebuildIfDirty(CollectorSetting setting);
    public bool TryGetCollector(string assetPath, out CollectorRef result);
    public bool IsAssetCollected(string assetPath);
}
```

设计要点：
- 惰性重建：仅在查询时且 `_dirty == true` 才全量重建
- Folder 类型 Collector：枚举目录下所有资产路径加入索引
- File 类型 Collector：直接索引单个路径
- 脏标记触发源：SO 修改（Undo callback）、资产导入/删除（AssetPostprocessor）

**新建** `Assets/FYAsset/Scripts/Build/Collector/Editor/CollectorAssetPostprocessor.cs`

```csharp
public class CollectorAssetPostprocessor : AssetPostprocessor
{
    static void OnPostprocessAllAssets(...) => CollectorReverseIndex.Instance.MarkDirty();
}
```

### 1E. CollectorSettingPanel 重做（Addressables Groups 风格）

**重写** `Assets/FYAsset/Scripts/Build/Editor/CollectorSettingPanel.cs`

```
┌─ Left (200px) ──────────┬─ Right (剩余宽度) ─────────────────────────────┐
│ 📦 Package A            │ Path        | PathType | Type | AddrRule | Pack │
│   📁 Group 1  ← 选中   │ Assets/UI/  | Folder   | Main | ByFile   | Dir │
│   📁 Group 2            │ Assets/a.png| File     | Main | ByFile   | Sep │
│ 📦 Package B            │             |          |      |          |     │
│   📁 Group 3            │             |          |      |          |     │
└─────────────────────────┴────────────────────────────────────────────────┘
```

职责：**全局配置总览**——Package/Group 结构管理 + Collector 列表查看编辑

关键交互：
- 左侧 Package/Group 可折叠列表，右键可增删改名
- 右侧选中 Group 的 Collector 列表，列头可排序
- 行内编辑：路径文本框 + "…" 按钮、枚举下拉、规则下拉
- Package 级别属性（SharePolicy）在选中 Package 时右侧显示

### 1F. CollectorPanel 重做（YooAsset 风格，纯表格）

**重写** `Assets/FYAsset/Scripts/Build/Collector/Editor/UI/CollectorPanel.cs`

**删除** `Assets/FYAsset/Scripts/Build/Collector/Editor/UI/CollectorTreeView.cs`

**删除** `Assets/FYAsset/Scripts/Build/Collector/Editor/UI/CollectorPropertyPanel.cs`

```
┌─ Toolbar ─────────────────────────────────────────────────────────────────┐
│ [Package ▼] [Group ▼]  [+ Add Folder] [+ Add File] [- Remove]  [Search…] │
├───────────────────────────────────────────────────────────────────────────┤
│ ☐ │ CollectPath       │ PathType │ CollType │ AddrRule   │ PackRule │ … │
│ ☐ │ Assets/UI/Icons   │ Folder   │ Main     │ ByFileName │ ByDir    │   │
│ ☐ │ Assets/sp/a.png   │ File     │ Main     │ ByFileName │ Separate │   │
│ ☐ │ Assets/Audio/     │ Folder   │ Static   │ ByFileName │ ByCollect│   │
├───────────────────────────────────────────────────────────────────────────┤
│ [Validation] [Scan Preview]          [Run Scan]                           │
│ ✅ 12 assets collected, 0 warnings                                        │
└───────────────────────────────────────────────────────────────────────────┘
```

职责：**收集操作面板**——快速添加/移除/编辑 Collector，运行扫描验证

关键交互：
- 顶部 Package/Group 下拉筛选当前显示的 Collector 列表
- 表格每行一个 Collector，勾选框支持多选批量操作
- 行内编辑所有字段（替代原 CollectorPropertyPanel）
- Add Folder / Add File 按钮分别打开文件夹/文件选择器
- 底部保留 Validation + ScanPreview 双 Tab

---

## Phase 2: Inspector 勾选

**新建** `Assets/FYAsset/Scripts/Build/Collector/Editor/UI/CollectorAssetInspectorGUI.cs`

```csharp
[InitializeOnLoad]
static class CollectorAssetInspectorGUI
{
    static CollectorAssetInspectorGUI()
    {
        Editor.finishedDefaultHeaderGUI += OnHeaderGUI;
    }

    static void OnHeaderGUI(Editor editor)
    {
        // 1. 获取选中资产路径
        // 2. CollectorReverseIndex.Instance.IsAssetCollected(path)
        // 3. 绘制 "Collected [✓]" + "Package/Group: xxx" 信息
        // 4. Toggle ON → 弹出 CollectorTargetPickerPopup
        // 5. Toggle OFF → 移除对应 Collector 条目
    }
}
```

**新建** `Assets/FYAsset/Scripts/Build/Collector/Editor/UI/CollectorTargetPickerPopup.cs`

- `PopupWindowContent` 子类
- 两级选择：Package 下拉 → Group 下拉
- 可选设置 CollectorType / Rules（或使用 Group 默认值）
- 确认后创建 File 类型 Collector 并加入目标 Group

Inspector 显示效果：
```
┌─ Inspector Header ──────────────────────────────┐
│ [✓] Collected    Package: Main  Group: UI       │
│─────────────────────────────────────────────────│
│ (原有 Inspector 内容)                            │
└─────────────────────────────────────────────────┘
```

---

## Phase 3: 拖拽 + 右键菜单

### 3A. 外部拖拽进表格

**修改** CollectorPanel + CollectorSettingPanel 的表格区域

```
拖拽检测逻辑：
1. EventType.DragUpdated / DragPerform 时检查 DragAndDrop.objectReferences
2. 判断拖入目标区域（表格内 = 当前选中 Group）
3. 对每个拖入对象：
   - AssetDatabase.IsValidFolder(path) → CollectPathType.Folder
   - 否则 → CollectPathType.File
4. 创建 Collector 条目，使用 Group 默认规则
5. MarkDirty 反向索引
6. DragAndDropVisualMode.Copy 视觉反馈
```

支持多选批量拖入。

### 3B. Project 窗口右键菜单

**新建** `Assets/FYAsset/Scripts/Build/Collector/Editor/UI/CollectorContextMenu.cs`

```csharp
[MenuItem("Assets/FYAsset/Add to Collector Group", false, 1000)]
static void AddToCollectorGroup()
{
    // Selection.assetGUIDs → 获取选中资产
    // 弹出 CollectorTargetPickerPopup（复用 Phase 2 组件）
    // 批量创建 Collector 条目
}

[MenuItem("Assets/FYAsset/Remove from Collector", false, 1001)]
static void RemoveFromCollector()
{
    // 查询反向索引 → 找到对应 Collector → 移除
}

[MenuItem("Assets/FYAsset/Remove from Collector", true)]
static bool RemoveFromCollectorValidate()
{
    // 仅当所有选中资产都已被收集时启用
}
```

---

## 文件变更总览

| 文件 | 操作 | 阶段 |
|------|------|------|
| `Build/Collector/CollectorEnums.cs` | 修改 | 1A |
| `Build/Collector/CollectorSetting.cs` | 修改 | 1A |
| `FYAssetConstants.cs` | 修改 | 1B |
| `Build/Collector/Editor/CollectorDataMigrator.cs` | **新建** | 1B |
| `Build/Collector/Editor/CollectionScanner.cs` | 修改 | 1C |
| `Build/Collector/Editor/CollectorReverseIndex.cs` | **新建** | 1D |
| `Build/Collector/Editor/CollectorAssetPostprocessor.cs` | **新建** | 1D |
| `Build/Editor/CollectorSettingPanel.cs` | **重写** | 1E |
| `Build/Collector/Editor/UI/CollectorPanel.cs` | **重写** | 1F |
| `Build/Collector/Editor/UI/CollectorTreeView.cs` | **删除** | 1F |
| `Build/Collector/Editor/UI/CollectorPropertyPanel.cs` | **删除** | 1F |
| `Build/Collector/Editor/UI/CollectorAssetInspectorGUI.cs` | **新建** | 2 |
| `Build/Collector/Editor/UI/CollectorTargetPickerPopup.cs` | **新建** | 2 |
| `Build/Collector/Editor/UI/CollectorContextMenu.cs` | **新建** | 3B |

所有路径前缀：`Assets/FYAsset/Scripts/`

---

## 验证方案

| # | 验证项 | 方法 |
|---|--------|------|
| 1 | 数据兼容性 | 打开现有 CollectorSetting.asset，确认 CollectPathType 默认 Folder，Scan 结果不变 |
| 2 | 配置迁移 | 删除新路径 SO，打开窗口，确认自动从旧路径迁移 |
| 3 | 面板布局 | Build Pipeline Window 中两个面板正确渲染，列头对齐，行内编辑可用 |
| 4 | 单文件 Collector | 手动添加 File 类型 Collector，Run Scan 正确收集该文件 |
| 5 | Inspector 勾选 | 选中资产 → 勾选 Collected → 确认 SO 数据更新；取消勾选 → 确认移除 |
| 6 | 拖拽 | Project 窗口拖文件/文件夹到表格 → Collector 自动创建 |
| 7 | 右键菜单 | 多选资产 → 右键 Add → 批量添加；Remove → 批量移除 |
| 8 | 反向索引性能 | 1000+ 资产 Group，Inspector 切换资产无卡顿 |

---

## 风险与缓解

| 风险 | 影响 | 缓解 |
|------|------|------|
| 反向索引全量重建慢 | 大项目首次打开卡顿 | 异步重建 + 进度条；后续增量更新 |
| IMGUI 表格列对齐复杂 | 开发耗时 | 复用 CollectorResultPanel 的列偏移模式 |
| 配置迁移丢失引用 | 其他脚本硬编码旧路径 | MigrateFromLegacyPath 保留旧路径 fallback 查找 |
| CollectorTreeView 删除影响测试 | 现有测试引用 | CollectorSettingInspectorTests 已标记删除，无阻塞 |
