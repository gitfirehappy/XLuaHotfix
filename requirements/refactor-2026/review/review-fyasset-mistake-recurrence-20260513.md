# Review: FYAsset Mistake Recurrence Audit

> **Review date**: 2026-05-13
> **Method**: Read `context/mistakes/` first, then re-scan `Assets/FYAsset/Scripts/` for verified recurrence patterns
> **Scope**: FYAsset code only
> **Focus**: previously verified mistake patterns, not visual/editor aesthetics

## Findings

### 1. [High] `PL-01` / `IP-15` recurred: runtime config still mixes Editor-only loading with startup-time frozen values

**Matched mistake rules**

- `PL-01: Runtime Assembly Directly Using UnityEditor APIs`
- `IP-15: Static Readonly Captures Config at Type-Init`

**Evidence**

- [FYAssetSettings.cs](/E:/unity/project/XLuaHotfix/Assets/FYAsset/Scripts/FYAssetSettings.cs:1) still conditionally includes `UnityEditor` and uses `AssetDatabase` inside `LoadOrCreate()`.
- [FYAssetSettings.cs](/E:/unity/project/XLuaHotfix/Assets/FYAsset/Scripts/FYAssetSettings.cs:84) falls back to `CreateInstance<FYAssetSettings>()` outside Editor, which means Player/runtime gets a transient in-memory object rather than a persisted project asset.
- [PathManager.cs](/E:/unity/project/XLuaHotfix/Assets/FYAsset/Scripts/Helpers/PathManager.cs:11) captures `FYAssetSettings.Instance.ProjectName` in `static readonly PersistentRoot`.
- [HotfixManager.cs](/E:/unity/project/XLuaHotfix/Assets/FYAsset/Scripts/LegacyRuntime/HotfixManager.cs:15) captures `FYAssetSettings.Instance.HotfixUrl` in `static readonly _hotfixUrl`.

**Why this is a recurrence**

The mistake notebook already records that config migrated from constants to SO must not be frozen via `static readonly`, and that Runtime-side code must not rely on Editor-only APIs. FYAsset still violates both halves of that rule:

- the config source is not a pure Runtime source
- the values intended to be configurable are still effectively startup constants

**Risk**

- Player/runtime behavior can diverge from Editor assumptions
- changing the settings asset does not reliably affect already initialized systems
- future config migrations will continue to look configurable while behaving like compile-time constants

---

### 2. [High] `IP-16` recurred: hotfix/runtime file I/O still bypasses `FileHelper` in multiple critical paths

**Matched mistake rule**

- `IP-16: Raw File/Directory I/O Bypassing Shared Helper`

**Evidence**

- [ABHotfixBackend.cs](/E:/unity/project/XLuaHotfix/Assets/FYAsset/Scripts/LegacyRuntime/ABHotfixBackend.cs:58) uses `File.Exists(...)` / [64](/E:/unity/project/XLuaHotfix/Assets/FYAsset/Scripts/LegacyRuntime/ABHotfixBackend.cs:64) on local manifest paths.
- [ABHotfixBackend.cs](/E:/unity/project/XLuaHotfix/Assets/FYAsset/Scripts/LegacyRuntime/ABHotfixBackend.cs:139) uses `File.Exists(...)` and [140](/E:/unity/project/XLuaHotfix/Assets/FYAsset/Scripts/LegacyRuntime/ABHotfixBackend.cs:140) `File.Delete(...)` for alternate manifest cleanup.
- [CatalogUpdater.cs](/E:/unity/project/XLuaHotfix/Assets/FYAsset/Scripts/LegacyRuntime/CatalogUpdater.cs:22) checks `File.Exists(catalogFullPath)`.
- [CatalogUpdater.cs](/E:/unity/project/XLuaHotfix/Assets/FYAsset/Scripts/LegacyRuntime/CatalogUpdater.cs:85) checks `File.Exists(localPath)` in redirect logic.
- [PathManager.cs](/E:/unity/project/XLuaHotfix/Assets/FYAsset/Scripts/Helpers/PathManager.cs:76) directly uses `Directory.CreateDirectory(...)` on every managed root.
- [PackageCleaner.cs](/E:/unity/project/XLuaHotfix/Assets/FYAsset/Scripts/LegacyRuntime/PackageCleaner.cs:108) builds `DirectoryInfo`, and [111](/E:/unity/project/XLuaHotfix/Assets/FYAsset/Scripts/LegacyRuntime/PackageCleaner.cs:111) walks `GetFiles("*", SearchOption.AllDirectories)`.

**Why this is a recurrence**

The project already documented that new file operations must extend `FileHelper` first instead of falling back to raw `File.*` / `Directory.*`. The hotfix/runtime area is exactly where this rule matters most, and it is still inconsistent.

**Risk**

- cross-platform semantics drift between subsystems
- partial deletes / existence checks / path handling keep fragmenting
- later fixes need to be patched in multiple places again instead of one shared abstraction

---

### 3. [Medium] `IP-04` recurred: exceptions are still swallowed without diagnostics in some non-trivial paths

**Matched mistake rule**

- `IP-04: Empty catch Block`

**Evidence**

