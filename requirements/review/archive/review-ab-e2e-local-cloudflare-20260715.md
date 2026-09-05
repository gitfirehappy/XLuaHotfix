# AB Local And Cloudflare E2E Review

> **Date**: 2026-07-15
> **Reviewer**: Codex
> **Scope**: Real AB Full/Hotfix export, Local and Cloudflare publication, Windows Player baseline and forward-update startup
> **Method**: Unity 2022.3.62f3 batch builds, hidden IL2CPP Players, localhost request tracing, Cloudflare Pages deployment, runtime logs, manifest inspection, and file hash/CRC verification

## Findings

### Resolved P0: AB Player Startup Could Not Load `LuaScriptsIndex`

The AB delivery and activation path succeeds, but every tested Player stops before `GameLauncher` becomes ready because `XLuaLoader` cannot load `LuaScriptsIndex`.

Evidence:

- AB Full 5.0.0 completed all 12 pipeline tasks and initialized `ABPackageManager` with 167 assets and 41 Bundles.
- The generated AB manifest contains six `LuaScriptContainer` entries and the `ModuleRegistry` TextAsset, but contains zero entries whose Address is `LuaScriptsIndex`.
- The generated index exists at `Assets/Build/LuaScriptsIndex.asset`; the active AB Collector inputs do not collect that path.
- Player logs report `[NOT_FOUND] LuaScriptsIndex`, then `[LuaLoader] Unable to load LuaScriptsIndex`, followed by `module 'ModuleRegistry' not found`.
- The failure reproduced in three independent runtime states: Local Full baseline 5.0.0, Local forward update to 5.0.1, and clean Cloudflare forward update to 5.0.1.

Impact:

- No AB Player can complete normal project startup even though package inspection, download, reuse, activation, pointer persistence, and `ABPackageManager` initialization succeed.
- The requested end-to-end runtime chain is therefore not product-complete.

No runtime, Collector, loader, or bridge fix was applied in this E2E run because its approved scope required diagnosis and reporting only.

## Resolution And Remaining Evidence

- S1 added independent AA/AB `LuaScriptsIndex` publication and manifest validation. Fresh AA and AB Full Players reached Lua initialization and `GameLauncher` completion on 2026-07-19.
- S2/S2R added typed AB resolution and BundleName-level physical-load single-flight. Fresh AA and AB Full/build/clean-runtime chains passed on 2026-07-20 without `LuaScriptsIndex`, extraction, dependency, or duplicate-Bundle errors.
- The original P0 is therefore resolved. This review remains active only because the post-fix Local AB Hotfix forward-update and Cloudflare runtime-update gates have not been rerun.

## Verified Delivery Behavior

- Full 5.0.0: 41 Bundles; Full output and StreamingAssets passed size, CRC32, and MD5 checks against `ABManifest.json`.
- Hotfix 5.0.1: one modified Bundle, 12,252-byte cumulative delivery, with StreamingAssets retained at Full 5.0.0.
- Local update: requested PackageIndex, `ABManifest.bin`, and exactly one changed Bundle; reused 40 Full Bundles and persisted a complete 41-Bundle 5.0.1 package.
- Cloudflare update: Pages deployment succeeded; custom-domain PackageIndex returned AB 5.0.1 with `no-store`; versioned manifest and Bundle returned one-year immutable caching.
- Cloudflare runtime: all 41 files passed size, CRC32, and MD5 checks; downloaded manifest and delivery Bundle matched the Cloudflare mirror by SHA-256.
- Cleanup: localhost port 54321 was released, Unity and Player process counts were zero, and the isolated persistentData root was removed.

## Environment Notes

- Wrangler 4.110.0 requires the existing localhost proxy at `127.0.0.1:12000` in this environment. Direct Wrangler API calls timed out until temporary process-only `HTTP_PROXY` and `HTTPS_PROXY` values were supplied.
- `-nographics` Player runs emit unsupported shader messages. They did not affect package download, Bundle verification, or `ABPackageManager` initialization and are not classified as product findings from this test.
- Full-worktree `git diff --check` exits 2 only for trailing spaces in Unity-generated `Assets/StreamingAssets/BuildIndex.json.meta` and `Assets/StreamingAssets/bundles.meta` empty fields. Pipeline-owned generated metadata was not edited manually; the authored configuration, plan, progress, and review files pass their scoped whitespace check.
