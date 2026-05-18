# Review Report: E7 + E13 Plan Audit

> **Date**: 2026-05-15
> **Reviewer**: Codex
> **Scope**: `requirements/refactor-2026/plan/plan-E7.md`, `requirements/refactor-2026/plan/plan-E13-legacy-sidebar.md`
> **Method**: static plan review

## Summary

E7 has one blocking plan contradiction and one contract drift point. E13 is directionally consistent, but its fallback behavior and editor-surface definition are still under-specified.

## Findings

### P0-1: E7 snapshot storage references two different head artifacts

**Files**: `plan-E7.md:67-68, 90-93, 140-147, 297-299, 325-330`

E7 defines the storage layout as `BuildData/Snapshots/` with `head.json`, `staged.bin`, and `history/*.bin`, and the release flow consistently works against `head.json`. But the task breakdown and invariant text still say `PrepareDiff` reads `head.bin`, and the diff check compares bundle digests against `head.bin`.

That is a hard contradiction: the plan names no `head.bin` artifact anywhere else. As written, the backend cannot be implemented consistently because the baseline file name is not stable.

**Fix**: choose one artifact name and make every reference match it. Based on D5, `head.json` is the current intended source of truth.

### P1-2: E7 `channel` extension point is mentioned, but not carried through the interface contract

**Files**: `plan-E7.md:134-136, 40-46, 292-302`

D5d introduces an optional `channel` parameter and says current implementation ignores it. However, the interface snippet, task table, and invariants do not include that parameter at all. The result is a half-defined API: the plan mentions a future extension point, but the concrete contract stays single-channel.

This is workable only if the team intends to keep E7 strictly single-channel. Otherwise, the plan needs the parameter wired through `IDiffPipeline`, backend methods, and the task table together.

**Fix**: either remove the `channel` mention from E7 or promote it into the public interface and task contracts now.

### P1-3: E13 legacy panel fallback is optional instead of required

**Files**: `plan-E13-legacy-sidebar.md:43-45, 61-63, 87-90`

LegacyConfigPanel is specified to embed the Addressables Settings inspector, but the only fallback is described as a downgrade if the render effect is poor. There is no explicit null guard or empty-state path for missing `AddressableAssetSettingsDefaultObject.Settings`.

For an editor sidebar panel, that is a real usability risk: the panel should degrade cleanly when the Addressables settings asset is absent or not yet initialized. Right now the plan leaves that behavior implicit.

**Fix**: add a required fallback path for missing settings, not just for poor rendering.

### P2-4: E13 scope wording is not fully aligned with the implementation surface

**Files**: `plan-E13-legacy-sidebar.md:13, 21-22, 43-45`

The objective says the legacy panel will embed "AA Groups", but the implementation hook is `Editor.CreateEditor(AddressableAssetSettingsDefaultObject.Settings)` and `editor.OnInspectorGUI()`, which is the Addressables Settings inspector, not the groups window.

This is not fatal, but the plan is ambiguous about whether the panel is a settings overview or a groups-management surface. That ambiguity will matter when implementation starts.

**Fix**: rename the objective or rewrite the implementation approach so both describe the same editor surface.

## Open Questions

1. Should E7 use `head.json` everywhere, including diff preparation and invariants?
2. Is E13 meant to render the Addressables Settings inspector, or a groups-management surface?

## Verdict

E7 is not implementation-ready until the snapshot head-file name is unified. E13 is close, but it still needs a required empty-state path and a clearer UI surface definition before execution.
