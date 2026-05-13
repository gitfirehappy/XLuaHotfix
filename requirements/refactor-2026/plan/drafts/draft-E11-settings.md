# Draft: FYAssetSettings 总设置 SO + 编辑器 Settings 面板

> **Date**: 2026-05-11
> **Status**: Promoted → [plan-E11-settings.md](../plan-E11-settings.md) (2026-05-11)
> **Depends on**: E10 (BuildProjectManager 双管线拆分) 之前或并行均可
> **影响范围**: FYAssetConstants 路径字段迁移 + BuildPipelineWindow 侧栏重构 + USE_AB_BACKEND 开关归属

---

## 已收敛决策

| # | 决策 | 结论 |
|---|------|------|
| 1 | SO 形式 | 新建 FYAssetSettings ScriptableObject |
| 2 | 程序集归属 | **Runtime 程序集**（PROJECTNAME/HOTFIX_URL/USE_AB_BACKEND 有 Runtime 引用） |
| 3 | Collector 面板归属 | **AB 区专属**（AA 有自己的资源管理，Collector 是 AB 管线组件） |
| 4 | SO 资产路径 | `Assets/FYAsset/FYAssetSettings.asset` |
| 5 | LoadOrCreate | 自动创建并写入默认值 |
| 6 | USE_AB_BACKEND 归属 | 移入 FYAssetSettings，BuildPipelineConfig 不再持有 BackendMode |
| 7 | 灰显行为 | 非激活区 `GUI.enabled = false`，可查看不可编辑 |
| 8 | 侧栏布局 | Settings 为第一项，AB 区 / AA 区根据开关灰显 |

---

## 目标

1. 新建 `FYAssetSettings` ScriptableObject 作为 FYAsset 模块的总配置入口（Runtime 程序集）
2. 将 FYAssetConstants 中的路径类硬编码字段迁移为 SO 可配置字段（保留默认值）
3. 将 `USE_AB_BACKEND` 开关从 FYAssetConstants 硬编码 + BuildPipelineConfig.DefaultBackendMode 统一收归 FYAssetSettings
4. BuildPipelineWindow 侧栏顶部新增 Settings 面板，作为第一配置入口
5. 编辑器 UI 根据开关状态分 AB 区 / AA 区，非激活区灰显只读

---

## FYAssetSettings SO 字段设计

```csharp
[CreateAssetMenu(fileName = "FYAssetSettings", menuName = "FYAsset/Settings")]
public class FYAssetSettings : ScriptableObject
{
    // ─── 项目元数据 ───
    [Header("Project")]
    public string ProjectName = "ProjectName";
    public string HotfixUrl = "https://firehappy-cfy.com/";

    // ─── 后端开关（总控） ───
    [Header("Backend")]
    [Tooltip("true = AB 新管线; false = Legacy Addressables")]
    public bool UseABBackend = false;

    // ─── 版本数据 ───
    [Header("Version")]
    [Tooltip("VersionDataBase SO 资产路径")]
    public string VersionDataBasePath = "Assets/Build/VersionDataBase.asset";

    // ─── Legacy Pipeline 路径 ───
    [Header("Legacy Pipeline Paths")]
    public string AddressableLabelsConfigPath = "Assets/Build/HelperBuildData/AddressableLabelsConfig.asset";
    public string SnapshotAssetPath = "Assets/Build/Snapshots.asset";
    public string BuildIndexJsonPath = "Assets/Build/LocalStaticData/BuildIndex.json";

    // ─── New Pipeline 路径 ───
    [Header("New Pipeline Paths")]
    public string CollectorDataFolder = "Assets/FYAsset/CollectorData";
    public string CollectorSettingPath = "Assets/FYAsset/CollectorData/CollectorSetting.asset";
    public string PipelineConfigPath = "Assets/Build/BuildPipelineConfig.asset";

    // ─── Singleton 访问 ───
    private static FYAssetSettings _instance;
    public static FYAssetSettings Instance => _instance ??= LoadOrCreate();
}
```

### 不纳入 SO 的字段（保留在 FYAssetConstants）

| 字段 | 原因 |
|------|------|
| `RULE_*` 规则类名 | 编译期反射标识，不应运行时可变 |
| `BUILD_PIPELINE_WINDOW_MENU_PATH` | 菜单路径，编译期常量 |
| `BINARY_SERIALIZER_GENERATE_PATH` | 代码生成路径，编译期常量 |
| `MANIFEST_FILE_NAME` / `_BIN` | 文件命名约定，不应可变 |
| `LUA_SCRIPTS_INDEX_ASSETPATH` | 用户自配置数据，不属于资源管理系统 |
| `AA_LABELS_CONFIG` / `HELPER_BUILD_DATA_GROUP_NAME` 等标识符 | 逻辑标识，非路径 |

---

## FYAssetConstants 改造

路径类字段改为从 SO 读取的 getter：

