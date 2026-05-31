# Plan: Collector Asset Metadata And Bundle Packing Refactor

> Status: Executed; awaiting sign-off
> Date: 2026-05-31
> Requirement ID: collector-asset-metadata-bundle-packing-20260531
> Scope: AB build-time Collector model, asset-level metadata editing, and bundle packing semantics.

## Goal

Refactor the current Collector-centered asset collection model into an asset-metadata-centered model:

- Package remains the package/release boundary.
- Group remains the Addressables-like configuration domain.
- Collector remains an editor-time collector and analyzer only.
- AssetEntry becomes the authoritative source for asset-level Address, Labels, Role, and PayloadKind during build.
- Bundle packing is controlled by Group `BundlePackingMode`, not Collector PackRules.

## Approved Decisions

1. `CollectorSetting` is replaced by `AssetCollectionSetting`.
2. Old `CollectorSetting.asset` compatibility and migration are not required; old resources/code may be deleted or replaced.
3. Do not store runtime/business metadata on Collector.
4. Collector still owns collection and analysis defaults:
   - collect path;
   - collect path type;
   - default role intent;
   - default/forced payload analysis;
   - filter/group routing;
   - ignore patterns.
5. `AssetEntry` stores authoritative asset-level metadata keyed by Unity asset GUID:
   - `AssetGUID`;
   - `AutoAddress`, `Address`;
   - `Labels`;
   - `AutoRole`, `Role`;
   - `AutoPayload`, `PayloadKind`.
6. New assets without an existing `AssetEntry` are initialized from Collector analysis.
7. Existing `AssetEntry` is not silently overwritten by rescan; explicit Reset Auto actions restore generated Address/Role/Payload values.
8. Asset Labels are manual and additive only.
9. Group Labels are mandatory and cannot be removed or overridden by AssetEntry.
10. Final labels are `Group.Labels + AssetEntry.Labels`.
11. Remove `AddressRuleName`, `IAddressRule`, `PackRuleName`, `IPackRule`, and old pack rule implementations.
12. Keep filter and group rules for collection-time inclusion/routing.
13. Replace PackRules with Group `BundlePackingMode`:
    - `PackTogether`;
    - `PackSeparately`;
    - `PackTogetherByLabel`.
14. Scene assets force `PackSeparately` behavior only; all naming rules remain the same as asset-level packing.
15. RawFile must be detected by Collector analysis automatically, not manually patched after scan.
16. `PackKey` is renamed to `BundleKey` for mode-specific build bucket keys.
17. Bundle names use mode-specific segments:
    - `PackTogether`: `{package}_{group}_all`
    - `PackSeparately`: `{package}_{group}_asset_{normalizedAddress}~{shortGuid8}`
    - `PackTogetherByLabel`: `{package}_{group}_labels_{labelA}~{labelB}`
    - unlabeled label bucket: `{package}_{group}_labels_$unlabeled`
18. `$` is reserved for system identifiers. User-authored Package, Group, and Labels must reject `$` and other reserved bundle-name characters.

## PRS Design

### Paradigm

- Asset collection hierarchy:
  - Data: `AssetCollectionSetting -> AssetCollectionPackage -> AssetCollectionGroup -> Collector`.
  - Invariant: Collector discovers and analyzes assets but does not own runtime/business metadata.
- Asset metadata registry:
  - Data: `AssetCollectionSetting.AssetEntries`, keyed by `AssetGUID`.
  - Invariant: one GUID has at most one AssetEntry.
- Metadata resolution:
  - Data: generated defaults + stored AssetEntry manual/auto flags + Group Labels.
  - Invariant: build consumes resolved asset metadata, not Collector labels/address/pack rules.
- Bundle grouping:
  - Data: `BundlePackingMode` + final labels + address + GUID.
  - Invariant: Collector path never participates in BundleName.

### Rules

