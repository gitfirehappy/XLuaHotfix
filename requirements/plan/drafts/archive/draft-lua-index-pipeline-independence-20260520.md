# Draft: LuaScriptsIndexExporter Pipeline Independence

Status: Promoted / Archived
Date: 2026-05-20
Updated: 2026-07-19

> Promotion note: the verified P0 and the broader runtime/label ownership decisions were promoted to
> `../../plan-lua-resource-boundary-separation-20260719.md`. This file remains the historical analysis trace.

## Problem

`LuaScriptsIndexExporter` currently depends on Addressables API for two operations:

1. **Address lookup**: `settings.FindAssetEntry(guid).address` to get container addresses
2. **Group registration**: `settings.CreateOrMoveEntry(guid, group)` to register the index SO into the "LuaScripts" Addressables group

AB runtime also needs `LuaScriptsIndex` (loaded by address via `ABPackageBackend.ResolveAssetEntryByAddress`). AA and AB use the same runtime address value, but they no longer share one build-time registration source: AA consumes Addressables entries, while AB consumes `AssetCollectionSetting` scan output.

## Current State

- `BuildProjectRunner` calls `LuaScriptsIndexExporter.ExportData()` before the selected backend build (both AA and AB execute it)
- The exporter is in `Assets/FYAsset/Scripts/AA/Build/Release/Editor/Addressables/` — physically located in the AA-specific directory
- The exporter creates `Assets/Build/LuaScriptsIndex.asset` and registers it only in the Addressables `LuaScripts` group
- AB `TaskCollectAssets` ignores Addressables groups and scans `AssetCollectionSetting`
- The active AB setting has no `Assets/Build` Collector and explicitly ignores `Assets/Build/**`
- `TaskCollectBuiltins` only adds Shaders and `Assets/Resources`, so it cannot add the generated index

## Confirmed Failure (2026-07-15 to 2026-07-19)

The deferred risk is now an active P0 blocker.

- AB Full 5.0.0 completed all 12 tasks and produced 167 assets / 41 Bundles, but the manifest contained zero entries with Address `LuaScriptsIndex`.
- The same manifest still contained six `LuaScriptContainer` entries and the `ModuleRegistry` TextAsset, proving the failure is specific to the generated bootstrap index rather than Lua collection generally.
- Local Full, Local Hotfix 5.0.1, and Cloudflare Hotfix 5.0.1 Players all failed at the same boundary: `XLuaLoader` could not load `LuaScriptsIndex`, then could not map `ModuleRegistry` to its container.
- `TaskGenerateManifest` faithfully serializes `CollectedAssets`; it does not lose an entry that was present upstream.
- `TaskVerifyBuildResult` checks physical output consistency only. No AB build task requires the bootstrap address, so an internally consistent but unusable package passes 12/12 tasks.

Root cause: shared index generation retained AA-only publication ownership. The AB pipeline neither registers the generated asset in its collector input nor validates that the required bootstrap address exists before publication.

## Design Question

Should the exporter be refactored to read addresses from a unified registry (e.g., `AssetAddressGenerator`) instead of Addressables settings, making it pipeline-agnostic?

### Option A: Keep As-Is (Rejected By Evidence)

- Exporter stays Addressables-dependent
- AA works because Addressables consumes the entry created by the exporter
- AB deterministically omits the index and cannot complete Player startup

### Option B: Unified Address Source

- Exporter reads addresses from a shared address registry (not Addressables-specific)
- Group registration becomes a separate AA-only step
- Exporter becomes a true pipeline-agnostic task

### Option C: Split Into Two Steps

- Pure index generation (pipeline-agnostic): scan containers, resolve addresses from a shared source
- AA registration (AA-only): ensure index SO is in the correct Addressables group
- AB registration (AB-only): explicitly add the generated bootstrap asset to AB collection without broadening the global `Assets/Build/**` scan boundary
- Backend build validation: fail before publication when the required `LuaScriptsIndex` address is absent

## Decision

The old deferral is no longer valid. Option C is the smallest direction consistent with the verified AA/AB ownership split, but this draft remains non-executable until the developer approves a concrete implementation plan.

Do not fix this by globally collecting `Assets/Build/**`; that directory also owns pipeline and bootstrap artifacts outside this asset's package contract.

## Trigger To Revisit

- If AB address namespace diverges from Addressables
- If `LuaScriptsIndexExporter` needs to become a DAG task
- If the exporter's Addressables dependency causes issues in AB-only builds
- Triggered: AB-only Full and Hotfix packages omit `LuaScriptsIndex` and block runtime startup
