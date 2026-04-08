# Sub-Plan B5-1: Runtime Entry Model & Index Rules

> **Risk**: Medium
> **Dependencies**: B1 + B2 completed
> **Status**: DONE — signed off 2026-03-30

---

## Objective

Define the minimal model for runtime asset entries, clarifying the semantic boundaries of `Address / PrimaryType / Labels / EntryId / AutoAddress`,
providing a unified data foundation for subsequent Resolve / Load / validation and migration.

---

## Background

The project's current runtime index still leans toward Addressables' 'key is unique' mindset.
However, this round has confirmed:

- `Address` allows duplicates
- `Group` does not participate in runtime filtering
- V1 retains only `PrimaryType`
- `Labels` are used for filtering and final disambiguation, not as the primary query entry point

Therefore, the new entry model must be defined first; otherwise the Resolve / Load contract in B5-2 cannot be stabilized.

---

## Design Scope

| Topic | Description |
|-------|-------------|
| `EntryId` | Internal unique identity; used only for caching, diagnostics, handle ownership |
| `Address` | Logical name; allows duplicates; no longer serves as globally unique identity |
| `PrimaryType` | V1's only exposed Type field; auto-derived by default, allows compatibility manual override |
| `Labels` | Unordered unique set; case-insensitive matching; display preserves original input |
| `Group` | Build metadata; participates in editor reports, not in runtime filtering |
| `SourcePath` | Editor location and conflict report information |
| `AutoAddress` | Marks whether Address is auto-generated or manually overridden |

---

## Confirmed Rules

1. `Address` can share names across different `PrimaryType` values
2. Same `Address + PrimaryType` can rely on different `Labels` for disambiguation, but requires a warning
3. `PrimaryType` auto-derived by default, manual override allowed, but must be **compatible with actual type**
4. For `ScriptableObject`, `PrimaryType` must use the **concrete class name**, cannot degrade to `ScriptableObject`
5. V1 **does not implement `AdditionalTypes`**; multi-classification needs all go through `Labels`
6. Auto short name default source is **filename without extension**
7. Auto entries can be rebuilt; manually overridden entries stay locked unless explicitly switched back to Auto
8. Path is only for editor display and location info, not a formal runtime query entry point

---

## Planned Tasks

### Task 1: Define Runtime Entry Minimal Field Set

- Clarify the minimal set of `EntryId / Address / PrimaryType / Labels / SourcePath / Group / AutoAddress`
- Clarify which fields are runtime-required vs editor-diagnostic only
- Clarify the compatibility constraint between `PrimaryType` and actual asset type

### Task 2: Define Address Auto-Generation & Override Strategy

- Define auto short name generation rules (short name source, upgrade strategy)
- Define the abstract rule for 'short name + type suffix upgrade'
- Define the contract for auto entry rebuild vs manual entry preservation

### Task 3: Define Uniqueness, Warning & Blocking Boundaries

- Clarify which conflicts are allowed in build but must warn
- Clarify which conflicts must block during manual scan and build phase
- Clarify `LabelSet` normalization method and comparison rules

---

## Preservation Requirements (Must Pass)

- [x] `Group` does not enter runtime Load / Resolve query parameters
- [x] V1 does not introduce `AdditionalTypes`
- [x] `Address` allows duplicates — this core direction is non-negotiable
- [x] Manual override of `PrimaryType` must support one-click restore to auto-derived value

---

## Acceptance Criteria

- [ ] A single entry model can fully express a runtime asset's logical identity, primary query type, labels, and editor location info
- [ ] Can clearly distinguish blocking items from warning items; no longer treats `Address` as a hard unique key
- [ ] `PrimaryType` and `Labels` have clear responsibility boundaries, no redundant `AdditionalTypes` design introduced
- [ ] Auto Address and manual override/rebuild relationships can be directly implemented as editor logic

---

## Out of Scope

- RawFile / non-Unity asset object indexing
- `AssetHandle<T>`, loading return values, release semantics
- Batch `Labels` query interface design

---

## Approval Checklist

- [x] Does V1 only retain `PrimaryType`?
  **Decision**: Yes. Auto-derived + allows compatibility manual override + supports one-click reset; multi-classification goes through `Labels` first.
- [x] Must manual override of `PrimaryType` be compatible with actual type?
  **Decision**: Must be compatible.
- [x] Is `Labels` an unordered unique set with case-insensitive matching?
  **Decision**: Yes. Internal normalized matching; display preserves original input.
- [x] Can `Address` share names across different `PrimaryType` values?
  **Decision**: Yes.
- [x] Does `EntryId` reuse Unity GUID, or generate a custom internal ID at build time?
  **Decision**: Reuse Unity GUID. Naturally unique and stable; runtime only does string comparison; no need to build custom uniqueness guarantee.
- [x] Specific format for auto Address upgrade — use `Filename_Type`, `Type_Filename`, or keep only the abstract 'short name + type suffix upgrade' rule?
  **Decision**: `Filename_Type` (underscore-separated). Type suffix at the end, parsed from right to left. E.g., `player_idle` upgrades to `player_idle_Sprite`.