# Plan-E10: BuildProjectManager 双管线拆分（IBuildBackend）

> **Status**: Executed (2026-05-11)
> **Risk**: Medium（代码搬家为主，逻辑不变；但 4 个 MenuItem + CLI 入口均受影响）
> **Dependencies**: BuildConfig 已落地 (post-review-fix-20260510), E9 已落地 (VersionNumber SemVer), E5-1/E5-2a/E5-2b/E6 已落地 (DAGScheduler + 7 Task)
> **Supersedes**: `drafts/draft-buildprojectmanager-split.md`
> **参照模式**: HotfixManager/IHotfixPipeline + AssetPackageManager/IPackageBackend

---

## Objective

将 `BuildProjectManager` 从 Addressables 直调单体拆为：

- **编排层** `BuildProjectManager`（static）— 版本管理、预/后处理、MenuItem、CLI 路由
- **接口** `IBuildBackend` — 构建执行、输出组织、版本描述
- **Legacy 实现** `LegacyAddressableBuildBackend` — 现有 Addressables 流程原封搬运
- **AB 实现** `ABBuildBackend` — DAGScheduler 驱动 7 个管线 Task

后端选择：`FYAssetConstants.USE_AB_BACKEND`（与 HotfixManager / AssetPackageManager 一致）

---

## 已收敛决策

| # | 决策 | 结论 |
|---|------|------|
| 1 | DifferentialProcessor.PrepareHotfix | **Legacy 独有**，不入接口 |
| 2 | BuildType (Full/Hotfix) | **保留为 `BuildAsync(version, buildType)` 参数**，不入 BuildConfig |
| 3 | VersionDataBase 版本管理 | **保留在编排层**，version 作为参数传入 Backend |
| 4 | MenuItem 入口 | **保留 4 个现有 MenuItem**，内部 `USE_AB_BACKEND` 切换 |
| 5 | ConfirmRelease/ResetGroups | **Legacy 独有**，不入接口 |
| 6 | CLI 一致性 | **BuildCommandLine 走同一 CreateBackend() 路径**，与 MenuItem 行为一致 |

---

## IBuildBackend 接口

```csharp
public interface IBuildBackend
{
    Task<bool> BuildAsync(VersionNumber version, BuildType buildType);
    void OrganizeOutput(string outputDir, VersionNumber version);
    void GenerateVersionState(string outputDir, VersionNumber version);
    string BuildSummary { get; }
}
```

---

## 两个 Backend 实现对比

| 方法 | LegacyAddressableBuildBackend | ABBuildBackend |
|------|:---|:---|
| `BuildAsync` | `ConfigureBasicSettings` + `AddressableAssetSettings.BuildPlayerContent`（从 ExecuteBuildFlow 搬运） | `DAGScheduler.Execute()` 跑 7 Task |
| `OrganizeOutput` | `BuildPathCustomizer.OrganizeBuildOutput`（搬运） | `TaskOrganizeOutput`（已实现） |
| `GenerateVersionState` | 现有 `GenerateVersionStateFile()`（搬运） | `ABManifest.SerializeToJson()`（已实现） |
| `BuildSummary` | 构建结果路径 + bundle 数 | Manifest 资产/Bundle 计数 |
| **依赖** | Addressables API + BuildPathCustomizer | BuildConfig + BuildContext + DAGScheduler |

---

## BuildProjectManager 编排层（重构后）

```
BuildFullPackage()
  ├─ versionData.IncrementVersion(true)     ← 共享
  ├─ HelperBuildDataExporter.ExportData()   ← 共享
  ├─ backend = CreateBackend()              ← 切换点
  ├─ backend.BuildAsync(version, Full)      ← 后端
  ├─ backend.OrganizeOutput(dir, version)   ← 后端
  ├─ backend.GenerateVersionState(dir, v)   ← 后端
  ├─ UpdateManifestFile(pkg, version)       ← 共享
  ├─ LocalStatusExporter.ExportData()       ← Full 独有
  └─ DifferentialProcessor.ReBuildSnapShots()← Full 独有

BuildHotfix()
  ├─ versionData.IncrementVersion()         ← 共享
  ├─ HelperBuildDataExporter.ExportData()   ← 共享
  ├─ DifferentialProcessor.PrepareHotfix()  ← Legacy 独有 (AB 跳过)
  ├─ backend = CreateBackend()              ← 切换点
  ├─ backend.BuildAsync(version, Hotfix)    ← 后端
  ├─ backend.OrganizeOutput(dir, version)   ← 后端
  └─ backend.GenerateVersionState(dir, v)   ← 后端

ConfirmReleaseHotfix()
  └─ DifferentialProcessor.ConfirmRelease() ← Legacy 独有 (直接保留)

ResetGroupsToOriginal()
  └─ DifferentialProcessor.RestoreOriginalGroups() ← Legacy 独有 (直接保留)

CreateBackend()
  └─ FYAssetConstants.USE_AB_BACKEND ? new ABBuildBackend() : new LegacyAddressableBuildBackend()
```

