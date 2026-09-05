# 三套独立导出集

> 返回总览：[FYAsset 资源管理总览](./资源管理架构文档.md)

本项目按"文选即集"组织三套可独立导出的运行时包。边界由 `tests/scenario/s3_resource_boundary` 的 `ExportBoundary` / `XLuaFrameworkBoundary` 文本门禁守护。

## 导出集与前置依赖

| 导出集 | 包含 | 前置依赖 | 明确不包含 |
|---|---|---|---|
| **AA 套装** | `Assets/FYAsset/Scripts/AA` + `Assets/FYAsset/Scripts/Shared` | `com.unity.addressables` | Compat、AB 树、AB Collector 资产、XLuaFramework |
| **AB 套装** | `Assets/FYAsset/Scripts/AB` + `Assets/FYAsset/Scripts/Shared` | 无 XLuaFramework / Addressables | Compat、AA 树、AA 相关资产、XLuaFramework |
| **XLuaFramework** | `Assets/XLuaFramework` | XLua | FYAsset 全树、项目壳层（UI/Game/Global 代码）、Compat |

## 规则

1. **三套严格独立编译**：`AA∪Shared`、`AB∪Shared`、`XLuaFramework` 分别导入空工程即可编译。彼此不得互引类型。
2. **Compat 永不进入三套基础导出**：它是宿主集成层（facade、CLI、测试矩阵、Cloudflare 目标、运维面层、Lua 索引构建 Task）。需要 lua 热更时，宿主额外导入 `Compat + XLuaFramework`，再通过 `BuildPipelineConfig.Tasks` 插入 `LuaScriptsIndexBuildTask`。
3. **管线骨架与自定义 Task 分槽**：AA/AB backbone 只校验骨架名单（查漏不拒外）。`config.Tasks` 中超出骨架的条目即自定义 Task，按列表顺序执行；类型由 `BuildTaskResolver` 按名解析。装配违约（空名/重名/找不到实现）明确 Fail，不静默跳过。
4. **Collector 资产分家**：`Assets/FYAsset/CollectorData/CollectorSetting.asset` 引用 AB 树脚本 guid——AA 套装打包时必须排除该资产（或将来拆 per-backend）。
5. **序列化无损**：三树资产 GUID 互引为零；移动资产必须 `git mv` + 同级 .meta 随迁；目录 meta 属父目录兄弟文件，**手动移动目录时必须手动搬它**。
6. **运行时资源接缝**：XLuaFramework 对运行期资源只露 `LuaAssetRuntime` 注入口（`ILuaAssetLoader`），宿主在启动壳 `SetLoader`（本项目：`GameLauncher.BootPhase` + `Compat/FYAssetLuaAssetLoaderAdapter`）。

## 验证

- `tests/scenario/s3_resource_boundary`：ExportBoundary（AA/AB/Shared 三方向互引 + →Compat 反引 + 三树零 LuaIndex 词表 + Compat Task 注入）+ XLuaFrameworkBoundary（XLF 对 FYAsset 类型零引用）+ UpperPackageBoundary（上层壳薄面规则）。
- 导出验证不可省略项：空工程分别仅导三集编译；AA 集 manifest 勾选 Addressables。Lua 集成能力不在三套基础导出的自足范围内。
