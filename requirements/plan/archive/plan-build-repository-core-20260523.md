# Plan: Build Repository Core

> Date: 2026-05-23
> Status: Archived — 2026-05-24; executed and superseded by the shipped repository batch
> Source Draft: `requirements/plan/drafts/draft-build-repository-20260518.md`
> Scope: Plan 2 of 2. Plan 1 extracted the artifact diff module; this plan adds repository storage, HEAD commits, status, and read-only diff preview.

## Summary

Implement the filesystem-backed Build Repository core and replace the legacy `BuildSnapshots` ScriptableObject as the authoritative HEAD source.

Every successful Full or Hotfix Build becomes a repository commit. Diff Preview is a read-only operation that can run at any time and must never commit or mutate repository HEAD. Push, release publishing, tags, and full repository CLI commands are deferred.

## Confirmed Decisions

- Repository storage uses JSON via Unity `JsonUtility` in Plan 2. Do not introduce binary serializers for repository snapshots. Migration to `Newtonsoft.Json` is recorded as a follow-up, not a Plan 2 deliverable.
- Successful Build equals commit. Release/push is future work.
- Do not persist `INDEX.json`; Diff Preview uses in-memory snapshots.
- Repository physical root is `BuildData/Snapshots/` at the project root and is checked into version control. Do not place it under `HotfixOutput/` (build output) or `Library/` (local cache).
- The repository channel key combines `BuildTarget`, optional `VersionNumber.Channel`, and `BackendMode`. AA and AB are fully isolated repository spaces inside the same channel; mixing AA and AB into one HEAD is not supported.
- AA repository HEAD stores source asset digests from `AddressableSourceArtifactScanner`.
- AB repository HEAD stores output bundle digests from `AbBundleOutputArtifactScanner`.
- The legacy `BuildSnapshots` ScriptableObject and `AssetSnapshot` are deleted in Plan 2. AA hotfix prepare reads HEAD through `IBuildRepository.GetHeadSnapshot` instead of `SnapshotAdapter.HeadToDigests`.
- Existing `BuildSnapshots.asset` data is not migrated. Initial repository HEAD is rebuilt by the next Full Build for each channel.
- Existing `ConfirmReleaseHotfix` and `-confirmRelease` stay available but become placeholder messages and must not mutate HEAD.
- Repository writes go through `FileHelper.WriteAllTextAtomic` (temp + rename). For a commit, write `objects/{Version}.json` first, then atomically swap `HEAD.json`. If HEAD swap fails after the object is written, log a warning and leave the orphan object in place; do not roll back the object file. Orphan GC is out of scope for Plan 2.
- AB Diff Preview uses a single-shot temporary directory under `Temp/BuildRepositoryPreview/{guid}/`. The preview path must delete that directory in a `finally` block whether the preview succeeded or failed.
- AB Diff Preview reaches the partial graph through a stop-after-task / whitelist parameter on `DAGScheduler.Execute`. Diff Preview must not introduce a separate preview task graph asset and must not rely on each tail task self-skipping by reading a context flag.
- Comments added during implementation should use Chinese explanations with English technical terms where they clarify repository state, atomic write, Diff Preview, or temporary AB preview behavior.

## Key Changes

### Repository model and storage

Add a single file-based `IBuildRepository` implementation under the build snapshot/repository area.

Repository storage layout:

```text
BuildData/Snapshots/
  {BuildTarget}[-{Channel}]/
    {BackendMode}/
      HEAD.json
      objects/
        {Version}.json
```

Channel key rules:

- `Channel` is omitted when `VersionNumber.Channel` is empty; otherwise the directory is `{BuildTarget}-{Channel}` (hyphen-joined, matching `VersionNumber.GetFullVersionString`).
- `BackendMode` is the canonical AA/AB enum string from `FYAssetSettings.UseABBackend`.
- The string form `"BuildTarget[-Channel]/BackendMode"` is the in-memory `channelKey` exposed by `IBuildRepository`.

Required DTOs:

```csharp
[Serializable]
public sealed class RepositoryCommit
{
    public VersionNumber Version;
    public string ChannelKey;
    public string BackendMode;
    public string BuildTarget;
    public string PackageName;
    public string CreatedAtUtc;
    public List<ArtifactDigest> Artifacts;
}

[Serializable]
public sealed class RepositoryHeadState
{
    public string HeadVersion;
}

[Serializable]
public sealed class RepositoryStatus
{
    public string ChannelKey;
    public bool HasHead;
    public string HeadVersion;
    public string PackageName;
    public int ArtifactCount;
}
```

