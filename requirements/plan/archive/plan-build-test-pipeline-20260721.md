# FYAsset AA/AB Build Test Pipeline Plan

> **Status**: Completed / Verified / Archived — 2026-07-24  

> **Requirement ID**: build-test-pipeline-20260721  
> **Date**: 2026-07-21  
> **Updated**: 2026-07-22  
> **Scope**: Build-side automated acceptance for AA and AB Full, Hotfix, and Chain, including explicit Target publication, remote identity verification, and always-restore Target/project state

## Goal

Replace repeated manual build acceptance with one CLI surface and shared engine that:

1. exercises the real AA/AB build paths;
2. independently verifies the build transaction on disk;
3. publishes the verified package to one or more explicitly named Push Targets;
4. verifies each Target's published identity remotely;
5. always restores project and Target state after success, failure, cancellation, or recovery.

```text
fyasset-test aa build full|hotfix|chain --target <id> [--target <id>...]
fyasset-test ab build full|hotfix|chain --target <id> [--target <id>...]
```

External Targets additionally require:

```text
--confirm-external-publish <id>
```

The CLI is primary. AA and AB Build Pipeline windows each receive one minimal Test page that invokes the same engine.

Player runtime, Player build, and GameLauncher smoke belong to the companion E2E plan. This plan never starts a Player.

## Confirmed Product Contract

### Entry ownership

1. `aa` and `ab` are the only top-level test entrypoints.
2. The selected entrypoint chooses `AABuildProjectManager` or `ABBuildProjectManager` directly; `UseABBackend` does not redirect the test.
3. CLI batchmode and Editor Test pages call one shared Build Test engine. Rules, isolation, acceptance, publish, and recovery must not be duplicated.
4. No independent `runtime` command exists. Runtime belongs only to the E2E plan.

### Target ownership

1. Every Build mode requires one or more explicit `--target <id>` values.
2. Target IDs are never inferred from:
   - current `HotfixUrl`;
   - Editor selected Target;
   - Target list order;
   - Target type;
   - PublicBaseUrl / Path values.
3. Each ID must resolve through exact case-insensitive lookup in `FYAssetSettings.PushTargets`.
4. Duplicate IDs, missing IDs, empty `Path`, invalid `PublicBaseUrl`, unsupported type, or service-root collisions fail preflight with exit code 7 before any mutation.
5. After successful resolve, the engine freezes a Target Snapshot for the whole run:

```text
TargetId
TargetType
ServiceRoot = ResolveServiceRoot(Path)
BackendPublishRoot = ServiceRoot/{AA|AB}
PublicBaseUrl
RuntimeUrl = PublicBaseUrl/{AA|AB}/
PackageIndexUrl = RuntimeUrl + PackageIndex.json
```

6. Publish destination and Runtime URL for a Target come only from that frozen snapshot. They are not second free-form CLI parameters.
7. External Targets (`CloudflarePages` and any future non-local type) also require matching `--confirm-external-publish <id>`. Missing confirmation fails preflight with exit code 7.
8. Confirmation authorizes the complete publish-to-restore window, not a single upload.

### Build modes

| Mode | Project initial state | Build sequence | Target initial state | Intended use |
|---|---|---|---|---|
| Full | Isolated `1.0.0 / Build 0`, empty selected-backend Repository | Full -> `2.0.0 / Build 1` | Empty or any fully restorable content | Focused Full regression + publish |
| Hotfix | Isolated copy of current valid Full baseline | Fixture change -> Hotfix | Exact matching Full identity on every Target | Focused Hotfix regression + publish |
| Chain | Isolated `1.0.0 / Build 0`, empty selected-backend Repository | Full `2.0.0 / 1` -> fixture change -> Hotfix `2.0.1 / 2` | Empty or any fully restorable content; Full is created by the run | Authoritative build-side acceptance |

Standalone Hotfix fails fast when:

- local Full Repository HEAD / package / StreamingAssets are invalid or inconsistent; or
- any explicit Target does not already expose the exact same Full identity.

