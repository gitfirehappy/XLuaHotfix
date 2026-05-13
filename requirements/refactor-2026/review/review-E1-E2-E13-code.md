# Code Review: E1 Rework + E2 + E1-3 Implementation

> **Review date**: 2026-04-27
> **Scope**: Commits `d105127` + `3fd23b7` — 10 new .cs files, 4 modified .cs files
> **Review type**: Post-execution code quality audit (plan vs implementation)
> **Processed**: 2026-05-11 · E1-E3 plans executed, fixes in `c617631` + `af1eb53`
> **Status**: 📦 Archived · Streamlined 2026-05-11

---

## Files Reviewed

| File | New/Modified | Lines | Plan Reference |
|------|:---:|------|---------------|
| `CollectionScanner.cs` | New | ~667 | plan-E1-3 |
| `GlobMatcher.cs` | New | ~56 | plan-E1-3 |
| `ScanResult.cs` | New | ~47 | plan-E1-3 |
| `BundleNameBuilder.cs` | New | ~50 | plan-E2 |
| `PackByDirectory.cs` | New | ~46 | plan-E2 |
| `PackByLabel.cs` | New | ~35 | plan-E2 |
| `PackSeparately.cs` | New | ~16 | plan-E2 |
| `GroupAll.cs` | New | ~15 | plan-E1-1 (audit) |
| `IGroupRule.cs` | New | ~31 | plan-E1-1 (audit) |
| `SystemIdentifiers.cs` | New | ~22 | (spontaneous extraction) |
| `CollectorSetting.cs` | Modified | +4 fields | plan-E1-1 (audit) |
| `RuleResolver.cs` | Modified | +GetGroupRule | plan-E1-1 (audit) |
| `Constants.cs` | Modified | +4 GROUP_RULE_* | plan-E1-1 (audit) |
| `IPackRule.cs` / `PackRuleContext` | Pre-existing | (E1-2 proactive) | plan-E2 (contract) |
| `IAddressRule.cs` / `AddressRuleContext` | Pre-existing | +PrimaryType field | plan-E1-1 (enhancement) |

---

## Critical Bugs (P0)

### P0-1: Ownership Dedup Logic — IsPathContained Parameters Reversed

**Status**: 🔴 FIXED (2026-04-27, during review)

**File**: `CollectionScanner.cs:155-165`

**Bug**: The exclusion-building loop passes `IsPathContained(currentPaths[j], currentPaths[i])` — checking whether the *deeper* path (j, sorted first in descending-depth order) contains the *shallower* path (i). This is geometrically impossible: a deeper sub-directory can never contain a shallower parent directory.

**Result**: `excludedPaths` is always empty for every collector. Deepest-path ownership dedup is completely non-functional. An asset under `Assets/Art/Audio/song.wav` would be collected by all three collectors (`Assets/Art/Audio/`, `Assets/Art/`, `Assets/`), violating E1-3 Invariant #2.

**Fix**: Swap parameters to `IsPathContained(currentPaths[i], currentPaths[j])` — check if shallower path `i` contains deeper path `j`.

```csharp
// BEFORE (broken):
if (IsPathContained(currentPaths[j], currentPaths[i]))

// AFTER (fixed):
// i is shallower (sorted later), j is deeper (sorted earlier)
// Check: does shallower path i contain deeper path j?
if (IsPathContained(currentPaths[i], currentPaths[j]))
```

---

## Plan Deviations

### D1: PATH_NOT_FOUND Severity — Warning → Error

**Status**: ⚠️ Needs developer decision

**Plan**: plan-E1-3.md error table — `PATH_NOT_FOUND` = `Warning`, does not block scan
**Code**: `CollectionScanner.cs:216-219` — treated as `Error`, returns `false` to abort

```csharp
// Current code:
result.Messages.Add(Error("PATH_NOT_FOUND", ...));
return false;

// Plan specified:
// result.Messages.Add(Warning("PATH_NOT_FOUND", ...));
// return true; // continue
```

