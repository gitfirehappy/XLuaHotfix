# Sub-Plan E1-2: Classifier + Default Rule Implementations

> **Risk**: Low (Editor-only logic, no runtime impact)
> **Dependencies**: E1-1 completed (enums, interfaces, data classes available)
> **Status**: Awaiting approval

---

## Objective

Implement the Classifier (PayloadKind auto-inference + AssetRole mapping) and the minimum set of default rule implementations (AddressByFileName, CollectAll, PackByCollectPath) so that E1-3's scan engine has concrete rules to work with.

Also adds `EForcePayloadKind` enum and `ForcePayloadKind` field to Collector data class (E1-1 addendum).

---

## Confirmed Design Decisions

### Classifier Logic

Classifier is a static utility class (not an interface — there's only one classification algorithm).

Input: `AssetPath` + `ECollectorType` + `EForcePayloadKind`
Output: `AssetClassification { EAssetRole, EPayloadKind }`

**EAssetRole mapping** (direct, no complex logic):
```
ECollectorType.Main   → EAssetRole.Main
ECollectorType.Static → EAssetRole.Static
ECollectorType.Depend → EAssetRole.Depend
```
`EAssetRole.ImplicitDependency` is never produced by Classifier — only by E4 dependency analysis.

**EPayloadKind inference**:
```
if ForcePayloadKind != Auto:
    return ForcePayloadKind → corresponding EPayloadKind
else:
    extension == ".unity" → EPayloadKind.Scene
    all others            → EPayloadKind.Serialized
```

RawFile is never auto-inferred. It requires explicit `ForcePayloadKind = RawFile` on the Collector. This is the safest approach — same extension (.bytes) can mean TextAsset (AB) or raw file (copy) depending on context.

### EForcePayloadKind (new enum, E1-1 addendum)

```csharp
/// <summary>
/// User configuration intent for payload kind override on Collector.
/// Semantically different from EPayloadKind (which is a classification result).
/// Auto = let Classifier decide; specific values = force override.
/// </summary>
public enum EForcePayloadKind
{
    Auto = 0,
    Serialized = 1,
    RawFile = 2,
    Scene = 3
}
```

Added to `CollectorEnums.cs`. `Collector` data class gains `public EForcePayloadKind ForcePayloadKind;` field (default Auto).

### Default Rule Implementations

| Rule Interface | Implementation | Behavior |
|---------------|---------------|----------|
| IAddressRule | `AddressByFileName` | File name without extension. Reuses `AssetAddressGenerator.GenerateShortAddress(assetPath, primaryType)` from B5-1 for type-suffix disambiguation |
| IFilterRule | `CollectAll` | Collects all assets. Excludes: `.meta`, `.cs`, `.dll`, `.asmdef`, `.asmref`, files under `Editor/` directories |
| IPackRule | `PackByCollectPath` | All assets under the same Collector.CollectPath go into one Bundle. Returns grouping key only: `{collectDirName}` (last segment of CollectPath). Framework assembles full logical name via BundleNameBuilder.Build(pkg, group, key) |

### AddressByFileName Reuse

`AddressByFileName.GetAddress()` internally calls `AssetAddressGenerator.GenerateShortAddress(assetPath, primaryType)` which already implements the filename + `_TypeName` suffix disambiguation logic from B5-1. No duplication.

### CollectAll Exclusion List

Hardcoded exclusion patterns (not configurable in E1-2, IgnoreRule in E1-3 handles custom exclusions):
```
Extensions: .meta, .cs, .dll, .asmdef, .asmref, .gitigore
Directories: any path segment == "Editor"
```

### PackByCollectPath — Grouping Key Only

PackByCollectPath returns only the grouping key (last directory segment of CollectPath):
```
GetPackKey returns: {lastDirectoryName}
```
The framework assembles the full logical name via `BundleNameBuilder.Build(packageName, groupName, packKey)` → `{packageName}_{groupName}_{lastDirectoryName}`. Hash and `.bundle` extension are appended by E5 build pipeline.

Full naming with labels/hash/type is E2's responsibility (PackByDirectory, PackSeparately, etc.).

---

## New Files

| File | Path | Assembly | Lines (est.) | Description |
|------|------|----------|-------------|-------------|
| AssetClassifier.cs | Build/Collector/Editor/ | Editor | ~50 | Static Classify method: AssetPath + CollectorType + ForcePayloadKind → AssetClassification |
| AddressByFileName.cs | Build/Collector/Editor/Rules/ | Editor | ~25 | IAddressRule impl, delegates to AssetAddressGenerator |
| CollectAll.cs | Build/Collector/Editor/Rules/ | Editor | ~35 | IFilterRule impl, exclusion list |
| PackByCollectPath.cs | Build/Collector/Editor/Rules/ | Editor | ~25 | IPackRule impl, minimal bundle naming |

All paths relative to `Assets/FYAsset/Scripts/`.

---

## Modified Files

| File | Change | Risk |
|------|--------|------|
| CollectorEnums.cs | Add `EForcePayloadKind` enum (E1-1 addendum) | Low — additive |
| CollectorSetting.cs | Add `EForcePayloadKind ForcePayloadKind` field to Collector class | Low — additive, default Auto |
| Constants.cs | Add default rule class name constants: `RULE_ADDRESS_BY_FILENAME`, `RULE_COLLECT_ALL`, `RULE_PACK_BY_COLLECT_PATH` under Collector Rules region | Low — additive |

---

## Task Breakdown

| Task | Content | Depends On |
|------|---------|-----------|
| E1-2-T1 | Add `EForcePayloadKind` to CollectorEnums.cs + `ForcePayloadKind` field to Collector class | E1-1 done |
| E1-2-T2 | Create `AssetClassifier.cs` (static Classify method) | T1 |
| E1-2-T3 | Create `AddressByFileName.cs` (IAddressRule, reuses AssetAddressGenerator) | E1-1 done |
| E1-2-T4 | Create `CollectAll.cs` (IFilterRule, exclusion list) | E1-1 done |
| E1-2-T5 | Create `PackByCollectPath.cs` (IPackRule, minimal naming) | E1-1 done |
| E1-2-T6 | Update Constants.cs with default rule class name constants | — |
| E1-2-T7 | Compilation verification (dotnet build) | All above |

---

## Invariants (Must Hold After E1-2)

1. `AssetClassifier.Classify` correctly maps all 3 ECollectorType values to corresponding EAssetRole
2. `AssetClassifier.Classify` with `ForcePayloadKind = Auto` returns Scene for `.unity`, Serialized for all others
3. `AssetClassifier.Classify` with `ForcePayloadKind != Auto` returns the forced kind regardless of extension
4. `AddressByFileName` produces same output as `AssetAddressGenerator.GenerateShortAddress` (no divergence)
5. `CollectAll` excludes .meta/.cs/.dll/.asmdef/.asmref and Editor/ directories
6. `RuleResolver` can resolve all 3 default rule class names to instances
7. `dotnet build XLuaHotfix.sln` passes with 0 errors

---

## Not In Scope

- Advanced pack rules: PackByDirectory, PackSeparately, PackByLabel (E2)
- Full bundle naming with labels/hash/type (E2)
- Directory scanning logic (E1-3)
- IgnoreRule / custom exclusion patterns (E1-3)
- Dependency analysis / ImplicitDependency (E4)
- Editor UI (E1-4)

---

## Approval Checklist

- [ ] Agree to `EForcePayloadKind` as independent enum (Auto/Serialized/RawFile/Scene) on Collector
- [ ] Agree that Classifier never auto-infers RawFile (only Scene vs Serialized; RawFile requires explicit ForcePayloadKind)
- [ ] Agree to AssetClassifier as static utility class (not interface)
- [ ] Agree to AddressByFileName reusing AssetAddressGenerator.GenerateShortAddress from B5-1
- [ ] Agree to CollectAll hardcoded exclusion list (.meta/.cs/.dll/.asmdef/.asmref + Editor/ dirs)
- [ ] Agree to PackByCollectPath as minimal default (full naming rules in E2)
- [ ] Agree to 3 default rule class name constants in Constants.cs
