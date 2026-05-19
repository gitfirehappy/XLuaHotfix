# Architecture Knowledge Index

AI-facing architecture documents. These files describe the current verified structure of the project, not aspirational design notes.

## Files
- `system-overview.md` - top-level system boundaries, module map, and project-wide constraints
- `resource-build-and-release.md` - build-time export flow, differential snapshots, and release operations
- `runtime-resource-loading.md` - runtime asset indexing/loading flow, hotfix pipeline boundary, and AB-vs-AA split
- `collector-framework.md` - current collector data model, rule contracts, and classification behavior
- `xlua-runtime.md` - project-side XLua integration, Lua loader, bridge lifecycle, event center, and coroutine bridge
- `xlua-third-party.md` - third-party XLua internals relevant to interop, generated wrappers, delegates, GC, and hotfix hooks

