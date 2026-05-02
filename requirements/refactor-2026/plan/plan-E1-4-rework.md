# Sub-Plan E1-4 Rework: Collector Editor UI Repair

> **Risk**: Low-Medium (Editor-only refactor, but touches the main Collector workflow and layout composition)
> **Dependencies**: landed E1-4 UI code, E1-3 (`CollectionScanner`), R1 (`BuildMessage` / `BuildSeverity`)
> **Parent**: [plan-E1-4.md](plan-E1-4.md)
> **Status**: Realized

---

## Objective

Repair the current E1-4 Collector editor UI so it becomes usable and visually coherent without changing the project's editor technology choice.

This rework keeps the existing **IMGUI + TreeView** stack, rewrites the broken layout composition, improves the shell aesthetics, adds an explicit **Scan Preview** area backed by `CollectionScanner.Scan`, and fixes empty-state presentation so the Collector tool becomes readable and operable.

---

## Background / Verified Problems

Current landed E1-4 code has these verified issues:

1. `CollectorPanel.cs` mixes `EditorGUILayout` flow layout with manual `Rect` layout (`DrawToolbar` + `DrawSplitView` + `DrawValidationResults`), causing top/bottom regions to overlap the middle content area.
2. `CollectorPropertyPanel.OnGUI(Rect rect)` ignores the incoming `rect` and draws with unconstrained global layout, so the inspector pane can visually escape its assigned area.
3. The center empty-state text is rendered as a bare label in the property scroll area, producing weak visual hierarchy and poor cross-column presentation.
4. The current bottom panel shows **validation messages only**. It does **not** expose E1-3 scan output, so the user's observed "no scan results" is primarily a missing UI feature rather than proof of scanner failure.
5. The splitter width ratio is updated against the window width rather than the actual content region and lacks strong min-width clamping for both panes.

---

## Confirmed Decisions

### D1: Keep IMGUI, Do Not Migrate to UI Toolkit

This rework stays on the current IMGUI / `TreeView` stack. External Unity references may inform layout principles, but they do not justify a technology migration in this repair round.

### D2: Add Explicit Scan Preview

Bottom results area becomes a tabbed region with:

- `Validation`
- `Scan Preview`

`Scan Preview` is populated only when the user explicitly triggers scan/refresh.

### D3: Shell Polish Is In Scope

`BuildPipelineWindow` sidebar/button spacing/content padding may be visually cleaned up, but no new functionality is added to Pipeline / Builder / Inspector / Settings.

### D4: Safe Refactor Only

No runtime/build-pipeline behavior changes. `CollectionScanner` logic remains the source of truth; the UI only invokes and displays its result.

---

## Scope

### In Scope

- Rebuild Collector UI layout with a single rect-driven composition model
- Constrain property inspector drawing to its assigned pane rect
- Improve Collector empty states and visual hierarchy
- Add scan preview trigger + result rendering using `CollectionScanner.Scan`
- Add bottom result tabs / segmented switching between validation and scan preview
- Improve splitter clamp logic and pane usability
- Apply light shell polish to `BuildPipelineWindow` sidebar/content spacing

### Out of Scope

- UI Toolkit migration
- Rewriting `CollectionScanner` scan rules or dedup algorithm
- Implementing the other 4 placeholder panels
- Build execution / pipeline task triggering
- Major data model changes to `CollectorSetting`

---

## Target Files

| File | Change Type | Purpose |
|------|-------------|---------|
| Build/Editor/BuildPipelineWindow.cs | Modify | shell spacing / content padding / sidebar polish |
| Build/Collector/Editor/UI/CollectorPanel.cs | Modify | main layout rewrite, tabs, scan preview integration |
| Build/Collector/Editor/UI/CollectorPropertyPanel.cs | Modify | rect-bounded inspector rendering, empty-state improvement |
| Build/Collector/Editor/UI/CollectorTreeView.cs | Modify | selection helpers / optional result-source navigation / splitter-friendly behavior |
| Build/Collector/Editor/CollectionScanner.cs | Read-only unless minimal UI-facing helper is truly needed | scan source of truth |
| Build/Collector/Editor/ScanResult.cs | Read-only unless a tiny display helper field is truly needed | scan output model |
| Build/Collector/Editor/UI/CollectorResultPanel.cs | New (optional but recommended) | encapsulate bottom Validation / Scan Preview rendering |

All paths relative to `Assets/FYAsset/Scripts/`.

---

## Task Breakdown

| Task | Content | Depends On |
|------|---------|-----------|
| E1-4R-T1 | Refactor `BuildPipelineWindow.cs` shell spacing and content padding while preserving 5-area routing | — |
| E1-4R-T2 | Rewrite `CollectorPanel.cs` into explicit top / middle / bottom rect sections with one layout system only | T1 |
| E1-4R-T3 | Refactor `CollectorPropertyPanel.cs` so all drawing is bounded by the provided rect and empty state becomes a centered card | T2 |
| E1-4R-T4 | Add stable splitter constraints based on content rect, with min tree width and min inspector width | T2 |
| E1-4R-T5 | Add bottom result mode switch (`Validation` / `Scan Preview`) and reusable rendering structure | T2 |
| E1-4R-T6 | Integrate explicit scan action calling `CollectionScanner.Scan(_setting)` and render scan assets/messages in the Scan Preview tab | T5 |
| E1-4R-T7 | Add optional result-to-selection navigation helpers where practical without over-expanding scope | T3, T5 |
| E1-4R-T8 | Verification: diagnostics on changed files + `dotnet build XLuaHotfix.sln` | All above |

---

## UX Rules

1. Toolbar, content split area, and bottom results area must be sibling regions with non-overlapping heights.
2. Property panel content must never render outside the inspector pane.
3. Empty state must be a centered, visually grouped panel with primary guidance text and one or two next-step hints.
4. Scan Preview must make the lack of results explicit (e.g. "No scan executed yet" vs "0 assets after scan").
5. Validation and Scan Preview must remain readable in narrow windows (minimum widths respected).
6. Shell polish must remain subtle and match existing project editor style.

---

## Invariants (Must Hold After Rework)

1. `BuildPipelineWindow` still opens from the same menu path and still routes 5 sidebar areas.
2. Collector tree editing behavior (add/delete/duplicate/reorder) remains available.
3. Top toolbar no longer overlaps the tree/property content area.
4. Bottom results area no longer covers the middle pane.
5. Property panel respects the inspector pane bounds at all times.
6. Empty-state presentation is visually centered and no longer appears as stray text across the middle layout.
7. Validation remains available and still uses `CollectorSettingValidator`.
8. Scan Preview explicitly runs `CollectionScanner.Scan` and displays returned assets/messages.
9. No runtime code path is changed.
10. `dotnet build XLuaHotfix.sln` passes with 0 errors.

---

## Verification Plan

1. `lsp_diagnostics` on all changed editor UI files
2. `dotnet build XLuaHotfix.sln`
3. Manual editor checks:
   - resize window narrow/wide
   - drag splitter to both extremes
   - select / deselect tree nodes
   - switch Validation / Scan Preview tabs
   - run scan preview with empty config, normal config, and path warnings

---

## Approval Checklist

- [x] Keep IMGUI; do not migrate to UI Toolkit in this round
- [x] Add explicit `Scan Preview` instead of validation-only bottom area
- [x] Include light `BuildPipelineWindow` shell polish
- [x] Restrict scope to UI repair; do not change runtime/build behavior
- [ ] Approve this rework plan for execution

---

## Change Log

| Date | Change |
|------|--------|
| 2026-05-02 | Initial rework plan created after code search + scan-flow investigation + external Unity UI reference review |