`RepositoryHeadState` only stores `HeadVersion`. The on-disk path of the HEAD snapshot is always derived from layout (`objects/{HeadVersion}.json`) so the two representations cannot drift.

`ArtifactDigest` (Plan 1) gains `[Serializable]` so `JsonUtility` can round-trip it inside `RepositoryCommit.Artifacts`.

All persistent writes must go through `FileHelper.WriteAllTextAtomic`. Missing or invalid HEAD must report explicit status or diagnostics; do not silently fall back to a fake valid state. A HEAD whose `HeadVersion` points to a non-existent object file must be reported as malformed, not as empty.

### Repository API

Add `IBuildRepository` with the minimum Plan 2 API:

```csharp
public interface IBuildRepository
{
    RepositoryStatus GetStatus(string channelKey);
    RepositoryCommit GetHeadCommit(string channelKey);
    List<RepositoryCommit> ListCommits(string channelKey);
    ArtifactDelta DiffHead(string channelKey, IReadOnlyList<ArtifactDigest> artifacts);
    void Commit(RepositoryCommit commit);
}
```

Rules:

- `Commit` writes `objects/{Version}.json` atomically, then atomically updates `HEAD.json`.
- `DiffHead` compares the provided in-memory artifact list against current HEAD and does not write files.
- `GetHeadCommit` must distinguish "no HEAD yet" from malformed HEAD.
- Repository has one implementation; AA/AB differences enter through scanners, the per-channelKey `BackendMode` segment, and `RepositoryCommit.BackendMode`.
- A `Commit` whose `BackendMode` does not match its `channelKey` segment must be rejected before any file is written.

### Build integration

Update `BuildProjectManager` so a successful build commits to the repository after backend completion:

| Backend | Commit artifact source |
| --- | --- |
| AA | `AddressableSourceArtifactScanner` source asset digests |
| AB | `AbBundleOutputArtifactScanner` bundle output digests |

Full Build and Hotfix Build both commit on success. The orchestrator builds `channelKey` from `BuildTarget` + `VersionNumber.Channel` + `BackendMode` and writes it into the commit. If the scanner or commit fails after backend success, surface a clear error and do not claim repository HEAD was updated.

Replace the legacy snapshot read paths in build/release flow:

- `DifferentialProcessor.PrepareHotfix` and `RebuildSnapshots` read HEAD through `IBuildRepository.GetHeadCommit` instead of `SnapshotAdapter.HeadToDigests`.
- Delete `BuildSnapshots`, `BuildSnapshot`, and `AssetSnapshot`. Remove `FYAssetSettings.SnapshotAssetPath` if it has no remaining consumer.
- Trim `SnapshotAdapter` so only the digest <-> Plan 1 transition helpers actually used during AA group movement remain. Delete `HeadToDigests`.

### Diff Preview

Add a Repository tab/region inside `BuildPipelineWindow`. Selecting a different Editor window for repository operations is not in scope.

Required behavior:

- Show current repository channel and HEAD status. The displayed channel is derived from the current `BuildTarget` plus `FYAssetSettings`/UI state; do not introduce a manual channel dropdown in Plan 2.
- Provide a Diff Preview button.
- AA preview scans current Addressable source assets and compares them with repository HEAD.
- AB preview runs the AB DAG only far enough to produce bundle/manifest-level artifact digests, compares against HEAD, and does not commit.
- AB preview must use a fresh `Temp/BuildRepositoryPreview/{guid}/` directory and must not update package index, HEAD, release state, or persistent repository objects. The preview path deletes that directory in a `finally` block on both success and failure.

Extend `DAGScheduler.Execute` with a stop-after-task / task whitelist parameter so AB Diff Preview can stop before final package organization, package index writes, release exports, and repository commit. Do not add a parallel preview-only task graph asset. Do not let tail tasks self-skip by reading a context flag.

### Release placeholder

Update `ConfirmReleaseHotfix` and command-line `-confirmRelease` behavior:

- Log or display that release/push is deferred to a future plan.
- Do not promote staged state.
- Do not update repository HEAD.
- Do not delete build artifacts or repository files.

## Out of Scope

- Persistent `INDEX.json`
- Push targets, CDN upload, `IPushTarget`, and published tags
- Full repository CLI command set
- Binary repository serializers and `Newtonsoft.Json` migration
- Migrating existing `BuildSnapshots.asset` head/staged data
- Orphan object garbage collection (when an object is written but HEAD swap fails)
- Manual channel selection in the Repository UI
- AB source-side differential build filtering
- Automatic release/published-state tracking

## Verification

Static checks:

