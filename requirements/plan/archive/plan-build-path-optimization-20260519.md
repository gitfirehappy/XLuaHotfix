# Sub-Plan BPO-1: Build Path Optimization

> **Status**: Archived — 2026-05-19; DONE, awaiting sign-off
> **Source Draft**: `../drafts/archive/draft-aa-ab-alignment-analysis-20260518.md` — path management refinement section
> **Developer Decision**: Build Repository is deferred. This plan only optimizes path responsibility boundaries.

## Objective

对齐构建侧和运行时路径管理边界：新增 Editor-only `BuildPathManager`，将运行时 `PathManager` 改名为 `RuntimePathManager`，并将 `BuildPathCustomizer` 改名为 `AddressablesBuildOutputOrganizer`。当前构建输出路径保持不变。

## Scope

| Item | Change |
|---|---|
| Build path source | 新增 `BuildPathManager`，集中暴露 `OutputRoot`、`PackagesDir`、`GetPackageDir()`、`GetBundlesDir()`、`PackageIndexPath`、`GetServerDataDir()` |
| Runtime path source | `PathManager` 改名为 `RuntimePathManager`，职责仍是运行时 `persistentDataPath` 下路径管理 |
| Addressables output organizer | `BuildPathCustomizer` 改名为 `AddressablesBuildOutputOrganizer`，保留 AA 专属产物整理职责 |
| Existing path layout | 保持 `HotfixOutput/Packages/Build_{yyyyMMdd}_{version}`、`manifest.json`、`bundles/` 不变 |
| UI Toolkit draft | 将已落地的 UI Toolkit migration draft 移入 `requirements/plan/drafts/archive/` 并更新 drafts index |

## PRS Design

### Paradigm

- `BuildPathManager`: 构建侧路径计算机制。Invariant: 不创建业务文件，不改变当前输出 layout。
- `RuntimePathManager`: 运行时设备端路径计算机制。Invariant: 仍以 `FYAssetSettings.Instance.ProjectName` + `BuildIndexData` 计算 hotfix/cache/save/log 根。
- `AddressablesBuildOutputOrganizer`: AA 构建产物整理机制。Invariant: 只处理 Addressables `ServerData` 到 package root 的复制/过滤规则。

### Rules

| Condition | Action | Order | Recovery |
|---|---|---|---|
| Build package name is generated | Use `BuildPathManager.GetPackageDir(packageName)` | Before backend `OrganizeOutput()` | If directory exists, backend organizer keeps current overwrite behavior |
| Legacy Addressables build starts | Clean `BuildPathManager.GetServerDataDir()` | Before `BuildPlayerContent` | Warn if deletion fails, keep current behavior |
| Legacy Addressables output is organized | `AddressablesBuildOutputOrganizer` copies catalog and bundles | After `BuildPlayerContent` | Missing source remains build error through existing backend checks |
| Runtime hotfix initializes | `RuntimePathManager.Initialize(buildIndex)` | Before hotfix download/apply | Existing fallback behavior unchanged |

### System

- `BuildProjectManager` consumes `BuildPathManager` for package root and `manifest.json` path.
- `LegacyAddressableBuildBackend` consumes `BuildPathManager.GetServerDataDir()` and `AddressablesBuildOutputOrganizer`.
- Runtime/hotfix/loader code consumes `RuntimePathManager`.
- No Build Repository APIs are introduced.

## Task Breakdown

| Task | Status | Description |
|---|---|---|
| BPO-1 | DONE | Add `BuildPathManager` and route build output path calculation through it |
| BPO-2 | DONE | Rename `PathManager` to `RuntimePathManager` and update runtime references |
| BPO-3 | DONE | Rename `BuildPathCustomizer` to `AddressablesBuildOutputOrganizer` and keep AA output rules |
| BPO-4 | DONE | Archive UI Toolkit migration draft and update indexes/progress/docs/context |
| BPO-5 | DONE | Verify compile/source audit |

## Verification

- [x] Source audit: active code no longer references `PathManager` or `BuildPathCustomizer`.
- [x] Project file sync: `Assembly-CSharp.csproj` includes `RuntimePathManager.cs`; `Assembly-CSharp-Editor.csproj` includes `BuildPathManager.cs` and `AddressablesBuildOutputOrganizer.cs`.
- [x] `dotnet build XLuaHotfix.sln` passed with 0 errors and 2 existing `System.Net.Http` conflict warnings.

## Invariants

1. Do not change output layout or file names.
2. Do not introduce Build Repository.
3. Do not change runtime loading behavior.
4. Do not change Addressables group movement or snapshot behavior.
5. Keep comments readable in Chinese, with English technical names preserved.

## Approval Checklist

- [x] `BuildPathCustomizer` 改名为 `AddressablesBuildOutputOrganizer`，不直接删除。
- [x] 新增 Editor-only `BuildPathManager`，对标 `RuntimePathManager` 的结构边界。
- [x] `PathManager` 改名为 `RuntimePathManager`。
- [x] 当前构建路径保持不变。
- [x] 本次只做路径优化，其余不动。
