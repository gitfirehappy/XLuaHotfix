# Plan: Build Repository Diff Module

> Date: 2026-05-23
> Status: Archived — 2026-05-24; executed and superseded by the shipped repository batch
> Source Draft: `requirements/plan/drafts/draft-build-repository-20260518.md`
> Scope: Plan 1 of 2. Plan 2 will cover filesystem storage, HEAD/INDEX, commits, tags, channel isolation, CLI/GUI, and `IPushTarget`.

## Summary

Extract artifact diffing out of `DifferentialProcessor` so the diff layer is pure and reusable while the AA legacy group-moving behavior is isolated in one transition container.

AA and AB share `ArtifactDigest`, `ArtifactDelta`, and `IArtifactScanner`, but each backend diffs once at its natural layer:

| Backend | Timing | Granularity | Plan 1 consumer |
| --- | --- | --- | --- |
| AA | pre-build | asset GUID | `DifferentialProcessor.PrepareHotfix()` uses the delta to move changed assets into the Hotfix group |
| AB | post-build | bundle name | no Plan 1 consumer; scanner is provided for Plan 2 push decisions |

This plan does not replace the `BuildSnapshots` ScriptableObject, does not introduce filesystem HEAD/INDEX storage, does not add CLI/GUI entry points, and does not implement AB source-side differential building.

## Confirmed Decisions

- `ArtifactDigest` is a plain DTO in Plan 1. Do not add `[BinarySerializable]` or generated binary serializers until Plan 2 introduces persistent repository storage.
- AA source scanning computes shallow content identity from the main asset file plus its `.meta` file. Implement this through shared `HashGenerator` composite-file helpers, not scanner-local hashing code.
- If a pending Hotfix group undo log exists, `PrepareHotfix()` must fail fast and require `ResetGroupsToOriginal()` / restore before another hotfix prepare can run. Do not auto-merge undo logs.
- Persistent undo-log writes must use `FileHelper.WriteAllTextAtomic`; directory creation, delete, and existence checks should use `FileHelper` where available.
- New scripts must be added to the relevant `.csproj` compile entries used by external `dotnet build` verification.

## Key Changes

### Shared diff model

Add plain runtime DTOs under `Assets/FYAsset/Scripts/Build/Snapshots/`:

```csharp
public class ArtifactDigest
{
    public string Name;   // AA: AssetGUID, AB: BundleName
    public string Hash;   // shallow MD5 content identity
    public long Size;     // bytes
    public uint CRC;      // CRC32 fast verification
}

public class ArtifactDelta
{
    public List<ArtifactDigest> Added = new();
    public List<ArtifactDigest> Modified = new();
    public List<string> Removed = new();
    public bool IsEmpty => Added.Count == 0 && Modified.Count == 0 && Removed.Count == 0;
}

public interface IArtifactScanner
{
    List<ArtifactDigest> Scan();
}
```

Add `ArtifactDiffer.Diff(IReadOnlyList<ArtifactDigest> from, IReadOnlyList<ArtifactDigest> to)` as a pure editor-side utility:

- `to` only -> `Added`
- same `Name` but different `Hash` -> `Modified`
- `from` only -> `Removed`
- no Unity API calls and no side effects

### Hashing

Extend `HashGenerator` with ordered composite-file helpers:

- `GenerateCompositeFileHash(params string[] filePaths)`
- `GenerateCompositeFileCRC(params string[] filePaths)`

The helpers must produce deterministic results from file content in the supplied order. Missing optional `.meta` files should not crash; the missing slot should still be represented deterministically. Missing main asset files are handled by the scanner as an invalid input and must not produce an empty digest.

`HashGenerator.GenerateDeepHash()` stays available for other callers, but no diff-module code may call it.

### Scanners

Add `AddressableSourceArtifactScanner` under `Assets/FYAsset/Scripts/Build/Snapshots/Editor/`:

- Iterate `AddressableAssetSettings.groups`.
- Skip null groups and `"Built In Data"`.
- For each entry, use `entry.guid` as `ArtifactDigest.Name`.
- Compute hash/CRC/size from `AssetDatabase.GUIDToAssetPath(entry.guid)` plus `assetPath + ".meta"`.
- Do not read or write `Address`, `Labels`, `CurrentGroupName`, `OriginalGroupName`, or `RemoteGroupName`.

Add `AbBundleOutputArtifactScanner` under the same editor folder:

- Constructor path 1 receives `IList<ManifestBundleEntry>` and converts `BundleName`, `FileHash`, `FileCRC`, and `FileSize` without recomputing hash/CRC.
- Constructor path 2 receives an output directory and scans actual bundle output files. Do not hard-code only `*.bundle`, because the AB pipeline can output raw files or Unity bundle files without that extension.

### AA legacy group container

Add `LegacyAddressableHotfixGroups` under `Assets/FYAsset/Scripts/Build/Release/Editor/Addressables/`.

