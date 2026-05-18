# Draft: BuildProjectManager 接口分离（双管线）

> **Date**: 2026-05-11
> **Status**: Promoted → [plan-E10-buildbackend.md](../plan-E10-buildbackend.md) (2026-05-11)
> **Depends on**: post-review-fix-20260510 (BuildConfig 已落地)
> **参照模式**: HotfixManager/IHotfixPipeline + AssetPackageManager/IPackageBackend

---

## 目标

将 `BuildProjectManager` 从 Addressables 直调单体拆为：
- **编排层** `BuildProjectManager`（static）— 版本管理、预/后处理、MenuItem
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

---

## IBuildBackend 接口

```csharp
/// <summary>
/// 构建后端接口 — 抽象 Addressables 构建 vs DAG 管线构建。
/// 参照 IHotfixPipeline / IPackageBackend 模式。
/// </summary>
public interface IBuildBackend
{
    /// <summary>执行构建主流程。编排层已处理好版本号、差异分析等前置步骤。</summary>
    Task<bool> BuildAsync(VersionNumber version, BuildType buildType);

    /// <summary>构建后整理输出目录结构（Backend 特定布局）。</summary>
    void OrganizeOutput(string outputDir, VersionNumber version);

    /// <summary>生成版本描述文件（version_state.json / ABManifest.json）。</summary>
    void GenerateVersionState(string outputDir, VersionNumber version);

    /// <summary>构建结果摘要文本（供日志 / EditorUtility 展示）。</summary>
    string BuildSummary { get; }
}
```

---

## 两个 Backend 实现对比

| 方法 | LegacyAddressableBuildBackend | ABBuildBackend |
|------|:---|:---|
| `BuildAsync` | `AddressableAssetSettings.BuildPlayerContent`（从 ExecuteBuildFlow 搬运） | `DAGScheduler.Execute()` 跑 7 Task |
| `OrganizeOutput` | `BuildPathCustomizer.OrganizeBuildOutput`（搬运） | `TaskOrganizeOutput`（已实现） |
| `GenerateVersionState` | 现有 `GenerateVersionStateFile()`（搬运） | `ABManifest.SerializeToJson()`（已实现） |
| `BuildSummary` | 构建结果路径 + bundle 数 | Manifest 资产/Bundle 计数 |
| **依赖** | Addressables API + BuildPathCustomizer | BuildConfig + BuildContext + DAGScheduler |

---

## BuildProjectManager 编排层（重构后）

```
BuildFullPackage()
  ├─ versionData.IncrementVersion(true)     ← 共享
  ├─ backend = CreateBackend()              ← 切换点
  ├─ backend.BuildAsync(version, Full)      ← 后端
  ├─ backend.OrganizeOutput(dir, version)   ← 后端
  ├─ backend.GenerateVersionState(dir, v)   ← 后端
  ├─ LocalStatusExporter.ExportData()       ← Full 独有
  └─ DifferentialProcessor.ReBuildSnapShots()← Full 独有

BuildHotfix()
  ├─ versionData.IncrementVersion()         ← 共享
  ├─ DifferentialProcessor.PrepareHotfix()  ← Legacy 独有 (AB 跳过)
  ├─ backend = CreateBackend()              ← 切换点
  ├─ backend.BuildAsync(version, Hotfix)    ← 后端
  ├─ backend.OrganizeOutput(dir, version)   ← 后端
  └─ backend.GenerateVersionState(dir, v)   ← 后端

ConfirmReleaseHotfix()
  └─ DifferentialProcessor.ConfirmRelease() ← Legacy 独有 (直接保留)

ResetGroupsToOriginal()
  └─ DifferentialProcessor.RestoreOriginalGroups() ← Legacy 独有 (直接保留)
```

---

## 创建/修改文件清单

| 操作 | 文件 | 说明 |
|------|------|------|
| **新建** | `IBuildBackend.cs` | 接口定义（Editor 程序集） |
| **新建** | `LegacyAddressableBuildBackend.cs` | Legacy 实现（从 ExecuteBuildFlow 搬运） |
| **新建** | `ABBuildBackend.cs` | AB 实现（调 DAGScheduler） |
| **修改** | `BuildProjectManager.cs` | 删 ExecuteBuildFlow 等私有方法，改为 backend 调用 + 编排逻辑 |

---

## 不变量

1. `dotnet build XLuaHotfix.sln` 0 errors
2. Legacy 路径行为与重构前完全一致（代码搬家不改逻辑）
3. AB 路径 DAGScheduler 通过（已有 7 Task + BuildConfig）
4. `USE_AB_BACKEND = false` 时，4 个 MenuItem 行为不变
5. `USE_AB_BACKEND = true` 时，BuildFullPackage/BuildHotfix 走新管线
6. ConfirmReleaseHotfix / ResetGroupsToOriginal 仅在 Legacy 路径有效（AB 路径 no-op 或提示）

---

## 与 BuildConfig 的关系

- `ABBuildBackend` 构造时接收 `BuildConfig`（或用 `TaskPrepareContext` 自己构造）
- `LegacyAddressableBuildBackend` 不感知 `BuildConfig` / `BuildContext` / `DAGScheduler`
- 编排层不创建 `BuildConfig`（那是 AB Backend 内部细节）

---

## Out of scope

- CLI 入口统一（`BuildCommandLine`）— 后续 plan
- DifferentialProcessor 重构 — 不属于本轮
- Test coverage
