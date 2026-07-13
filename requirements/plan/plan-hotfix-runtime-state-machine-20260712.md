# Hotfix Runtime State Machine and Local Activation

## Status

Implemented and verified; the 2026-07-13 Major-baseline follow-up supersedes the original configurable Major-mismatch policy and pointer timing. Local Unity cleanup E2E remains pending developer acceptance.

## Goal

Make the remote `PackageIndex.json` a stable package locator while keeping the last successfully activated local package authoritative during offline or recoverable remote failures. Eliminate same-package manifest/catalog/bundle downloads and self-copy behavior without changing the published AA package layout or deploying AA 4.0.1 to Cloudflare.

## Runtime Contract

1. The remote PackageIndex selects the target package directory.
2. The local PackageIndex selects the last successfully activated isolated package directory.
3. A package is locally complete when its exact package manifest parses, its backend/version matches the local pointer, the AA catalog exists when applicable, and every expected Bundle exists with the expected size.
4. Same remote/local pointer plus a complete local package activates local content without downloading the remote manifest, catalog, or Bundles.
5. Same pointer plus an incomplete local package fetches the cumulative remote manifest and repairs only missing or invalid files.
6. A different pointer fetches the remote manifest, reuses unchanged hash-matching Bundles from the active package, downloads changed files through temporary files, activates and initializes the target package, and only then atomically persists the new local PackageIndex.
7. Remote rollback is followed even when the selected same-Major package version is lower than the active local package.

## Failure Policies

- Add `HotfixRemoteFailurePolicy` with `ContinueWithLocal` and `FailStartup`.
- Major handling now follows fixed direction rules; `HotfixMajorVersionMismatchPolicy` was removed by the approved follow-up.
- Defaults continue with the last complete local package or built-in baseline.
- Recoverable fallback emits `OnWarning`, activates local/baseline content, finalizes, and emits `OnFinished` exactly once.
- Fatal failure emits `OnError` and faults `InitializeAsync` with `HotfixFatalException`.
- A newer remote Major or a client older than its local active package emits `OnClientUpdateRequired(ClientUpdateRequiredInfo)`; the framework does not create UI.
- Compare the remote Major against the app `BuildIndex`, not the local hotfix manifest.

## Network Policy

- Existing configurable retry count and exponential backoff apply to all hotfix requests.
- PackageIndex, package manifest, and catalog use `HotfixMetadataTimeoutSeconds` with a 15-second default.
- Bundle downloads use `HotfixBundleTimeoutSeconds` with a 300-second default.
- AA and AB settings own the timeout values while shared settings own failure policy.

## Pipeline Changes

- Extend `IHotfixPipeline` to inspect an exact package directory without StreamingAssets fallback, validate local completeness, persist remote metadata separately, and activate an already local package.
- Preserve the previous valid AA catalog until the target package is complete and activation succeeds.
- Make final package-manager initialization return a checked success result.
- Add AA, AB, and compatibility facade events for warnings and required client updates.
- Add `ABManifest.FileHash`, upgrade AB manifest schema from 3 to 4, generate the hash from canonical manifest content with `FileHash` empty, and regenerate the binary serializer.

## Acceptance

1. Static decision self-checks cover same-package local activation, incomplete repair, pointer changes/rollback, remote failure policy, and directional remote Major handling; Unity smoke acceptance asserts exactly-once finalization.
2. Build and Unity compilation pass after serializer regeneration.
3. Current AA 4.0.0 local publish supports a clean first install.
4. A second launch for the same complete package requests only `PackageIndex.json`.
5. Offline launch activates the complete local package without remote metadata.
6. Deleting one local Bundle repairs only the missing Bundle from the cumulative 4.0.0 package.
7. Change the first dialogue line in `Assets/Test/ConvertTestAssets/Csv/TalkTest1.csv` with an observable 4.0.1 marker, build AA Hotfix 4.0.1, publish only to Local, and verify unchanged Bundle reuse, changed Bundle download, catalog replacement, and updated dialogue behavior.
8. Keep AA 4.0.1 as Repository HEAD/output, do not deploy it to Cloudflare, and restore `FYAssetAASettings.HotfixUrl` to `https://firehappy-cfy.com/AA/` after local verification.

## Boundaries

- Do not restore or accept AB Full/Hotfix in this plan.
- Do not implement strong publish validation or post-deploy auditing here.
- Do not change the package directory layout or introduce same-version remote publication.
- Preserve unrelated worktree changes and do not commit without a separate developer request.

## Package Cleanup Simplification Follow-up

- Remove `PackageCleaner`; keep new-build clearing and inactive-package cleanup as private `HotfixFlowBase` operations.
- Reuse Bundle files only from the target directory or the package selected by the previous trusted local PackageIndex; never scan historical package directories.
- After update, rollback, or same-package repair has activated and `FinishHotfix()` succeeds, delete every other direct `Build_*` child under HotfixRoot before raising `OnFinished`.
- Keep fallback and direct same-package activation paths non-destructive. Cleanup is best-effort and has no retention count, timestamp sorting, disk threshold, or LRU policy.
- Move directory-size calculation and human-readable byte formatting into `FileHelper` and remove duplicate editor implementations.
