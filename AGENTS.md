# XLuaHotfix - AI Collaboration Guide

## Project Overview
XLuaHotfix is a Unity Addressables + XLua hot-update framework that covers resource build, version diffing,
runtime Lua/C# interoperability, automation tools, and a componentized development workflow.

## Technology Stack
- Engine: Unity (Addressables-based resource management)
- Hot-update language: Lua (XLua)
- Primary languages: C# + Lua
- Resource management: Unity Addressables
- Build tooling: custom differential snapshot pipeline (`DifferentialProcessor`)

## Collaboration Principles
- Understand the requirement before coding; ask when uncertain instead of guessing.
- Explain important decisions with reasons.
- Prefer Chinese in conversation; keep technical terms in English.
- Every code change should consider maintainability.
- Before changing Lua-C# bridge code, confirm whether XLua feature configuration also needs updates.
- For UI, editor, or other user-facing documents that should be easier to read, prefer HTML over Markdown; if the format is unclear, ask the developer first.

## Knowledge Management
- Read `context/INDEX.md` before starting work to understand existing knowledge.
- Write new knowledge into the corresponding layered directory by topic, such as `context/architecture/`, `context/dependencies/reference/`, `context/dependencies/integration/`, and `context/mistakes/`.
- Record only verified knowledge; mark anything unconfirmed as `[UNVERIFIED]`.

## Requirement Workflow
1. Create `requirements/{requirement-id}/brief.md` to describe the goal and background.
2. Record key progress in `requirements/{requirement-id}/progress.txt` (start, done, decision, blocked, next).
3. Write problems and solutions into `context/mistakes/troubleshooting.md`.
4. After a requirement is complete, migrate valuable verified facts into `context/` and keep it aligned with the latest real project state.

### Execution Protocol (Mandatory)
```
1. Developer approves the sub-plan by answering the approval checklist
   |
2. Execute the sub-plan step by step
   |
3. When execution is complete -> explain the changes -> ask for developer sign-off
   |
4. Developer may ask questions at any time; the executor is responsible for answering
   |
5. After sign-off -> ask whether to start the next sub-plan
   |
6. If not satisfied -> adjust the current sub-plan (return to step 2)
```
Do not modify code without explicit developer approval.

### Post-Plan Checklist (Mandatory)
After each sub-plan, follow this order:
1. Append `progress.txt`: record completed work, key decisions, and verified facts.
2. Update the plan status table: change completed sub-plans from TODO to DONE.
3. Sync `README.md`: reflect new capabilities or structural changes.
4. Update `context/`: add newly verified facts, dependency notes, external references, or historical prevention rules; do not write plan text here.
5. Ask for developer sign-off: present the summary and ask, "Is this confirmed? May I proceed to the next sub-plan?"
   Do not start the next sub-plan before sign-off.
   Record the sign-off in `progress.txt` as `[done] YYYY-MM-DD plan-XX SIGNED OFF`.

### Approval Checklist Format
Each sub-plan must end with specific approval questions, not generic approval prompts.

Good checklist:
```markdown
## Approval Checklist
- [ ] Directory-to-container mapping: support one directory to multiple containers?
- [ ] Game/ directory handling: scan only `Game/Player/`, or create one container for every subdirectory under `Game/`?
- [ ] Auto-trigger scanning on Unity asset save, or manual only?
```

Bad checklist:
```markdown
- [ ] Do you approve this plan?
- [ ] Are you sure?
```

### progress.txt Format
```text
# {requirement-id} Progress Log
# Format: [type] YYYY-MM-DD description
# Types: start / done / decision / blocked / next
# Sign-off format: [done] YYYY-MM-DD plan-XX SIGNED OFF - {summary}
```

### Drafts Workflow
Use `requirements/{id}/plan/drafts/` for non-executable planning material:

- rough direction convergence
- idea capture
- option comparison before a precise formal plan exists

Rules:
- Drafts are not executable plans and do not authorize implementation.
- Drafts can be partial, fuzzy, or exploratory.
- When a direction becomes precise and approved, promote it into a proper plan.
- When promoting, condense and annotate the original entry; never delete, always leave a trace.
- Do not copy draft language into `context/`.

## Session Recovery
When you say "continue {requirement-id}", I will:
1. Read `requirements/{requirement-id}/progress.txt` to understand the latest progress.
2. Summarize the current status and the recommended next step in 2-3 sentences.
3. Wait for your confirmation before continuing.

## Knowledge Boundary Rules
- `context/` is AI-facing and must stay in English.
- `context/` must stay aligned with the latest verified project reality.
- `docs/` is human-facing and should stay in Chinese; update it only when there is an actual human documentation need.
- `requirements/` is the only place for plans, sequencing, approvals, and progress tracking.
- Do not let `plan-*`, `TODO`, `next step`, or workflow text leak into `context/`.
- Use `context/dependencies/integration/` for direct project dependency notes.
- Use `context/dependencies/reference/` for external framework, engine, protocol, or paper references.
- Use `context/mistakes/` for verified historical errors, troubleshooting, and prevention rules.

