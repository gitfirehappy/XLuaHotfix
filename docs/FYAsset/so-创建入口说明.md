# SO 创建入口说明

> **关联代码**  
> `Assets/FYAsset/Scripts/FYAssetSettings.cs`  
> `Assets/FYAsset/Scripts/Build/Editor/Settings/SettingsPanel.cs`  
> `Assets/FYAsset/Scripts/Build/Editor/Manage/VersionPanel.cs`  
> `Assets/FYAsset/Scripts/Helpers/Editor/SOAddressableTagger.cs`  
> `Assets/FYAsset/Scripts/Build/Editor/ABPipeline/AssetsCollectionPanel.cs`
> `Assets/FYAsset/Scripts/Build/Editor/ABPipeline/PipelinePanel.cs`  
> `Assets/XLuaFramework/Scripts/Editor/LuaFileCreatorWithName.cs`  
> `Assets/XLuaFramework/Scripts/Editor/LuaDirectoryScanner.cs`

---

## 说明

项目内 `ScriptableObject` 的创建入口分三类：

- GUI 面板创建
- 自动创建
- 菜单手动创建

原则上同一资产类型只保留一个主入口。已经有面板按钮或自动生成逻辑的类型，不再重复暴露 `CreateAssetMenu`。

---

## 入口清单

| SO 类型 | 推荐入口 | 说明 |
|---|---|---|
| `FYAssetSettings` | `SettingsPanel` 中的创建按钮 / 自动补齐 | 默认路径为 `Assets/Resources/FYAssetSettings.asset`，保存全局项目、构建输出、版本路径和 PushTargets |
| `FYAssetAASettings` | `AAConfigPanel` 自动创建/编辑 | 默认路径为 `Assets/Resources/FYAssetAASettings.asset`，保存 AA 热更与构建参数 |
| `FYAssetABSettings` | `ABConfigPanel` 自动创建/编辑 | 默认路径为 `Assets/Resources/FYAssetABSettings.asset`，保存 AB 热更、构建参数与 AssetCollection 配置 |
| `VersionDataBase` | `VersionPanel` 中的创建按钮 | 路径由 `FYAssetSettings.VersionDataBasePath` 决定 |
| `ScriptObjectDataBase` | `SOAddressableTagger` 中的“创建新数据库” | 用于 SO 标签管理，不建议从菜单重复创建 |
| `ScriptObjectContainer` | `LuaFileCreatorWindow` / `LuaDirectoryScanner` / `LuaAddressableTagger` 的创建流程 | 由工具根据数据库和目录自动创建 |
| `AssetCollectionSetting` | `AssetsCollectionPanel` 中的创建按钮 | 资产收集配置资产，走 AB Pipeline 的 AssetsCollection 面板入口 |
| `BuildPipelineConfig` | `PipelinePanel` 中的创建按钮 | 由构建管线窗口创建并立即加载 |
| `LuaDataBase` | `LuaFileCreatorWindow` / `LuaBatchConverterWindow` 内部创建流程 | 作为 Lua 工具的数据库资产 |
| `LuaScriptContainer` | `LuaFileCreatorWindow` / `LuaDirectoryScanner` 内部创建流程 | 作为 Lua 脚本容器资产 |

---

## 仍保留菜单创建的类型

这些类型当前仍以菜单手动创建为主，没有更强的专属入口：

- `LuaAutoSyncConfig`
- `TypeMemberListSO`
- `LuaBehaviourConfigSO`
- `ScriptObjectBridgeConfig`
- `StateAnimationConfigSO`
- `UIResourceConfigSO`
- `UIFormConfigSO`
- `ConfigConvertSettings`
- `ConfigConvertChannel`
- `CharacterConfig`
- `PlayerControllerSO`

---

## 不再推荐菜单创建的类型

这些类型已经有更明确的 GUI 或自动生成入口，不建议再通过 `CreateAssetMenu` 重复暴露：

- `FYAssetSettings`
- `FYAssetAASettings`
- `FYAssetABSettings`
- `VersionDataBase`
- `ScriptObjectDataBase`
- `ScriptObjectContainer`

## 使用建议

- 找到对应 Editor 面板，再创建资产。
- 自动生成的资产由工具负责，不手动绕过工具创建。
- 如果某个 SO 已经有 `LoadOrCreate()` 或专属创建按钮，优先使用该入口。
- 当新增新的配置类时，先判断是否需要单独创建入口，再决定是否保留 `CreateAssetMenu`。