It must not silently build a Full package. That is Chain's responsibility.

## Permanent Test Assets

### Shared test domain

```text
Assets/Test/FYAssetPipeline/
  FYAssetPipelineSmokeAsset.cs
  FYAssetPipelineAsync.asset
  FYAssetPipelineSync.txt
  FYAssetPipelineRaw.fyraw
  FYAssetPipelineSmoke.lua
  FYAssetPipelineLuaContainer.asset
```

Logical group name for both backends:

```text
FYAssetPipelineTest
```

These assets permanently join normal Full packages. Tests never temporarily create/remove the Group or collection membership.

### AA membership

Permanent Addressables Group `FYAssetPipelineTest` with explicit entries only:

| Asset | Address | First label |
|---|---|---|
| `FYAssetPipelineAsync.asset` | `FYAssetPipelineAsync` | `FYAssetPipelineSmokeAsset` |
| `FYAssetPipelineSync.txt` | `FYAssetPipelineSync` | `TextAsset` |
| `FYAssetPipelineLuaContainer.asset` | `FYAssetPipelineLua` | `LuaScriptContainer` |

Shared group label: `FYAssetPipelineTest`.  
`FYAssetPipelineRaw.fyraw` is not an AA entry.

### AB membership

Permanent AB Group `FYAssetPipelineTest`:

```text
BundlePackingMode: PackSeparately
Collector:
  CollectPath: Assets/Test/FYAssetPipeline
  CollectPathType: Folder
  CollectorType: Main
  ForcePayloadKind: Auto
  FilterRuleName: CollectAll
  GroupRuleName: GroupAll
```

Real scan results:

| Path | Expected scan result |
|---|---|
| `.cs` | excluded by CollectAll |
| `.asset` SO / Container | Serialized Main |
| `.txt` | Serialized Main |
| `.fyraw` | Auto RawFile Main because no importer -> DefaultAsset |
| `.lua` | Serialized Main; accepted real scan result, also referenced by Container |

No AssetEntry PayloadKind override is used for RawFile. Preflight must assert:

1. Unity main type of `FYAssetPipelineRaw.fyraw` is `DefaultAsset`;
2. scan classification is `RawFile`.

If a future importer makes `.fyraw` serializable, preflight fails instead of silently changing the fixture path.

### Fixed addresses

All four runtime entry assets use `AutoAddress=false` and fixed addresses:

```text
FYAssetPipelineAsync
FYAssetPipelineSync
FYAssetPipelineLua
FYAssetPipelineRaw
```

### Fixture contents

| Asset | Full content | Hotfix content | Role |
|---|---|---|---|
| `FYAssetPipelineAsync.asset` | `Marker=fyasset-pipeline-async:v1` | unchanged | typed async smoke only |
| `FYAssetPipelineSync.txt` | `fyasset-pipeline-sync:v1` | `fyasset-pipeline-sync:v2` | AA Hotfix fixture |
| `FYAssetPipelineRaw.fyraw` | `fyasset-pipeline-raw:v1` | `fyasset-pipeline-raw:v2` | AB Hotfix fixture |
| `FYAssetPipelineSmoke.lua` | `marker=fyasset-pipeline-lua:v1` | unchanged | Lua smoke only |

LuaScriptsIndex must map:

```text
FYAssetPipelineSmoke -> FYAssetPipelineLua
```

### Hotfix Delta contract

```text
AA:
  only the physical Addressables artifact containing FYAssetPipelineSync may change as business payload

AB:
  only the physical RawFile artifact for FYAssetPipelineRaw may change as business payload
  Serialized/Lua artifacts must keep their Full hashes
```

Do not use the old phrase "fixture Bundle" as the universal term. Use **fixture physical artifact**.

## Functional Requirements

### FR-1: Shared Build Test engine

Inputs:

- Backend: `AA` or `AB`
- Mode: `Full`, `Hotfix`, or `Chain`
- Explicit Target ID set
- External confirmation set
- Progress callback
- Log/result root
- CLI-only timeout/cancellation signal

Owns:

