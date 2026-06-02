# Review Hardening Plan 2026-06-02

> **Status**: Approved / In progress
> **Origin**: Active review items from `review-build-repository-batch-20260524.md` and `review-fyasset-full-20260531.md`
> **Approval**: Developer approved execution of the five confirmed items on 2026-06-02

## Summary

Fix five confirmed review items without changing build artifact ownership or package distribution semantics.

## Scope

1. Repository HEAD handling follows git-style semantics: missing HEAD is no usable HEAD; malformed HEAD is an explicit error.
2. Repository Push remains simple package publication: replace the built package on the target and align local repository push history; do not reinterpret package-internal `PackageIndex.json`.
3. AB manifest generation rejects duplicate physical bundle names.
4. Scene logical bundle names must not map to multiple physical outputs; fail fast if the current `Scene -> PackSeparately -> short GUID` invariant is violated.
5. `TaskExportLocalBuildData` documents its Full-only `OutputPath` read despite static `ReadKeys`.

## Implementation Checklist

- [x] Add repository HEAD error API so HEAD-dependent operations can fail explicitly instead of treating corrupted HEAD as empty.
- [x] Change local Push target to replace the target package directory with the built package directory as a whole, then update PushHistory after success.
- [x] Keep AB Full Build recording current artifacts without reading HEAD; Hotfix/Diff Preview still require repository HEAD.
- [x] Add build-time duplicate bundle and scene logical/physical output validation in `TaskGenerateManifest`.
- [x] Add runtime duplicate `ManifestBundleEntry.BundleName` validation in `ABManifest.Initialize()`.
- [x] Document the `TaskExportLocalBuildData.ReadKeys` conditional-read limitation.
- [x] Record the repeated review mistake in `context/mistakes/implementation-pitfalls.md`.
- [x] Update progress and review status, then run verification.

## Acceptance

- Repository status distinguishes empty HEAD from malformed HEAD.
- Diff/hotfix tasks that require HEAD receive an explicit repository error when HEAD is malformed.
- Push does not delta-copy individual bundles; target package directory is replaced by the already built package directory.
- Duplicate bundle names and scene logical-output collisions fail before manifest publication.
- `dotnet build XLuaHotfix.sln --no-restore` passes or any failure is reported with evidence.