- [PackageCleaner.cs](/E:/unity/project/XLuaHotfix/Assets/FYAsset/Scripts/LegacyRuntime/PackageCleaner.cs:116) has `catch { }` around directory size calculation, with only a comment-equivalent behavior of returning partial size.
- [ABPackageBackend.cs](/E:/unity/project/XLuaHotfix/Assets/FYAsset/Scripts/Runtime/Backends/AB/ABPackageBackend.cs:379) catches `Exception` during asset extraction and drops the actual exception details, returning a generic `AssetExtractionFailed(...)`.
- [CollectorReverseIndex.cs](/E:/unity/project/XLuaHotfix/Assets/FYAsset/Scripts/Build/Collector/Editor/CollectorReverseIndex.cs:192) catches `Exception` from rule resolution and silently returns `true` from `ShouldSkipAsset`.
- [CollectorSettingValidator.cs](/E:/unity/project/XLuaHotfix/Assets/FYAsset/Scripts/Build/Collector/Editor/CollectorSettingValidator.cs:216) catches `Exception` and collapses all causes into generic `RuleNotFound`.

**Why this is a recurrence**

The notebook already calls out that “catch-and-continue” without enough diagnostics makes later debugging much harder. These sites still prefer survivability over diagnosability even where the suppressed information would be highly actionable.

**Risk**

- real root causes become invisible in Collector/rule validation paths
- runtime extraction failures lose the exact exception that would explain bad `SourcePath`, type mismatch, or bundle corruption

---

### 4. [Medium] `PL-12` recurred: build version fallback still uses local time via `DateTime.Now`

**Matched mistake rule**

- `PL-12: DateTime.Now for Version Timestamps`

**Evidence**

- [TaskPrepareContext.cs](/E:/unity/project/XLuaHotfix/Assets/FYAsset/Scripts/Build/Pipeline/Editor/Tasks/TaskPrepareContext.cs:35) builds the fallback version string from `DateTime.Now.ToString("yyyyMMdd-HHmmss")`.

**Why this is a recurrence**

The mistake file already records that build/version identifiers should use `DateTime.UtcNow` unless the value is purely user-facing. This fallback string is used for build output naming, so local timezone differences can still leak into artifact identity.

**Risk**

- same code built in different timezones produces different identifiers
- CI and local output naming become less comparable than necessary

---

### 5. [Medium] `IP-18` / `IP-22` partially recurred: error/result channels are still inconsistent across FYAsset subsystems

**Matched mistake rules**

- `IP-18: Ad-Hoc Error Transport Instead of Structured Result`
- `IP-22: Inconsistent Result/Message Patterns`

**Evidence**

- Runtime/hotfix path now has `HotfixStepResult`, but many APIs still return `null` / `bool`:
  - [ABHotfixBackend.cs](/E:/unity/project/XLuaHotfix/Assets/FYAsset/Scripts/LegacyRuntime/ABHotfixBackend.cs:93) returns `null` on remote manifest fetch failure
  - [CatalogUpdater.cs](/E:/unity/project/XLuaHotfix/Assets/FYAsset/Scripts/LegacyRuntime/CatalogUpdater.cs:20) still returns `Task<bool>`
- Build path uses `BuildTaskResult.Fail(...)`, but Collector validation still emits `BuildMessage` directly, and runtime loading uses `RuntimeMessage`.
- The system has improved from the old state, but the error/result surface is still mixed rather than truly unified.

**Why this is a recurrence**

The project already learned that `bool` / `null` / generic logs are not enough once orchestration gets more complex. FYAsset has started to introduce better result objects, but the migration is only partial.

**Risk**

- top-level flows still lose structured diagnostics at some boundaries
- debugging remains inconsistent depending on which subsystem failed first

---

### 6. [Low] `PP-09` recurred historically and is only partially fixed: ownership comments drifted during config migration

**Matched mistake rule**

- `PP-09: Ownership Comment Not Updated After Config Migration`

**Evidence**

- The mistake notebook recorded this exact issue around `BuildPipelineConfig.DefaultBackendMode`.
- [BackendMode.cs](/E:/unity/project/XLuaHotfix/Assets/FYAsset/Scripts/Build/BackendMode.cs:3) is now corrected to `FYAssetSettings.Instance.UseABBackend`, which shows the previous drift did exist.
- This is not a current failing line anymore, but it confirms the same drift pattern has already reappeared during recent FYAsset work.

**Why include it**

This one is not a current blocking bug, but it is worth noting because the recurrence already happened once inside the same refactor wave and only got corrected afterward. That raises the chance of similar ownership-doc drift elsewhere in the ongoing plans.

## Summary

The mistake recurrence count is high enough to justify a report. The strongest repeat offenders in FYAsset right now are:

1. editor/runtime boundary confusion around `FYAssetSettings`
2. config still being frozen by `static readonly`
3. raw file I/O bypassing `FileHelper` in hotfix/runtime paths
4. exception swallowing that hides useful diagnostics

The overall pattern is not “one isolated slip,” but “the same architectural mistakes reappearing in adjacent subsystems.” If you want, I can take this report and immediately turn it into a concrete fix list ordered by risk.