```text
preflight
target snapshot freeze
project + target backup
isolated project preparation
production build invocation
independent disk acceptance
publish to every explicit Target
remote identity probe for every Target
restoration of project and Targets
result persistence
stale-run recovery
```

### FR-2: Production path reuse

```text
AA -> AABuildProjectManager -> BuildProjectRunner -> AABuildBackend -> BuildPipelineRunner
AB -> ABBuildProjectManager -> BuildProjectRunner -> ABBuildBackend -> BuildPipelineRunner
```

Publish reuses:

```text
PushTargetUtility.Create / ResolveServiceRoot / ResolveBackendRoot / GetBackendHotfixUrl
LocalDirectoryPushTarget / CloudflarePagesPushTarget
PackagePublishTransaction
LocalHotfixServerController for LocalDirectory only
```

The test layer must not reimplement package generation, Repository commit, PackageIndex meaning, serialization, CRC/hash algorithms, or Push destination layout.

### FR-3: Full acceptance

A Full test passes only when all conditions hold:

1. Every configured Task for the selected backend succeeds. The test does not hardcode Task count/order.
2. `VersionDataBase` advances from `1.0.0 / Build 0` to `2.0.0 / Build 1` only after the build transaction succeeds.
3. Selected backend Repository has exactly the expected Full HEAD with no Parent.
4. Project root PackageIndex identifies selected backend + Full package/version.
5. Final package, Manifest, and every declared physical artifact exist and pass size/CRC/hash validation.
6. StreamingAssets/Bootstrap identify and contain the same Full baseline.
7. AA output satisfies AA artifact contract and contains no AB Manifest/baseline residue.
8. AB output satisfies AB artifact contract and contains no AA Manifest/catalog/baseline residue.
9. Permanent Test Group assets are present and resolve with fixed addresses:
   - both backends: Async SO, Sync Text, Lua Container/module mapping;
   - AB only: RawFile address `FYAssetPipelineRaw` with PayloadKind RawFile.
10. Every explicit Target is published and remotely verified for that Full identity.
11. Project and all Targets restore to their pre-run state.
12. No Player is built or started.

### FR-4: Hotfix acceptance

Standalone Hotfix requires before mutation:

1. Local Full Repository HEAD, package, PackageIndex/BuildIndex, and StreamingAssets form one exact Full identity.
2. Every explicit Target already exposes that exact same Full identity:
   - BackendMode;
   - LatestPackage;
   - LatestVersion;
   - Manifest hash;
   - Full package physical set still present under the Target backend root.
3. Target PackageIndex points at that Full package.

A Hotfix test then passes only when:

1. Backend-specific fixture is the only mutated business asset.
2. Every configured Hotfix Task succeeds.
3. Version advances from copied Full to expected Patch/Build successor.
4. Hotfix Repository HEAD has Full as Parent.
5. Generated package/Manifest are physically complete.
6. Artifact Delta is non-empty and includes the fixture physical artifact.
7. Fixture physical artifact is the only changed business payload artifact. Metadata changes allowed; unrelated payload changes fail.
8. Project PackageIndex points to Hotfix; StreamingAssets remains byte-identical to Full.
9. Every Target is published and verified:
   - PackageIndex now points to Hotfix;
   - Full package still exists on the Target;
   - Hotfix package complete;
   - Manifest/hashes match build result.
10. Project and all Targets restore.
11. No Player is built or started.

### FR-5: Chain acceptance

Chain uses the same real two-phase Target rhythm as E2E, without Player:

```text
global preflight + freeze all Target snapshots
snapshot project + all Target service roots
external Targets: local mirror == public content, or fail

Build Full once
independent Full disk acceptance

for each Target in CLI order:
  publish Full
  remote Full probe
  keep Target at test Full

if any Target Full fails:
  finish remaining Target Full probes if already started
  restore every modified Target with full remote re-verify
  restore project
  stop; do not Build Hotfix

fixture mutation
Build Hotfix once
independent Hotfix disk acceptance

for each Target in CLI order:
  publish Hotfix
  remote Hotfix probe proving Full remains and pointer moved

restore every Target with full remote re-verify
restore project
write result
```

