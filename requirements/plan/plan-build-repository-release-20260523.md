# Plan: Build Repository Release & Push

> Date: 2026-05-23
> Status: Awaiting Sign-off
> Source Draft: `requirements/plan/drafts/draft-build-repository-20260518.md`
> Depends on: `plan-build-repository-core-20260523.md` (Plan 2 must be in place; HEAD/objects/JsonUtility/atomic write/channelKey already exist)
> Scope: Plan 3 of repository series. Activates the AB Push path, removes the Plan 2 release placeholder, and adds Repository CLI plus push-history surfacing in `BuildPipelineWindow`.

## Summary

Plan 2 made every successful build a repository commit but kept release a placeholder. Plan 3 turns repository commits into something a CI/CD pipeline can actually publish: a manual, repeatable Push that copies a delta of bundle files + the existing AB/Package manifests into a configurable PushTarget, records the push in the repository, and exposes the same surface to both the editor and the CLI.

Plan 3 is AB-only on the Push side. AA Push needs commit-level Addressables catalog metadata that does not exist yet and is split out into its own follow-up plan.

## Confirmed Decisions

- "Successful Build = commit" stays. Push is a separate manual operation; there is no Staged/HEAD double pointer and no automatic post-build push.
- Tags / `published.json` are out. Release status of a commit is derived purely from `PushHistory.json`.
- `ConfirmReleaseHotfix` and command-line `-confirmRelease` are deleted (the Plan 2 placeholder is not promoted, it is removed).
- Push is supported only for AB in Plan 3. AA Push is deferred to a separate plan because it requires extending the AA commit with a GUID -> bundle map / catalog reverse lookup, plus hotfix group naming knowledge.
- `IPushTarget` is introduced as an abstraction; Plan 3 ships exactly one implementation: `LocalDirectoryPushTarget`. CDN SDK targets are out of scope.
- `PushTarget` configuration lives on `FYAssetSettings` (`List<PushTargetConfig> PushTargets`). Each config has `Id`, `Type` (LocalDirectory in Plan 3), and `Path`. UI shows a dropdown.
- `RepositoryCommit` is extended with `GitCommitHash`, `IsDirty`, and `PackageRootDir`. No other metadata in Plan 3. `SourceDiffSummary`, environment fingerprint, and free-form description are out, recorded as follow-ups.
- `fromVersion` is mandatory on every Push call. The repository never silently picks a baseline. CLI/UI must collect it explicitly.
- The repository computes the artifact delta. PushTarget receives a concrete file list and a small target manifest plan, not raw commits, so all delta logic stays in one place.
- PushTarget does not write any business metadata. `PushHistory.json` is written by the repository at `BuildData/Snapshots/{BuildTarget[-Channel]}/{BackendMode}/PushHistory.json` after the target reports success.
- PushTarget output directory must stay clean: the only files Push deposits there are the delta bundle files, the (full) `ABManifest.json` for the new package, and the updated `PackageIndex.json` at the target root. No PushHistory or other repository bookkeeping leaks into the target.
- `PackageIndex.json` at the PushTarget root is rewritten on every successful push to point at the freshly published package directory, matching `HotfixManager`'s existing download contract.
- `BuildRepositoryCLI` exposes `Status`, `Diff`, `Push`, `ListCommits`. `Commit` is not exposed - commits happen as a side effect of `BuildCommandLine`'s build invocation. `Reset` is not exposed because there is no Staged state.
- The Repository tab in `BuildPipelineWindow` adds: Push HEAD button, Push arbitrary commit (history selector), and a PushHistory panel.
- Implementation comments use Chinese explanations with English technical terms where they clarify push delta computation, manifest reuse, atomic `PackageIndex.json` rewrite, or PushHistory append semantics.

## Key Changes

### Commit metadata extension

