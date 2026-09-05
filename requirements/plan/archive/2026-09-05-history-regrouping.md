# History Regrouping Proposal

> **Date**: 2026-09-05
> **Status**: Executed / archived 2026-09-05. H01-H17 reconstruction and the final `--no-ff` merge completed in isolated worktrees; endpoint tree matched original `e5de777` exactly. Backup ref/bundle and dirty-state copies remain under `Logs/history-cleanup-20260905/`; no push or backup deletion performed.

## Spec

### Purpose
Regroup the 66 local commits after the recorded `origin/main` into reviewable module-sized units, folding iterative fixes and progress-only commits without losing the original history, final committed content, or pre-existing dirty work.

### Boundaries
- Base: `68b75a52e1887ac88917e15f9adc416621fa3d6b`.
- Original HEAD: `e5de777a541d9a024035d26870643624f8e08e71`.
- The local tracking ref is not fresh remote evidence. The local boundary was not refreshed; the approved reconstruction used `68b75a5` as the recorded base. Do not push or assume the remote includes the rewritten history until a separately approved remote check is performed.
- The 58 pre-existing dirty paths, this round's AGENTS/requirements changes, and any newly observed user changes were outside the 66-commit content range. Their staged/unstaged split and untracked copies were preserved under the ignored history-cleanup log before the ref update.
- The endpoint comparison is `git diff --name-status --no-renames BASE HEAD`: 737 path changes, including move sources and destinations separately. It is not 737 independent semantic changes.
- Exact path assignments and all 66 original hashes/messages are preserved in the ignored preservation copy `Logs/history-cleanup-20260905/history-inventory.json`; the review directory contains no machine-generated inventory. Every endpoint path is assigned once; none is silently dropped.
- The reconstruction produced H01-H17 commits plus the final `--no-ff` merge commit `20d0af2`; it was then applied to `main` as the equivalent history endpoint followed by this round's separate code/requirements commits. The original `e5de777` endpoint tree and the reconstructed tree were equal.
- This inventory is a grouping proposal, not an executable cherry-pick list. Dependency ordering and paired moves need staging review before each commit. Do not claim every proposed intermediate tree compiles.
- No production fix is authorized by this history plan. Review findings and the uncommitted backend-selection work remain separate.

### Success Criteria
1. Original history has a recoverable backup ref and an independently readable bundle before any rewrite.
2. The reconstructed endpoint tree equals the original HEAD tree byte-for-byte unless the developer explicitly approves a listed content exception.
3. Dirty tracked content, staged entries, and untracked files are preserved independently, including the staged/unstaged split of renamed BackendMode files.
4. All old commits map to the reconstruction ledger; progress text and archived decisions are retained, not replaced with short summaries.
5. Each new commit uses the approved scope, exact paths, and concise Chinese subject below; additional splitting or messages require renewed approval.
6. The final grouped round uses a `--no-ff` merge. Verification is rerun on the merged result, not inferred from the current dirty checkout's green build.
7. No push, force-push, branch deletion, worktree deletion, or backup removal occurs without separate approval.

## Proposed Content Units

These are whole-path endpoint buckets, not chronological replay. H01-H10 have dependency edges; sequence and atomic migration checkpoints must be reviewed in the isolated reconstruction. The original 66 commits remain the provenance for intermediate decisions.