Chain is authoritative build-side acceptance. Full and Hotfix remain focused diagnostics.

### FR-6: Target snapshot and restore

1. Snapshot the entire Target service root, not only the current backend subdirectory. Cloudflare Pages deploys the full service root.
2. All Targets are snapshotted and proven restorable before the first build/publish.
3. Always restore Targets at the end of the run, whether PASS or FAIL.
4. LocalDirectory restore rewrites the service root from snapshot and re-probes PackageIndex/Manifest/package hashes.
5. CloudflarePages restore:
   - restore local service mirror from snapshot;
   - redeploy that restored mirror;
   - re-probe the public PackageIndex/Manifest/package hashes.
6. If pre-run public content and local Cloudflare mirror disagree, fail before any deploy. There is no trusted restore source.
7. Restore failure is exit code 6 and aborts remaining Targets.

### FR-7: Multi-Target failure policy

1. Targets run serially in CLI order.
2. If one Target fails during publish/probe:
   - restore that Target;
   - fully re-verify remote identity against the original snapshot;
   - if restore/re-verify succeeds, continue remaining Targets;
   - if restore/re-verify fails, stop the whole run immediately.
3. Final result is FAIL if any Target failed, even if later Targets passed.
4. First failure remains the primary failure; later Target outcomes are still recorded.

### FR-8: Independent disk acceptance

After production build reports success, the engine reloads package, Manifest, Repository, PackageIndex, StreamingAssets, and Bootstrap from disk and independently compares contracts. Production serializers/hash helpers may be reused; algorithms must not be reimplemented in test-only code.

### FR-9: Complete project build-state isolation

Snapshot/restore only known build state:

- `VersionDataBase`
- Selected backend Repository
- Packages and root PackageIndex
- StreamingAssets and Bootstrap build data
- Selected backend temporary build output and reports
- Fields changed in `FYAssetSettings`, `FYAssetAASettings`, `FYAssetABSettings`
- Selected backend fixture file bytes

Must not use `git stash/reset/clean/checkout`; must not back up the whole project; must not modify unrelated assets/`Library`.

Unrelated dirty-worktree changes are allowed and must survive byte-for-byte. Final Git status must equal initial Git status for non-test-owned paths.

### FR-10: Recovery

Before the first mutation, write durable `recovery.json` describing every owned project/Target path and backup. Normal completion, failure, validation failure, and cancellation all attempt restoration.

If a later invocation finds incomplete recovery:

1. restore only recorded state;
2. verify restoration;
3. exit without starting a new test;
4. require developer inspection before rerun.

Recovery failure preserves backup evidence and never continues into another build/publish.

### FR-11: CLI contract

```text
fyasset-test aa build full --target local
fyasset-test ab build hotfix --target local
fyasset-test aa build chain --target local --target cloudflare --confirm-external-publish cloudflare
fyasset-test ab build chain --target local
```

Non-interactive by default. Prints live stages, elapsed time, PASS/FAIL, key version/artifact counts, per-Target publish/probe/restore status, and log/result paths.

Exit codes:

| Code | Meaning |
|---:|---|
| 0 | Passed |
| 2 | Invalid CLI usage |
| 3 | Precondition failed: Editor lock, invalid Full baseline, modified fixture, missing Target Full for Hotfix |
| 4 | Unity or build pipeline failed |
| 5 | Independent project-disk acceptance failed |
| 6 | Restoration failed |
| 7 | Target config/snapshot/preflight failed |
| 8 | Publish or remote probe failed |
| 9 | Reserved for E2E Player/runtime failure; Build never emits 9 |
| 130 | User interrupted |

CLI preflight fails immediately when the same project is open in Unity Editor. It does not wait, kill the Editor, or copy the project. Timeout/Ctrl+C may terminate only processes started by the run, then restoration executes.

### FR-12: Minimal Unity Test pages

AA and AB Build Pipeline windows each add one backend-owned Test page:

