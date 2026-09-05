# FYAsset AA/AB E2E Test Pipeline Plan

> **Status**: Completed / Verified / Archived — 2026-07-24  

> **Requirement ID**: e2e-test-pipeline-20260722  
> **Date**: 2026-07-22  
> **Depends on**: `plan-build-test-pipeline-20260721.md` shared contracts for Targets, fixtures, restore, and CLI shape  
> **Scope**: Same-process E2E orchestration of Build + Target publication + Windows Player runtime acceptance for AA and AB Full, Hotfix, and Chain

## Goal

Automate the real delivery path that currently requires manual E2E:

```text
build package
-> publish to explicit Target
-> verify published identity
-> build Target-specific Player
-> run startup + resource smoke
-> for Hotfix/Chain: forward update on retained Player/persistent state
-> always restore Target and project
```

```text
fyasset-test aa e2e full|hotfix|chain --target <id> [--target <id>...]
fyasset-test ab e2e full|hotfix|chain --target <id> [--target <id>...]
```

External Targets:

```text
--confirm-external-publish <id>
```

There is no independent `runtime` command. Runtime exists only inside an E2E transaction that still owns:

- build state;
- Target snapshots;
- temporary Players;
- isolated persistentData roots;
- recovery records.

## Relationship To Build Plan

| Concern | Build plan | E2E plan |
|---|---|---|
| Resource build | Yes | Reuses same production managers / acceptance |
| Target publish + remote probe | Yes | Yes |
| Always restore Targets/project | Yes | Yes |
| Player build | No | Yes |
| Player runtime smoke | No | Yes |
| PersistentData isolation | No | Yes |
| Local server lifecycle | Publish probe only if needed | Full runtime lifecycle for LocalDirectory |
| Independent package handoff for later runs | No | No; same-run only |

E2E must not reimplement Build acceptance. It composes:

```text
Build Test engine stages
+ Player prepare/run/teardown stages
```

## Confirmed Product Contract

### Entry ownership

1. Same top-level entrypoints: `aa` and `ab`.
2. Backend selection is explicit and does not follow `UseABBackend`.
3. Modes: `full`, `hotfix`, `chain`.
4. Targets are mandatory and never inferred.
5. External Targets require dual confirmation covering the complete publish-to-restore window.

### Mode meanings

| Mode | Meaning |
|---|---|
| `e2e full` | Build Full once, then for every Target: publish Full, build Player, run Full startup/smoke, restore Target |
| `e2e hotfix` | Require exact pre-existing Full on local + every Target; run Full Player first; then Build Hotfix; then publish Hotfix and rerun same Player/persistentData |
| `e2e chain` | Create Full itself, run Full on every Target, require all Full stages PASS, Build Hotfix, run forward update on retained Player/persistentData, restore all |

### Chain rhythm

```text
global preflight
freeze Target snapshots
snapshot project + all Target service roots
external Targets: local mirror == public, or fail

Build Full once
Full project-disk acceptance

for each Target:
  publish Full
  remote Full probe
  temporarily apply derived RuntimeUrl to selected backend settings
  build Player into TestRuns-owned path
  restore project Player-build side effects immediately
  accept Player content
  start isolated persistentData root
  run Full coordinator smoke
  stop Player
  keep Player binary + persistentData for Hotfix phase
  keep Target at test Full

if any Target Full failed:
  restore modified Targets with full remote re-verify
  restore project
  stop; do not Build Hotfix

fixture mutation
Build Hotfix once
Hotfix project-disk acceptance

for each Target that passed Full:
  publish Hotfix
  remote Hotfix probe
  rerun same Player + same persistentData
  coordinator verifies forward update + smoke
  stop Player
  restore that Target with full remote re-verify

restore project
cleanup Players / persistentData / local servers
write result
```

### Hotfix rhythm

```text
validate local Full identity
validate every Target already has exact same Full
snapshot project + Targets

for each Target:
  ensure Target PackageIndex points at existing Full
  temporarily apply RuntimeUrl
  build Player from current Full StreamingAssets
  restore project Player-build side effects
  run Full coordinator smoke on clean persistentData
  keep Player + persistentData

if any Target Full runtime failed:
  restore and stop; do not Build Hotfix

fixture mutation
Build Hotfix once
Hotfix acceptance

for each Target that passed Full runtime:
  publish Hotfix
  remote probe
  rerun same Player + persistentData
  verify update + smoke
  restore Target

restore project and cleanup
```

