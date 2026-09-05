# Windows Hotfix State Machine Convergence

## Status

Executed / Verified main scenarios / Archived 2026-07-14.

Post-implementation findings remain active in
`requirements/review/review-project-plan-context-alignment-20260713.md`; they are review follow-ups, not unfinished plan execution.

## Goal

Make Windows startup deterministic around a verified built-in baseline, a required local `PackageIndex`, forward-only same-Major hotfixes, destructive cleanup of failed targets, and pointer persistence only after runtime initialization succeeds. Simplify the human Hotfix documentation to the resulting decisions instead of implementation-level branches.

## Runtime Contract

1. Validate `BuildIndex`, backend identity, safe path segments, built-in manifest, required metadata, and every built-in Bundle before local or remote decisions. Any failure is fatal.
2. Treat a missing, invalid, or different-Major local `PackageIndex` as repair-required and compare the remote target against the built-in baseline identity. Do not scan or delete historical directories at startup.
3. Treat a local pointer matching both `BuildIndex.BuildGUID` and `BuildIndex.Version` as a baseline placeholder. Its persistent package directory may be empty.
4. A local Hotfix package is launchable only when its exact manifest and required metadata are valid and every Bundle has a safe name, non-negative size, non-zero CRC, matching size, and matching CRC.
5. Remote PackageIndex failure starts complete local content and otherwise fails. A higher remote Major also raises `OnClientUpdateRequired`; a lower remote Major is a publish warning. Neither direction repairs invalid local state.
6. Same identity plus complete local content activates without requesting the remote manifest. A repair-required baseline pointer rewrites only the pointer after runtime initialization. An incomplete same Hotfix package fetches and repairs the target.
7. Within the same Major, only a strictly higher remote release version updates. Lower versions and same-version different package names are publish anomalies and never change the local pointer.
8. Forward updates use a clean target directory, reuse matching `FileHash` Bundles only from the verified built-in baseline or complete active Hotfix package, and download the remainder through temporary files. Every copied or downloaded Bundle is verified by size and CRC.
9. Any target manifest, metadata, Bundle, activation, or initialization failure removes the target directory. A complete previous package may be activated; repair-required local state fails startup.
10. Successful commit order is verify target, activate, initialize PackageManager, atomically persist the pointer when required, clean inactive direct `Build_*` directories, then raise `OnFinished` once.

## Implementation

- Remove `HotfixRemoteFailurePolicy`, `FYAssetSettings.RemoteFailurePolicy`, serialized asset data, and policy-dependent tests.
- Extend `IHotfixPipeline.InspectPackageAsync` and `HotfixPackageInspection` for exact built-in inspection with optional package-directory identity checking.
- Keep one effective local identity and one inspection result; do not add a persisted error marker or scan untrusted package directories.
- Update `HotfixStateDecider` to compare complete package identity and `VersionNumber`, including baseline-pointer repair, forward update, remote rejection, and fatal actions.
- Validate package and Bundle names before path construction or deletion. All destructive paths must remain direct children of `RuntimePathManager.HotfixRoot`.
- Keep existing Android helpers unchanged. Mark changed built-in inspection and reuse entry points with `Android deferred`; implementation and acceptance are Windows-only.
- Replace the comprehensive Hotfix Mermaid graph with one compact flow and decision table. Reduce the scenario document to an acceptance matrix.

## Verification

- Focused state-machine RED/GREEN scenario tests.
- Minimal Windows filesystem inspection checks for missing files, version mismatch, unsafe names, invalid size, zero CRC, size mismatch, and CRC mismatch.
- `dotnet build XLuaHotfix.sln --no-restore` and Unity 2022.3.62f3 Windows batchmode compilation.
- Controlled localhost AA workflow covering baseline pointer repair, cached offline startup, missing/corrupt pointer failure, forward update with baseline Bundle reuse, same-package repair, rollback rejection, same-version replacement rejection, and failed-target cleanup.
- AB compilation and manifest round-trip only; real AB Full/Hotfix acceptance remains separate.
- No Cloudflare Push, package-format change, Android acceptance, commit after implementation, or plan archive before developer sign-off.
