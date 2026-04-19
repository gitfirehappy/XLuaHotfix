# Sub-Plan E1-1: Collector Data Model + Rule Interfaces

> **Risk**: Low (pure data definitions, zero runtime logic)
> **Dependencies**: None (foundational layer for E1-2/E1-3/E1-4/E2)
> **Status**: Awaiting approval

---

## Objective

Define the Collector framework's data model layer: the hierarchical ScriptableObject structure (Setting → Package → Group → Collector), enum types, the AssetClassification contract, rule interfaces (IAddressRule / IPackRule / IFilterRule), the CollectedAssetInfo intermediate data structure, and the RuleResolver reflection utility.

E1-1 contains NO rule implementations and NO scanning logic. It establishes the "vocabulary" that all subsequent E1/E2 sub-plans build upon.

---

## Background

The current build pipeline uses `HelperBuildDataExporter` to iterate Addressables groups and generate `AddressableLabelsConfig`. The new Collector framework replaces this with a standalone, Addressables-independent asset collection system referencing YooAsset's Setting/Package/Group/Collector hierarchy.

Key alignment targets (already implemented in Phase 2/3):
- `RuntimeAssetEntry` (B5-1): EntryId(GUID), Address, PrimaryType, Labels, SourcePath, Group
- `ManifestAssetEntry` (B6): RuntimeAssetEntry fields + BundleIndex
- `ABManifest` (B6): AssetEntries + BundleEntries

The Collector framework's output (CollectedAssetInfo) feeds into the build pipeline, which transforms it into ManifestAssetEntry/ManifestBundleEntry.

---

## Confirmed Design Decisions

### Hierarchy: Setting → Package → Group → Collector

```
CollectorSetting (ScriptableObject, global singleton)
 └── CollectorPackage[]
      ├── PackageName
      ├── SharePolicyConfig         (field placeholder, E4 consumes)
      └── CollectorGroup[]
           ├── GroupName
           ├── Tags[]               (group-level labels, explicit)
           └── Collector[]
                ├── CollectPath      (directory path)
                ├── CollectorType    (ECollectorType)
                ├── AddressRuleName  (string class name → IAddressRule)
                ├── PackRuleName     (string class name → IPackRule)
                ├── FilterRuleName   (string class name → IFilterRule)
                └── Tags[]          (collector-level labels)
```

### SO Structure: Global Singleton

One CollectorSetting.asset contains all Packages. Stored at `Assets/Build/CollectorSetting.asset`. Path constant added to Constants.cs.

### Rule Reference: String Class Name

Collector stores rule class names as strings. RuleResolver resolves them to instances via reflection at Editor time. All built-in rule class name strings registered in Constants.cs under a `Collector Rules` region.

### Assembly Split

- **Runtime assembly**: CollectorSetting SO + data classes + enums + AssetClassification (SO serialization requires runtime visibility)
- **Editor assembly**: Rule interfaces + Context structs + CollectedAssetInfo + RuleResolver (pure Editor code)

String class names on Collector bridge the two assemblies without type dependency.

### Tags Merge Strategy

CollectedAssetInfo.Labels = Group.Tags ∪ Collector.Tags (union, deduplicated). Collector.Tags appends to Group.Tags, does not override. Empty Collector.Tags means Group.Tags only.

### No Default Rule Implementations in E1-1

All rule implementations (AddressByFileName, CollectAll, PackByDirectory, etc.) deferred to E1-2 and E2. E1-1 is pure interface + data.

---

## Enum Definitions

### ECollectorType (user configuration intent)

```csharp
/// <summary>
/// Collector type — user's intent for how collected assets are used.
/// </summary>
public enum ECollectorType
{
    /// <summary> Loadable entry assets (runtime Address-based loading) </summary>
    Main = 0,
    /// <summary> Internal assets (packaged but not directly loadable) </summary>
    Static = 1,
    /// <summary> Dependency-only assets (referenced by other assets) </summary>
    Depend = 2
}
```

### EPayloadKind (Classifier auto-inferred, E1-2)

```csharp
/// <summary>
/// Asset payload kind — auto-inferred by Classifier based on asset type.
/// Determines build pipeline routing (AB serialization vs file copy vs scene bundle).
/// </summary>
public enum EPayloadKind
{
    /// <summary> Standard serialized asset (Prefab/Texture/Material/...) </summary>
    Serialized = 0,
    /// <summary> Raw file (direct file copy, not packed into AB) </summary>
    RawFile = 1,
    /// <summary> Scene file (separate AB, Unity requirement) </summary>
    Scene = 2
}
```

