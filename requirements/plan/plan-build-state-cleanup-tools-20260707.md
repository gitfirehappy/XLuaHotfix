# Build State Cleanup Tools Plan 2026-07-07

> **Status**: Implemented / Verified / Pending developer sign-off
> **Requirement ID**: build-state-cleanup-tools-20260707
> **Origin**: `draft-version-system-test-features-20260707.md`, `draft-buildresults-management-panel-20260707.md`, `draft-repository-reset-20260707.md`
> **Scope**: Version test reset, package output deletion, and repository test reset.

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

## Verification

- `dotnet build XLuaHotfix.sln --no-restore` exited 0. Existing `System.Net.Http` conflict warnings remain.
- `git diff --check` exited 0. Git reported LF/CRLF working-copy warnings only.
- Static checks confirmed `BuildPackageResultsView`, `ClearChannelForTest`, and `VersionPanel` test reset/read-only metadata are present.
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
