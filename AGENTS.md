# XLuaHotfix - AI Collaboration Guide

## Core Rules
- Speak with the developer in Chinese; keep code, identifiers, and AI-facing files in English.
- Read `context/INDEX.md` before non-trivial work. For risky work, also read `context/mistakes/INDEX.md`.
- Treat this file as collaboration guidance only. Verify concrete file paths, symbol relationships, and current behavior
  from code; use Codegraph too when it is available and use `context/` only for stable human-curated knowledge.
- Do not modify code without explicit developer approval.
- Ask before changing runtime loading, hot-update flow, build artifact format, package distribution, Addressables/AB
  replacement, or Lua-C# bridge behavior.
- Keep changes scoped and maintainable. Explain important decisions with reasons.

## Knowledge Boundaries
- `context/`: AI-facing stable knowledge, English only. No generated code index, TODOs, plan status, approval text, or
  next-step workflow.
- `context/reference/`: external references only. Do not store installed-package inventories or direct project
  integration notes there.
- `requirements/`: plans, approvals, progress, drafts, and reviews.
- `docs/`: human-facing Chinese documentation, updated only when there is a real documentation need.
- For UI/editor/human-facing documents, prefer HTML over Markdown when readability matters; ask if the format is unclear.

## Requirements
- Use `requirements/README.md` for the detailed requirements workspace rules.
- Keep the authoritative plan in `requirements/plan.md`.
- Use `requirements/plan/` only as the shared active-plan queue and `requirements/plan/archive/` as the shared plan
  archive.
- Do not create independent `plan.md` files or `plan/` folders inside requirement-specific directories unless the
  developer explicitly asks for that structure.
- Do not keep scattered requirement-specific progress logs long term. Merge their entries into
  `requirements/progress.txt` before deleting the standalone folder.
- Never delete progress history silently. Small `progress.txt` files may be removed only after their entries are copied
  into the main progress log with the requirement id preserved.

## Workflow (Non-Trivial Work)
Non-trivial: new feature, mechanism replacement, or cross-file behavior change; when unsure, treat it as non-trivial.
- Pipeline: grill → plan → implement. The plan carries its own spec (purpose, constraints, success criteria) as its first section; no implementation before the plan is approved and landed.
- Grilling: read the current state first; verify facts from code yourself; present decisions one at a time, each with a recommended answer; keep going until no silently assumed residue remains on the design tree. Land the conclusions into the plan's spec section.
- Plan location: `requirements/plan/drafts/YYYY-MM-DD-<topic>.md` (pre-approval), `requirements/plan/YYYY-MM-DD-<topic>.md` (approved); move to `requirements/plan/archive/` on completion. Never write planning docs into human-facing `docs/`.
- Split the plan into tasks that are independently testable and reviewable. Replace existing mechanisms as: run the new one alongside → atomic switch → delete the old.
- After each committed task, have a clean-context subagent review the commit range independently (give it only the spec and the diff, no session history). Fix Critical/Important findings before continuing; record Minor ones for the final branch review. Review the whole branch before merge/PR.
- On review feedback: verify each point against the codebase before accepting or raising a technical objection. No performative agreement, no blind implementation.
- Record task progress in `requirements/progress.txt`.

## Iron Rules
1. No production code without a failing test; bug fixes start with a test that reproduces the bug. Where test infrastructure cannot cover the change (UI, scene, build pipeline, hot-update style work), substitute the smallest runnable verification and record the exact command and output.
2. No fix without a located root cause; after 3 failed fix attempts stop patching, switch to grilling mode and question the architecture.
3. No completion/pass/fixed claims without this round's verification output (full command + output + exit code). Personally verify subagent success reports: the diff, the run results, the landing point.
4. When blocked or uncertain, stop and ask the developer. Never guess and proceed.

## File Discipline
- Survey before creating files.
- Reuse existing directories and naming patterns.
- Keep generated files inside the project tree.
- Ask before moving, renaming, or restructuring existing directories.
- Do not encode volatile implementation file paths in this guide.

## Project Guardrails
- Follow nearby C# and Lua naming/style conventions.
- Lua scripts must clearly follow the project’s Class-style or Module-style pattern.
- New Lua-callable C# APIs must be checked against the current XLua exposure configuration.
- Prefer the current project-approved resource loading facade in new runtime code; verify the current facade from code
  and use Codegraph too when it is available.
- Do not manually edit generated or pipeline-owned resource grouping/output unless the approved plan requires it.
- Cross-language event registration must follow the project’s current event system conventions.

## Git
- Commit types: `feat`, `fix`, `refactor`, `docs`, `chore`, `test`.
- Keep commits focused.
- Run required generation or verification before committing; confirm XLua generation needs for XLua exposure changes.
- Develop non-trivial changes in isolated worktrees; prefer the harness-native worktree tool. Branch names: `<type (feat/fix/chore, ...)>/<description>`.
- Commit message: English type prefix + one-line Chinese summary.
- Group each requirement round with a `--no-ff` merge commit; fold un-pushed iterative commits of the same round back into the group (no interactive rebase in this environment; use `git reset --soft` or `--autosquash`).
- Re-run the full verification on the merged result. Ask before deleting worktrees or branches.
