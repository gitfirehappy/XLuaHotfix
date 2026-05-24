# Process Pitfalls

> Plan-implementation divergence, documentation drift, and cross-plan coordination failures.

## PP-01: Implementation Silently Drops Approved Interface

**Symptom:** Plan approved interfaces/rules; implementation delivered a subset without detection.

**Root cause:** No post-execution audit step verified that all approved items exist in the final codebase.

**Prevention:** Every execution plan must include a verification step that checks ALL approved interfaces, rules, and types exist in the codebase. Never rely on memory.

---

## PP-02: Implementation Changes Plan-Approved Severity Without Flagging

**Symptom:** Plan specified `Warning`; code implemented `Error`. Severity change was neither flagged nor approved.

**Root cause:** Implemented from memory instead of checking the plan's specification table row-by-row.

**Prevention:** When implementing enumeration tables (error codes, config tables, state machines), verify each row against the source spec. Never implement from memory.

---

## PP-03: Plan Internally Contradictory

**Symptom:** Plan's task table and modified-files table disagree on a field name. Implementation followed one branch; the other contradicts.

**Root cause:** Tables were not cross-checked before plan approval.

**Prevention:** Cross-check the task table against the modified-files table before execution. Where they disagree, the plan is not ready.

---

## PP-04: Plan File Paths Not Updated After Code Relocation

**Symptom:** Plan references `Assets/AboutXLua/` but code was moved to `Assets/FYAsset/`.

**Root cause:** Plan documents not synchronized when code was relocated.

**Prevention:** Plan and design documents must be synchronized with actual code paths before execution.

---

## PP-05: Plan Step Contradicts Plan Invariant

**Symptom:** Step says a context read is optional (`Get<T>`); invariant says the value must be populated. Cannot both be true.

**Root cause:** Step descriptions and invariant list written independently, not cross-checked.

**Prevention:** Every plan must cross-check step descriptions against its invariant list.

---

## PP-06: Cross-Plan Enum Extension Without Upstream Update

**Symptom:** Downstream plan adds a value to an enum defined upstream. Upstream plan unaware — numbering may conflict.

**Root cause:** No communication back to the upstream plan.

**Prevention:** When extending an enum defined upstream, add a forward-reference note in the upstream plan, or reserve the value at the upstream level immediately.

---

## PP-07: Cross-Plan Contract as Code Comment

**Symptom:** `// public SharePolicyConfig SharePolicy;` is a commented-out placeholder that a downstream plan depends on. If name/type drifts, contract silently breaks.

**Root cause:** Cross-plan contract encoded as source comment rather than formal spec.

**Prevention:** Cross-plan contracts must be in the plan document or a shared interface. Commented-out placeholders are not contracts.

---

## PP-08: Monolithic Task With Excessive Dependencies

**Symptom:** One task depends on 7+ upstream tasks and combines 10 sub-steps. Any upstream delay blocks everything.

**Root cause:** Task not split along natural sub-step boundaries.

**Prevention:** Any task depending on more than 3 upstream tasks should be split into independently schedulable units.

---

## PP-09: Ownership Comment Not Updated After Config Migration

**Symptom:** Comments say "configured by `BuildPipelineConfig.DefaultBackendMode`" but that field was deleted; ownership moved to `FYAssetSettings`.

**Root cause:** Source comments not updated when ownership transferred.

**Prevention:** When transferring ownership of a config value, update ALL source comments referencing the old owner. Stale ownership comments mislead maintenance.

---

## PP-10: Performance Claim in API Docs Contradicted by Implementation

**Symptom:** Comments claim "zero allocation" and "cached references"; methods allocate new arrays/lists per call.

**Root cause:** Comments written during design; implementation evolved but comments did not.

**Prevention:** Performance claims in comments must be verified against the final implementation. Either deliver the claim or soften the docs.

---

## PP-11: Field Semantics Docs Stale After Plan Execution

**Symptom:** Reference table documents field types/status from pre-plan reality. Several fields already changed; several "pending" items already unified.

**Root cause:** Documentation sync not included as plan acceptance criterion.

**Prevention:** Every execution plan must include documentation sync as a formal acceptance criterion.

---

## PP-12: Naming Inconsistency Across Same-Category Types

**Symptom:** `PackSeparately`, `PackByDirectory`, `PackByLabel` — no consistent naming pattern. New additions must guess.

**Root cause:** No naming convention established for the category.

**Prevention:** Define a naming pattern before the third variant appears. Public API types in the same category must follow a consistent convention.

---

## PP-13: Language Mixing in Code Regions/Docs

**Symptom:** Collector files use Chinese regions; Editor files use English. No consistent policy.

**Root cause:** Mixed developer language preferences during implementation.

**Prevention:** Pick one language for regions/xmldoc and enforce across all files.

---

## PP-14: Typo/Drift in Public API Names

**Symptom:** `DEAULT_XLUA_TYPE_CONFIG_LOAD_LABEL` (typo). `ScriptObjectDataBse` (typo). Constants mix Pascal, UPPER, and mixed styles.

**Root cause:** Multiple generations of code with no enforced standard.

**Prevention:** Define one naming policy for constants and types. Fix high-surface mistakes first.

---

## PP-15: Editor UI Accepted Without Host-Workflow Verification

**Symptom:** BuildGraph looked acceptable in a static HTML/reference view and compiled with `dotnet build`, but Unity showed an empty/refreshing panel, wrong ownership placement, editable lines, and missing task data.

**Root cause:** Verification stopped at compile/static layout and did not replay the actual Unity Editor workflow: open the target panel, switch tabs, reload config, inspect GraphView content, and test right-click creation behavior.

**Prevention:** Editor UI work must be verified in the host workflow, not only by compile or mock/reference HTML. Check panel ownership, lifecycle visibility, data source population, interaction constraints, and reload behavior before claiming done.

## PP-16: New Editor Scripts Not Added to Unity Project File

**Symptom:** Newly created editor panels compiled in source control but `dotnet build` failed with missing-type errors until the project file was updated.

**Root cause:** The repository relies on Unity-generated `Assembly-CSharp-Editor.csproj` entries for external build verification, and new editor `.cs` files are not guaranteed to appear there immediately.

**Prevention:** After adding Runtime/Editor scripts in this project, verify the corresponding `.csproj` `Compile Include` entries before relying on `dotnet build` as the validation signal.

---

## PP-17: Scope Expansion From Partial UI Parity

**Symptom:** A request asked to align AA with AB for `BuildMode` and `Build`, but implementation also exposed AB `Build Options` in the AA panel.

**Root cause:** "Align with AB" was applied to the whole toolbar instead of the explicitly requested controls. AA uses Addressables-owned configuration, so AB `BuildPipelineConfig` options are not automatically valid for AA.

**Prevention:** When a request names specific UI controls, implement only those controls. For partial parity requests, list excluded adjacent controls before editing and keep configuration surfaces tied to their real owner; AA Build Options require an explicit Addressables integration plan before being exposed.

---

## PP-18: Sidebar Group Range Overlap

**Symptom:** A shared management panel appeared under both a backend-specific group and the Manage group; selecting it made the shell treat the panel as backend-owned and apply the wrong disabled state.

**Root cause:** Panel group membership was encoded as `StartIndex` + `Count` ranges, and adding a new panel shifted the intended ownership boundary without an overlap audit.

**Fix:** Keep backend-specific group ranges non-overlapping and place shared panels only in the Manage range.

**Prevention:** After adding or reordering Editor shell panels, audit every group range for overlap and verify `GetGroupLabelByPanelIndex` returns the intended ownership for each panel.
