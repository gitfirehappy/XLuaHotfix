# FYAsset Full Audit, Localization, And Slimming Plan 2026-07-14

> **Status**: Implemented / Verified / Pending developer sign-off
> **Requirement ID**: fyasset-full-audit-localization-slim-20260714
> **Scope**: All C# source under `Assets/FYAsset/`, with supporting tests and requirements records only when needed.

## Language Policy

- Keep technical terms, identifiers, API names, type names, field names, variable names, error codes, file names, and
  product names in English.
- Translate explanatory comments and direct `Debug.Log*` descriptions to Chinese.
- Keep wording in English when a Chinese translation would be ambiguous, unnatural, or less searchable.

## Approved Work

1. Audit all FYAsset source for dead code, unused compatibility surfaces, single-use abstractions, delegation-only
   wrappers, duplicated logic, oversized files, and avoidable editor/runtime complexity.
2. Audit and localize English explanatory comments and direct `Debug.Log*` descriptions under the language policy.
3. Remove only clearly unused, low-risk code whose call graph and project references are empty.
4. Record higher-risk simplification candidates without implementing them.
5. Verify with residual text scans, the focused scenario executable, solution compilation, and `git diff --check`.

## Guardrails

- Preserve existing uncommitted work; the pre-audit workspace was committed as `3559850`.
- Do not change runtime loading, Hotfix flow, build artifact format, package distribution, AA/AB behavior, or Lua-C#
  bridge behavior without a separate developer decision.
- Do not translate serialized identifiers, reflection keys, menu paths, error codes, or protocol text merely because
  they contain English.
- Prefer deletion and direct code over new abstractions.

## Deliverables

- One current review report in `requirements/review/` with ranked findings and a net reduction estimate.
- Localized comments and direct logs that follow the approved language policy.
- A minimal verified diff containing only low-risk removals and wording changes.
