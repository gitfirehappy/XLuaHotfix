# Plan-E11: FYAssetSettings 总设置 SO（合并 FYAssetConstants）

> **Status**: Realized — 2026-05-12
> **Risk**: Medium（FYAssetConstants 全局删除，~30 引用点迁移 + BuildPipelineWindow 侧栏重组）
> **Dependencies**: E10 已落地（CreateBackend 读取 FYAssetConstants.USE_AB_BACKEND，迁移后自动生效）
> **Supersedes**: `drafts/draft-E11-settings.md`

---

## 已收敛决策

| # | 决策 | 结论 |
|---|------|------|
| 1 | SO 形式 | 新建 FYAssetSettings ScriptableObject |
| 2 | 程序集归属 | **Runtime 程序集**（PROJECTNAME/HOTFIX_URL/USE_AB_BACKEND 有 Runtime 引用） |
| 3 | FYAssetConstants | **完全删除**，可配置字段为 SO 实例字段，纯常量为 SO 的 static const 成员 |
| 4 | Collector 面板归属 | **AB 区专属** |
| 5 | SO 资产路径 | `Assets/Resources/FYAssetSettings.asset` |
| 6 | LoadOrCreate | 自动创建并写入默认值 |
| 7 | USE_AB_BACKEND 归属 | 移入 FYAssetSettings，BuildPipelineConfig 不再持有 BackendMode |
| 8 | 灰显行为 | 非激活区 `GUI.enabled = false`，可查看不可编辑 |
| 9 | 侧栏布局 | SETTINGS → AB PIPELINE → MANAGE |

---

## Objective

1. 新建 `FYAssetSettings` ScriptableObject（Runtime 程序集）— 统一承载可配置字段 + 纯常量
2. **删除 `FYAssetConstants.cs`**，所有引用点迁移到 `FYAssetSettings`
3. USE_AB_BACKEND 从硬编码 + BuildPipelineConfig.DefaultBackendMode 统一收归 FYAssetSettings
4. BuildPipelineWindow 侧栏顶部新增 Settings 面板
5. AB PIPELINE 组根据开关灰显

---

## Task Breakdown

### E11-T1: FYAssetSettings SO 定义

**新建** `Assets/FYAsset/Scripts/FYAssetSettings.cs`（Runtime 程序集，替代 FYAssetConstants.cs）

```csharp
[CreateAssetMenu(fileName = "FYAssetSettings", menuName = "FYAsset/Settings")]
public class FYAssetSettings : ScriptableObject
{
    // ═══ 可配置字段（SO 实例数据） ═══

    [Header("Project")]
    public string ProjectName = "ProjectName";
    public string HotfixUrl = "https://firehappy-cfy.com/";

    [Header("Backend")]
    public bool UseABBackend = false;

    [Header("Version")]
    public string VersionDataBasePath = "Assets/Build/VersionDataBase.asset";

    [Header("Legacy Pipeline Paths")]
    public string AddressableLabelsConfigPath = "Assets/Build/HelperBuildData/AddressableLabelsConfig.asset";
    public string LuaScriptsIndexPath = "Assets/Build/HelperBuildData/LuaScriptsIndex.asset";
    public string SnapshotAssetPath = "Assets/Build/Snapshots.asset";
    public string BuildIndexJsonPath = "Assets/Build/LocalStaticData/BuildIndex.json";

    [Header("New Pipeline Paths")]
    public string CollectorDataFolder = "Assets/FYAsset/CollectorData";
    public string CollectorSettingPath = "Assets/FYAsset/CollectorData/CollectorSetting.asset";
    public string PipelineConfigPath = "Assets/Build/BuildPipelineConfig.asset";

    // ═══ 纯编译期常量（static const） ═══

    // --- 旧管线标识符 ---
    public const string AA_LABELS_CONFIG = "AddressableLabelsConfig";
    public const string HELPER_BUILD_DATA_GROUP_NAME = "HelperBuildData";
    public const string LUA_SCRIPTS_INDEX = "LuaScriptsIndex";
    public const string DEFAULT_XLUA_TYPE_CONFIG_LOAD_LABEL = "XLuaConfigs";
    public const string HOTFIX_GROUP_NAME = "HotfixGroup";
    public const string BUILD_INDEX_FILENAME = "BuildIndex.json";

    // --- 新管线文件命名 ---
    public const string MANIFEST_FILE_NAME = "ABManifest.json";
    public const string MANIFEST_FILE_NAME_BIN = "ABManifest.bin";

    // --- 编辑器路径 ---
    public const string BUILD_PIPELINE_WINDOW_MENU_PATH = "XLua/Build Pipeline";
    public const string BINARY_SERIALIZER_GENERATE_PATH = "Assets/Tools/Scripts/Serialization/Generated";

    // --- Collector 规则名 ---
    public const string RULE_ADDRESS_BY_FILE_NAME = "AddressByFileName";
    public const string RULE_COLLECT_ALL = "CollectAll";
    public const string RULE_PACK_BY_COLLECT_PATH = "PackByCollectPath";
    public const string RULE_PACK_SEPARATELY = "PackSeparately";
    public const string RULE_PACK_BY_DIRECTORY = "PackByDirectory";
    public const string RULE_PACK_BY_LABEL = "PackByLabel";
    public const string RULE_GROUP_ALL = "GroupAll";
    public const string RULE_GROUP_BY_TYPE = "GroupByType";
    public const string RULE_GROUP_BY_LABEL = "GroupByLabel";
    public const string RULE_GROUP_BY_DIRECTORY = "GroupByDirectory";

    // ═══ Singleton ═══
    private static FYAssetSettings _instance;
    public static FYAssetSettings Instance => _instance ??= LoadOrCreate();
    private const string DEFAULT_ASSET_PATH = "Assets/Resources/FYAssetSettings.asset";
    private static FYAssetSettings LoadOrCreate() { /* ... */ }
}
```

