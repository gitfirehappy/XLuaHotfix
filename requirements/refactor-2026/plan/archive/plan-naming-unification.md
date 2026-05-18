# Sub-Plan: 旧管线字段命名 PascalCase 统一

> **Source**: `drafts/plan-naming-unification-draft.md` (2026-05-08), field-semantics.md 审查
> **Status**: Executed 2026-05-09
> **Priority**: Low — 纯命名重构，不影响功能
> **Depends on**: 无前置依赖，可独立执行
> **Complements**: `plan-review-fix-20260509.md` T11（新代码 PascalCase 规则）+ T9/T10（typo 修复）

---

## Objective

旧管线数据结构（`VersionState` / `BundleInfo` / `Manifest`）使用 camelCase 字段，新管线统一 PascalCase。同语义字段命名不一致增加混淆风险——例如旧 `hash` vs 新 `FileHash`。

## Design Decision

- **方案 B：直接改，旧数据作废**。个人学习项目，不加 JSON 兼容属性（`[JsonProperty]` / `[FormerlySerializedAs]`）。旧 version_state.json / manifest.json 自然通过一次构建重新生成

---

## Tasks

| # | Task | Files |
|---|------|-------|
| N1 | `VersionState.cs` 字段重命名：`version`→`Version`, `hash`→`FileHash`, `totalSize`→`TotalSize`, `bundles`→`Bundles` | 1 |
| N2 | `BundleInfo`（同文件）字段重命名：`bundleName`→`BundleName`, `hash`→`FileHash`, `size`→`FileSize` | 1 (同 N1) |
| N3 | `Manifest.cs` 字段重命名：`latestPackage`→`LatestPackage`, `latestversion`→`LatestVersion` | 1 |
| N4 | 引用点批量更新：`HotfixManager.cs` / `LegacyHotfixBackend.cs` / `BuildProjectManager.cs` / `TaskBuildBundles.cs` | 4 |
| N5 | `dotnet build` 编译验证 | — |

---

## Modified Files

| File | Change |
|------|--------|
| `VersionState.cs` | 4 字段重命名 + 2 嵌套类字段（版本号字段保持小写 `version` → 不改，VersionNumber 自身已是 PascalCase 属性） |
| `Manifest.cs` | 2 字段重命名 |
| `HotfixManager.cs` | `latestPackage` → `LatestPackage`（4 处） |
| `LegacyHotfixBackend.cs` | `versionState.hash` / `bundle.bundleName` / `bundle.hash` / `versionState.totalSize` / `versionState.bundles`（5 处） |
| `BuildProjectManager.cs` | `versionState.totalSize` / `versionState.hash` / `bundleInfo.size` / `versionState.bundles`（7 处） |
| `TaskBuildBundles.cs` | `rawFileEntries[r].bundleName`（1 处） |

---

## Field Mapping

```
VersionState:
  version    → Version     (VersionNumber 类型，不改)
  hash       → FileHash
  totalSize  → TotalSize
  bundles    → Bundles

BundleInfo (nested in VersionState.cs):
  bundleName → BundleName
  hash       → FileHash
  size       → FileSize

Manifest:
  latestPackage  → LatestPackage
  latestversion  → LatestVersion
```

---

## Invariants

1. `dotnet build XLuaHotfix.sln` passes with 0 errors
2. 字段语义不变 — 纯重命名，无行为变化
3. JSON 反序列化旧数据可能失败 — 可接受，一次构建即可重新生成

---

## Change Log

| Date | Change |
|------|--------|
| 2026-05-08 | Initial draft from field-semantics review |
| 2026-05-09 | Promoted to formal plan: added task breakdown, modified files table, invariants, JSON compat decision |
