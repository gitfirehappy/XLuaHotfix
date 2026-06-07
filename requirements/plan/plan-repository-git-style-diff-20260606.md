# Repository Git-Style Diff Plan

Status: Implemented / Awaiting sign-off

## Summary

Redefine Build Repository diff semantics to match a simple Git mental model.

Each Repository commit stores its own fixed `CommitDelta` against the previous same-channel/backend HEAD at commit time. The first commit has no parent and stores a full Added delta from an empty artifact set to the current artifact set. The Repository UI is reorganized around GitHub Desktop-style `Changes` and `History` views:

- `History` shows persisted commit diffs.
- `Changes` shows staging diff: current uncommitted preview output versus Repository HEAD.

Push `From/To` is intentionally left out of this plan except for removing the editable `From` / `To` fields from the main Repository panel. Push remains whole-package publication of the selected target using Repository HEAD.

## Scope

- Persist git-style commit diff data in Repository commits.
- Show persisted commit diff in History without rerunning preview DAG.
- Keep a separate Changes/Staging Diff flow for current preview output vs HEAD.
- Rework `RepositoryStatusPanel` layout toward GitHub Desktop: tabs, list, detail.
- Keep AB Hotfix delivery diff unchanged: Full baseline -> current output still determines `ABDeliveryBundles`.
- Do not change runtime manifest schema, package output format, hotfix download flow, backend selection, or Lua bridge behavior.

## Implementation Checklist

1. Extend repository data contract:
   - `Assets/FYAsset/Scripts/Build/Repository/RepositoryCommit.cs`
     - add `public string ParentVersion;`
     - add `public ArtifactDelta CommitDelta;`
   - Treat missing `CommitDelta` in old commit JSON as an empty delta in UI code only; do not rewrite old objects during load.
2. Compute commit delta at commit time:
   - `Assets/FYAsset/Scripts/Build/Repository/Editor/FileBuildRepository.cs`
     - before writing the new object, load the current HEAD for `commit.ChannelKey`
     - set `ParentVersion` to the parent commit version string, or empty for the first commit
     - set `CommitDelta = ArtifactDiffer.Diff(parentArtifacts, commit.Artifacts)`
     - for first commit use empty parent artifacts so all current artifacts appear in `Added`
     - write object after these fields are set, then swap `HEAD.json` as today
3. Keep existing build pipeline outputs unchanged:
   - `BuildRepositoryFacade.Commit(...)` continues to assemble `RepositoryCommit` from build results and delegates parent/delta assignment to `FileBuildRepository.Commit(...)`
   - `TaskScanABHotfixDiff` current-vs-HEAD and Full-baseline delivery calculations remain unchanged
   - `TaskScanAddressableHotfixDiff` current-vs-HEAD build task behavior remains unchanged
4. Rework Repository UI:
   - `Assets/FYAsset/Scripts/Build/Repository/Editor/RepositoryStatusPanel.cs`
   - replace the current commit/detail/push split layout with:
     - top status bar: repository title, channel/backend, HEAD, package, artifacts, last push, action buttons
     - left panel tabs: `Changes` and `History`
     - left list: staging artifacts or commit rows depending on tab
     - middle list: changed artifact rows for the selected staging/commit delta
     - right detail pane: artifact metadata diff
     - bottom/right compact Push target editor area with no editable From/To fields
   - `History` default selection is HEAD when available
   - selecting a commit displays `commit.CommitDelta`
   - `Changes` displays staging diff only after `Refresh Staging` runs the existing preview DAG
   - `Changes` result is in-memory only and is cleared/rebuilt on panel rebuild or refresh
5. Add UI helpers inside `RepositoryStatusPanel` only unless extraction is clearly necessary:
   - `RepositoryViewMode` enum: `Changes`, `History`
   - selected commit, selected artifact, current staging delta, and current staging delivery summary fields
   - artifact row rendering for Added/Modified/Removed
   - artifact detail rendering for `ArtifactDigest` metadata
6. Push UI adjustment:
   - remove editable `_fromVersionField` and `_toVersionField`
   - Push button publishes Repository HEAD to selected `Target`
   - if no HEAD, Push fails with a visible message before calling repository Push
   - keep `PushTargetConfig` add/remove/path editing
   - keep existing `FileBuildRepository.Push(...)` method signature for CLI compatibility in this plan
7. CLI compatibility:
   - leave `BuildRepositoryCLI.Push` and `IBuildRepository.Push` unchanged
   - do not use CLI Push changes to define UI behavior
8. Align records after implementation:
   - `context/architecture/resource-build-and-release.md`
   - `docs/FYAsset/资源管理架构文档.md`
   - `requirements/plan.md`
   - `requirements/plan/INDEX.md`
   - `requirements/progress.txt`

## UI Behavior Details

### History

- Left `History` list shows repository commits newest first.
- Each commit row displays:
  - `HEAD` marker when it matches current HEAD
  - version
  - build type
  - local created time
  - delta summary: `+A ~M -R`
- Selecting a commit renders its persisted `CommitDelta`.
- First commit displays all artifacts as Added.
- Old commits without `CommitDelta` display an explicit `No persisted diff in this commit` empty state.

### Changes

- `Changes` is staging diff, not a Repository commit.
- `Refresh Staging` runs:
  - AB: `RepositoryPreviewRunner.RunABPreviewDetailed(_request)`
  - AA: `RepositoryPreviewRunner.RunAAPreview(_request)`
- Staging diff compares current preview output against Repository HEAD through the existing preview task path.
- AB Changes view also keeps the existing delivery summary from `ABRepositoryPreviewResult`, but labels it as Hotfix delivery information, not commit diff.

### Artifact Detail

- Added artifact: show new name/hash/CRC/size.
- Removed artifact: show old name only unless old digest is available from the parent commit.
- Modified artifact: show name plus old/new hash/CRC/size when old digest can be found from parent artifacts; otherwise show new digest and a warning that old metadata is unavailable.
- No text/binary file diff is attempted.

## Acceptance Criteria

- First Repository commit JSON contains empty `ParentVersion` and `CommitDelta.Added.Count == Artifacts.Count`.
- Second Repository commit JSON contains `ParentVersion` equal to the previous HEAD version and a persisted `CommitDelta` matching parent/current artifact diff.
- Repository History displays persisted commit diff without executing `RepositoryPreviewRunner`.
- Repository Changes executes preview only when the user clicks `Refresh Staging`.
- Repository panel has no editable `From` or `To` version fields.
- Push from the panel targets current HEAD and still publishes whole package output through existing `LocalDirectoryPushTarget`.
- AB Hotfix delivery package behavior remains unchanged and still uses Full baseline -> current output.
- Existing CLI Push remains available with its current argument contract.
- Compile passes with existing warnings only.
- Scoped whitespace check passes for modified files.

## Verification Plan

1. Static checks:
   - search for `_fromVersionField` and `_toVersionField` in `RepositoryStatusPanel.cs` and confirm they are removed
   - search for `CommitDelta` and `ParentVersion` assignments in `FileBuildRepository.Commit`
   - search for `TaskScanABHotfixDiff` delivery logic to confirm Full-baseline diff remains
2. Build:
   - `dotnet build XLuaHotfix.sln --no-restore`
3. Whitespace:
   - `git diff --check -- <modified files>`
4. Self-simulated flows:
   - first commit: empty parent -> all Added
   - second commit: previous HEAD -> current artifacts
   - History selection: persisted diff only
   - Changes refresh: preview DAG result only
   - Push HEAD: no manual From/To inputs