Extend `RepositoryCommit` (added in Plan 2) with the minimum fields Push needs:

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

    // Plan 3 additions
    public string GitCommitHash;     // git rev-parse HEAD at build time, empty when repo absent
    public bool IsDirty;             // working tree dirty at build time
    public string PackageRootDir;    // absolute or BuildOutputRoot-relative path to the built package directory
}
```

`PackageRootDir` is filled by `BuildProjectManager` from `BuildPackageRequest.OutputDir`. Older commits written before Plan 3 may have it empty; Push refuses to operate on a commit without it and reports a clear error.

### IPushTarget abstraction

```csharp
public interface IPushTarget
{
    string Id { get; }
    PushReceipt Push(PushPayload payload);
}

public sealed class PushPayload
{
    public RepositoryCommit FromCommit;       // baseline; never null in Plan 3
    public RepositoryCommit ToCommit;
    public IReadOnlyList<string> DeltaBundleFiles; // absolute paths inside ToCommit.PackageRootDir
    public string AbManifestPath;             // absolute path to ToCommit's ABManifest.json
    public string PackageIndexPath;           // absolute path to ToCommit's PackageIndex.json
}

public sealed class PushReceipt
{
    public bool Success;
    public string TargetId;
    public string TargetLocation;             // for LocalDirectory: absolute output dir
    public string PushedAtUtc;
    public string FailureReason;              // empty on success
}
```

`LocalDirectoryPushTarget` implementation rules:

- Resolve `Path` from `PushTargetConfig.Path` at construction.
- Verify `Path` exists (or create it) before any write.
- Copy each file in `DeltaBundleFiles` into `{Path}/{ToCommit.PackageName}/bundles/` preserving original file names. Use `FileHelper` copy + atomic rename if available; fall back to `FileHelper.WriteAllBytesAtomic` semantics for individual files.
- Copy `AbManifestPath` to `{Path}/{ToCommit.PackageName}/ABManifest.json` (full manifest, atomic write).
- Update `{Path}/PackageIndex.json` atomically: `LatestPackage = ToCommit.PackageName`, `LatestVersion = ToCommit.Version`. Read-modify-write through `FileHelper.WriteAllTextAtomic`.
- Return a `PushReceipt` with `TargetLocation = {Path}`.

### Push orchestration in `IBuildRepository`

Extend the Plan 2 interface:

```csharp
public interface IBuildRepository
{
    // Plan 2
    RepositoryStatus GetStatus(string channelKey);
    RepositoryCommit GetHeadCommit(string channelKey);
    List<RepositoryCommit> ListCommits(string channelKey);
    ArtifactDelta DiffHead(string channelKey, IReadOnlyList<ArtifactDigest> artifacts);
    void Commit(RepositoryCommit commit);

    // Plan 3
    PushReceipt Push(string channelKey, VersionNumber fromVersion, VersionNumber toVersion, IPushTarget target);
    List<PushHistoryEntry> ListPushHistory(string channelKey);
}
```

`Push` rules:

- Reject when `BackendMode` segment of `channelKey` is `AA`. AA Push is out of scope.
- Reject when `fromVersion` is null/empty. The caller must supply it explicitly.
- Load `fromCommit` and `toCommit`. Reject if either is missing.
- Reject if `toCommit.PackageRootDir` is empty or unreadable.
- Compute `delta = ArtifactDiffer.Diff(fromCommit.Artifacts, toCommit.Artifacts)`. `delta.Removed` is informational; only `Added + Modified` produce file copies.
- Map each `Added/Modified` artifact `Name` (BundleName for AB) to `{toCommit.PackageRootDir}/bundles/{Name}` (or the actual extension produced by AB). Reject if any mapped file is missing.
- Build a `PushPayload` and call `target.Push(payload)`.
- On `PushReceipt.Success == true`, append a new `PushHistoryEntry` to `BuildData/Snapshots/{channelKey}/PushHistory.json` through `FileHelper.WriteAllTextAtomic` (read-modify-write).
- On failure, do not write `PushHistory.json`. Surface the receipt's `FailureReason`.

```csharp
[Serializable]
public sealed class PushHistoryEntry
{
    public string FromVersion;
    public string ToVersion;
    public string TargetId;
    public string TargetLocation;
    public string PushedAtUtc;
    public int DeltaFileCount;
}
```

`PushHistory.json` is one array per channelKey. Read-modify-write under `FileHelper.WriteAllTextAtomic` is sufficient because writers are serialized through the editor/CLI invocation; no multi-process locking.

### Removing the Plan 2 release placeholder

- Delete `BuildProjectManager.ConfirmReleaseHotfix()` and any caller of it that would have invoked the placeholder.
- Delete the `-confirmRelease` switch from `BuildCommandLine`.
- Delete the corresponding UI button from the legacy build window if it still exists.
- `DifferentialProcessor.ConfirmRelease()` from Plan 1 already lost its meaning when Staged was removed in Plan 2; remove it as well.

### FYAssetSettings push-target configuration

Add to `FYAssetSettings`:

```csharp
[Serializable]
public sealed class PushTargetConfig
{
    public string Id;
    public PushTargetType Type;        // LocalDirectory only in Plan 3
    public string Path;
}