- No repository code uses binary serializer attributes or generated serializers.
- No persistent repository write bypasses `FileHelper.WriteAllTextAtomic`.
- No Plan 2 code writes `INDEX.json`.
- `ConfirmReleaseHotfix` and `-confirmRelease` do not mutate `HEAD.json`.
- Diff Preview code calls read-only repository APIs and does not call `Commit`.
- AA commit path uses source asset digests; AB commit path uses output bundle digests.
- `BuildSnapshots`, `BuildSnapshot`, and `AssetSnapshot` are removed from the codebase, including `.csproj` compile entries.
- `SnapshotAdapter.HeadToDigests` is removed; remaining helpers in `SnapshotAdapter` are still in use or the file itself is removed.
- AB Diff Preview deletes its `Temp/BuildRepositoryPreview/{guid}/` directory whether the preview succeeds or fails.
- `IBuildRepository.Commit` rejects mismatched `BackendMode` versus `channelKey`.

Build:

- Run `dotnet build XLuaHotfix.sln`.
- Expected result: 0 errors. Existing `System.Net.Http` conflict warnings are acceptable if unchanged.

Behavior scenarios:

| Scenario | Expected result |
| --- | --- |
| Empty repository status | UI/API reports no HEAD for the selected channel without fabricating a valid commit |
| Successful AA Full Build | AA source digest commit is written to `objects/`, and `HEAD.json` points to that version under `{BuildTarget}[-Channel]/AA/` |
| Successful AA Hotfix Build | AA source digest commit is written after backend success |
| Successful AB Build | AB bundle output digest commit is written under `{BuildTarget}[-Channel]/AB/` |
| Same channel switching backend | AA HEAD and AB HEAD are isolated; switching does not corrupt the other side |
| AA Diff Preview | Computes delta against HEAD without writing repository files |
| AB Diff Preview | Uses temporary preview artifacts under `Temp/BuildRepositoryPreview/{guid}/`, computes delta, leaves HEAD/package index untouched, deletes the temp directory |
| AB Diff Preview failure | Temporary directory is still deleted in the `finally` path |
| ConfirmRelease placeholder | Displays/logs deferred release message and leaves HEAD unchanged |
| Malformed HEAD | Reports an explicit error instead of silently treating it as empty repository |
| Object written then HEAD swap fails | Object file remains as orphan, warning is logged, repository continues to report previous HEAD |

## Documentation And Workflow

- Record execution progress in `requirements/progress.txt` using the project progress format.
- After implementation, update `README.md` only for real user-facing capability changes.
- Update `context/architecture/resource-build-and-release.md` with verified repository facts after code is complete, including the storage layout and the AA/AB isolation rule.
- Keep plan sequencing, TODOs, and workflow text out of `context/`.

## Follow-ups (Not Plan 2)

- Re-evaluate `Newtonsoft.Json` for repository serialization (cleaner handling of plain DTOs and nullable fields than `JsonUtility`). Track as a separate plan.
- Orphan object GC for the case where an object file was written but HEAD swap failed.
- Repository CLI command set, push targets, and tag mechanism (per draft Open Questions Q2-Q4).

## Approval Checklist

- [x] AA repository HEAD stores source asset digests, not Addressables output bundle digests.
- [x] Plan 2 does not persist `INDEX.json`.
- [x] Successful Build commits automatically; Diff Preview is always read-only.
- [x] Repository physical root is `BuildData/Snapshots/` at the project root and is version-controlled.
- [x] `channelKey` includes `BackendMode`; AA and AB are isolated within the same `BuildTarget[-Channel]`.
- [x] Repository serialization uses `JsonUtility` in Plan 2; `Newtonsoft.Json` is logged as a follow-up only.
- [x] Legacy `BuildSnapshots` SO and `AssetSnapshot` are deleted; existing SO data is not migrated.
- [x] HEAD swap is atomic; orphan objects after a failed HEAD swap are tolerated, not rolled back.
- [x] AB Diff Preview uses `Temp/BuildRepositoryPreview/{guid}/` with `finally`-block cleanup and reaches the partial DAG via `DAGScheduler.Execute` stop-after-task / whitelist.
- [x] Repository UI lives in a `BuildPipelineWindow` tab; channel is derived from build target and settings, no manual dropdown in Plan 2.
- [x] `ConfirmReleaseHotfix` and `-confirmRelease` become placeholders and do not mutate HEAD.
- [x] Push, tag, full repository CLI, and CDN publishing remain out of Plan 2.
- [x] Implementation comments use Chinese descriptions plus English technical terms where helpful.

## Progress Log

Execution progress will be recorded in `requirements/progress.txt` after the developer signs off the next sub-plan kickoff.
