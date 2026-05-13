# Sub-Plan E1-3: Collection Scan Engine

> **Risk**: Low-Medium (Editor-only logic, no runtime impact, but scan output includes bundle logical names via BundleNameBuilder — directly touches build-product naming chain. This naming convention is internal to the new AB build pipeline only; existing Addressables build output and live hotfix bundles are not affected)
> **Dependencies**: E1-1 (data model, enums, interfaces, **including IGroupRule** `[审计修正]`), E1-2 (Classifier, default rules, ForcePayloadKind), E2 (IPackRule.GetPackKey contract, PackRuleContext.Labels field, BundleNameBuilder.Build utility)
> **Status**: Realized — 2026-04-27, CollectionScanner + GlobMatcher + ScanResult landed

---

## Objective

Implement `CollectionScanner`: transform `CollectorSetting` SO into `List<CollectedAssetInfo>` via `AssetDatabase.FindAssets`, deepest-path dedup, IgnorePatterns matching, labels merge, and per-asset rule execution. Also adds `IgnorePatterns` field to Collector class.

---

## Confirmed Design Decisions

### Scan Engine: Static Utility Class

`CollectionScanner` is a static Editor-only utility class (like `AssetClassifier`). No instance state, no lifecycle.

```
CollectionScanner.Scan(CollectorSetting) → List<CollectedAssetInfo>
```

### Asset Discovery: AssetDatabase

Uses `AssetDatabase.FindAssets("", new[] { collectPath })` + `AssetDatabase.GUIDToAssetPath`. Unity standard Editor API — auto-skips `.meta`, provides GUID and Type information directly.

### Ownership Resolution: Package-Scoped Deepest-Path

Ownership resolution runs **within each Package independently**. Cross-Package path overlap is treated as a configuration error and reported immediately.

Within a Package:
1. Collect all Collector `collectPath` values
2. Sort by path depth descending (deeper = higher priority)
3. Same-depth same-path = configuration conflict → error
4. Each Collector scans its `collectPath` but **excludes sub-paths** already claimed by deeper Collectors
5. Result: every asset belongs to exactly one Collector

**Path depth**: `collectPath.Count(c => c == '/')` — segment count.

**Exclusion check**: `assetPath.StartsWith(excludedCollectPath + "/")` or `assetPath == excludedCollectPath` — simple string prefix, O(1) per check.

### Cross-Package Overlap Detection

Before per-Package scanning, a global pre-check runs:
- For every pair of Collectors across different Packages, check path containment
- If Package A has `collectPath = "Assets/Art/"` and Package B has `collectPath = "Assets/Art/Audio/"` → report error, abort scan
- Same-path across Packages also reports error

This catches configuration mistakes early. Packages are independent packaging units — path overlap between them has no valid use case.

### Execution Order Within Each Collector

```
AssetDatabase.FindAssets (directory scope)
  → Exclude owned sub-paths (deepest-path dedup)
    → FilterRule (CollectAll: skip .meta/.cs/.dll/.asmdef/.asmref/Editor/)
      → IgnorePatterns (glob match against relative path)
        → Classify (AssetClassifier.Classify)
        → GroupRule (IGroupRule.GetTargetGroup) — determines target Group  [审计新增]
        → AddressRule (AddressByFileName etc.) — uses target Group name
        → Labels merge (targetGroup.Labels ∪ Collector.Labels) — Labels from target Group
        → PackRule.GetPackKey (via PackRuleContext with Labels + target GroupName)
        → BundleNameBuilder.Build (packageName, targetGroupName, packKey)
        → Assemble CollectedAssetInfo (GroupName = targetGroupName)
```

### IgnorePatterns: Simplified Gitignore Subset

`Collector` gains a `public List<string> IgnorePatterns = new();` field.

Each pattern is matched against the **relative path** (asset path minus collectPath prefix).

**Supported patterns** (3 forms):

| Pattern | Meaning | Example |
|---------|---------|---------|
| `*.ext` | Exclude files with extension | `*.bak`, `*.tmp`, `*.psd` |
| `dirname/` | Exclude directory (recursive) | `Backup/`, `Test/`, `WIP/` |
| `*keyword*` | Exclude paths containing keyword | `*_backup*`, `*~*`, `*_old*` |

**Not supported** (intentional simplification):
- `!` negation rules — FilterRule handles type-based inclusion, no need for IgnoreRule negation
- `#` comments — not a file format, it's a `List<string>` field on SO
- `**` recursive glob — collectPath already scans recursively, `dirname/` covers recursive exclusion
- `?` single-char wildcard — no practical use case in asset paths

**Matching rules**:
1. Relative path = `assetPath.Substring(collectPath.Length + 1)` (strip collectPath prefix + separator)
2. Pattern ending with `/` → directory match: check if any path segment equals the directory name
3. Pattern starting and ending with `*` → substring match: check if relative path contains the keyword
4. Pattern starting with `*` (but not ending with `*`) → extension match: check if relative path ends with suffix
5. Any pattern match → asset excluded from collection

### GlobMatcher: Minimal Glob Utility

