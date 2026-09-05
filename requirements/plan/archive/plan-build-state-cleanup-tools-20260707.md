# Build State Cleanup Tools Plan 2026-07-07

> **Status**: Executed / Static Verified / Archived 2026-07-14
> **Requirement ID**: build-state-cleanup-tools-20260707
> **Origin**: `draft-version-system-test-features-20260707.md`, `draft-buildresults-management-panel-20260707.md`, `draft-repository-reset-20260707.md`
> **Scope**: Version test reset, package output deletion, and repository test reset.

> **2026-07-11 Supersession Note**: `plan-build-panel-task-slim-20260711.md` removed `VersionPanel`. The implemented
> version display/reset now lives in Repository. References to `VersionPanel` below document the original approved
> implementation scope rather than the current UI owner.

> **Archive Note**: Remaining self-check and disposable manual acceptance moved to
> `requirements/plan/drafts/draft-legacy-plan-review-followups-20260714.md`.

## Goal

Make local build testing recoverable without manual file surgery:

- Version metadata can be reset for tests without editing build-owned fields.
- Generated package folders can be listed and deleted from the editor.
- Repository state for the current channel/backend can be cleared intentionally for test rebuilds.

## Locked Decisions

1. Keep this as editor-only tooling; do not change runtime loading, hot-update flow, package format, or repository commit format.
2. Keep the first pass small: no `.buildmeta.json`, tags, notes, date picker, retention policy, or production/test classification.
3. Version reset affects only `VersionDataBase`: `CurrentVersion = 1.0.0`, `Build = 0`, empty `Channel`, empty `LastBuildTime`, `DailyBuildCount = 0`.
4. Build metadata fields remain visible but read-only in `VersionPanel`.
5. Package deletion deletes selected package directories under `BuildPathManager.PackagesDir` only after confirmation.
6. Repository reset is test-only and channel-scoped: current `BuildTarget + Channel + Backend` only.
7. Repository reset physically clears `HEAD.json`, `objects/*.json`, cached repository head errors, and any legacy `PushHistory.json` residue for that channel.
8. Package pointer cleanup writes an empty `PackageIndex.json` at `BuildPathManager.PackageIndexPath`.
9. `BuildIndex.json` and `StreamingAssets` cleanup are not part of the default reset; they require a separate explicit checkbox because they affect startup baseline state.
10. AB package deletion removes only reports whose normalized `Header.PackagePath` matches a successfully deleted package directory. Unmatched reports are preserved automatically, but any currently selected success, failure, stale, or abandoned report may be deleted explicitly after confirmation.

## Implementation Checklist

### 1. Version Panel Reset

- Update `VersionPanel` so `LastBuildTime` and `DailyBuildCount` render as disabled/read-only fields.
- Add `Reset to 1.0.0 (Test)` button with `EditorUtility.DisplayDialog` confirmation.
- Reset the in-memory `VersionDataBase`, mark it dirty, save assets, and rebuild the panel.
- Do not add a new `ReadOnlyAttribute` or PropertyDrawer.

### 2. Package Result Management

- Add a small editor helper that scans `BuildPathManager.PackagesDir`.
- Parse existing package folders named `Build_yyyyMMddHHmmss_version`.
- Show package name, version, build time, size, and path in the existing Build Result area.
- Add `Delete Selected` with confirmation listing selected package names and total size.
- Delete only directories inside `BuildPathManager.PackagesDir`; reject paths outside that root.
- For AB, show the matching report count, delete matching reports only after package deletion succeeds, and refresh the report dropdown immediately.
- Warn when a successful AB report points to a missing package directory and allow confirmed deletion of that stale report.
- Add a persistent `Delete Report` action for the current report regardless of build success or package-directory existence; deleting a report never deletes its package directory.

### 3. Repository Test Reset

- Add `FileBuildRepository.ClearChannelForTest(channelKey)` and facade wrapper.
- Clear only the selected channel root files listed in Locked Decisions.
- Add `RepositoryStatusPanel` action behind confirmation text that includes channel key and backend.
- Add optional checkboxes:
  - Clear output `PackageIndex.json`
  - Delete local package folders
  - Clear startup `BuildIndex.json` / FYAsset-owned `StreamingAssets` baseline
- Default all destructive optional checkboxes to off.

### 4. Draft Cleanup

- Archive the three source drafts after this plan is promoted.
- Keep unrelated documentation and architecture drafts active for later discussion.

## Acceptance Criteria

- Version reset does not advance or persist a build version through normal build paths.
- `LastBuildTime` and `DailyBuildCount` cannot be edited from `VersionPanel`.
- Package deletion cannot delete anything outside `BuildPathManager.PackagesDir`.
- Repository reset cannot affect another backend/channel/build target.
- After repository reset, Repository Health reports empty/OK rather than corrupted.
- Empty `PackageIndex.json` does not point to a stale package.
- Deleting an AB package deletes only reports with the same normalized `Header.PackagePath`; unrelated and failed-build reports remain.
- A successful report with missing package output is visible as a conflict and can be removed explicitly.
- Failed or abandoned reports without package output can also be deleted explicitly from the report toolbar.

## Verification

- `dotnet build XLuaHotfix.sln --no-restore` exited 0. Existing `System.Net.Http` conflict warnings remain.
- `git diff --check` exited 0. Git reported LF/CRLF working-copy warnings only.
- Static checks confirmed `BuildPackageResultsView`, `ClearChannelForTest`, and `VersionPanel` test reset/read-only metadata are present.
- 2026-07-11 follow-up: `dotnet build XLuaHotfix.sln --no-restore` exits 0 after AB report/package synchronization changes; Unity batchmode self-check is pending because the project is open in another Unity Editor instance.
- Manual editor checks:
  - Reset version, cancel and confirm paths.
  - Delete one disposable package folder.
  - Reset current AB repository channel and confirm Health is OK/empty.

## Non-Goals

- No package metadata database.
- No retention policy.
- No CI command surface unless later requested.
- No scene validation build task.
- No automatic deletion of `StreamingAssets` or startup baseline by default.
- No report database, retention policy, or automatic cleanup of unrelated/stale reports.