### File & Directory Discipline
- Survey first, create later. Before creating a file or folder, inspect the project root and relevant subdirectories. Adapt to the existing layout; never impose your own.
- No freelancing outside the workspace. All file creation must stay within the project's existing directory tree.
- Reuse, don't duplicate. If a directory already serves the target purpose, use it. Do not create redundant variants.
- Flat over nested when it fits. Do not introduce deep folder hierarchies unless existing project conventions already use them.
- Ask before restructuring. Moving or renaming existing directories requires explicit developer approval.

### Web Search Credibility
When sourcing from web results, prioritize:
1. Official documentation - docs, specs, man pages, vendor sources.
2. Reputable technical blogs - recognized engineers, verified accuracy.
3. Community / unvetted posts - treat as hints, not facts. Flag to the developer before relying on them.

Never source from pages with poor reputations, negative reviews, or known misinformation.

### Module Design Principles (Paradigm-Rule-System)
When designing any module, organize thinking in three layers:

- Paradigm: Core mechanisms and data structures - what capabilities does this module have?
- Rule: Activation conditions, ordering, constraints, and recovery behavior - when and under what preconditions?
- System: Public API, integration points, lifecycle, and error boundaries - what does this module expose?

Benefits: Paradigm ensures completeness; Rule ensures controllability; System ensures simple integration. When a module changes, revisit all three layers rather than patching only the part that broke.

### Mistake Prevention Protocol
When the project has `context/mistakes/` files:

- Before significant tasks: Read `context/mistakes/INDEX.md`, then scan relevant thematic files for prevention rules that apply to the planned work.
- When a review finds errors: Re-read relevant mistake records. Reviews discovering repeated patterns indicate the records were not consulted.
- When the developer expresses dissatisfaction: Check whether a known prevention rule was violated.
- After fixing a bug: If the root cause is reusable knowledge, write a concise entry to the appropriate `context/mistakes/{topic}.md` file (English, AI-facing, structured as symptom -> root cause -> fix -> prevention rule).

Thematic files follow: `context/mistakes/{topic}.md`.

## Project-Specific Rules

### Coding Standards
- C# class names use PascalCase; Lua modules use PascalCase; local variables use camelCase.
- XLua bridge components must end with `Bridge` (for example `InputBridge`, `AnimBridge`).
- When adding a new Lua-callable C# type, update the `TypeMemberListSO` configuration at the same time.

### Architecture Constraints
- Lua scripts support both Class (instantiated OOP) and Module (static) modes; each new script must explicitly choose one.
- Resource loading must go through `AssetPackageManager`; direct use of raw Addressables APIs is not recommended.
  - The `IPackageBackend` interface refactor is in progress (see `requirements/refactor-2026/plan-B.md`).
  - New code should prefer `AssetPackageManager`; existing Addressables calls should be migrated gradually during refactoring.
  - The lower-level hot-update flow (`HotfixManager`/`NetworkDownloader`/`CatalogUpdater`) still depends on Addressables and will be handled in B4 / a dedicated design review.
- Hot-update resource grouping is automatically managed by `DifferentialProcessor`; manual edits to the Hotfix group are forbidden.
- Cross-language event registration and unregistration must go through `EventCentre`; do not subscribe across Lua with raw C# delegates.

### Major Decision Confirmation Rules (Mandatory)
- If the change involves AB package replacement, hot-update pipeline changes, or Addressables core API replacement, ask the developer repeatedly for confirmation.
- Interface separation (for example `IAssetIndex`, `IPackageBackend`) is a safe refactor and may proceed normally.
- Any change that affects runtime loading behavior, build artifact format, or hot-update distribution flow must be confirmed with `ask_user` at every step.
- When in doubt, ask more often rather than deciding on a potentially unstable solution alone.

### Build Flow
- `BuildFullPackage` -> major version increment (Major+1), and all resource groups must be restored before running it.
- `BuildHotfix` -> patch version increment (Patch+1), with `DifferentialProcessor` detecting changed assets automatically.
- `ConfirmRelease` -> snapshot promotion from Staged to Head, called after official release.

### Git Conventions
- Commit types: `feat` for new features, `fix` for bug fixes, `refactor` for refactoring, `docs` for documentation, and `chore` for engineering/config changes.
- Make sure XLua code generation is run before committing.

## Key File Index
| File / Directory | Description |
|------------------|-------------|
| `Assets/XLua/` | XLua framework and custom extensions |
| `Assets/Plugins/` | Third-party plugins |
| `Assets/StreamingAssets/` | Initial packaged assets |
| `HotfixOutput/` | Hot-update package output |
| `context/` | AI collaboration knowledge base |
| `docs/` | Human-facing Chinese documentation |
| `requirements/` | Requirement tracking directory |