- Mode buttons: Full / Hotfix / Chain
- Explicit Target toggles sourced only from configured Push Targets; default none selected
- No separate external-confirmation toggle; selecting an external Target prompts once when the action starts
- Current stage / Busy
- Last PASS/FAIL and key summary
- Open Log / Open Result

No Target selected means the action is disabled. The page never infers "current Target".

Pages invoke the shared engine in the current Editor. They are the sole manual test entry; top-level `FYAsset/Tests/*` menus are removed. They do not launch a second Unity process or shell CLI. Unsaved Scene/Asset state is rejected. No force-kill is offered during synchronous build stages.

### FR-13: Results and retention

```text
HotfixOutput/TestRuns/<backend>/build/<mode>/<run-id>/
  result.json
  unity.log
  recovery.json
  targets/
    <targetId>/
      snapshot-meta.json
      publish.json
      probe-before.json
      probe-after-publish.json
      probe-after-restore.json
      result.json
```

`result.json` records at least:

- stages and durations;
- backend/mode;
- frozen Target snapshots;
- versions;
- Task totals;
- package path;
- artifact count/size;
- Manifest hash;
- Repository HEAD/Parent;
- PackageIndex identity;
- StreamingAssets baseline hash;
- fixture hashes and fixture physical artifact identity;
- first failure;
- exit category;
- per-Target publish/probe/restore outcomes;
- restoration result.

Retain latest 20 run directories per `AA/AB × Full/Hotfix/Chain`. Cleanup may delete only TestRuns-owned directories. Official Packages/Repository objects are not retained as test evidence.

Because independent runtime is deleted, Build runs do not keep long-lived package handoff for later commands. Temporary publish inputs may exist only while the same run still owns them, then are removed during restore/cleanup.

## Non-Functional Requirements

1. Minimal implementation: one shared engine, one CLI wrapper, one shared UI panel parameterized by backend, permanent fixtures. No test framework package dependency.
2. No second authority: production config owns Task order; production serializers/hash utilities own formats; production Push Targets own destination/layout.
3. Diagnostics first: every failure names first failing stage, expected/actual, Target id if relevant, restoration outcome, and full log path.
4. Safe deletion: every delete/move/restore target is normalized and verified inside a recorded project/test/Target-owned root.
5. Determinism: fixed Full/Chain versions and permanent fixtures produce repeatable expectations.
6. AA/AB independence: selecting one backend must not permanently change the other backend Repository/fixture/`UseABBackend`.
7. Publish is a test transaction, never a permanent release.

## Proposed Responsibility And Files

| Area | Proposed change |
|---|---|
| Shared Editor build tests | Build Test engine, snapshot/recovery, acceptance, Target publish/probe models under `Assets/FYAsset/Scripts/Shared/Build/Tests/Editor/` |
| CLI | Project-local `fyasset-test` using Python stdlib + Unity `-executeMethod` under `CommandLine/` |
| Unity batch entry | Thin Editor CLI adapter for backend/mode/targets/result root |
| Shared Test panel | Backend-parameterized UI Toolkit panel with explicit Target multi-select |
| Permanent fixtures | `Assets/Test/FYAssetPipeline/**` plus AA/AB permanent Groups |
| Production code | Reuse managers/runners/path helpers/repository facade/Push targets/serializers/hash helpers; change production code only if a missing observable result blocks acceptance and receives separate approval |

## Implementation Slices

### T0: Permanent Test Group assets

- Create the shared folder, SO type, assets, Lua module/container.
- Register AA explicit entries and AB Folder Collector Group.
- Preflight assertions for addresses, labels, LuaScriptsIndex mapping, and `.fyraw` Auto RawFile classification.
- Small disposable self-check that scan/classification matches the matrix.

### T1: Result, state, recovery core

- Backend/mode/stage/result contracts including Target snapshot fields.
- Path-safe project snapshot/restore.
- Durable recovery and stale-run recovery-only behavior.
- Temporary-directory self-check for restore on success/failure/interruption simulation.