**Est.**: ~90 lines

### E11-T2: 删除 FYAssetConstants + 引用点迁移

**删除** `Assets/FYAsset/Scripts/FYAssetConstants.cs` + `.meta`

**全局替换引用点**（~30 处）：

| 旧引用 | 新引用 |
|--------|--------|
| `FYAssetConstants.PROJECTNAME` | `FYAssetSettings.Instance.ProjectName` |
| `FYAssetConstants.HOTFIX_URL` | `FYAssetSettings.Instance.HotfixUrl` |
| `FYAssetConstants.USE_AB_BACKEND` | `FYAssetSettings.Instance.UseABBackend` |
| `FYAssetConstants.AA_LABELS_CONFIG_ASSETPATH` | `FYAssetSettings.Instance.AddressableLabelsConfigPath` |
| `FYAssetConstants.LUA_SCRIPTS_INDEX_ASSETPATH` | `FYAssetSettings.Instance.LuaScriptsIndexPath` |
| `FYAssetConstants.SNAPSHOT_ASSET_PATH` | `FYAssetSettings.Instance.SnapshotAssetPath` |
| `FYAssetConstants.BUILD_INDEX_JSON_PROJECT_PATH` | `FYAssetSettings.Instance.BuildIndexJsonPath` |
| `FYAssetConstants.COLLECTOR_DATA_FOLDER` | `FYAssetSettings.Instance.CollectorDataFolder` |
| `FYAssetConstants.COLLECTOR_SETTING_ASSET_PATH` | `FYAssetSettings.Instance.CollectorSettingPath` |
| `FYAssetConstants.PIPELINE_CONFIG_ASSET_PATH` | `FYAssetSettings.Instance.PipelineConfigPath` |
| `FYAssetConstants.XXX`（const 成员） | `FYAssetSettings.XXX`（static const 直接访问） |

**Est.**: ~30 文件受影响，每处 1 行替换

### E11-T3: BuildPipelineConfig 清理

**修改** `Assets/FYAsset/Scripts/Build/Pipeline/Editor/BuildPipelineConfig.cs`

1. 删除 `DefaultBackendMode` 字段
2. 确认 DAGScheduler / TaskPrepareContext 不再读取此字段

**Est.**: ~10 lines removed

### E11-T4: SettingsPanel 面板

**新建** `Assets/FYAsset/Scripts/Build/Editor/SettingsPanel.cs`