A small static utility class for pattern matching. Supports `*` (any sequence) only — no `?`, no `**`, no character classes. This is sufficient for the 3 IgnorePattern forms above.

```csharp
public static class GlobMatcher
{
    /// <summary>
    /// Match input against a simple glob pattern (only * wildcard supported).
    /// </summary>
    public static bool IsMatch(string input, string pattern);
}
```

Implementation: split pattern by `*`, check if input contains all segments in order. O(n) per match.

### Labels Merge

As decided in E1-1: `Labels = Group.Labels ∪ Collector.Labels` (union, deduplicated). Implementation uses `HashSet<string>` for dedup.

### PrimaryType Extraction

`AssetDatabase.GetMainAssetTypeAtPath(assetPath).Name` — returns short type name (e.g., "Texture2D", "GameObject", "AudioClip"). Null-safe: if type cannot be determined, use "Unknown".

### GUID Uniqueness Validation

After all Collectors in a Package complete scanning, verify no duplicate GUIDs in the result. Duplicate GUID = internal logic error (same asset collected twice despite dedup) → report error.

---

## Complete Scan Flow

```
CollectionScanner.Scan(CollectorSetting setting)
│
├── Step 0: Cross-Package overlap detection
│   ├── For all (Collector_i, Collector_j) where Package_i ≠ Package_j
│   ├── Check: collectPath_i contains collectPath_j or vice versa
│   ├── Check: collectPath_i == collectPath_j
│   └── Any overlap → ScanError, abort
│
├── For each Package in setting.Packages:
│   │
│   ├── Step 1: Build Ownership Map
│   │   ├── Collect all Collectors from all Groups in this Package
│   │   ├── Sort by collectPath depth descending
│   │   ├── Detect same-depth same-path conflicts → ScanError
│   │   └── For each Collector, compute excludedPaths = set of deeper Collectors' collectPaths
│   │
│   ├── Step 2: Per-Collector scan
│   │   For each Collector (deepest-first order):
│   │   ├── a. guids = AssetDatabase.FindAssets("", new[] { collector.CollectPath })
│   │   ├── b. For each guid → assetPath = GUIDToAssetPath(guid)
│   │   ├── c. Skip if assetPath.StartsWith(any excludedPath)
│   │   ├── d. Skip if !FilterRule.IsCollectable(assetPath, extension, collectPath)
│   │   ├── e. Skip if any IgnorePattern matches relative path
│   │   ├── f. classification = AssetClassifier.Classify(assetPath, collectorType, forcePayloadKind)
│   │   ├── g. targetGroupName = GroupRule.GetTargetGroup(groupRuleCtx)  [审计新增]
│   │   ├── h. address = AddressRule.GetAddress(assetPath, targetGroupName, collectPath)
│   │   ├── i. labels = targetGroup.Labels ∪ Collector.Labels (HashSet dedup)  [审计修正: targetGroup]
│   │   ├── j. packRuleCtx = new PackRuleContext { AssetPath, GroupName=targetGroupName, CollectPath, PackageName, Classification, Labels }
│   │   ├── k. packKey = PackRule.GetPackKey(packRuleCtx)
│   │   ├── k. bundleName = BundleNameBuilder.Build(packageName, groupName, packKey)
│   │   ├── l. primaryType = AssetDatabase.GetMainAssetTypeAtPath(assetPath).Name
│   │   └── m. Add CollectedAssetInfo to result list
│   │
│   └── Step 3: GUID uniqueness validation
│       └── Duplicate GUID → ScanError (internal logic error)
│
└── Return: List<CollectedAssetInfo> (all Packages combined)
```

---

## Error Reporting

`CollectionScanner.Scan` returns a result object containing both the collected assets and any errors/warnings:

```csharp
public class ScanResult
{
    public List<CollectedAssetInfo> Assets;
    public List<ScanMessage> Messages;
    
    public bool HasErrors => Messages.Any(m => m.Severity == ScanSeverity.Error);
}

public class ScanMessage
{
    public ScanSeverity Severity;  // Error, Warning
    public string Code;            // e.g. "CROSS_PACKAGE_OVERLAP", "SAME_PATH_CONFLICT", "DUPLICATE_GUID"
    public string Message;
    public string CollectorPath;   // Which Collector triggered this
}

public enum ScanSeverity
{
    Warning = 0,
    Error = 1
}
```

Error conditions abort the scan for that Package. Warnings are collected and reported but don't stop scanning.

| Condition | Severity | Code |
|-----------|----------|------|
| Cross-Package path overlap | Error | `CROSS_PACKAGE_OVERLAP` |
| Same-depth same-path within Package | Error | `SAME_PATH_CONFLICT` |
| Duplicate GUID in result | Error | `DUPLICATE_GUID` |
| Empty collectPath on Collector | Error | `EMPTY_COLLECT_PATH` |
| collectPath directory not found | Warning | `PATH_NOT_FOUND` |
| Rule class name cannot be resolved | Error | `RULE_NOT_FOUND` |
| No assets found for a Collector | Warning | `EMPTY_COLLECTOR` |

---

## New Files