**Trade-off**: Treating missing paths as errors catches configuration mistakes early (preferred for CI/CD). But the approved plan explicitly says Warning. Needs explicit developer confirmation.


### D2: AddressRuleContext Added PrimaryType Field

**Status**: ✅ Accepted enhancement

**Plan**: plan-E1-1 AddressRuleContext = { AssetPath, GroupName, CollectPath }
**Code**: `IAddressRule.cs:26` — added `public string PrimaryType`

**Rationale**: `AddressByFileName` (E1-2) calls `AssetAddressGenerator.GenerateShortAddress(assetPath, primaryType, useTypeSuffix)`, which needs PrimaryType for auto-address generation with type suffix. Without this field, the address generation pipeline breaks.


### D3: GroupRuleContext Added ParentGroupName Field

**Status**: ✅ Accepted enhancement

**Plan**: plan-E1-1 GroupRuleContext = { AssetPath, Classification, CollectPath, PackageName }
**Code**: `IGroupRule.cs:30` — added `public string ParentGroupName`

**Rationale**: `GroupAll.GetTargetGroup()` returns `ctx.ParentGroupName` — the default rule literally cannot function without this field.


### D4: BundleNameBuilder SanitizeSegment Preserves `$`

**Status**: ✅ Accepted enhancement (documentation gap)

**Plan**: plan-E2.md D2 — lowercase + non-alphanumeric → `_`, hyphen preserved
**Code**: `BundleNameBuilder.cs:39` — additionally preserves `$` character

**Rationale**: `$orphan` sentinel from PackByLabel and `$shared` GroupName from E4 require `$` prefix preservation. `SystemIdentifiers` class formalizes the `$` prefix convention. Without `$` preservation, `$orphan` becomes `_orphan`, losing system-reserved recognizability.

**Action**: plan-E2.md should document the `$` preservation rule.


### D5: EMPTY_PACKAGE Error Code — Not in Spec

**Status**: ✅ Accepted addition

**Plan**: plan-E1-3.md error table has 7 codes, `EMPTY_PACKAGE` not listed
**Code**: `CollectionScanner.cs:44-46` — added as Warning for Packages with zero Groups

**Rationale**: Graceful handling of edge case configuration. Non-blocking Warning is appropriate.

---

## Code Quality Issues (P2)

### Q1: Silent Skip on Null/Empty PackageName

**File**: `CollectionScanner.cs:40-41`

```csharp
if (package == null || string.IsNullOrEmpty(package.PackageName))
    continue;  // silent — no ScanMessage generated
```

A Package with null or empty PackageName is silently skipped with zero diagnostic output. The user gets no warning that a configured Package was ignored.

**Recommendation**: Generate a Warning-level ScanMessage with a new error code (e.g., `INVALID_PACKAGE`).


### Q2: Labels Merge Dedup — Case-Insensitive but Preserves Original Case

**File**: `CollectionScanner.cs:534-563`

`HashSet<string>(StringComparer.OrdinalIgnoreCase)` deduplicates "UI" and "ui" correctly, but first-writer-wins on casing. `CollectedAssetInfo.Labels` may contain mixed case depending on Group vs Collector label declaration order. `PackByLabel.ToLowerInvariant()` normalizes case before sort+join, so packKey is always deterministic lowercase.

**Impact**: Cosmetic — only affects debug/display of `CollectedAssetInfo.Labels`. Runtime packKey determination is correct.


### Q3: ResolveRuleSafe Uses typeof() Chain

**File**: `CollectionScanner.cs:502-532`

```csharp
if (typeof(T) == typeof(IAddressRule))
    return RuleResolver.GetAddressRule(className) as T;
if (typeof(T) == typeof(IPackRule))
    return RuleResolver.GetPackRule(className) as T;
// ...
```

Functional but fragile — adding a 5th rule type requires adding another `if` branch. Acceptable given current 4-rule count; refactor if rule count exceeds 6.


