# Plan Directory

Executable plan files for the requirement. Only active (unexecuted, in-progress, or pending approval) plans stay here.

Completed and abandoned plans go to `archive/`.

## Rules

- Keep approved executable plan files here
- Keep rough, pre-approval, or idea-stage materials under `drafts/`
- Move executed or abandoned plans to `archive/` (never delete — always leave a trace)
- Do not put review artifacts here; use `review/`

## Active Plans

| File | Status | Description |
|------|--------|-------------|
| plan-build-request-output-ownership-20260520.md | Executed, awaiting sign-off | AA/AB Task 统一第一步：统一 build request 与最终 output ownership |
| plan-hotfix-pipeline-unification-20260519.md | Executed, awaiting sign-off | Hotfix 双管线统一与健壮性改进（下载重试、tmp 文件、Manifest 输出、日志边界） |
| plan-so-createassetmenu-entry-unification-20260519.md | Signed off | SO 创建入口统一（移除重复 CreateAssetMenu，补充 docs 入口说明） |

## Subdirectories

- `drafts/` — Non-executable planning drafts and convergence notes
- `archive/` — Executed, realized, superseded, or cancelled plans

## Archive Criteria

A plan moves to `archive/` when:
- Status is Realized / Executed / DONE (explicitly executed)
- Status is Superseded / Cancelled / Deprecated (explicitly abandoned)
- Status is Container and all sub-plans are archived

Never archive a plan solely because it was "approved" — approval alone is not execution.