```csharp
public static class FYAssetConstants
{
    // ─── 从 FYAssetSettings SO 读取 ───
    public static string PROJECTNAME => FYAssetSettings.Instance.ProjectName;
    public static string HOTFIX_URL => FYAssetSettings.Instance.HotfixUrl;
    public static bool USE_AB_BACKEND => FYAssetSettings.Instance.UseABBackend;
    public static string VERSION_DATABASE_PATH => FYAssetSettings.Instance.VersionDataBasePath;
    public static string AA_LABELS_CONFIG_ASSETPATH => FYAssetSettings.Instance.AddressableLabelsConfigPath;
    public static string SNAPSHOT_ASSET_PATH => FYAssetSettings.Instance.SnapshotAssetPath;
    public static string BUILD_INDEX_JSON_PROJECT_PATH => FYAssetSettings.Instance.BuildIndexJsonPath;
    public static string COLLECTOR_DATA_FOLDER => FYAssetSettings.Instance.CollectorDataFolder;
    public static string COLLECTOR_SETTING_ASSET_PATH => FYAssetSettings.Instance.CollectorSettingPath;
    public static string PIPELINE_CONFIG_ASSET_PATH => FYAssetSettings.Instance.PipelineConfigPath;

    // ─── 保留为编译期常量 ───
    public const string BUILD_PIPELINE_WINDOW_MENU_PATH = "XLua/Build Pipeline";
    public const string BINARY_SERIALIZER_GENERATE_PATH = "Assets/Tools/Scripts/Serialization/Generated";
    // ... 规则名、标识符等不变 ...
}
```

**注意**: `const` → `static` 属性变更会导致所有引用点重新编译，但不需要代码修改（语法兼容）。

---

## BuildPipelineConfig 变更

- **删除** `DefaultBackendMode` 字段（开关已归 FYAssetSettings）
- **保留** `FileNameStyle`、`BundleCompression`、`SequentialMode`、`Tasks` 等 pipeline 内部配置
- BuildPipelineConfig 定位为「管线执行细节配置」，不再承担后端选择职责

---

## BuildPipelineWindow 侧栏重构

### 新布局

```
┌─────────────────────┐
│ ★ SETTINGS          │  ← 新增组，始终第一
│   [Settings]        │  ← FYAssetSettings 编辑面板
├─────────────────────┤
│ AB PIPELINE         │  ← AB 区（UseABBackend=false 时灰显）
│   [CollectorSetting]│
│   [Collector]       │
│   [Pipeline]        │
│   [Builder]         │
├─────────────────────┤
│ MANAGE              │  ← 共享区，始终可用
│   [Version]         │
└─────────────────────┘
```

### AB 区 / AA 区划分

| 区域 | 面板 | 激活条件 |
|------|------|----------|
| **共享** | Settings, Version | 始终可用 |
| **AB 区** | CollectorSetting, Collector, Pipeline, Builder | `UseABBackend = true` 时激活，否则灰显只读 |

> Collector 是 AB 管线专属组件。AA 管线有自己的资源管理（Addressables Groups 窗口），Collector 仅为 AB 管线提供资产收集配置。

### 灰显行为

- `UseABBackend = false` 时：AB PIPELINE 组所有面板灰显（`GUI.enabled = false`），可查看但不可编辑
- `UseABBackend = true` 时：所有面板正常
- 灰显面板顶部显示提示条：「切换到 AB 后端以启用此面板」

---

## FYAssetSettings SO 存放位置

| 方案 | 路径 | 优劣 |
|------|------|------|
| **A (推荐)** | `Assets/FYAsset/FYAssetSettings.asset` | 与模块根目录一致，易发现 |
| B | `Assets/Build/FYAssetSettings.asset` | 与其他 Build 数据放一起 |

默认路径常量（用于 LoadOrCreate fallback）：
```csharp
private const string DEFAULT_ASSET_PATH = "Assets/FYAsset/FYAssetSettings.asset";
```

---

## 与 E10 的关系

- E10 中 `CreateBackend()` 读取 `FYAssetConstants.USE_AB_BACKEND` — 重构后该属性从 SO 读取，E10 代码无需修改
- E10 可先于本草稿执行（USE_AB_BACKEND 仍为硬编码 false），本草稿执行后自动生效
- 建议执行顺序：E10 先落地 → E11 再迁移开关

---

## Runtime vs Editor 引用分析

| 字段 | Runtime 引用 | Editor 引用 |
|------|-------------|-------------|
| PROJECTNAME | PathManager | — |
| HOTFIX_URL | HotfixManager | — |
| USE_AB_BACKEND | HotfixManager, AssetPackageManager | BuildProjectManager |
| AA_LABELS_CONFIG_ASSETPATH | — | HelperBuildDataExporter |
| SNAPSHOT_ASSET_PATH | — | DifferentialProcessor |
| BUILD_INDEX_JSON_PROJECT_PATH | — | （未直接引用，通过 LocalStatusExporter） |
| COLLECTOR_SETTING_ASSET_PATH | — | 9 个 Editor 文件 |
| PIPELINE_CONFIG_ASSET_PATH | — | ABBuildBackend |

**结论**: PROJECTNAME / HOTFIX_URL / USE_AB_BACKEND 有 Runtime 引用 → FYAssetSettings 必须在 Runtime 程序集。

---

## 待讨论点

（已全部收敛，无遗留讨论点）

---

## Out of Scope

- VersionPanel 重做（独立计划）
- LuaScriptsIndex 路径配置（用户自配置数据）
- ProjectSettings Provider 注册（后续可选增强）
