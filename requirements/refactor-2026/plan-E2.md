# Sub-Plan E2: PackRule Implementations + BundleNameBuilder

> **Risk**: Low-Medium (Editor-only code, but defines bundle logical naming convention that propagates to E5 build output, diff snapshots, and hotfix distribution)
> **Compatibility boundary**: This naming convention is internal to the new AB build pipeline only. It takes effect when E5 TaskBuildBundles runs under the new backend. Existing Addressables build output and live hotfix bundles are not affected — the legacy backend retains its own naming. No migration or rename of existing bundles is required.
> **Dependencies**: E1-1 (data model, enums, IPackRule interface), E1-2 (PackByCollectPath default rule, Classifier)
> **Status**: Approved

---

## Objective

Implement the 3 advanced PackRule implementations (PackSeparately, PackByDirectory, PackByLabel) and the BundleNameBuilder framework utility that assembles standardized bundle logical names from PackRule output.

Also applies a breaking interface change to IPackRule: `GetBundleName` → `GetPackKey`, narrowing PackRule's responsibility to pure grouping-key output while the framework controls naming format.

---

## Confirmed Design Decisions

### D1: IPackRule Interface Change — GetBundleName → GetPackKey

PackRule's responsibility is narrowed to **grouping decision only**. The interface method changes from:
```csharp
// OLD (E1-1 original)
string GetBundleName(PackRuleContext ctx);

// NEW (E2 change)
string GetPackKey(PackRuleContext ctx);
```

PackRule outputs a semantic grouping key (e.g. `"prefabs"`, `"icons"`, `"panel-common"`). The framework assembles the final bundle name via BundleNameBuilder.

**Rationale**: Separating grouping logic from naming format ensures all rules produce consistent names. Changing naming format requires modifying only BundleNameBuilder, not every rule.

**Impact on E1-1/E1-2**: IPackRule.cs interface signature changes. PackByCollectPath.cs (E1-2) requires both method rename AND return value semantic change: the old `GetBundleName` returned a full logical name `{packageName}_{groupName}_{collectDirName}`, but the new `GetPackKey` must return only the grouping key `{collectDirName}` (last segment of CollectPath). The framework's BundleNameBuilder.Build() handles the pkg/group prefix. If E1-1/E1-2 are already implemented when E2 executes, apply both changes; if not yet implemented, update the plan definitions directly.

### D2: BundleNameBuilder — Framework-Side Name Assembly

Static utility class. All bundle logical names go through this single method:

```csharp
public static class BundleNameBuilder
{
    /// <summary>
    /// Assembles a standardized bundle logical name from components.
    /// Output does NOT include hash or file extension — those are appended by E5 build pipeline.
    /// </summary>
    public static string Build(string packageName, string groupName, string packKey)
    {
        string safePkg = SanitizeSegment(packageName);
        string safeGroup = SanitizeSegment(groupName);
        string safeKey = SanitizeSegment(packKey);
        return $"{safePkg}_{safeGroup}_{safeKey}";
    }

    /// <summary>
    /// Normalizes a name segment: lowercase + replace illegal characters with underscore.
    /// </summary>
    private static string SanitizeSegment(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return "default";
        var sb = new System.Text.StringBuilder(raw.Length);
        for (int i = 0; i < raw.Length; i++)
        {
            char c = char.ToLowerInvariant(raw[i]);
            sb.Append(char.IsLetterOrDigit(c) || c == '-' ? c : '_');
        }
        return sb.ToString();
    }
}
```

**Output format**: `{packageName}_{groupName}_{packKey}` (all lowercase, non-alphanumeric replaced with `_`, hyphen `-` preserved for label separators)

**Not in E2 scope**: Hash suffix and `.bundle` extension are appended by E5 TaskBuildBundles. E2 outputs logical names only.

### D3: PackRuleContext — Add Labels Field

PackByLabel needs access to asset labels. PackRuleContext gains a `Labels` field:

```csharp
public struct PackRuleContext
{
    public string AssetPath;
    public string GroupName;
    public string CollectPath;
    public string PackageName;
    public AssetClassification Classification;
    public IReadOnlyList<string> Labels;    // NEW — merged Group.Tags ∪ Collector.Tags
}
```

This is an additive change to E1-1's definition. The Labels field is populated by E1-3's CollectionScanner when constructing the context.

### D4: Separator Convention

- **Segment separator**: `_` (underscore) — separates packageName, groupName, packKey
- **Label separator**: `-` (hyphen) — separates multiple labels within packKey (PackByLabel only)

Example: `hotfix_ui_panel-common` = package `hotfix`, group `ui`, labels `common` + `panel` (sorted)

### D5: Three Built-in PackRule Implementations

