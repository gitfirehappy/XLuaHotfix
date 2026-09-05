# AA/AB Decoupling Plan 2026-09-03

> **Date correction**: This is September 2026 decoupling work; the original `2026-07-24` stamp was a copy-paste from the July closeout. Decisions unchanged.
> **Status**: Approved (grilled 2026-09-03, decisions Q1-Q4 confirmed by developer) / **COMPLETED 2026-09-03**
> **Origin**: Developer audit — BuildPipelineBackbone mixes AA/AB task lists; historical decision unified AA/AB behind shared interfaces

## Spec

### Purpose
Make AA and AB independently exportable file sets on top of a backend-neutral Shared
foundation, eliminating all symbol-level AND behavior-level coupling between backends.

### Locked Decisions (from grilling)
1. **End-state (B-lite)**: two file sets `{AA ∪ Shared}` and `{AB ∪ Shared}` each compile
   standalone. Export = .unitypackage file selection. NO asmdef/package.json for now.
2. **Compat is project glue, not framework**: `AssetPackageManager`/`HotfixManager`/
   `BuildProjectManager` + the three CLI entries (`BuildCommandLine`, `BuildTestCommandLine`,
   `E2ETestCommandLine`) move to `Assets/FYAsset/Scripts/Compat/`, excluded from both exports.
3. **Neutrality standard B**: symbol-level zero tolerance AND no backend-value behavior
   branches in Shared. `BackendMode` enum degrades to pure config vocabulary / repository
   directory naming (no class references).
4. **Verification**: scenario-test-style subset csproj gates (`aa-export-subset`,
   `ab-export-subset`) as final acceptance, added after scrub turns them green.
5. **Sequencing**: in-flight standalone/playmode line landed first (done: `6abeb1e`).

### Constraints
- No production behavior change; moves via `git mv` (GUID/meta preserved).
- Hotfix area remains high-risk: no behavior edits there in P0-P3, relocation only.
- Each P is an independently testable, reviewable commit.
- Unity compile cannot be verified in this environment; verification =
  scenario test exes + textual residual scans. Record exact commands.

### Success Criteria
- Zero `AA*`/`AB*` specific identifiers and no `BackendMode`-value behavior branches in
  `Shared/` (excluding pure vocabulary enums and repository directory naming).
- Zero AA↔AB cross references outside Compat.
- `aa-export-subset` and `ab-export-subset` scenario csproj compile + pass.

## Tasks

| # | Task | Status |
|---|------|--------|
| P0 | Move `Shared/Compatibility` → `Scripts/Compat/`; move 3 CLI entries into `Compat/Editor/`; fix S2 csproj link + docs paths | **Done** (`53cbe88`) |
| P1 | Split `BuildPipelineBackbone`: `AAPipelineBackbone`/`ABPipelineBackbone` hold task lists; Shared mechanics renamed `BuildTaskListUtility`; `BuildPipelineRunner.Execute` gains required `expectedBackboneTasks` param (null on whitelist preview path); delete backend sniffing. Grilling: Q1→A required param, validation kept (A1), Q2→B utility rename, Q3 backbone shape confirmed, Q4→B preview literals deferred to P2 | **Done** (`d11e827`) |
| P2 | AA hotfix-group restore sovereignty reclaimed to `AABuildProjectManager` (Q1); `Build/Tests` whole dir -> `Compat/Editor/Tests` (Q2, test matrix is project glue by design); preview whitelist + stop-after data-driven via `BuildPipelineConfig.HotfixDiffTaskName` (Q3) | **Done** (`afd1081`, `44026f3`) |
| P3 | `TaskExportLocalBuildData` baseline staging/validate/apply sunk into `IBaselinePackageHandler` (impl on AABuildBackend/ABBuildBackend, context-injected in pipeline, param-injected post-commit); `TaskPrepareContext` left as-is (request carries mode; legacy fallback noted) | **Done** (`e2db4d6`) |
| P4 | `PipelinePanel` neutralized via `BuildPanelActions` ctor injection + moved to `Shared/Build/Editor/UI/`; `LocalAAHotfixSmokeTest` → `Compat/Editor/Tests/` | **Done** (`ff15c4c`, `1d995ed`) |
| P5 | ①AA/AB context keys moved (`259b242`); ②settings dual-provider dissolved (17 call sites → per-backend `Settings.Instance`, SizeGuard value-injected, `9f3f6ff`); ③RepositoryPreviewRunner AA prep split (pending); ④Repository CLI/StatusPanel dual shape + provider final deletion (pending) | ①② **Done**, ③④ Pending |