| Condition | Action | Order | Recovery |
|---|---|---|---|
| Asset has no AssetEntry | Create runtime scan output from Collector analysis defaults and optionally persist through editor save actions | After filter/group routing, before bundle naming | Rescan can recreate preview output |
| AssetEntry exists with `AutoAddress=true` | Use generated address | Before bundle key calculation | Reset Address Auto keeps same behavior |
| AssetEntry exists with `AutoAddress=false` | Use manual `Address` | Before bundle key calculation | Reset Address Auto regenerates address |
| AssetEntry exists with `AutoRole=true` | Use Collector analyzed role | Before classification output | Reset Role Auto regenerates role |
| AssetEntry exists with `AutoPayload=true` | Use Collector analyzed payload | Before classification output | Reset Payload Auto regenerates payload |
| Group has Labels | Add them to final labels and display as inherited | Before label validation and bundle grouping | AssetEntry cannot remove them |
| AssetEntry has Labels | Add them after Group Labels with de-duplication | Before label validation and bundle grouping | User can edit asset labels |
| Group mode is `PackTogether` | Build one Group bundle named `{package}_{group}_all` | After final metadata resolution | Validation blocks invalid names |
| Group mode is `PackSeparately` or payload is Scene | Build one asset bundle named `{package}_{group}_asset_{address}~{guid8}` | After final metadata resolution | GUID suffix prevents duplicate address collision |
| Group mode is `PackTogetherByLabel` | Group by sorted final labels, or `$unlabeled` | After final metadata resolution | `$unlabeled` is system-only |

### System

- `CollectionScanner.Scan(AssetCollectionSetting setting)` returns resolved `CollectedAssetInfo` records.
- `TaskCollectAssets` loads `AssetCollectionSetting`, validates it, scans it, and writes build context values.
- Collector UI loads and edits `AssetCollectionSetting`.
- Scan Preview displays resolved Address, inherited Group Labels, asset Labels, Role, PayloadKind, BundlePackingMode, and BundleName.
- Asset detail editing initially lives in the right-side details area for selected scan-preview assets.

## Implementation Checklist

1. Rename and reshape the data model:
   - replace `CollectorSetting` with `AssetCollectionSetting`;
   - replace package/group type names with `AssetCollectionPackage` and `AssetCollectionGroup`;
   - add `AssetEntry`;
   - add `BundlePackingMode`;
   - remove Collector `AddressRuleName`, `PackRuleName`, and `Labels`.
2. Update build settings and default asset path:
   - rename `SharedBuildSettings.CollectorSettingPath` to `AssetCollectionSettingPath`;
   - default path: `Assets/FYAsset/AssetCollectionData/AssetCollectionSetting.asset`.
3. Refactor scanning:
   - remove address/pack rule resolution;
   - generate default address through `AssetAddressGenerator`;
   - resolve AssetEntry metadata by GUID;
   - merge Group Labels + Asset Labels;
   - build mode-specific BundleName through `BundleNameBuilder`;
   - keep filter/group rule execution and Collector RawFile/Scene analysis.
4. Refactor validation:
   - validate package/group names, labels, unique GUID entries, Collector paths, filter/group rules, and reserved `$`;
   - validate bundle packing modes without PackRule validation.
5. Refactor Editor UI:
   - rename visible setting references from CollectorSetting to AssetCollectionSetting;
   - remove Address/Pack rule controls;
   - add Group `BundlePackingMode` control;
   - remove Collector Labels UI;
   - add asset-level metadata editing in Scan Preview/details with Reset Auto actions for Address, Role, and Payload.
6. Remove old rule code:
   - delete `IAddressRule`, `IPackRule`, `AddressByFileName`, `PackByCollectPath`, `PackByDirectory`, `PackByLabel`, `PackSeparately`;
   - update `RuleResolver` and `RuleDropdownHelper` to only keep remaining rule families.
7. Update build and dependency analysis call sites:
   - use `AssetCollectionSetting`;
   - update shared policy collection;
   - rename bundle key terminology where active code means the new build bucket key.
8. Update project files if required by this Unity project.
9. Verification:
   - static grep for removed symbols;
   - `dotnet build XLuaHotfix.sln --no-restore`;
   - `git diff --check`.
10. Documentation alignment:
   - update `context/architecture/collector-framework.md`;
   - update relevant human docs under `docs/` if they mention CollectorSetting/PackRule behavior;
   - update `requirements/progress.txt`;
   - archive this plan after execution/sign-off workflow reaches the appropriate point.

## Non-Goals

- Do not change runtime loading behavior.
- Do not change Lua/C# bridge behavior.
- Do not change Package release semantics.
- Do not add old serialized data migration.
- Do not preserve old PackRule/AddressRule as active alternate paths.

## Approval

Approved by developer on 2026-05-31 with instruction to write this standard plan first, then execute on `main`.

Executed on 2026-05-31. The plan remains active until developer sign-off, per the shared requirements workflow.
