# LuaAssetRuntime Seam (XLuaFramework ⇄ FYAsset)

XLuaFramework is exportable as a standalone mini-framework depending only on XLua + UnityEngine.

- boundary contract: `XLuaFramework/Scripts/Resource/ILuaAssetLoader.cs` (async/sync/unload, error as `string`, null = success).
- injection: `LuaAssetRuntime.SetLoader(...)` called explicitly by the host startup shell (`GameLauncher.BootPhase`, before any lua bridge load). No attribute magic; unregistered use throws `InvalidOperationException` with remediation text.
- FYAsset adapter: `FYAsset/Scripts/Compat/Runtime/FYAssetLuaAssetLoaderAdapter.cs` (Compat side; downgrade `RuntimeMessage` → `error?.ToString()`).
- verification: s3 scenario `XLuaFrameworkBoundary` (XLuaFramework must not reference any FYAsset-declared type), plus `UpperPackageBoundary` (evolved 2026-07-04: UI/Game/Dialogue keep facade, XLuaFramework zero).
- error-shape downgrade note: runtime consumers only ever used `error?.ToString()` / null checks at the 4 bridge/loader call sites, so the string shape is behavior-isomorphic.

Decision record: requirements/plan/archive/2026-07-24-xluaframework-export.md