This matches "先 Full 再 Hotfix" and real update continuity.

## Functional Requirements

### FR-1: Shared orchestration, not a second build engine

E2E reuses Build plan contracts for:

- Target snapshot freeze;
- project isolation;
- production build invocation;
- independent disk acceptance;
- fixture mutation/restoration;
- Target publish/probe/restore;
- recovery records.

E2E adds only Player and runtime ownership.

### FR-2: Target URL and Player binding

1. Each Target freezes:

```text
RuntimeUrl = PublicBaseUrl/{AA|AB}/
```

2. For that Target's Player build only:

```text
backup selected backend HotfixUrl + UseABBackend
write UseABBackend according to selected backend
write HotfixUrl = RuntimeUrl
build Player
immediately restore those settings
```

3. Player binary is copied/built into:

```text
HotfixOutput/TestRuns/<backend>/e2e/<mode>/<run-id>/targets/<targetId>/player/
```

4. Project settings restoration must not affect the already built Player.
5. No production runtime CLI URL override is introduced. The real settings path is used only during Player build.
6. Multi-Target means one Player per Target, never shared.

### FR-3: Player-build side effects

After every Target Player build:

1. Restore any project package/StreamingAssets/ServerData/Bootstrap/Addressables temporary mutation caused by Player build.
2. Re-verify the frozen Full project baseline identity before continuing.
3. Record known independent Addressables processor residue separately from FYAsset backend selection.

### FR-4: Cross-backend behavioral boundary

Player acceptance is behavioral, not path-name based.

AB Player may contain independent Addressables physical files only if all hold:

1. Player `BuildIndex` selects AB and never AA.
2. Runtime binds only AB facade.
3. No FYAsset AA PackageIndex/AAManifest/catalog activation path is used.
4. Request log for the selected Target never hits `/AA/` during an AB run, and never hits `/AB/` during an AA run.
5. Player build does not rewrite the other backend's official Repository/package identity.

If any condition fails, that Target fails before or during runtime. The plan does not paper over FYAsset backend crossover.

### FR-5: Dedicated test coordinator

1. Coordinator code is compiled only under a dedicated scripting define enabled for E2E Player builds.
2. Ordinary production Players never include the coordinator surface.
3. Coordinator responsibilities:

```text
wait for selected-backend package manager init
wait for facade Bind
wait for regular Lua init
wait for GameLauncher ready
verify expected backend/package/version identity if observable
run resource smoke matrix
observe error window
write structured result JSON
Application.Quit(0|1)
```

4. Coordinator does not drive Dialogue/UI gameplay flows.
5. External CLI still owns:
   - process timeout;
   - forced kill of run-owned Player only;
   - request-log acceptance;
   - cleanup.

### FR-6: Resource smoke matrix

Uses permanent Test Group assets from the Build plan.

#### Common AA/AB

1. Async:

```text
LoadAssetAsync<FYAssetPipelineSmokeAsset>("FYAssetPipelineAsync")
assert Marker == fyasset-pipeline-async:v1
UnloadAsset
```

2. Sync:

```text
LoadAssetSync<TextAsset>("FYAssetPipelineSync")
Full: assert text == fyasset-pipeline-sync:v1
AA Hotfix/Chain update: assert text == fyasset-pipeline-sync:v2
AB Hotfix/Chain update: assert text remains fyasset-pipeline-sync:v1
UnloadAsset
```

3. Lua:

```text
require("FYAssetPipelineSmoke")
assert marker == fyasset-pipeline-lua:v1
```

#### AB only

4. Raw async:

```text
ABPackageManager.LoadRawBytesAsync("FYAssetPipelineRaw")
Full: content == fyasset-pipeline-raw:v1
Hotfix/Chain update: content == fyasset-pipeline-raw:v2
```

5. Raw sync:

```text
ABPackageManager.LoadRawBytesSync("FYAssetPipelineRaw")
same content expectations as async
```

Raw uses AB public API, not the shared Object facade.

#### Required startup markers

Each must appear exactly once for the expected backend:

```text
package manager initialized
AssetPackageManager Bound: AA|AB
Initialized regular Lua modules
[GameLauncher] 所有系统启动完毕
```