| Rule | packKey Source | Example Input | Example packKey |
|------|--------------|---------------|-----------------|
| PackSeparately | Asset filename (no extension) | `Assets/Art/UI/Panel.prefab` | `panel` |
| PackByDirectory | Asset's parent directory name relative to CollectPath; falls back to CollectPath last segment if asset is at root | `Assets/Art/UI/Icons/star.png` (CollectPath=`Assets/Art/UI`) | `icons` |
| PackByLabel | Sorted labels joined by `-`; `unlabeled` if no labels | Labels=`["ui","panel"]` | `panel-ui` |

### D6: RawFile Naming — Unified

RawFile assets use the same naming rules as Serialized assets. The PackRule does not differentiate by PayloadKind. E5 build pipeline routes RawFile to file-copy instead of AB packing, but the logical name follows the same convention.

### D7: Hash Not in E2 Scope

Hash computation depends on actual bundle content (post-packing). E2 outputs logical names without hash. E5 TaskBuildBundles appends `_{hash}.bundle` to the logical name after packing.

### D8: Naming Extension Boundary (Developer Note)

BundleNameBuilder is intentionally minimal (3 segments). Future naming extensions (e.g. adding PrimaryType segment) must be discussed before adding — naming format should not grow unbounded.

---

## PackRule Implementation Details

### PackSeparately

Each asset gets its own bundle. packKey = filename without extension.

```csharp
public class PackSeparately : IPackRule
{
    public string GetPackKey(PackRuleContext ctx)
    {
        return Path.GetFileNameWithoutExtension(ctx.AssetPath);
    }
}
```

### PackByDirectory

Assets in the same directory go into one bundle. packKey = directory name relative to CollectPath.

```csharp
public class PackByDirectory : IPackRule
{
    public string GetPackKey(PackRuleContext ctx)
    {
        string assetDir = Path.GetDirectoryName(ctx.AssetPath).Replace('\\', '/');
        string collectDir = ctx.CollectPath.Replace('\\', '/').TrimEnd('/');

        // Asset directly under CollectPath → fall back to CollectPath last segment
        if (string.Equals(assetDir, collectDir, StringComparison.OrdinalIgnoreCase))
            return Path.GetFileName(collectDir);

        // Use the first sub-directory level relative to CollectPath
        string relative = assetDir.Substring(collectDir.Length + 1);
        int slashIndex = relative.IndexOf('/');
        return slashIndex >= 0 ? relative.Substring(0, slashIndex) : relative;
    }
}
```

**Edge case**: If CollectPath = `Assets/Art/UI` and asset = `Assets/Art/UI/Panel.prefab` (no sub-directory), packKey = `UI` (last segment of CollectPath). This matches PackByCollectPath behavior for root-level assets.

### PackByLabel

Assets with the same label combination go into one bundle. Labels sorted case-insensitively, joined by `-`.

```csharp
public class PackByLabel : IPackRule
{
    public string GetPackKey(PackRuleContext ctx)
    {
        if (ctx.Labels == null || ctx.Labels.Count == 0)
            return "unlabeled";

        var sorted = new List<string>(ctx.Labels.Count);
        for (int i = 0; i < ctx.Labels.Count; i++)
            sorted.Add(ctx.Labels[i].ToLowerInvariant());
        sorted.Sort(StringComparer.Ordinal);
        return string.Join("-", sorted);
    }
}
```

**Note**: Labels are lowercased before sorting to ensure deterministic output regardless of input casing.

---

## Integration: How PackRule Output Becomes BundleName

The call site (E1-3 CollectionScanner or E5 build pipeline) assembles the final name:

```csharp
// Framework-side integration (in CollectionScanner or build task)
IPackRule packRule = RuleResolver.GetPackRule(collector.PackRuleName);
var ctx = new PackRuleContext { /* ... */ };
string packKey = packRule.GetPackKey(ctx);
string bundleLogicalName = BundleNameBuilder.Build(ctx.PackageName, ctx.GroupName, packKey);
// → e.g. "hotfix_ui_prefabs"

// Later in E5 TaskBuildBundles:
// string finalName = $"{bundleLogicalName}_{hash}.bundle";
// → e.g. "hotfix_ui_prefabs_a1b2c3d4.bundle"
```

CollectedAssetInfo.BundleName stores the logical name (without hash/extension).

---

## New Files

| File | Path | Assembly | Lines (est.) | Description |
|------|------|----------|-------------|-------------|
| BundleNameBuilder.cs | Build/Collector/Editor/ | Editor | ~40 | Static Build method + SanitizeSegment |
| PackSeparately.cs | Build/Collector/Editor/Rules/ | Editor | ~20 | IPackRule impl, packKey = filename |
| PackByDirectory.cs | Build/Collector/Editor/Rules/ | Editor | ~35 | IPackRule impl, packKey = directory name |
| PackByLabel.cs | Build/Collector/Editor/Rules/ | Editor | ~30 | IPackRule impl, packKey = sorted labels |