Public contract:

```csharp
public static class LegacyAddressableHotfixGroups
{
    public static bool HasPendingMoves { get; }
    public static bool Apply(ArtifactDelta delta);
    public static void Restore();
}
```

Rules:

- `Apply` moves `delta.Added + delta.Modified` GUIDs into `FYAssetSettings.HOTFIX_GROUP_NAME`.
- `Apply` records authoritative original group names at the moment of movement.
- If `HasPendingMoves` is true, `Apply` returns false and logs an error instead of merging logs.
- Undo log path: `Assets/FYAsset/Editor/Generated/HotfixGroupUndoLog.json`.
- Restore reads the undo log and moves entries back to their recorded groups; if the original group no longer exists, use `settings.DefaultGroup` with a warning.
- After successful restore, delete or clear the undo log.

### DifferentialProcessor

Keep the public entry points used by `BuildProjectManager`, but replace internals:

| Method | Required behavior |
| --- | --- |
| `PrepareHotfix(VersionNumber)` | scan current AA digests, read Head via adapter, diff, return false if empty, block if pending undo log exists, apply group moves, write staged snapshot through adapter |
| `RestoreOriginalGroups()` | delegate to `LegacyAddressableHotfixGroups.Restore()` |
| `ConfirmRelease()` | keep staged-to-head behavior |
| `ReBuildSnapShots(VersionNumber)` | scan current AA digests and rebuild Head through adapter |

Remove the old private responsibilities from `DifferentialProcessor`: current project scanning, modified-list diffing, and bundle identifier calculation.

### Snapshot adapter

Add `SnapshotAdapter` under `Assets/FYAsset/Scripts/Build/Snapshots/Editor/`:

- `ToDigests(IList<AssetSnapshot>)`: `Name = AssetGUID`, `Hash = FileHash`, `Size = 0`, `CRC = 0` for legacy SO data.
- `FromDigests(IList<ArtifactDigest>)`: write `AssetGUID` and `FileHash`; set `Address`, `Labels`, `CurrentGroupName`, `OriginalGroupName`, and `RemoteGroupName` to empty values.
- `HeadToDigests(BuildSnapshots)`: return the current Head as digests or an empty list only where the caller explicitly permits no Head.

Keep `BuildSnapshots` and `AssetSnapshot` fields for Plan 1 compatibility. Update `BuildSnapshot.Timestamp` to UTC ISO-8601 to avoid local-time build state.

## Verification

Static checks:

- No diff-module code calls `GenerateDeepHash`.
- `AddressableSourceArtifactScanner` does not use `Address`, `Labels`, `CurrentGroupName`, `OriginalGroupName`, or `RemoteGroupName`.
- The manifest-entry constructor path in `AbBundleOutputArtifactScanner` does not call `GenerateFileHash`, `GenerateFileCRC`, or composite hash helpers.
- `DifferentialProcessor.PrepareHotfix()` does not directly call `settings.MoveEntry`.
- `LegacyAddressableHotfixGroups.Apply()` writes the undo log; `Restore()` reads it.
- New `.cs` files are present in the corresponding `.csproj` compile entries.
- Touched persistent writes use `FileHelper` atomic write helpers.

Build:

- Run `dotnet build XLuaHotfix.sln`.
- If Unity-generated project files are stale, update compile entries explicitly and rerun.

Behavior scenarios:

| Scenario | Expected result |
| --- | --- |
| AA Full | `ReBuildSnapShots()` rebuilds Head with shallow composite hashes and empty legacy group/address/label fields |
| AA Hotfix with changes | Hotfix group receives `delta.Added + delta.Modified`; undo log count matches moved entries |
| AA Hotfix with no changes | No group movement and no undo log write |
| AA Hotfix with pending undo log | Build prepare is blocked and instructs developer to restore first |
| AA Reset | undo log is replayed, entries return to original/default groups, undo log is removed or cleared |
| AB Full/Hotfix | Main DAG path does not call `DifferentialProcessor`; AB scanner can convert `ABManifest.BundleEntries` without recomputing hash/CRC |

## Approval Checklist

- [x] Scope is limited to the diff module; repository storage, HEAD/INDEX, CLI/GUI, and push stay in Plan 2.
- [x] `ArtifactDigest` remains non-binary-serialized in Plan 1.
- [x] AA group moves are isolated in `LegacyAddressableHotfixGroups`.
- [x] Hashing uses shared `HashGenerator` composite helpers for main asset + `.meta`.
- [x] Pending undo log blocks a new hotfix prepare instead of auto-merging.
- [x] `BuildSnapshots` SO remains as a compatibility bridge through `SnapshotAdapter`.
- [x] Developer approved execution on 2026-05-23.

## Progress Log

Execution progress is recorded in `requirements/progress.txt` per project workflow.