#### Forbidden after ready through observation window

```text
[GameLauncher] failed
LuaScriptsIndex failure
NOT_FOUND
TYPE_MISMATCH
AMBIGUOUS_MATCH
DEPENDENCY_FAILED
ASSET_EXTRACTION_FAILED
BUNDLE_LOAD_FAILED
BUNDLE_NOT_FOUND
Unity same-file / already-loaded diagnostics
NullReferenceException
unhandled exception
wrong-backend Bind
```

### FR-7: Local server and request acceptance

For `LocalDirectory` Targets:

1. Start the existing token-protected localhost server against the frozen service root.
2. Health check must pass before Player launch.
3. Request log is retained per Target.
4. Full run expected request shape:

```text
health
/{AA|AB}/PackageIndex.json
optional versioned metadata/artifacts required by that backend
shutdown
```

5. Hotfix/Chain update run may request PackageIndex plus changed fixture artifact and any required metadata; it must not request the opposite backend root.
6. Server is stopped and port released during teardown.

For CloudflarePages Targets:

1. No localhost server.
2. Public probe uses the frozen public URLs.
3. Request acceptance is based on Player success plus public identity hashes, not localhost request logs.

### FR-8: PersistentData isolation

1. Each Target gets a unique isolated persistent root owned by the run.
2. Full phase starts clean.
3. Hotfix/Chain update reuses the same root for that Target.
4. Root is deleted after Target completion if restore/runtime succeeded; on failure it may be retained only under TestRuns evidence and never left as the developer's real project persistent path.
5. No reliance on machine-global leftover `fyasset` state.

### FR-9: Multi-Target failure policy

Same as Build plan:

1. Serial Target order.
2. On Target failure: restore that Target, full remote re-verify, then continue remaining Targets if restore succeeded.
3. Restore failure stops the whole run.
4. Overall result FAIL if any Target failed.
5. For Chain/Hotfix, any Full-phase Target failure blocks global Hotfix build.

### FR-10: Always restore

Always restore:

- every modified Target service root / public deployment;
- project build state;
- fixture bytes;
- temporary HotfixUrl/UseABBackend writes;
- Player-build side effects;
- local servers, Player processes, ports;
- TestRuns temporary Players and persistent roots after evidence is finalized.

Build/E2E publication is a test transaction, never a permanent release.

### FR-11: CLI and exit codes

```text
fyasset-test aa e2e full --target local
fyasset-test ab e2e hotfix --target local
fyasset-test ab e2e chain --target local --target cloudflare --confirm-external-publish cloudflare
```

Exit codes:

| Code | Meaning |
|---:|---|
| 0 | Passed |
| 2 | Invalid CLI usage |
| 3 | Precondition failed |
| 4 | Unity/build/Player-build pipeline failed |
| 5 | Independent project-disk acceptance failed |
| 6 | Restoration failed |
| 7 | Target config/snapshot/preflight failed |
| 8 | Publish or remote probe failed |
| 9 | Player/runtime coordinator or smoke failed |
| 130 | User interrupted |

### FR-12: Results layout

```text
HotfixOutput/TestRuns/<backend>/e2e/<mode>/<run-id>/
  result.json
  unity-build.log
  recovery.json
  targets/
    <targetId>/
      snapshot-meta.json
      publish-full.json
      probe-full.json
      player-build.log
      player/
      player-runtime-full.log
      player-runtime-full-result.json
      publish-hotfix.json
      probe-hotfix.json
      player-runtime-hotfix.log
      player-runtime-hotfix-result.json
      requests-full.log
      requests-hotfix.log
      probe-after-restore.json
      result.json
```

Retain latest 20 runs per `AA/AB × Full/Hotfix/Chain` under `e2e`.

### FR-13: Editor Test page actions

AA/AB Test pages expose:

```text
Build Full / Hotfix / Chain
E2E Full / Hotfix / Chain
```

Both require explicit Target selection. The Test page shows only configured Target toggles; selecting an external Target produces one confirmation dialog when the action starts, not a second confirmation checkbox. E2E actions additionally warn about:

- external publication;
- Player build cost;
- temporary public exposure for external Targets during the test window.

## Non-Functional Requirements