| ID | Paths | Scope | Proposed Subject |
|----|-------|-------|------------------|
| H01 | 26 | FYAsset Shared runtime/hotfix/helpers and historical AssetSystem paths | `refactor: 收敛共享资源与热更新运行时契约` |
| H02 | 27 | FYAsset AA runtime and hotfix | `refactor: 独立 AA 资源生命周期与热更新实现` |
| H03 | 60 | FYAsset AB runtime and hotfix, including offline/editor loading | `feat: 收敛 AB 加载与离线运行链路` |
| H04 | 115 | FYAsset remaining Shared build/settings; baseline, publication, linear pipeline and neutral editor support | `refactor: 以双槽基线替换仓库内核并收敛共享构建链` |
| H05 | 62 | FYAsset remaining AA build/editor/settings paths | `refactor: 收归 AA 构建与编辑器职责` |
| H06 | 71 | FYAsset remaining AB build/editor/settings and collector configuration | `refactor: 收归 AB 构建采集与编辑器职责` |
| H07 | 57 | Compat runtime/CLI/test/publish/Lua-index integration and directory metadata | `refactor: 集中宿主适配与跨后端集成入口` |
| H08 | 74 | XLuaFramework plus relocated Game Lua script destinations and metadata | `refactor: 独立 XLuaFramework 资源接口并迁出示例脚本` |
| H09 | 47 | Host resource consumers, test fixtures/scenarios and CommandLine test orchestration | `refactor: 对齐宿主资源接线与验收用例` |
| H10 | 51 | Build, Resources, Addressables, Prefab, SO and Scene configuration | `chore: 对齐资源边界与构建配置资产` |
| H11 | 5 | Assets/Tools configuration conversion and serialization editor tools | `chore: 对齐转换与序列化编辑器工具` |
| H12 | 2 | Packages manifest and lock | `chore: 更新 Unity MCP 包引用` |
| H13 | 15 | Human-facing documentation | `docs: 对齐资源框架与构建测试文档` |
| H14 | 25 | Historical AGENTS and curated context changes, excluding this round's working edits | `docs: 整理协作约定与历史经验` |
| H15 | 64 | Historical requirements plans/reviews/progress, excluding this round's working edits | `docs: 归并计划评审与执行历史` |
| H16 | 28 | Existing output/evidence deletions and gitignore changes | `chore: 清理历史构建输出并保留产物忽略规则` |
| H17 | 8 | Existing generated editor log, font asset and ProjectSettings changes | **HOLD**; disposition must be approved before reconstruction |

Target: 16 ordinary content units plus H17 disposition and the round merge. This is not a promise that a valid migration can always be staged in exactly 17 commits. If a unit contains unrelated changes or a move crosses units, split/reassign the exact inventory paths and resubmit that change before committing.

## Success Criteria

1. Original history has a recoverable backup ref and an independently readable bundle before any rewrite.
2. The reconstructed endpoint tree equals the original HEAD tree byte-for-byte unless the developer explicitly approves a listed content exception.
3. Dirty tracked content, staged entries, and untracked files are preserved independently, including the staged/unstaged split of renamed BackendMode files.
4. All old commits map to the reconstruction ledger; progress text and archived decisions are retained, not replaced with short summaries.
5. Each new commit uses the approved scope, exact paths, and concise Chinese subject below; additional splitting or messages require renewed approval.
6. The final grouped round uses a `--no-ff` merge. Verification is rerun on the merged result, not inferred from the current dirty checkout's green build.
7. No push, force-push, branch deletion, worktree deletion, or backup removal occurs without separate approval.

## Execution Result (2026-09-05)

- H01-H17 were committed in the isolated reconstruction branch, followed by `20d0af2 chore: 合并模块化历史整理结果` with two parents.
- The reconstructed tree was `fe6ac7e442840c80fae34daf4caa9e302d3ed721`, exactly equal to the original `e5de777` tree.
- `main` was advanced with `git update-ref` only after verifying it still pointed at `e5de777`; the final main history now points at the no-ff merge before this round's separate commits.
- The 66 old commits became 17 module/content commits plus one no-ff merge. The rewritten main is not pushed; the recorded remote-tracking ref still represents the pre-rewrite history.
- H17 was preserved without content exclusion. Its generated/editor assets, font asset, and ProjectSettings changes remain in the endpoint tree as explicitly approved.
- This draft is archived after execution. Review findings and ACCEPT-01/02/03 remain active elsewhere and are not silently marked complete.


