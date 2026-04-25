# XLua Runtime Integration

Last reviewed: 2026-04-25

## Scope

This document describes the project-side XLua integration under `Assets/XLuaFramework/Scripts/`. It covers the custom loader, bridge lifecycle, cross-language events, coroutine waiting, and XLua config assets.

Third-party XLua engine internals are documented separately in `xlua-third-party.md`.

## Runtime Entry Pieces

### `LuaEnvManager`

- owns access to the project Lua environment
- acts as the central entry point for code that needs the shared `LuaEnv`

### `XLuaLoader`

- registers a custom loader into a `LuaEnv`
- supports three modes:
  - `EditorOnly`
  - `AddressablesOnly`
  - `Hybrid`

## Lua Script Loading Model

### Editor path

- resolves module names to disk paths
- searches configured editor roots and supported extensions
- returns raw file bytes directly

### Runtime packaged path

- lazily loads `LuaScriptsIndex` through `AssetPackageManager`
- uses the index to map normalized Lua module names to Addressables keys
- synchronously loads `LuaScriptContainer`
- copies `TextAsset.bytes` into the loader cache
- immediately unloads the container asset after the bytes are copied

### Cache behavior

`XLuaLoader` keeps:

- `_contentCache`: module name -> byte array
- `_luaIndexAsset`: loaded `LuaScriptsIndex`

Release options:

- `ReleaseScriptCacheByContainer()`
- `ReleaseScriptCacheByLabel()`
- `ClearAllContentCache()`

## `LuaBehaviourBridge`

`LuaBehaviourBridge` binds Unity `MonoBehaviour` lifecycle to one or more Lua scripts loaded from a config asset.

### Script modes

- `Class`: Lua script must expose `New()`
- `Module`: the required Lua table itself becomes the runtime instance

Every new script should choose one mode explicitly.

### Initialization flow

1. wait for `LaunchSignal`
2. load `LuaBehaviourConfigSO` through `AssetPackageManager`
3. collect all `IBridge` components on the same `GameObject`
4. sort them by a fixed bridge order
5. require each configured Lua script
6. build a Lua instance according to `Class` or `Module` mode
7. cache hot lifecycle functions
8. initialize all bridge components against that Lua instance
9. invoke Lua-side startup callbacks

### Fixed bridge order

The hard-coded bridge order is:

1. `ScriptObjectBridge`
2. `InputBridge`
3. `Physics2DBridge`
4. `Collision2DBridge`
5. `AnimBridge`
6. `UIEventBridge`
7. `GizmosBridge`

This order is a project rule, not a suggestion.

### Cached lifecycle functions

For each Lua instance, the bridge caches references to:

- `Awake`
- `Start`
- `OnEnable`
- `OnDisable`
- `Update`
- `FixedUpdate`
- `LateUpdate`
- `OnDestroy`

The goal is to avoid repeated Lua table lookups on hot Unity callbacks.

## Bridge Components

Current bridge components include:

- `ScriptObjectBridge`
- `InputBridge`
- `Physics2DBridge`
- `Collision2DBridge`
- `AnimBridge`
- `UIEventBridge`
- `GizmosBridge`

All project bridge components are expected to implement the shared `IBridge` contract and initialize against a Lua instance.

## XLua Attribute Configuration

### `TypeMemberListSO`

This ScriptableObject stores project-managed XLua type/member configuration.

Supported tags:

- `Hotfix`
- `LuaCallCSharp`
- `CSharpCallLua`

It is explicitly intended for upper-layer project types; the file notes that system types are registered statically in XLua config.

### `XluaTypeConfigLoader`

- asynchronously loads tagged config assets
- builds the effective whitelist used by project XLua setup

Rule:

- if a new C# type becomes callable from Lua, the corresponding XLua config must be updated as part of the same change

## `EventCentre`

`EventCentre` is the approved cross-language event hub.

### Supported ports

- `CsharpToCsharp`
- `LuaToLua`
- `LuaToCsharp`
- `CsharpToLua`

### Why it exists

Lua function identity cannot be matched safely by reconstructing delegates on unregister. `EventCentre` solves this by storing the created delegate instance in `luaDelegateMap` with:

`(EventPort, eventName, LuaFunction) -> Delegate`

This is why Lua event unregistration must also go through `EventCentre`.

### Dispatch model

- 0, 1, and 2 argument cases use typed dispatch helpers
- 3+ arguments fall back to `DynamicInvoke`

## Coroutine Bridge

The project exposes a bidirectional coroutine waiting layer:

- `CoroutineBridge`
- `CSharpCoroutineScheduler`
- `LuaCoroutineScheduler`

Purpose:

- let Lua wait on C# coroutine-like work
- let C# coordinate with Lua coroutine progression
- keep and clean waiting relationships explicitly

## Practical AI Rules

- Prefer `LuaBehaviourBridge` and existing bridge components over ad hoc Lua lifecycle glue.
- Prefer `EventCentre` over raw delegate wiring for Lua/C# communication.
- Treat `TypeMemberListSO` updates as part of the definition of done for XLua API exposure changes.
