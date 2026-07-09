# XLuaHotfix - AI Collaboration Guide

## Core Rules
- Speak with the developer in Chinese; keep code, identifiers, commit messages, and AI-facing files in English.
- Read `context/INDEX.md` before non-trivial work. For risky work, also read `context/mistakes/INDEX.md`.
- Treat this file as collaboration guidance only. Verify concrete architecture, file paths, symbol relationships, and
  current behavior from Codegraph and code; use `context/` only for stable human-curated knowledge.
- Do not modify code without explicit developer approval.
- Ask before changing runtime loading, hot-update flow, build artifact format, package distribution, Addressables/AB
  replacement, or Lua-C# bridge behavior.
- Keep changes scoped and maintainable. Explain important decisions with reasons.

## Knowledge Boundaries
- `context/`: AI-facing stable knowledge, English only. No generated architecture index, TODOs, plan status, approval
  text, or next-step workflow.
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

## Execution Loop
1. Clarify the requirement.
2. Present a concrete checklist for non-trivial work.
3. Wait for explicit approval before implementation.
4. Execute only the approved scope.
5. Explain what changed and ask for sign-off.
6. Do not continue to the next sub-plan without sign-off.

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
- Prefer the current project-approved resource loading facade in new runtime code; verify the current facade from
  Codegraph and code.
- Do not manually edit generated or pipeline-owned resource grouping/output unless the approved plan requires it.
- Cross-language event registration must follow the project’s current event system conventions.

## Git
- Commit types: `feat`, `fix`, `refactor`, `docs`, `chore`, `test`.
- Keep commits focused.
- Run required generation or verification before committing; confirm XLua generation needs for XLua exposure changes.