1. Minimal additions: coordinator under define, Player launcher, request/result collectors. No test framework package.
2. Same Target/restore contracts as Build plan.
3. Diagnostics first: first failing stage/Target, expected/actual, coordinator result path, restore outcome.
4. Safe deletion of only run-owned roots.
5. Deterministic fixture markers and fixed versions.
6. No permanent mutation of production Hotfix URLs or public Targets after restore.

## Proposed Responsibility And Files

| Area | Proposed change |
|---|---|
| Shared E2E orchestration | Editor/test orchestration composing Build engine + Player stages |
| Player coordinator | Runtime script compiled only with E2E define |
| Player build adapter | Temporary settings write, Player build, side-effect restore, copy into TestRuns |
| Local server control | Reuse `LocalHotfixServerController` / `hotfix_server.py` |
| CLI | Extend `fyasset-test` with `e2e` subcommand |
| UI | Same Test pages gain E2E actions |
| Fixtures | Same permanent `Assets/Test/FYAssetPipeline` domain as Build plan |

## Implementation Slices

### T0: Shared fixture readiness

- Depends on Build plan T0 permanent Test Group.
- Verify AA/AB collection and fixed addresses with a non-runtime static check.

### T1: Coordinator and define-gated Player surface

- Implement smoke matrix and structured result writer.
- Ensure define-off production builds contain no coordinator entry.

### T2: Player prepare/run/teardown core

- Temporary URL write and restore.
- TestRuns-owned Player output.
- PersistentData isolation.
- Process timeout/kill scoped to run-owned Player.
- Player-build side-effect restore.

### T3: E2E Full

- Compose Build Full + per-Target publish + Player Full smoke + restore.
- Local Target first.

### T4: E2E Hotfix and Chain

- Full-then-Hotfix continuity with retained Player/persistentData.
- All-Full-pass gate before Hotfix build.
- Multi-Target restore-before-continue.

### T5: CLI/UI and external Target gate

- `fyasset-test ... e2e ...`
- Editor E2E actions.
- Cloudflare path only with dual confirmation, mirror consistency gate, always restore via redeploy.

### T6: Verification and project alignment

- AA/AB E2E Full local.
- AA/AB E2E Hotfix local with pre-existing Full.
- AA/AB E2E Chain local.
- Controlled Player smoke failure proving exit code 9 + restore.
- Controlled Target restore path.
- Process/port cleanup verification.
- Solution build, Unity compile, `git diff --check`, progress/context update after verification.

## Acceptance Criteria

- `e2e full|hotfix|chain` exist for both backends and require explicit Targets.
- No independent runtime command remains in the design.
- Full-phase Target failure blocks Hotfix build for Hotfix/Chain.
- Chain creates Full itself; Hotfix requires exact pre-existing Target Full.
- Each Target uses its own Player and persistentData.
- Full then Hotfix reuses the same Player/persistentData per Target.
- Resource smoke matrix passes with fixed markers.
- Always restore returns Targets and project to original identity.
- External Targets cannot run without dual confirmation and mirror consistency.
- Exit code 9 is used for Player/runtime failures; 8 for publish/probe; 6 for restore.
- No permanent public release residue after a successful restore path.

## Approval Checklist

- [x] No independent runtime command
- [x] E2E modes Full/Hotfix/Chain
- [x] Explicit Targets + external dual confirmation
- [x] Always restore
- [x] Whole service-root snapshot
- [x] Full-then-Hotfix continuity
- [x] All Full Targets must pass before Hotfix build
- [x] One Player per Target
- [x] Player-build side effects restored immediately
- [x] Define-gated coordinator
- [x] Permanent Test Group smoke matrix
- [x] AB `.fyraw` Auto RawFile path
- [x] Behavioral cross-backend boundary
- [x] Exit codes 7/8/9
- [x] Developer approved requirements; execution still requires a separate explicit start confirmation
- [x] Developer authorizes execution of this plan

## Change Log

| Date | Change |
|---|---|
| 2026-07-22 | Initial E2E plan created from confirmed Build/Target/Player/Test Group discussion. Implementation not authorized. |
| 2026-07-22 | Requirements approved by developer; implementation remains unauthorized until a separate execution confirmation. |
| 2026-07-22 | Execution authorized. Landed E2E Full player path, define-gated coordinator, CLI/UI hooks. Hotfix/Chain currently compose Build engine; retained-player forward-update continuity still needs deeper Editor acceptance. |