### T2: Full mode with Target publish

- Fixed isolated Full setup.
- Invoke concrete managers.
- Independent Full disk acceptance.
- Publish + remote probe for every explicit Target.
- Always restore Targets and project.

### T3: Hotfix and Chain modes

- Fixture exact-byte mutation/restoration.
- Hotfix preflight of local + Target Full identity.
- Backend-aware exact fixture physical-artifact Delta acceptance.
- Chain two-phase Target rhythm without Player.

### T4: CLI

- `fyasset-test aa|ab build full|hotfix|chain --target ...`
- External confirmation parsing.
- Editor lock preflight, timeout/Ctrl+C, exit codes, retention cleanup.

### T5: Minimal Editor pages

- Shared Test panel with explicit Target multi-select and external confirmation.
- Register in AA/AB windows.
- Reject unsaved Editor state.

### T6: Verification and project alignment

- State/recovery self-checks.
- AA Full/Hotfix/Chain CLI against local Target.
- AB Full/Hotfix/Chain CLI against local Target.
- One multi-Target local-only run if a second local test Target exists or can be created as a test-owned Path under project output.
- Controlled acceptance mismatch proving exit code 5 + restore.
- Controlled Target probe mismatch proving exit code 8 + restore.
- Interrupted CLI-owned batchmode run proving exit code 130 + recovery.
- Git status equality, no residual project/Target state, result schema, retention, solution build, Unity compile, `git diff --check`.
- Update progress/context only after implementation and verification.

## Acceptance Criteria

- Both entrypoints expose exactly Full/Hotfix/Chain Build modes.
- Every mode requires explicit Target IDs and never infers them.
- Full and Hotfix focused tests pass project-disk and Target contracts for both backends.
- AA Chain and AB Chain independently pass the two-build sequence, two-phase Target publication, and full restore.
- Missing/invalid local Full or missing Target Full blocks standalone Hotfix before mutation.
- Unrelated payload artifact change fails Hotfix/Chain acceptance.
- Build success cannot mask independent disk-contract failure or Target probe failure.
- Always-restore returns every Target to original identity with full remote re-verify.
- Normal failure, Ctrl+C, and stale recovery restore or clearly report preserved recovery evidence.
- Incoming unrelated Git changes survive; fixtures and test-owned state restore exactly.
- CLI and Editor pages report the same stages/results from one engine.
- No Player, no independent runtime command, no test framework package dependency.

## Approval Checklist

- [x] Two top-level entrypoints: `aa` and `ab`
- [x] Build modes: Full, Hotfix, Chain
- [x] Explicit Target IDs required; no inference
- [x] External Targets require dual confirmation
- [x] Always restore project and Targets
- [x] Snapshot entire Target service root
- [x] Hotfix requires exact Target Full identity
- [x] Chain creates Full itself, then Hotfix
- [x] Multi-Target serial complete acceptance with restore-before-continue
- [x] Fixture physical-artifact Delta contract
- [x] Permanent FYAssetPipeline Test Group
- [x] `.fyraw` Auto RawFile fixture for AB
- [x] Independent disk acceptance
- [x] Durable recovery restores first and exits
- [x] Exit codes include 7/8 Target classes
- [x] No Player in this plan
- [x] Developer approved requirements; execution still requires a separate explicit start confirmation
- [x] Developer authorizes execution of this plan

## Change Log

| Date | Change |
|---|---|
| 2026-07-21 | Initial Build-only plan created. |
| 2026-07-22 | Expanded with mandatory explicit Targets, always-restore publication, multi-Target failure policy, permanent Test Group, fixture physical-artifact Delta, and revised exit codes. Player/runtime moved to companion E2E plan. |
| 2026-07-22 | Requirements approved by developer; implementation remains unauthorized until a separate execution confirmation. |
| 2026-07-22 | Execution authorized and implemented: permanent fixtures, shared BuildTestEngine, CLI `fyasset-test`, AA/AB Test pages. Pending Unity compile refresh + local Full/Hotfix/Chain acceptance. |
