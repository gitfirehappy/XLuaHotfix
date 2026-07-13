# Hotfix Major Baseline and Active Pointer Simplification

## Status

Implemented and verified; pending developer sign-off.

## Goal

Remove the legacy `build_guid.txt` marker and make Major-version direction the only client compatibility boundary. A local `PackageIndex` must identify the last package that completed activation and runtime initialization.

## Runtime Rules

1. Keep `BuildGUID` only as the unique Full-build baseline package identity and initial path name; never use it for compatibility.
2. After loading a trusted local PackageIndex:
   - Build Major greater than local Major: delete HotfixRoot, discard the local pointer, then run the normal remote PackageIndex/manifest flow.
   - Build Major lower than local Major: delete HotfixRoot, raise `OnClientUpdateRequired`, and fail startup.
   - Equal Major: inspect and reuse the local package normally.
   - Missing or invalid local PackageIndex: do not infer state by scanning directories.
3. Remote Major greater than Build Major: raise the update event and warning, skip remote package content, then start the complete current-Major local package or built-in baseline.
4. Remote Major lower than Build Major: warn about the publish/channel anomaly and use the same local/baseline fallback without an update event.
5. Remove `HotfixMajorVersionMismatchPolicy`; keep ordinary remote-failure policy unchanged.

## Successful Target Transaction

For repair, forward update, and rollback:

1. Preserve valid target Bundles, reuse only the previous active package, and download the remainder.
2. Persist metadata and verify every Bundle by size and CRC when `FileCRC != 0`.
3. Activate the target.
4. Complete `FinishHotfix()`.
5. For a changed pointer, atomically replace the local PackageIndex.
6. Delete inactive direct `Build_*` children.
7. Raise `OnFinished`.

PackageIndex persistence failure is fatal and must not clean old packages or raise `OnFinished`. Same-package repair does not rewrite an unchanged pointer.

## File and Documentation Changes

- Remove marker reads/writes, startup Unity cache clearing, and Addressables cache deletion.
- Make `FileHelper.ReplaceFile` use `File.Replace` when the destination exists and `File.Move` otherwise; atomic text/byte writes reuse it and delete failed temporary files.
- Replace the compact Hotfix flow table with one comprehensive Mermaid chart covering startup Major relations, remote failure, both remote Major directions, direct activation, repair, update/rollback, fallback, fatal paths, pointer persistence, cleanup, and `OnFinished`.
- Align Hotfix, versioning, FileHelper docs, the plan queue, and progress.
- Check Wrangler Pages CLI support for renaming the existing project to FYAsset. Do not emulate rename by creating/deleting projects.

## Verification

- `dotnet build XLuaHotfix.sln --no-restore`
- `dotnet run --project tests/scenario/HotfixRuntimeStateMachineTests.csproj --no-restore`
- Disposable-directory checks for atomic replace and missing/existing destination behavior.
- Static checks for removed marker/policy/cache-clear symbols and correct finalization order.
- Markdown fence, Mermaid presence, relative-link checks, and `git diff --check`.
- Run safe local Unity acceptance when the existing smoke entry can isolate persistentData and localhost; otherwise record the exact pending cases.

## Verification Result

- `dotnet build XLuaHotfix.sln --no-restore` completed with 0 errors and the 2 existing `System.Net.Http` warnings.
- The focused state-machine scenario and Python syntax check passed.
- Disposable directories verified atomic replacement, missing-source safety, temporary-file cleanup, and same-size CRC mismatch detection with `FileCRC == 0` compatibility.
- All 19 Markdown documents passed link and fence checks; the Hotfix Mermaid chart passed required-branch and node-reference checks.
- Static checks confirmed the marker, configurable Major policy, startup cache clearing, and obsolete cleanup symbols are absent from runtime code.
- Unity update/rollback/repair cleanup E2E remains pending because this pass did not start a localhost server or mutate Unity persistentData.

## Cloudflare CLI Result

Wrangler 4.110.0 exposes Pages project `list`, `create`, and `delete`, but no rename operation. The existing project remains `my-game-xlua-hotfix`; no create/delete workaround or cloud mutation was performed. Rename to `FYAsset` remains a manual Cloudflare follow-up, and `FYAssetSettings.ProjectName` must continue matching the actual project slug until then.

## Boundaries

- Offline Full-package delivery is not introduced.
- AA/AB manager APIs, `IHotfixPipeline`, BuildIndex, PackageIndex, manifest formats, and `OnClientUpdateRequired` remain unchanged.
- Existing legacy `build_guid.txt` files are ignored, not migrated.
