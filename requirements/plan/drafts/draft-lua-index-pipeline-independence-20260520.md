# Draft: LuaScriptsIndexExporter Pipeline Independence

Status: Draft (Open Decision)
Date: 2026-05-20

## Problem

`LuaScriptsIndexExporter` currently depends on Addressables API for two operations:

1. **Address lookup**: `settings.FindAssetEntry(guid).address` to get container addresses
2. **Group registration**: `settings.CreateOrMoveEntry(guid, group)` to register the index SO into the "LuaScripts" Addressables group

AB runtime also needs `LuaScriptsIndex` (loaded by address via `ABPackageBackend.ResolveAssetEntryByAddress`). Both AA and AB share the same address namespace — addresses originate from Addressables entry configuration.

## Current State

- `BuildProjectManager` calls `LuaScriptsIndexExporter.ExportData()` before backend selection (both AA and AB execute it)
- The exporter is in `Assets/FYAsset/Scripts/Build/Release/Editor/Addressables/` — physically located in the AA-specific directory
- AB builds need the index to exist and be up-to-date so it gets packed into AB bundles

## Design Question

Should the exporter be refactored to read addresses from a unified registry (e.g., `AssetAddressGenerator`) instead of Addressables settings, making it pipeline-agnostic?

### Option A: Keep As-Is

- Exporter stays Addressables-dependent
- Works because both pipelines currently derive addresses from Addressables configuration
- Risk: if AB ever diverges from Addressables address namespace, the index becomes invalid for AB

### Option B: Unified Address Source

- Exporter reads addresses from a shared address registry (not Addressables-specific)
- Group registration becomes a separate AA-only step
- Exporter becomes a true pipeline-agnostic task

### Option C: Split Into Two Steps

- Pure index generation (pipeline-agnostic): scan containers, resolve addresses from a shared source
- AA registration (AA-only): ensure index SO is in the correct Addressables group

## Decision

Deferred. Does not block any current plan execution. Revisit after BIC-1 (backend interface cleanup) lands and the full AA/AB Task alignment is complete.

## Trigger To Revisit

- If AB address namespace diverges from Addressables
- If `LuaScriptsIndexExporter` needs to become a DAG task
- If the exporter's Addressables dependency causes issues in AB-only builds