public List<PushTargetConfig> PushTargets = new();
```

`SettingsPanel` gains a small list editor. A default `LocalDirectoryPushTarget` config is auto-created at first run pointing at `{BuildOutputRoot}/PushTargets/local/` so the path stays inside an existing build-related directory tree.

### BuildRepositoryCLI

New file `Assets/FYAsset/Scripts/Build/Repository/Editor/CLI/BuildRepositoryCLI.cs`. Each method parses `Environment.GetCommandLineArgs()` for key-value flags (`-channel`, `-backend`, `-from`, `-to`, `-target`).

```csharp
public static class BuildRepositoryCLI
{
    public static void Status();         // -channel -backend
    public static void Diff();           // -channel -backend -from -to
    public static void Push();           // -channel -backend -from -to -target
    public static void ListCommits();    // -channel -backend
}
```

- `Status`: prints HEAD version, commit count, last push entry, all to stdout.
- `Diff`: prints `ArtifactDelta` Added/Modified/Removed counts and names. Optional `-json=<path>` writes a JSON dump.
- `Push`: parses fromVersion/toVersion/targetId, looks up `IPushTarget` from `FYAssetSettings.PushTargets`, calls `IBuildRepository.Push`, prints `PushReceipt` to stdout, exit code 0 on success, non-zero on failure.
- `ListCommits`: prints version, timestamp, packageName per commit.

CLI must not call into the diff module's editor-only Unity APIs except where strictly required for resolving paths (`AssetDatabase` is allowed because the CLI runs through `-batchmode -executeMethod` inside Unity).

### Editor UI: Repository tab additions

Inside the Plan 2 Repository tab in `BuildPipelineWindow`, add three regions:

- **Push HEAD**: PushTarget dropdown + `fromVersion` field (defaults empty - user must pick) + Push button. Push HEAD is just "toVersion = current HEAD".
- **Push arbitrary commit**: history selector listing `IBuildRepository.ListCommits` + same `fromVersion`/PushTarget controls + Push button.
- **PushHistory**: read-only list driven by `IBuildRepository.ListPushHistory`. Columns: `PushedAtUtc`, `FromVersion`, `ToVersion`, `TargetId`, `TargetLocation`, `DeltaFileCount`.

UI is intentionally explicit about `fromVersion`. There is no "auto-pick last push" affordance in Plan 3.

## Out of Scope

- AA Push. Tracked as a follow-up plan because it requires `RepositoryCommit` AA-specific extensions plus catalog reverse lookup.
- CDN SDK push targets (Aliyun OSS / Tencent COS / S3 / etc).
- Push history aggregation across channels.
- Concurrent multi-process push coordination (file locking).
- `SourceDiffSummary`, build environment fingerprint, commit description.
- Auto-push on build success.
- Reintroducing Staged / Index / soft-reset semantics.
- Repository-side garbage collection of orphan objects (still inherited from Plan 2 follow-ups).

## Verification

Static checks:

- `BuildProjectManager.ConfirmReleaseHotfix` and `-confirmRelease` are gone from the codebase.
- `RepositoryCommit` now has `GitCommitHash`, `IsDirty`, `PackageRootDir` and remains `[Serializable]`-clean for `JsonUtility`.
- `IPushTarget` has only one implementation in Plan 3 (`LocalDirectoryPushTarget`).
- `IBuildRepository.Push` rejects AA `channelKey`, missing `fromVersion`, missing `toCommit.PackageRootDir`, and missing mapped delta files.
- `LocalDirectoryPushTarget` writes only delta bundles, the full `ABManifest.json`, and the rewritten `PackageIndex.json` at the target root. No `PushHistory.json` is written under the PushTarget directory.
- `BuildData/Snapshots/{channelKey}/PushHistory.json` is appended only after `PushReceipt.Success == true` and is written through `FileHelper.WriteAllTextAtomic`.
- `BuildRepositoryCLI` exposes only `Status`, `Diff`, `Push`, `ListCommits`. No `Commit`/`Reset`/`Tag`.

Build:

- Run `dotnet build XLuaHotfix.sln`. Expected: 0 errors, only the existing `System.Net.Http` warnings unchanged.

Behavior scenarios:

| Scenario | Expected result |
| --- | --- |
| AB Push with valid `fromVersion` | Delta bundles + full ABManifest land under `{TargetPath}/{PackageName}/`; PackageIndex updated; PushHistory appended |
| AB Push with `fromVersion` empty | Push refuses with explicit error; PushHistory unchanged |
| AB Push when PackageRootDir is missing on disk | Push refuses; PushHistory unchanged |
| AB Push when a delta bundle file is missing | Push refuses before invoking PushTarget; no partial copy left in target |
| AA Push attempt | Push refuses with "AA Push not supported in this version" message |
| Push then read PushHistory via CLI/UI | Both surface the same entry, identical fields |
| Successful build then immediate Push HEAD with `from=previous HEAD` | Standard happy path; PackageIndex on target points at the new package directory |
| Editor: open Repository tab without any prior pushes | PushHistory panel is empty but does not error |
| Removed `-confirmRelease` | BuildCommandLine treats it as unknown switch (or simply has no handler); release-style behavior is unreachable |

## Documentation And Workflow

- Record execution progress in `requirements/progress.txt` per project format.
- After implementation, update `context/architecture/resource-build-and-release.md` with verified Push facts (PushTarget abstraction, manifest reuse, PackageIndex rewrite contract, PushHistory location).
- Do not put plan sequencing or TODOs into `context/`.
- After Plan 3 lands, archive the parent draft `draft-build-repository-20260518.md` only if both Plan 2 and Plan 3 are executed and AA Push has its own follow-up plan filed (per draft "leave a trace" rule).

## Follow-ups (Not Plan 3)

- AA Push: extend `RepositoryCommit` with AA GUID -> bundle mapping or invoke Addressables catalog reverse lookup; reuse the same `IPushTarget` and `PushHistory` contracts.
- CDN SDK push targets (`AliyunOssPushTarget`, etc).
- `SourceDiffSummary`, build environment fingerprint, commit description metadata.
- Concurrent push coordination (file locking) when multiple machines share `BuildData/Snapshots/`.
- Optional `published` derived view (UI badge) computed purely from `PushHistory.json`.

## Approval Checklist

- [ ] Plan 3 ships only AB Push; AA Push is explicitly deferred and filed as follow-up.
- [ ] `RepositoryCommit` extension is limited to `GitCommitHash`, `IsDirty`, `PackageRootDir`.
- [ ] `IPushTarget` lands with one implementation: `LocalDirectoryPushTarget`. CDN is not implemented.
- [ ] PushTarget configuration lives on `FYAssetSettings.PushTargets`.
- [ ] `fromVersion` is mandatory on every push; repository does not auto-pick a baseline.
- [ ] PushHistory is written at `BuildData/Snapshots/{channelKey}/PushHistory.json`; nothing leaks into the PushTarget directory.
- [ ] PushTarget directory only contains delta bundles, full `ABManifest.json`, and updated `PackageIndex.json`.
- [ ] CLI exposes `Status`, `Diff`, `Push`, `ListCommits` only. No `Commit`/`Reset`/`Tag`.
- [ ] `ConfirmReleaseHotfix` and `-confirmRelease` are deleted.
- [ ] Comments use Chinese descriptions plus English technical terms where helpful.

## Progress Log

Execution progress will be recorded in `requirements/progress.txt` after the developer signs off Plan 3 kickoff.