The current endpoint already contains these changes, mixed into historical checkpoint commits. Source diff inspection found an empty `Entries` undo log, Unity settings serialization/schema additions, a new 60-fps Timeline settings asset, and a font asset diff of 580 additions / 94 deletions. Those facts do not establish that all changes are disposable local state:
- `Assets/FYAsset/Editor.meta`.
- `Assets/FYAsset/Editor/Generated.meta`.
- `Assets/FYAsset/Editor/Generated/HotfixGroupUndoLog.json` and its `.meta`.
- `Assets/TextMesh Pro/Fonts/SmileySans-Oblique SDF.asset`.
- `ProjectSettings/ProjectSettings.asset`.
- `ProjectSettings/QualitySettings.asset`.
- `ProjectSettings/TimelineSettings.asset`.

The imported rule prohibits new local generated editor state in commits. Blindly carrying the undo log into a freshly reconstructed commit conflicts with that rule; silently excluding it conflicts with endpoint preservation. The font and project settings may be intentional user work and cannot be discarded based on appearance.

Recommended handling: preserve all eight in the original backup; review their original diffs; retain intentional project/font settings as separately approved scopes, and exclude only confirmed generated local undo state under an explicit endpoint-difference exception. No exclusion is currently approved. If full endpoint preservation is chosen instead, retaining pre-existing generated state requires an explicit exception, not a hidden mechanical commit.

## Preservation And Execution Protocol

1. Recheck HEAD, index, dirty files, untracked files and remote boundary; stop if the approved inventory is stale.
2. Create `backup/pre-history-cleanup-20260905` at the original HEAD and a bundle under ignored `Logs/history-cleanup-20260905/`. Record the full hash and verify bundle readability. Keep both until the developer approves removal.
3. Independently capture the index bytes, `git diff --cached --binary`, `git diff --binary`, status with NUL separators, and copies plus SHA-256 hashes of untracked files. A backup branch alone does not preserve dirty work. Avoid `git stash` as the sole backup because staged/unstaged renames need explicit preservation.
4. Reconstruct on `chore/history-cleanup-20260905` in an isolated worktree under ignored project `Temp/`. Do not reset the dirty main worktree or run broad `git add -A Assets`.
5. Stage only approved exact paths/hunks. Keep source/destination `.meta` identity and related call sites together. Fold failed partial-assembly experiments into their final Shared build result; do not reproduce known broken intermediate commits as milestones.
6. Review each proposed staged diff against its scope, record old-to-new provenance, then commit only under the approved message. A pure progress commit is folded into its owning module or H15, never used as a new iteration checkpoint.
7. Compare the final tree with the preserved original HEAD and report any approved H17 exception explicitly. Run applicable scenario and compilation verification; a current dirty-tree build is not proof for reconstructed HEAD.
8. Prepare the `--no-ff` round merge in isolation. Only after evidence review switch the main ref with an operation that does not touch or restage the dirty working files. Verify the saved index/content hashes and status shape afterward.
9. Rerun verification on the merged result and on the restored dirty working tree separately. Report which tree each result covers.

## Provenance Notes

- `44026f3` has a test-directory-move subject but changes only one documentation file; the actual move is in `afd1081`. Group from actual diffs, not subjects.
- Initial checkpoints `3559850`, `f96df52`, `88e5034` and `a728b80` mix production, configuration, outputs and history. Whole-commit cherry-picking cannot satisfy the new scope rule without re-splitting.
- `57fd551`, `1eb4314`, `e5de777` include the cross-assembly partial-class attempt and correction. Preserve the narrative in progress/backup, not as three new standalone corrective commits.
- Existing committed `AGENTS.md` differs from this round's imported rules. Reconstruct only historical HEAD content first; land this round's policy/docs later with its own approval.

## Approval Checklist

- [ ] Approve H01-H16 subjects and exact path inventory, subject to explicit dependent-move staging review.
- [ ] Decide H17 intentional content versus generated-state exception.
- [ ] Approve remote-boundary check and backup bundle/ref creation before history rewrite.
- [ ] Approve isolated reconstruction and final no-content-loss merge strategy.
- [ ] Approve final staged scopes/messages after any necessary split.
- [ ] No push or cleanup of recovery artifacts is implied.
