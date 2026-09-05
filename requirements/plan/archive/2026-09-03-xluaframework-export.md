# XLuaFramework 独立导包化（2026-09-03 议定并完工）

> **Date correction**: This is September 2026 export-set work; the original `2026-07-24` stamp was a copy-paste from the July closeout. Decisions unchanged.

## Spec

### 目的
使 `Assets/XLuaFramework` 成为"仅依赖 XLua + UnityAPI"的可独立导包小框架：从 FYAsset 全局编译图中摘除，同时项目内运行时行为不变。

### 约束
1. 接缝形状 = 决策 1（已拍板）：`ILuaAssetLoader`（async/sync/unload 三方法，错误形状 `string`）+ `LuaAssetRuntime` 显式注入口，**无 attribute 自注册魔法**。
2. 注册点 = 决策 2 终案（已拍板）：`GameLauncher.Awake` 在配置 XLuaLoader 之前显式 `SetLoader`；未注册被调即 `InvalidOperationException`，错误消息指明修复步骤。
3. 依赖方向：Compat→XLuaFramework 单向；XLuaFramework 零 FYAsset 引用（含直接符号与通过泛型返回走私的 `RuntimeMessage`）。
4. 现有运行时行为不变：4 处 facade 调用的语义（成功/失败/日志路径）与现在逐一同构。
5. 不引入新 asmdef/package.json（与 aa-ab-decoupling 的导出组织标准一致：文选即集）。
6. `AssetPackageManager` facade 本身不动（Compat 侧改造仅限新增适配器）。

### 成功判据
- 新文本门禁：`XLuaFramework` 树不得引用任何 FYAsset 声明类型（含 `AssetPackageManager`/`RuntimeMessage` 全词表）。
- `LuaAssetRuntime` 未注册即调 → `InvalidOperationException`，消息含 "SetLoader"。
- 4 处调用点全部改走 `LuaAssetRuntime.Loader`；`GameLauncher.Awake` 在 XLuaLoader 配置之前注入适配器。
- 场景测试 4 套全绿（exit=0）。
- 项目内运行验证（Unity 侧）：lua VM 启动、Anim/ScriptObject/LuaBehaviour 三条桥链按现行为贴近 —— 由开发者编辑器内回归，不属于本环境的验证范围。

### 非目标
- XLuaFramework 的 asmdef 化/包化处理（仅做到"文选导出编译自足"）。
- facade/`AAPackageManager`/`ABPackageManager` 的任何修改。
- XLuaFramework 内部其他重构（Bridge/Loader 架构不动）。

## 现状事实（勘探结论）
- 全耦合 = 4 文件 × 1 符号（`AssetPackageManager`）：
  - `Bridge/Anime/AnimBridge.cs:27`（async）
  - `Bridge/ScriptObjectBridge.cs:34,47`（async ×2）
  - `Bridge/Utils/LuaBehaviourBridge.cs:126`（async）
  - `XLuaLoader/XLuaLoader.cs:172`（async）+ `:195`（sync）+ `:213`（unload）
- 错误消费形态收敛：`error?.ToString()` + 非空判断 + Debug 日志 → `string` 形状同构无损。
- `GameLauncher.Awake` 已持有 XLuaLoader 启动配置 → 显式注入落点现成且人眼可读。

## Tasks

| # | Task | 验证 |
|---|------|------|
| X1 | XLuaFramework 新增 `Resource/ILuaAssetLoader.cs` + `Resource/LuaAssetRuntime.cs`（SetLoader/IsRegistered/fail-fast getter）| 新文件 + 单元语义审查 |
| X2 | 4 处调用点机械替换为 `LuaAssetRuntime.Loader.*`；`RuntimeMessage` 折降为 string 在调用边处理 | 文本门禁（新增 XLuaFramework 边界 scenario） |
| X3 | Compat 新增 `Runtime/LuaAssetLoaderAdapter.cs`（纯适配类）；`GameLauncher` 显式注入（XLuaLoader 配置之前）| 串审 + 门禁 |
| X4 | 新 scenario 门禁 `test_xluaframework_boundary.cs`（注册进 s3 场景集）：XLuaFramework 不得引用 FYAsset 类型 | 门禁自身 PASS；与 ExportBoundary 兼容 |
| X5 | 全场景 4 套回归 + 旧契约复核（S2 facade 绑定测试不受影响，facade 本身未动）| exit=0 ×4 |
| X6 | `docs/FYAsset/` 不迁动；新接缝入 `context/` 说明；plan 归档 | docs/context 同步 |

## 风险与降级
- **错误形状降级**（RuntimeMessage→string）：前提是 consume/失败语义在 4 处以 ToString+非空判断即可承载；验证 = X2 完成后 grep 全部 `error` 消费点人审（<12 处）。
- **Fail-fast 时机**：未注册即报错在首次加载前，启动链路一条；模拟验证门槛低（门禁 + 代码审查双重）。
- **运行时回归**：编辑器/打机 phase 由开发者自测；已记入成功判据并明确责任边界。

## 执行顺序
X1 → X2 → X3 (`07891ea`) → X4 → X5 (门禁+演进过甜，S3 6/6，四套 exit=0) → X6 (context/`reference/lua-asset-loader-seam.md`、本档归档)。运行时 Unity 端回归（lua VM 启动、anim/SO/behaviour 三桥链）由开发者执行，不属本环境验证面。

---

## Appendix — 终审处置（2026-07-24）

- **C1（FYAsset→XLF Editor 引用，9 文件）**：采声明方案——XLuaFramework 声明为 AA/AB 两套装的前置包，固化于 `docs/FYAsset/export-sets-导出集.md`；不下沉第二道 seam（上游已否决的架构税）。
- **I2**：`LuaScripts/Game`（9 个 demo lua）迁出至 `Assets/Game/LuaScripts/`，同级 Game.meta 原 GUID 随迁并修复 orphan（防 git mv 兄弟 meta 陷阱）。
- **I3**：CollectorData 资产分家规则写入导出集规范；拆 per-backend 记档期货。
- **I4**：`Unity.Collections.ReadOnly` 替换为 XLF 本地 `ReadOnlyAttribute`，collections 依赖清零。
- **伴生修复**：扫全库补 4 个目录 meta（Compat/Editor/Repository、Compat/Runtime、Compat/Runtime/E2E、Game/LuaScripts）；Lua Auto Sync Config 的 AboutXLua 陈旧路径修为现路径。
- **I5/M6 留档**：`AssetAssociationSearchWindow`/`UITextureImportRule` 的 `Assets/AboutXLua/` 陈旧引用（年初重构残留，影响面=编辑器小工具，改修再议）；无 asmdef 现状维持。