## Repository Slimming (supersedes P5 ③④; approved 2026-07-24)

**Locked decisions**
1. Framework keeps ONLY baseline semantics: rolling `{Platform}[-{Channel}]/{AA|AB}/baseline.json` with two slots (`Latest`, `LatestFull`), VCS-tracked, atomic write; history/audit = git. Self-made VCS (objects history, channel protocol, repair logs, `FileBuildRepository`/`IBuildRepository`/`BuildRepositoryFacade`) is **deleted**, not moved.
2. Baseline written only after the FULL flow (build + publish) succeeds; `TryRollbackHead` concept deleted.
3. `LocalHotfixServerController` stays (generic hotfix dev-loop tool).

**Tasks**
| # | Task | Status |
|---|------|--------|
| R1 | `BuildBaselineStore` + dual-slot `BuildBaselineState` + channel key helper + one-time legacy HEAD migration | **Done** (`3421c01`) |
| R2 | diff tasks read `BuildBaselineStore` (Latest / FindFullBaseline dual-slot) | **Done** (`0aa6f32`) |
| R3 | runner: three repository hooks removed; baseline written post-publish-success | **Done** (`b7956df`) |
| R4 | preview split: neutral `BuildPreviewRunner` + `AARepositoryPreview`/`ABRepositoryPreview`; ABDelivery keys → `ABBuildContextKeys` | **Done** (`a9e159d`) |
| R5 | ops面层 → `Compat/Editor/Repository/` (StatusPanel, CLI, push stack, self-checks ×2, PushModels) | **Done** (`d5d013b`) |
| R6 | push stack cut over to `BuildBaseline` (+`PackageRootDir`/`BackendMode`/`ParentVersion`/`CommitDelta`) and re-homed to `Shared/Build/Publish/` (`BuildPublisher`/`IPushTarget`/`LocalDirectory`/transaction/utility); Cloudflare stays Compat; panel/CLI switched to baseline display, history/health/repair UI excised; Compat tests rewired; kernel 4 files + legacy data deleted (-1437 lines) | **Done** (`37d4f1b`) |
| R7 | `FYAssetBuildSettingsProvider` deleted; last consumer (status panel) rewired to per-backend `Settings.Instance` | **Done** |

| G  | ExportBoundary 文本边界门禁（自维护类型集合交叉扫描，注册进 s3 场景）| **Done** |

---

## Appendix — Review Findings Ledger (2026-07-24)

两轮干净上下文独立 review + 一轮框架级审计，已处置：2 Critical（编译）+ 2 Important（逻辑）于 `b2b3279`；第二轮解耦（面板归位/双注入契约/ManifestFileNames 随基线/target 注入工厂/hotfix 绑定脱离 facade/E2E 归位/测试面板自立窗口）于 `59b19e1`；docs 术语同步于 `4b3e9d5`。

**留档 Minor（评估为低收益搬移，暂不处理，回归时再评估）**：
1. `FYAssetSettings.UseABBackend` 路由开关与双侧 manifest 常量同居 Shared（老契约、新建机制禁止扩用 — TaskPrepareContext 已补永久豁免注释）。
2. `SettingsPanel` 内 AB 专属 UI 段（`DrawAbEditorPlayModeSection`）与 `EPlayMode`（仅 AB 消费）仍在 Shared，错层候选。
3. `AABuildPipelineWindow` 单文件双公共类（window + AAHotfixGroupMaintenancePanel），AB 无对称面板；AA 面板平铺 `Build/Editor/`、AB 归 `Build/Editor/ABPipeline/`，目录组织不对称。
4. JsonUtility/SerializationUtility 双轨（baseline 用前者、PackageIndex/preview 用后者），暂不统一，新增字段需注意前者不序列化属性。
