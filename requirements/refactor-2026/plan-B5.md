# Sub-Plan B5: Runtime Asset Index & Resolve/Load Contract Refactoring

> **Status**: Execution completed (B5-1/B5-2 done, B5-3 cancelled, B5-4 deferred)
> **Dependencies**: B1 + B2 + B3 completed; B4 not in this round's scope
> **Scope**: Runtime loading layer only (Index / Resolve / Load / Handle / Validation)
> **Sub-files**: plan-B5-1.md / plan-B5-2.md / plan-B5-3.md / plan-B5-4.md

---

## Background & Objectives

B1 / B2 have abstracted runtime resource management into IAssetIndex and IPackageBackend,
but the current runtime still follows the Addressables mindset of single key -> single asset -> string-based unload.

After this round of discussion, the resource management goals have clearly diverged from Addressables' default assumptions:

- Address **allows duplicates**, no longer serves as globally unique identity
- Group only serves build & collection, does not enter runtime queries
- Runtime needs both **strict queries** and **convenience queries**
- Release semantics must shift from string key unload to **Handle-first**
- Subsequent ABPackageBackend / ABAssetIndex implementations need a stable upper-level Resolve / Load contract as foundation

Therefore, before entering B4 (Catalog / Locator / hotfix core pipeline), B5 is added first:
to define the **runtime entry model, Resolve/Load contract, Handle model, editor validation, and migration path** clearly.

---

## In Scope

- Runtime asset entry index model
- Address / PrimaryType / Labels / EntryId rules
- ResolveByAddress / ResolveByTypeKey / LoadByAddress / LoadByTypeKey contracts
- AssetHandle<T> and structured error model
- Manual scan validation, build hard-block, conflict reports, suggested Address
- Legacy API compatibility wrappers and migration sequence

## Explicitly Out of Scope

- No changes to HotfixManager / CatalogUpdater / NetworkDownloader execution logic
- No introducing Group into runtime filtering
- No RawFile / non-Unity asset loading interfaces in this round
- No direct entry into B4's catalog / locator rewrite

---

## Converged Design Consensus

1. Address allows duplicates; internal unique identity uses EntryId
2. Group only serves build & collection, does not enter runtime Resolve / Load API
3. V1 Type model retains only PrimaryType; ScriptableObject assets use **concrete class name** not ScriptableObject
4. Labels are **unordered unique sets**, matching is case-insensitive, display preserves original input
5. Resolve / Load uses dual-track semantics:
   - Strict: ByAddress
   - Convenience: ByTypeKey (Labels optional)
6. Load API centers on AssetHandle<T>; release uses **Handle-first**
7. Legacy LoadAssetAsync<T>(key) maps to LoadByAddress first, migrate legacy interfaces after new API stabilizes
8. Validation strategy: **manual scan + build hard-block**; same Address + PrimaryType distinguished by Labels is allowed but warned

---

## Sub-Plan Overview

| Sub-Plan | File | Objective | Risk | Status |
|----------|------|-----------|------|--------|
| B5-1 | plan-B5-1.md | Define runtime entry model, Address/Type/Label/EntryId rules | Medium | DONE |
| B5-2 | plan-B5-2.md | Define Resolve/Load API, AssetHandle, error model & compat layer | Medium | DONE |
| B5-3 | plan-B5-3.md | Define manual scan, build validation, conflict reports & suggested Address tools | Medium | CANCELLED (moved to Phase 6 build pipeline) |
| B5-4 | plan-B5-4.md | Define migration path, legacy API deprecation conditions & rollout sequence | Medium | Deferred (evolves with implementation) |

---

## Recommended Order

B5-1 -> B5-2 -> B5-3 -> B5-4

After B5 is fully stable, decide whether to proceed with B4.

---

## Relationship with B4

B4 addresses catalog / locator / hotfix core pipeline replacement — high risk.

B5 first defines how the runtime identifies an asset, resolves queries, loads and releases.
This way, whether the underlying layer continues with AddressablesBackend or switches to ABPackageBackend in the future,
the AssetPackageManager upper-level contract will not keep oscillating.

---

## Resolved Refinement Questions (Approved 2026-03-30)

- Auto Address upgrade format: Filename_Type (underscore separator, type suffix at end)
- Batch Labels query: both tiers retained (layered) — ResolveMany + LoadMany base + LoadByLabels convenience wrapper
- Structured errors: Result-style primary — AssetHandle carries Result role, add .ThrowIfFailed() extension when needed
- Editor suggested Address: first phase generates candidate list only, one-click write-back as future enhancement
- EntryId: reuses Unity GUID
- Build hard-block: standalone precheck first, integrate into main pipeline after build pipeline refactoring
- First migration batch: AssetPackageManager internals
- UnloadAsset Obsolete timing: after first batch of call sites migrated