| File | Path | Assembly | Lines (est.) | Description |
|------|------|----------|-------------|-------------|
| CollectionScanner.cs | Build/Collector/Editor/ | Editor | ~180 | Static Scan method: CollectorSetting → ScanResult (List\<CollectedAssetInfo\> + errors) |
| GlobMatcher.cs | Build/Collector/Editor/ | Editor | ~40 | Simple glob matching utility (* wildcard only) |
| ScanResult.cs | Build/Collector/Editor/ | Editor | ~35 | ScanResult + ScanMessage + ScanSeverity |

All paths relative to `Assets/FYAsset/Scripts/`.

---

## Modified Files

| File | Change | Risk |
|------|--------|------|
| CollectorSetting.cs | Add `public List<string> IgnorePatterns = new();` to Collector class | Low — additive, default empty list |

---

## Task Breakdown

| Task | Content | Depends On |
|------|---------|-----------|
| E1-3-T1 | Add `IgnorePatterns` field to Collector class in CollectorSetting.cs | E1-1 done |
| E1-3-T2 | Create `GlobMatcher.cs` (simple * wildcard matching) | — |
| E1-3-T3 | Create `ScanResult.cs` (ScanResult + ScanMessage + ScanSeverity) | — |
| E1-3-T4 | Create `CollectionScanner.cs` — Step 0: cross-Package overlap detection | T3 |
| E1-3-T5 | Create `CollectionScanner.cs` — Step 1: ownership map + deepest-path sorting + conflict detection | T3, T4 |
| E1-3-T6 | Create `CollectionScanner.cs` — Step 2: per-Collector scan (FindAssets + exclude + Filter + Ignore + Classify + Address + Labels + PackKey + BundleNameBuilder + Type) | T1, T2, T3, T4, T5, E1-2 done, E2 done |
| E1-3-T7 | Create `CollectionScanner.cs` — Step 3: GUID uniqueness validation | T6 |
| E1-3-T8 | Compilation verification (dotnet build) | All above |

---

## Invariants (Must Hold After E1-3)

1. `CollectionScanner.Scan` with non-overlapping Collectors produces one `CollectedAssetInfo` per asset (GUID uniqueness)
2. Deepest-path Collector wins: an asset under `Assets/Art/Audio/` is collected by `collectPath = "Assets/Art/Audio/"` not `collectPath = "Assets/Art/"`
3. Cross-Package path overlap produces `ScanSeverity.Error` with code `CROSS_PACKAGE_OVERLAP`
4. Same-depth same-path within Package produces `ScanSeverity.Error` with code `SAME_PATH_CONFLICT`
5. IgnorePatterns `*.bak` excludes `subfolder/texture_backup.bak` but not `subfolder/texture.png`
6. IgnorePatterns `Backup/` excludes all assets under any `Backup` directory segment
7. Labels merge is union: Group.Labels = ["ui"], Collector.Labels = ["panel"] → Labels = ["ui", "panel"]
8. FilterRule runs before IgnorePatterns (filter first, ignore second)
9. `ScanResult.HasErrors` is false when all Collectors have valid paths and no conflicts
10. `dotnet build XLuaHotfix.sln` passes with 0 errors

---

## Not In Scope

- Editor UI for IgnorePatterns editing (E1-4)
- Advanced pack rules: PackByDirectory, PackSeparately (E2)
- Dependency analysis / ImplicitDependency discovery (E4)
- SharePolicy / shared bundle extraction (E4)
- Build pipeline integration (E5)
- Incremental/cached scanning — full scan each time (project scale is ~1000 assets, millisecond-level)

---

## Approval Checklist

- [ ] Agree to `CollectionScanner` as static utility class (no instance state)
- [ ] Agree to `AssetDatabase.FindAssets` for asset discovery
- [ ] Agree to Package-scoped deepest-path ownership (cross-Package overlap = error)
- [ ] Agree to IgnorePatterns as `List<string>` on Collector (simplified gitignore subset: `*.ext`, `dirname/`, `*keyword*`)
- [ ] Agree to IgnorePatterns matching against relative path (relative to collectPath)
- [ ] Agree to `GlobMatcher` as minimal glob utility (* wildcard only)
- [ ] Agree to `ScanResult` return type (assets + messages with severity)
- [ ] Agree to 7 error/warning conditions (table above)
- [ ] Agree to execution order: FindAssets → exclude sub-paths → FilterRule → IgnorePatterns → Classify → **GroupRule** → Address → Labels → PackKey → BundleNameBuilder.Build `[审计修正]`
- [ ] Agree to full scan each time (no incremental/cache)

---

## Change Log

| Date | Change |
|------|--------|
| 2026-04-18 | Initial version: 8 tasks, 3 new files. Approved by developer |
| 2026-04-23 | Dependency update: added E2 contract dependency (GetPackKey + PackRuleContext.Labels + BundleNameBuilder.Build). Approved |
| 2026-04-26 | **Direction audit**: inserted GroupRule step in scan pipeline (after Classify, before Address). GroupName now sourced from IGroupRule.GetTargetGroup() instead of Collector's parent Group. Labels merge uses target Group, not parent Group |
