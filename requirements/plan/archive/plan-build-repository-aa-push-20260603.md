# Plan: Build Repository AA Push Completion

> Date: 2026-06-03
> Status: Signed off and archived on 2026-06-04
> Scope: Build Repository closure prerequisite. Add AA Push parity with the existing AB Push path before marking E7 main path complete.
> Approval: Developer approved execution on 2026-06-03.
> Sign-off: Developer accepted and requested archive on 2026-06-04.

## Summary

AA Push must be treated as a basic build-pipeline capability with the same status as AB Push. The current Build Repository batch already provides repository HEAD/object storage, status, diff preview, AB Push, PushHistory, CLI, and UI entry points. E7 cannot be closed as "main path complete" until AA Push can use the same repository Push surface.

The minimal AA Push target is whole-package publication, matching the post-hardening Push boundary: Push publishes an already built package directory and records local repository PushHistory. Push must not reinterpret Addressables catalogs, regenerate manifests, or rewrite package-internal metadata.

## Key Changes

- Remove the AA channel rejection from `FileBuildRepository.Push()` so AA and AB can share `IPushTarget`, `LocalDirectoryPushTarget`, `PushPayload`, `PushReceipt`, and `PushHistoryEntry`.
- Keep Push backend-neutral after commit lookup: load `fromCommit` and `toCommit`, compute artifact diff count for history, then delegate publication to the configured `IPushTarget`.
- Preserve current Push ownership boundaries:
  - build DAG tasks own package contents, `AAManifest`, `ABManifest`, and `PackageIndex.json`;
  - repository Push publishes `RepositoryCommit.PackageRootDir` as a package directory;
  - repository Push writes only local `PushHistory.json` after the target reports success.
- Keep AA artifact granularity unchanged for this plan: AA commits remain source-asset GUID digests; no AA GUID-to-bundle map, catalog reverse lookup, or delta-file push is introduced.
- Keep CLI/UI entry points unchanged except that `-backend AA` and the Repository panel in AA mode are allowed to push when the normal preconditions pass.
- After AA Push is implemented and verified, update E7 in `requirements/plan.md` to "Build Repository main path complete; follow-ups deferred".

## Acceptance Criteria

- AA Push with valid `fromVersion`, `toVersion`, and PushTarget publishes the `toCommit.PackageRootDir` package directory to `{TargetPath}/{PackageName}`.
- AA Push appends `BuildData/Snapshots/{BuildTarget}[-Channel]/AA/PushHistory.json` only after successful target publication.
- AA Push failure cases do not write PushHistory:
  - missing `fromVersion`;
  - missing `toVersion`;
  - missing baseline commit;
  - missing target commit;
  - missing or unreadable `toCommit.PackageRootDir`;
  - missing PushTarget.
- AB Push behavior remains unchanged.
- Push does not parse, regenerate, or validate Addressables catalog content.
- Push does not rewrite package-internal `PackageIndex.json`.
- Repository status and PushHistory display work for both AA and AB channels.

## Test Plan

- Static checks:
  - `rg -n "AA Push not supported|channelKey.IndexOf\\(\"/AA\"|/AA" Assets/FYAsset/Scripts/Build/Repository`
  - `rg -n "PushHistory|LocalDirectoryPushTarget|PackageRootDir" Assets/FYAsset/Scripts/Build/Repository`
- Build check:
  - `dotnet build XLuaHotfix.sln --no-restore`
  - Expected result: 0 errors; existing `System.Net.Http` warnings are acceptable if unchanged.
- Manual/Unity verification:
  - Create or reuse two AA repository commits with valid package output directories.
  - Run AA Push through the Repository panel or `BuildRepositoryCLI.Push -backend AA -from <version> -to <version> -target <id>`.
  - Confirm target package directory replacement and AA `PushHistory.json` append.
  - Repeat an AB Push smoke test to confirm no regression.

## Explicitly Out Of Scope

- Repository orphan-object cleanup.
- Concurrent push/file-lock coordination.
- Repository serialization replacement or Newtonsoft.Json migration.
- Published-state derived view or UI badges.
- CDN push target implementations.
- AA delta-file publication.
- AA commit bundle mapping or Addressables catalog reverse lookup.
- Any change to runtime hotfix loading, package index download rules, Addressables catalog loading, or Lua/C# bridge behavior.

## Approval Checklist

- [x] AA Push is required before E7 can be marked main-path complete.
- [x] Whole-package publication is accepted as the AA/AB shared Push semantics for this slice.
- [x] Push must not reinterpret catalog or manifest contents.
- [x] Push must not rewrite package-internal `PackageIndex.json`.
- [x] AA Push reuses existing `IPushTarget` and `PushHistory` contracts.
- [x] Repository hardening items stay deferred until separately prioritized.
- [x] Developer approves execution of this plan before any C# changes.

## Execution Notes

- Removed the AA channel rejection in `FileBuildRepository.Push()`.
- AA and AB now share the same whole-package Push path and PushHistory write path.
- No runtime hotfix, package index writing, Addressables catalog, PushTarget, or repository DTO changes were made.