### EAssetRole (final role after dependency analysis)

```csharp
/// <summary>
/// Asset role — determined by ECollectorType mapping + dependency analysis (E4).
/// </summary>
public enum EAssetRole
{
    /// <summary> Loadable entry asset (from Main collector) </summary>
    Main = 0,
    /// <summary> Internal packaged asset (from Static collector) </summary>
    Static = 1,
    /// <summary> Explicitly declared dependency (from Depend collector) </summary>
    Depend = 2,
    /// <summary> Implicit dependency discovered by dependency analysis (E4) </summary>
    ImplicitDependency = 3
}
```

---

## Data Structures

### AssetClassification (Classifier output contract)

```csharp
/// <summary>
/// Classifier output consumed by PackRule (E2) and dependency analysis (E4).
/// Two orthogonal dimensions: what the asset IS (Role) and how it's STORED (PayloadKind).
/// </summary>
[Serializable]
public struct AssetClassification
{
    public EAssetRole Role;
    public EPayloadKind PayloadKind;
}
```

### CollectorSetting Hierarchy (Runtime assembly, [Serializable])

```csharp
[CreateAssetMenu(fileName = "CollectorSetting", menuName = "XLua/CollectorSetting")]
public class CollectorSetting : ScriptableObject
{
    public List<CollectorPackage> Packages = new();
}

[Serializable]
public class CollectorPackage
{
    public string PackageName;
    public List<CollectorGroup> Groups = new();
    // SharePolicy placeholder fields (E4 consumes, defined here for serialization)
    // public SharePolicyConfig SharePolicy;  // uncomment when E4 is implemented
}

[Serializable]
public class CollectorGroup
{
    public string GroupName;
    public List<string> Tags = new();
    public List<Collector> Collectors = new();
}

[Serializable]
public class Collector
{
    public string CollectPath;
    public ECollectorType CollectorType;
    public string AddressRuleName;
    public string PackRuleName;
    public string FilterRuleName;
    public List<string> Tags = new();
}
```

### CollectedAssetInfo (Editor assembly, build pipeline intermediate data)

```csharp
/// <summary>
/// Intermediate data produced by the collection scan (E1-3).
/// Consumed by dependency analysis (E4), packing (E2), and manifest generation (E6).
/// Ultimately transformed into ManifestAssetEntry + ManifestBundleEntry.
/// </summary>
public class CollectedAssetInfo
{
    public string AssetPath;
    public string AssetGUID;
    public string Address;
    public string PrimaryType;
    public List<string> Labels;
    public string GroupName;
    public string PackageName;
    public string BundleName;
    public AssetClassification Classification;
    public ECollectorType CollectorType;
}
```

---

## Rule Interfaces (Editor assembly)

### IAddressRule

```csharp
public interface IAddressRule
{
    string GetAddress(AddressRuleContext ctx);
}

public struct AddressRuleContext
{
    public string AssetPath;
    public string GroupName;
    public string CollectPath;
}
```

### IPackRule

```csharp
public interface IPackRule
{
    string GetBundleName(PackRuleContext ctx);
}

public struct PackRuleContext
{
    public string AssetPath;
    public string GroupName;
    public string CollectPath;
    public string PackageName;
    public AssetClassification Classification;
}
```

### IFilterRule

```csharp
public interface IFilterRule
{
    bool IsCollectable(FilterRuleContext ctx);
}

public struct FilterRuleContext
{
    public string AssetPath;
    public string Extension;
    public string CollectPath;
}
```

### RuleResolver

```csharp
/// <summary>
/// Resolves rule class name strings to instances via reflection.
/// Caches instances per class name (rules are stateless).
/// Editor-only.
/// </summary>
public static class RuleResolver
{
    public static IAddressRule GetAddressRule(string className);
    public static IPackRule GetPackRule(string className);
    public static IFilterRule GetFilterRule(string className);
    // Internal: reflection scan + cache
}
```

---

## New Files

