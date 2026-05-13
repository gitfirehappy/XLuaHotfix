# Knowledge Index

Start here before any task.

## Architecture
- `architecture/INDEX.md` - module structure, technical stack, runtime systems
- `architecture/system-overview.md` - verified top-level module map, stack baseline, and architecture boundaries
- `architecture/resource-build-and-release.md` - build-time exports, differential snapshots, and release operations
- `architecture/runtime-resource-loading.md` - runtime loading facade, hotfix boundary, and Legacy-vs-AB split
- `architecture/collector-framework.md` - collector data model, rule contracts, and current classification behavior
- `architecture/xlua-runtime.md` - project-side XLua integration, bridge lifecycle, and cross-language runtime services
- `architecture/xlua-third-party.md` - third-party XLua internals used to reason about wrappers, delegates, GC, and hotfix

## Business
- `business/INDEX.md` - domain-specific gameplay and business knowledge

## Conventions
- `conventions/INDEX.md` - coding, git, testing, and collaboration conventions
- `conventions/collaboration.md` - project workflow and resource-management rules
- `conventions/field-semantics.md` - field naming semantics reference (Labels vs Tags, Address vs AssetPath, etc.)

## Dependencies
- `dependencies/INDEX.md` - dependency domain index
- `dependencies/integration/INDEX.md` - direct project dependency notes
- `dependencies/reference/INDEX.md` - external framework, protocol, and research references
  - `dependencies/reference/zhihu-resource-management/INDEX.md` - Zhihu column: Unity resource management deep-dive (10/17 chapters)

## Mistakes
- `mistakes/INDEX.md` - verified historical errors, troubleshooting notes, and prevention rules
- `mistakes/troubleshooting.md` - confirmed problems and solutions

Rules:
- Only store verified knowledge here.
- Mark uncertain items as [UNVERIFIED].
- Keep `context/` in English because it is AI-facing.
- Keep `context/` aligned with the latest verified project reality.
- Keep plan sequencing, TODOs, and workflow tracking out of `context/`.
- Keep human-facing documentation in `docs/`, in Chinese, and update it only when there is an actual human documentation need.