---

## BuildCommandLine 适配

**File**: `Assets/FYAsset/Scripts/Build/BuildManage/Editor/BuildCommandLine.cs`

当前 `Build()` 直接调用 `BuildProjectManager.BuildFullPackage()` / `BuildProjectManager.BuildHotfix()`。
重构后无需修改 BuildCommandLine 本身 — 它调用的 BuildProjectManager 公共 API 签名不变，内部已通过 `CreateBackend()` 切换。

**验证要求**: CLI 路径 (`-executeMethod BuildCommandLine.Build -buildType full/hotfix`) 与 MenuItem 路径行为完全一致。

---

## Task Breakdown

### E10-T1: IBuildBackend 接口定义

**新建** `Assets/FYAsset/Scripts/Build/BuildManage/Editor/IBuildBackend.cs`

- 4 个成员如上述接口定义
- Editor 程序集内（与 BuildProjectManager 同目录）

**Est.**: ~20 lines

### E10-T2: LegacyAddressableBuildBackend 实现

**新建** `Assets/FYAsset/Scripts/Build/BuildManage/Editor/LegacyAddressableBuildBackend.cs`

从 `BuildProjectManager.ExecuteBuildFlow` 搬运以下逻辑：
1. `BuildAsync` — `ConfigureBasicSettings` + `BuildPlayerContent` + `CleanServerData`
2. `OrganizeOutput` — 委托 `BuildPathCustomizer.OrganizeBuildOutput`
3. `GenerateVersionState` — 搬运 `GenerateVersionStateFile` 逻辑（扫描 bundles、计算 hash、写 version_state.json）
4. `BuildSummary` — 输出路径 + bundle 数

**搬运原则**: 代码搬家不改逻辑，保持与重构前行为完全一致。

**Est.**: ~120 lines（从 ExecuteBuildFlow 搬运）

### E10-T3: ABBuildBackend 实现

**新建** `Assets/FYAsset/Scripts/Build/BuildManage/Editor/ABBuildBackend.cs`

1. `BuildAsync` — 构造 BuildConfig → 创建 BuildContext → `DAGScheduler.Execute()`
2. `OrganizeOutput` — 从 BuildContext 读取 TaskOrganizeOutput 结果（已实现）
3. `GenerateVersionState` — 从 BuildContext 读取 ABManifest → `SerializeToJson()`
4. `BuildSummary` — Manifest 资产/Bundle 计数

**Est.**: ~80 lines

### E10-T4: BuildProjectManager 重构

**修改** `Assets/FYAsset/Scripts/Build/BuildManage/Editor/BuildProjectManager.cs`

1. 删除 `ExecuteBuildFlow` 私有方法
2. 删除 `ConfigureBasicSettings` / `SetSchemaPathToRemote` / `GenerateVersionStateFile` 私有方法（已搬入 Legacy Backend）
3. 新增 `CreateBackend()` 静态方法
4. `BuildFullPackage` / `BuildHotfix` 改为编排层流程（如上述流程图）
5. `ConfirmReleaseHotfix` / `ResetGroupsToOriginal` 保持不变
6. `LastBuildSuccess` 属性保留

**Est.**: 净减少 ~80 lines（搬出 > 新增编排代码）

### E10-T5: 编译验证 + CLI 路径验证

1. `dotnet build` 零错误
2. 确认 `BuildCommandLine.Build()` 调用链不变（BuildProjectManager 公共 API 签名未改）
3. 确认 4 个 MenuItem 仍正确注册
4. `USE_AB_BACKEND = false` 时行为与重构前一致
5. `USE_AB_BACKEND = true` 时 BuildFullPackage/BuildHotfix 走 DAGScheduler

