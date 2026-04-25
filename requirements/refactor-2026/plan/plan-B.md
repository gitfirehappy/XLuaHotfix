# Sub-Plan B: AB Package Management Replacement — Overview

> **Status**: In progress (B1/B2/B3/B5/B6/B7/B8 completed; B4 concept stage, B9 pending planning)
> **Sub-files**: plan-B1.md / plan-B2.md / plan-B3.md / plan-B5.md / plan-B6.md / plan-B6-manifest.md / plan-B7.md / plan-B7-1.md / plan-B7-2.md / plan-B8.md / plan-B4.md

---

## Background & Objectives

Replace the runtime Addressable API with a custom AB package management system, keeping the AssetPackageManager external API unchanged.

**Not touched this round**: Build-side Editor code (BuildProjectManager / DifferentialProcessor / HelperBuildDataExporter / SOAddressableTagger / LuaAddressableTagger) still uses AddressableAssetSettings API.

---

## Design Rationale (For Developer Understanding)

### Why split into 'completed stage + runtime contract stage + high-risk stage'?

The current use of Addressables in the project needs to be understood in 5 layers, each with different replacement risk:

```
[B1] Data Layer — AddressableLabelsConfig provides Label/Type -> Key mapping
     | depends on
[B2] Loading Layer — Addressables.LoadAssetAsync / Release (wrapped by AssetPackageManager)
     | depends on
[B3] Module Layer — DialogueDataManager direct calls (designed as pluggable independent module, dual-mode preserved)
     | depends on
[B5] Contract Layer — Runtime Entry / Resolve / Load / Handle / Validation
     | depends on
[B4] Hotfix Core — CatalogUpdater (Catalog redirect + Locator replacement)
     Highest risk, evaluated independently
```

B1 / B2 / B3 have already established the 'abstraction layer separation', but haven't stabilized 'how runtime assets are uniquely resolved, loaded, and released'.
Therefore B5 was added on 2026-03-29: stabilize the runtime contract first, then decide whether to proceed with B4.

---

## Phase Overview

| Phase | File | Core Objective | Risk | Status |
|-------|------|---------------|------|--------|
| B1 | plan-B1.md | IAssetIndex — interface-based asset index layer | Low | DONE |
| B2 | plan-B2.md | IPackageBackend + ABPackageBackend asset loading | Medium | DONE |
| B3 | plan-B3.md | DialogueDataManager independent dual-mode (preserving direct call toggle) | Low | DONE |
| B5 | plan-B5.md | Runtime asset index / Resolve-Load / Handle / Validation / Migration | Medium | DONE (B5-1/B5-2 done, B5-3 cancelled, B5-4 deferred) |
| B6 | plan-B6.md / plan-B6-manifest.md | ABAssetIndex + ABManifest data layer and initialization path | Medium | DONE |
| B7 | plan-B7.md / plan-B7-1.md / plan-B7-2.md | ABBundleLoader + ABPackageBackend runtime loading backend | Medium | DONE |
| B8 | plan-B8.md | AssetHandle struct + HandleRegistry + error propagation unification | Medium | DONE |
| B4 | plan-B4.md | Catalog redirect layer replacement (high risk, evaluated independently) | High | Concept stage |

---

## Code Standards (Applies to All Phases)

- Add `///` doc comments to new files, consistent with existing code style
- Use `#region` to separate logical blocks
- When modifying complex logic, explain the rationale in code comments

## Execution Protocol

After each sub-phase is completed:
1. Request developer sign-off (functional verification + code review)
2. After sign-off, ask whether to proceed to the next phase
3. Developer may ask questions at any time; the executor is responsible for explaining