1. 实现 `IBuildPipelinePanel` 接口
2. 加载 FYAssetSettings SO
3. 用 `SerializedObject` + `EditorGUILayout.PropertyField` 绘制所有可配置字段
4. UseABBackend 切换时触发 BuildPipelineWindow 刷新

**Est.**: ~80 lines

### E11-T5: BuildPipelineWindow 侧栏重组

**修改** `Assets/FYAsset/Scripts/Build/Editor/BuildPipelineWindow.cs`

1. Groups 数组重组为：SETTINGS (1) → AB PIPELINE (4: CollectorSetting/Collector/Pipeline/Builder) → MANAGE (1: Version)
2. AB PIPELINE 组在 `UseABBackend = false` 时 `GUI.enabled = false`
3. 灰显面板顶部提示条
4. SettingsPanel 注册为第一个面板

**Est.**: ~40 lines changed

### E11-T6: 创建 .asset + 编译验证

1. 创建 `Assets/Resources/FYAssetSettings.asset`（默认值）
2. `dotnet build` 零错误
3. 确认所有引用点正常
4. 确认 BuildPipelineWindow 侧栏布局正确
5. 确认灰显行为正确

---

## 执行顺序

```
E11-T1 (FYAssetSettings SO)
  → E11-T2 (FYAssetConstants 迁移)
    → E11-T3 (BuildPipelineConfig 清理)
      → E11-T4 (SettingsPanel)
        → E11-T5 (BuildPipelineWindow 侧栏重组)
          → E11-T6 (创建 .asset + 编译验证)
```

顺序执行。T1/T2 紧耦合（T2 依赖 T1 的类型定义）。T4/T5 可理论并行但为简化顺序执行。

---

## 创建/修改文件清单

| 操作 | 文件 | 说明 |
|------|------|------|
| **新建** | `Scripts/FYAssetSettings.cs` | SO 定义 + static const（Runtime 程序集） |
| **删除** | `Scripts/FYAssetConstants.cs` + `.meta` | 完全删除，功能合并入 FYAssetSettings |
| **修改** | ~30 个引用文件 | `FYAssetConstants.XXX` → `FYAssetSettings.Instance.XXX` 或 `FYAssetSettings.XXX` |
| **修改** | `Scripts/Build/Pipeline/Editor/BuildPipelineConfig.cs` | 删除 DefaultBackendMode |
| **新建** | `Scripts/Build/Editor/SettingsPanel.cs` | Settings 面板 |
| **修改** | `Scripts/Build/Editor/BuildPipelineWindow.cs` | 侧栏重组 + 灰显逻辑 |
| **新建** | `Assets/Resources/FYAssetSettings.asset` | SO 实例 |

> 文件路径前缀: `Assets/FYAsset/`

---

## 不变量

1. `dotnet build XLuaHotfix.sln` 0 errors
2. 所有现有引用点行为不变（PathManager / HotfixManager / AssetPackageManager / Editor 文件）
3. FYAssetSettings.asset 不存在时自动创建（LoadOrCreate）
4. UseABBackend=false 时 AB PIPELINE 面板灰显不可编辑
5. UseABBackend=true 时所有面板正常
6. BuildPipelineConfig 不再持有 BackendMode 相关字段
7. FYAssetSettings 在 Runtime 程序集，打包时包含在 build 中
8. FYAssetConstants.cs 不再存在

---

## Acceptance Criteria

1. 编译零错误
2. `FYAssetConstants` 类不存在（grep 确认零引用）
3. FYAssetSettings.asset 存在且字段默认值正确
4. 修改 SO 中 ProjectName → `PathManager` 通过 `FYAssetSettings.Instance` 读取到新值
5. 修改 SO 中 UseABBackend → BuildProjectManager.CreateBackend() 切换后端
6. BuildPipelineWindow 侧栏显示 SETTINGS → AB PIPELINE → MANAGE
7. UseABBackend=false 时 Collector/Pipeline/Builder 面板灰显
8. BuildPipelineConfig 中无 BackendMode 字段

---

## Out of Scope

- VersionPanel 重做（独立计划）
- LuaScriptsIndex 路径配置（用户自配置数据）
- ProjectSettings Provider 注册（后续可选增强）
- FYAssetSettings 自定义 Inspector（默认 Inspector 足够）