**2026-05-11 执行结果**:
- 代码已完成：`IBuildBackend` / `LegacyAddressableBuildBackend` / `ABBuildBackend` / `BuildProjectManager.CreateBackend()`
- 静态调用链已确认：`BuildCommandLine` 未改签名，仍通过 `BuildProjectManager.BuildFullPackage()` / `BuildProjectManager.BuildHotfix()` 进入统一后端选择路径
- 结构对齐补充：`ABBuildBackend.OrganizeOutput()` 现将新管线 bundle 输出整理到 `{PackageRoot}/bundles/`，与 `HotfixManager` 下载目录和 `ABBundleLoader` 运行时查找路径一致
- `dotnet build XLuaHotfix.sln` 未完成真实代码验证：当前会在沙箱内因访问 `C:\Users\cfy\AppData\Local\Microsoft SDKs` 被拒绝而提前失败，属于环境权限阻塞，不是已确认的代码编译错误

---

## 执行顺序

```
E10-T1 (IBuildBackend 接口)
  → E10-T2 (LegacyAddressableBuildBackend)
  → E10-T3 (ABBuildBackend)
    → E10-T4 (BuildProjectManager 重构)
      → E10-T5 (编译验证 + CLI 验证)
```

顺序执行，无并行。T2/T3 理论上可并行但为降低合并风险顺序执行。

---

## 创建/修改文件清单

| 操作 | 文件 | 说明 |
|------|------|------|
| **新建** | `Editor/IBuildBackend.cs` | 接口定义 |
| **新建** | `Editor/LegacyAddressableBuildBackend.cs` | Legacy 实现（从 ExecuteBuildFlow 搬运） |
| **新建** | `Editor/ABBuildBackend.cs` | AB 实现（调 DAGScheduler） |
| **修改** | `Editor/BuildProjectManager.cs` | 删私有方法，改为 backend 调用 + 编排逻辑 |
| **不变** | `Editor/BuildCommandLine.cs` | 公共 API 签名不变，无需修改 |

> 所有文件路径前缀: `Assets/FYAsset/Scripts/Build/BuildManage/`

---

## 不变量

1. `dotnet build XLuaHotfix.sln` 0 errors
2. Legacy 路径行为与重构前完全一致（代码搬家不改逻辑）
3. AB 路径 DAGScheduler 通过（已有 7 Task + BuildConfig）
4. `USE_AB_BACKEND = false` 时，4 个 MenuItem + CLI 行为不变
5. `USE_AB_BACKEND = true` 时，BuildFullPackage/BuildHotfix 走新管线
6. ConfirmReleaseHotfix / ResetGroupsToOriginal 仅在 Legacy 路径有效（AB 路径 no-op 或提示）
7. BuildCommandLine.Build() 与对应 MenuItem 行为完全一致（同一 CreateBackend 路径）

---

## 与已有组件的关系

| 组件 | 关系 |
|------|------|
| `BuildConfig` | ABBuildBackend 构造时使用，Legacy 不感知 |
| `BuildContext` / `DAGScheduler` | ABBuildBackend 内部细节 |
| `DifferentialProcessor` | 编排层直接调用（Legacy 独有步骤），不入 Backend |
| `VersionDataBase` | 编排层管理版本号，Backend 只接收 VersionNumber 参数 |
| `BuildPathCustomizer` | Legacy Backend 内部使用 |
| `HelperBuildDataExporter` | 编排层共享步骤 |
| `BuildCommandLine` | 调用 BuildProjectManager 公共 API，无需感知 Backend 切换 |

---

## Out of Scope

- DifferentialProcessor 重构 — 不属于本轮
- Test coverage — 后续独立计划
- BuildPipelineWindow / Builder 面板 UI 联动 — 后续编辑器子计划
- 增量构建 / 缓存

---

## Acceptance Criteria

1. 编译零错误
2. `USE_AB_BACKEND = false`: MenuItem 和 CLI 构建行为与重构前完全一致
3. `USE_AB_BACKEND = true`: BuildFullPackage/BuildHotfix 走 DAGScheduler 管线
4. BuildCommandLine `-buildType full` 和 `-buildType hotfix` 与对应 MenuItem 行为一致
5. ConfirmReleaseHotfix / ResetGroupsToOriginal 在 Legacy 模式正常工作
6. 新增文件均在 Editor 程序集内，不影响 Runtime 编译

## Change Log

| Date | Change |
|------|--------|
| 2026-05-11 | Executed E10-T1~T4. Added `IBuildBackend`, `LegacyAddressableBuildBackend`, `ABBuildBackend`; refactored `BuildProjectManager` to orchestrator + `CreateBackend()`; kept `BuildCommandLine` unchanged; aligned AB package layout to `bundles/` runtime contract |
| 2026-05-11 | E10-T5 partially blocked by sandboxed `dotnet build` access to `C:\Users\cfy\AppData\Local\Microsoft SDKs`; static verification completed, external compile confirmation pending |