### Q4: ScanResult.HasErrors Uses LINQ in Editor Code

**File**: `ScanResult.cs:16`

```csharp
public bool HasErrors => Messages.Any(m => m.Severity == ScanSeverity.Error);
```

Editor code path, not hot runtime path — LINQ is acceptable per project conventions (LINQ allowed in editor/build code). Non-issue.


---

## Positive Findings

1. **ResolveRuleSafe<T> generic pattern** — Four rule resolver calls unified into one generic method, avoiding 4 near-identical resolve+nullcheck+error blocks.

2. **SystemIdentifiers extraction** — `$`-prefixed sentinel values (`$orphan`, `$shared`) centralized with validation utility. Good spontaneous design pattern not specified in any plan.

3. **E2 zero-back-change execution** — E1-1/E1-2 proactively adopted E2 contract (GetPackKey, Labels field, collectDirName-only return), so E2 implementation touched zero existing .cs files. Good cross-phase coordination.

4. **ScanResult.HasErrors lazy evaluation** — Re-evaluates each access rather than caching. In Editor context with small message lists (<10 items), this is correct and avoids cache invalidation bugs.

5. **BundleNameBuilder string.Concat** — Uses `string.Concat(safePkg, "_", safeGroup, "_", safeKey)` instead of `$"{...}"` interpolation, avoiding intermediate string allocations.

---

## Invariant Verification Matrix

### plan-E1-3 Invariants

| # | Invariant | Status |
|---|-----------|:------:|
| 1 | GUID uniqueness enforcement | ✅ CheckGuidUniqueness at Step 3 |
| 2 | Deepest-path Collector wins | 🔴 BROKEN (P0-1, now fixed) |
| 3 | Cross-Package overlap → Error | ✅ CheckCrossPackageOverlaps |
| 4 | Same-depth same-path → Error | ✅ CheckSameDepthConflicts |
| 5 | IgnorePatterns `*.bak` excludes correctly | ✅ GlobMatcher.IsMatch |
| 6 | IgnorePatterns `dirname/` excludes recursively | ✅ ContainsPathSegment |
| 7 | Labels merge = union deduplicated | ✅ MergeLabels + HashSet |
| 8 | FilterRule before IgnorePatterns | ✅ Execution order verified |
| 9 | ScanResult.HasErrors false on clean scan | ✅ |
| 10 | dotnet build 0 errors | ✅ (per progress log) |

### plan-E2 Invariants

| # | Invariant | Status |
|---|-----------|:------:|
| 1 | IPackRule method is GetPackKey | ✅ |
| 2 | PackRuleContext has Labels field | ✅ |
| 3 | BundleNameBuilder lowercase output | ✅ |
| 4 | BundleNameBuilder illegal char → _ | ✅ (also preserves $ and -) |
| 5 | PackSeparately = filename no ext | ✅ |
| 6 | PackByDirectory = sub-dir name | ✅ |
| 7 | PackByLabel sorted lowercase + -- join | ✅ |
| 8 | PackByLabel empty → $orphan | ✅ |
| 9 | RuleResolver resolves all 3 rules | ✅ (+ IGroupRule = 4) |
| 10 | dotnet build 0 errors | ✅ |

---

## Summary

| Severity | Count | Items |
|----------|:-----:|-------|
| P0 Critical Bug | 1 | Ownership dedup parameters reversed (FIXED) |
| P1 Plan Deviation | 5 | PATH_NOT_FOUND severity (⚠️), PrimaryType (✅), ParentGroupName (✅), $ preservation (✅), EMPTY_PACKAGE (✅) |
| P2 Quality | 4 | Silent skip (⚠️), Label case (minor), typeof chain (OK), LINQ in ScanResult (OK) |

**Overall**: Implementation quality is high. The single critical bug (P0-1) is a 1-character parameter swap — fixed during review. Plan deviations are sensible enhancements except PATH_NOT_FOUND severity change which needs explicit confirmation.