All paths relative to `Assets/FYAsset/Scripts/`.

---

## Modified Files

| File | Change | Risk |
|------|--------|------|
| IPackRule.cs (E1-1) | Rename `GetBundleName` → `GetPackKey` | Low — E1-1 not yet implemented, plan-level change |
| PackRuleContext (E1-1) | Add `IReadOnlyList<string> Labels` field | Low — additive |
| PackByCollectPath.cs (E1-2) | Method rename `GetBundleName` → `GetPackKey` AND semantic change: return only `{collectDirName}` (grouping key), not full `{pkg}_{group}_{dir}` | Low — E1-2 not yet implemented, plan-level change |
| plan-E1-3.md | Sync scan pipeline: step h/i/j updated to labels→GetPackKey→BundleNameBuilder.Build flow; step order adjusted (labels before PackRule) | Low — plan-level change |
| Constants.cs | Add `RULE_PACK_SEPARATELY`, `RULE_PACK_BY_DIRECTORY`, `RULE_PACK_BY_LABEL` constants | Low — additive |

---

## Task Breakdown

| Task | Content | Depends On |
|------|---------|-----------|
| E2-T1 | Update E1-1 plan: IPackRule `GetBundleName` → `GetPackKey` + PackRuleContext add Labels field | — |
| E2-T2 | Update E1-2 plan: PackByCollectPath method rename + semantic change (return only collectDirName, not full logical name) | T1 |
| E2-T3 | Update E1-3 plan: sync scan pipeline steps h/i/j to labels→GetPackKey→BundleNameBuilder.Build flow | T1 |
| E2-T4 | Create `BundleNameBuilder.cs` (Build + SanitizeSegment) | E1-1 done |
| E2-T5 | Create `PackSeparately.cs` | E1-1 done |
| E2-T6 | Create `PackByDirectory.cs` | E1-1 done |
| E2-T7 | Create `PackByLabel.cs` | E1-1 done |
| E2-T8 | Update `Constants.cs` with 3 new rule class name constants | — |
| E2-T9 | Compilation verification (dotnet build) | All above |

---

## Invariants (Must Hold After E2)

1. IPackRule interface method is `GetPackKey` (not `GetBundleName`)
2. PackRuleContext contains `Labels` field of type `IReadOnlyList<string>`
3. `BundleNameBuilder.Build("Hotfix", "UI", "Prefabs")` → `"hotfix_ui_prefabs"` (all lowercase)
4. `BundleNameBuilder.Build("pkg", "grp", "a/b c")` → `"pkg_grp_a_b_c"` (illegal chars replaced)
5. `PackSeparately.GetPackKey` returns filename without extension
6. `PackByDirectory.GetPackKey` returns sub-directory name; falls back to CollectPath last segment for root-level assets
7. `PackByLabel.GetPackKey` with labels `["UI","Panel"]` → `"panel-ui"` (lowercase, sorted, hyphen-joined)
8. `PackByLabel.GetPackKey` with empty/null labels → `"unlabeled"`
9. `RuleResolver` can resolve all 3 new rule class names to instances
10. `dotnet build XLuaHotfix.sln` passes with 0 errors

---

## Not In Scope

- Hash computation and `.bundle` extension (E5 TaskBuildBundles)
- Bundle naming template configurability (deferred — current 3-segment format is fixed)
- PrimaryType segment in bundle name (deferred — discuss if needed)
- RawFile special routing in build pipeline (E5)
- Dependency analysis / shared bundle extraction (E4)
- CollectionScanner code integration (E1-3 implements the actual code; E2 only syncs the plan-level call flow)

---

## Approval Checklist

- [ ] Agree to IPackRule interface change: `GetBundleName` → `GetPackKey` (grouping key only)
- [ ] Agree to BundleNameBuilder as framework-side name assembly (3-segment: pkg_group_key)
- [ ] Agree to SanitizeSegment: lowercase + non-alphanumeric → underscore, hyphen preserved
- [ ] Agree to PackRuleContext adding `IReadOnlyList<string> Labels` field
- [ ] Agree to separator convention: `_` between segments, `-` between labels
- [ ] Agree to PackSeparately: packKey = filename without extension
- [ ] Agree to PackByDirectory: packKey = sub-directory name, root fallback to CollectPath last segment
- [ ] Agree to PackByLabel: sorted lowercase labels joined by `-`, empty → `unlabeled`
- [ ] Agree to RawFile unified naming (no special treatment)
- [ ] Agree to 4 new files + 5 modified files (including E1-1/E1-2 plan updates + E1-3 scan pipeline sync)
