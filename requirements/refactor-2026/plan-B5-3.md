# Sub-Plan B5-3: Manual Scan, Build Validation & Conflict Diagnostic Tools

> **Risk**: Medium
> **Dependencies**: B5-1 + B5-2 approval completed
> **Status**: CANCELLED — moved to Phase 6 build pipeline (decision: 2026-04-07)

---

## Objective

Define the editor validation and diagnostic tools for B5, clarifying:

- When to scan / validate
- Which situations warn, which block the build
- What information conflict reports should display
- How to provide suggested filter conditions and suggested Addresses

---

## Background

With `Address` allowing duplicates and `Labels` participating in final disambiguation,
without stable editor validation and error reporting, runtime conflicts would be very difficult to debug.

Therefore this sub-plan does not write runtime code directly, but first locks down **scan entry points, hard-block conditions, conflict reports, and suggestion outputs**.

---

## Confirmed Rules

1. Validation trigger method: **manual scan + build hard-block**
2. Same `Address + PrimaryType` distinguished by different `Labels`: **allowed but warned**
3. Must check 'label subset ambiguity' and provide warnings
4. Path alias is only for editor display / location; not a formal runtime query entry point
5. Conflict report must at minimum display:
   - `EntryId`
   - `Address + PrimaryType`
   - `Labels + Auto status`
   - `SourcePath + Group`
6. Editor can provide suggested Address candidates; default approach: **primary Type first, then manual confirmation**

---

## Planned Tasks

### Task 1: Define Scan & Validation Entry Points

- Define manual scan entry point and output summary
- Define full validation checkpoints that must execute during build
- Define the relationship between scan results and build validation result output formats

### Task 2: Define Warning & Blocking List

- Clarify which conflicts only warn
- Clarify which conflicts must block the build
- Clarify the report level for label subset ambiguity

### Task 3: Define Conflict Report & Suggestion Capabilities

- Define output rules for candidate lists and suggested filter conditions
- Define suggested Address generation principles
- Define whether editor locate / jump / one-click write-back capabilities are included in the first phase

---

## Preservation Requirements (Must Pass)

- [x] Manual scan is the daily primary entry point; no dependency on real-time scan
- [x] `Group` used only for editor reports and build semantics, does not enter runtime API
- [x] Validation tools must not secretly auto-select a runtime candidate asset for the developer
- [x] Report content must directly support 'locate conflict entry and modify rules'

---

## Acceptance Criteria

- [ ] Developer can proactively discover duplicate clusters, manifest conflicts, label subset ambiguity in the editor
- [ ] Build phase can hard-block issues defined as blocking in this round
- [ ] Runtime Resolve conflict and editor scan report fields are consistent, facilitating cross-reference debugging
- [ ] Suggested Addresses and suggested filter conditions are sufficient to support manual fixes, not just a vague error message

---

## Out of Scope

- `AssetHandle<T>` and loading return value implementation
- `HotfixManager` / `CatalogUpdater` / `NetworkDownloader` runtime modifications
- RawFile / non-Unity asset dedicated diagnostic entry points

---

## Approval Checklist

- [x] Is the validation timing 'manual scan + build hard-block'?
  **Decision**: Yes.
- [x] Same `Address + PrimaryType` distinguished by different `Labels` — allowed but warned?
  **Decision**: Yes.
- [x] Check label subset ambiguity and provide warnings?
  **Decision**: Yes.
- [x] Path alias only for editor display and location?
  **Decision**: Yes.
- [x] Build hard-block entry point: integrate into `BuildProjectManager` main flow, or standalone precheck step first?
  **Decision**: Standalone precheck first. BuildProjectManager also needs main flow refactoring later (currently a simplified flow); integrate after main flow skeleton refactoring. Validation logic verified independently first, naturally integrated during Phase 6 build pipeline rewrite.
- [x] Should first-phase suggested Address only provide candidate list, or allow one-click write-back to current entry?
  **Decision**: First phase candidate list only. Generation rules still being validated; one-click write-back as future editor tool enhancement.
- [x] Should first-phase conflict report include one-click asset location / open Inspector / copy suggested filter conditions?
  **Decision**: First phase includes 'one-click asset location + copy suggested filter conditions'. Low implementation cost (PingObject + string copy), significant efficiency gain. Inspector opens automatically with asset location.