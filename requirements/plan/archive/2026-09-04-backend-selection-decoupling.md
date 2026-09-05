# 后端选择配置移出 Shared（2026-09-04）

> **Status**: Code/config migration executed; archived on 2026-09-05 under the developer-approved housekeeping boundary. Historical records report S3 6/6, runtime-resource 10/10, S2 5/5, Hotfix state-machine checks, solution compilation, and Unity batch compilation passing. Developer Editor runtime confirmation remains pending as ACCEPT-02 in `requirements/plan.md`. Archival does not assert acceptance or fresh verification.

## Spec

### 目的
删除 `FYAssetSettings.UseABBackend`，将 AA/AB 运行时选择收归 Compat 宿主配置，并保持 Shared、AA、AB 的独立导出边界。

### 已确认决策
- 新配置类型放在 `Assets/FYAsset/Scripts/Compat/Runtime/`。
- `FYAssetBackendSettings` 是普通数据型 `ScriptableObject`，仅保存 `BackendMode`。
- `BackendMode.Unspecified = 0`，有效值为 `AA` 与 `ABManifest`。
- `GameLauncher` 通过序列化字段直接引用配置资产，不使用 `Resources`、路径查找或默认回退。
- 配置资产默认放在 `Assets/Build/FYAssetBackendSettings.asset`，主要用于项目管理和 CLI 定位；运行时只使用 GameLauncher 引用。
- 后端启动时选择一次，运行中不切换；初始化失败直接中止启动并保留错误。
- 旧 `UseABBackend` 数据与旧设计代码直接删除，不做迁移或兼容读取。
- `BackendMode` 与 `BackendModeNames` 从 Shared 移到 Compat Runtime。
- Shared 构建/运行时公共数据不引用 Compat 类型；需要携带后端身份的内部构建参数改用中性字符串键。
- AA/AB 专用构建入口继续显式传入自己的后端键，不读取全局选择配置。

### 成功标准
- `FYAssetSettings`、Shared 全部源码不再出现 `UseABBackend`。
- Shared 不声明或引用 `BackendMode` 类型及 `BackendModeNames`；Shared 序列化协议中的 `BackendMode` 字符串字段保持现有格式。
- `GameLauncher` 具有序列化 `FYAssetBackendSettings` 引用，并对缺失/Unspecified 配置报错。
- AB Editor PlayMode 只由 AB 调用路径和 `PlayMode` 决定，不读取 Shared 后端选择字段。
- Compat 旧统一构建入口读取新的 Compat 配置；AA/AB 具体窗口和构建管理器保持独立。
- 场景门禁及四套静态场景测试通过；Unity 编译由开发者回归确认。

## Tasks

1. 在 S3 ExportBoundary 增加 Shared 后端选择耦合门禁，先得到 RED。
2. 在 Compat Runtime 新增 `BackendMode`、`BackendModeNames`、`FYAssetBackendSettings`，并创建默认配置资产。
3. 删除 `FYAssetSettings.UseABBackend`，从 Shared Settings 面板移除该控件。
4. 将 Shared 构建参数、Repository UI、发布工具和 Hotfix 校验中的 `BackendMode` 类型依赖改为中性字符串键。
5. 将 GameLauncher 改为序列化配置引用；Compat 统一构建入口和测试改读/改写新配置，删除旧 UseABBackend 测试状态。
6. 清理 ABPackageManager 的旧字段判断，更新配置资产/Prefab 引用及文档进度。
7. 运行 S3、静态编译验证与 `git diff --check`，再交给 Unity 重新编译和运行时验证。

## 风险
- 构建请求 API 的属性从枚举改为字符串会影响 Editor 调用链；通过完整源码搜索和 dotnet 场景门禁验证。
- `GameLauncher` Prefab 是当前运行时入口；默认资产引用必须同步到 Prefab，否则 Unity 启动会按已确认策略直接报错。
- 旧 `FYAssetSettings.asset` 中的 `UseABBackend` 行必须删除，不保留未识别序列化数据，也不做迁移或兼容读取。

## Execution Status

| Task | Status | Evidence |
|------|--------|----------|
| T1 | Done | `ExportBoundary` covers Shared backend-selection ownership and legacy field absence |
| T2-T6 | Done | Compat settings/task migration, call-site conversion, config/Prefab wiring, active docs and plan index synchronized |
| T7 | Automated verification done | S3 6/6, runtime-resource 10/10, S2 5/5, Hotfix pass, solution build 0 errors, Unity batch compile return code 0 |
| Developer acceptance | Pending | Open Unity Editor and confirm GameLauncher starts with the referenced `FYAssetBackendSettings` asset and selected AB backend |