| File | Path | Assembly | Lines (est.) | Description |
|------|------|----------|-------------|-------------|
| CollectorSetting.cs | Build/Collector/ | Runtime | ~60 | SO + CollectorPackage + CollectorGroup + Collector data classes |
| CollectorEnums.cs | Build/Collector/ | Runtime | ~40 | ECollectorType + EPayloadKind + EAssetRole |
| AssetClassification.cs | Build/Collector/ | Runtime | ~15 | Struct definition |
| CollectedAssetInfo.cs | Build/Collector/Editor/ | Editor | ~25 | Build pipeline intermediate data |
| IAddressRule.cs | Build/Collector/Editor/Rules/ | Editor | ~20 | Interface + AddressRuleContext |
| IPackRule.cs | Build/Collector/Editor/Rules/ | Editor | ~20 | Interface + PackRuleContext |
| IFilterRule.cs | Build/Collector/Editor/Rules/ | Editor | ~20 | Interface + FilterRuleContext |
| RuleResolver.cs | Build/Collector/Editor/ | Editor | ~60 | String → instance reflection resolver with cache |

All paths relative to `Assets/AboutXLua/Scripts/Core/Hotfix_AssetPackageManage/`.

---

## Modified Files

| File | Change | Risk |
|------|--------|------|
| Constants.cs | Add `COLLECTOR_SETTING_ASSET_PATH` + `Collector Rules` region with built-in rule class name constants (placeholders for E1-2/E2) | Low — additive |

---

## Task Breakdown

| Task | Content | Depends On |
|------|---------|-----------|
| E1-1-T1 | Create `CollectorEnums.cs` (ECollectorType + EPayloadKind + EAssetRole) | — |
| E1-1-T2 | Create `AssetClassification.cs` | T1 |
| E1-1-T3 | Create `CollectorSetting.cs` (SO + Package + Group + Collector data classes) | T1 |
| E1-1-T4 | Create rule interfaces: `IAddressRule.cs` + `IPackRule.cs` + `IFilterRule.cs` (with Context structs) | T2 |
| E1-1-T5 | Create `CollectedAssetInfo.cs` | T1+T2 |
| E1-1-T6 | Create `RuleResolver.cs` (reflection resolver + cache) | T4 |
| E1-1-T7 | Update `Constants.cs` — add SO path constant + Collector Rules region | — |
| E1-1-T8 | Compilation verification (dotnet build) | All above |

---

## Invariants (Must Hold After E1-1)

1. All new types compile without errors in their respective assemblies (Runtime / Editor)
2. CollectorSetting SO can be created via Unity menu and serialized/deserialized correctly
3. Runtime assembly has zero dependency on Editor types (string class names bridge the gap)
4. No rule implementations exist — only interfaces and data definitions
5. CollectedAssetInfo fields align with RuntimeAssetEntry / ManifestAssetEntry field names (AssetGUID→EntryId, Address, PrimaryType, Labels, GroupName)
6. `dotnet build XLuaHotfix.sln` passes with 0 errors

---

## Not In Scope

- Rule implementations: AddressByFileName, CollectAll, PackByDirectory, etc. (E1-2 / E2)
- Classifier logic: PayloadKind auto-inference (E1-2)
- Directory scanning + deepest-path dedup + IgnoreRule (E1-3)
- Editor UI: CollectorSetting inspector, tree editing (E1-4)
- SharePolicy data structure (E4, placeholder comment in CollectorPackage)
- Dependency analysis (E4)
- Build pipeline integration (E5/E6)

---

## Approval Checklist

- [ ] Agree to 4-level hierarchy: CollectorSetting(SO) → CollectorPackage → CollectorGroup → Collector
- [ ] Agree to global singleton SO at `Assets/Build/CollectorSetting.asset`
- [ ] Agree to 3 enums: ECollectorType (Main/Static/Depend), EPayloadKind (Serialized/RawFile/Scene), EAssetRole (Main/Static/Depend/ImplicitDependency)
- [ ] Agree to AssetClassification = { EAssetRole, EPayloadKind } two-field struct
- [ ] Agree to 3 rule interfaces (IAddressRule/IPackRule/IFilterRule) with Context structs
- [ ] Agree to string class name rule reference + RuleResolver reflection
- [ ] Agree to Runtime/Editor assembly split (data classes in Runtime, rules+logic in Editor)
- [ ] Agree to Tags merge: Group.Tags ∪ Collector.Tags (union, deduplicated)
- [ ] Agree to CollectedAssetInfo as Editor-only intermediate data (not serialized)
- [ ] Agree that E1-1 contains zero rule implementations (all deferred to E1-2/E2)
