# XLua Third-Party Runtime

Last reviewed: 2026-04-25

## Scope

This document summarizes the third-party XLua runtime model used by the project. It is meant for AI agents that need to understand why project-side bridge code works the way it does.

Project-specific wrapper code lives in `xlua-runtime.md`. This file covers the XLua engine layer under `Assets/XLua/`.

## Core Runtime Topology

XLua bridges two worlds:

- C# world: `LuaEnv`, `ObjectTranslator`, generated wrappers, delegate bridges
- Lua world: Lua stack, tables, metatables, userdata

The lowest bridge is the Lua C API exposed through `LuaDLL.Lua`.

## Key Types

| Type | Responsibility |
| --- | --- |
| `LuaEnv` | owns one Lua VM and its `lua_State*` |
| `ObjectTranslator` | central C# object <-> Lua value translation layer |
| `LuaDLL.Lua` | P/Invoke surface for the Lua/XLua native API |
| `ObjectPool` | pool of C# objects referenced from Lua userdata |
| `ObjectTranslatorPool` | global `lua_State* -> ObjectTranslator` lookup |
| `StaticLuaCallbacks` | C# implementations of Lua metamethod callbacks |
| `LuaBase` | base class for C# wrappers around Lua references |
| `LuaFunction` | C# handle for calling a Lua function |
| `LuaTable` | C# handle for a Lua table |
| `DelegateBridgeBase` | base layer for Lua-function-to-C#-delegate bridging |

## `LuaEnv` Initialization Model

One `LuaEnv` typically:

1. creates a new `lua_State`
2. opens XLua support
3. creates an `ObjectTranslator`
4. registers helper libraries such as XLua import/cast functions
5. registers itself into `ObjectTranslatorPool`
6. installs panic/error helpers
7. adds searchers/loaders
8. exposes the `CS` namespace table to Lua

This is why project code can register a custom loader and then use `require(...)` plus `CS.TypeName`.

## C# Object Push Model

### Reference types

Reference types are tracked through `ObjectPool` plus a reverse map.

High-level model:

- if the C# object already has a pool slot, reuse it
- otherwise add it to `ObjectPool`
- push userdata containing the object-pool index
- attach the type metatable

Reason:

- the same C# object should not appear as unrelated Lua userdata instances

### Value types

Performance-sensitive structs can use direct by-value packing/unpacking instead of boxing-heavy generic object paths.

This is why types such as vectors and colors can be much cheaper on hot Lua/C# call paths.

## Lua Calling C#

### Generated wrapper path

Types marked for `LuaCallCSharp` can produce generated `*Wrap.cs` files.

Those wrappers:

- register instance methods, static methods, getters, setters, and constructors
- create the metatable shape used by Lua
- avoid slower reflection-based fallback paths

### Reflection fallback

If generated wrappers are missing, XLua can still use reflection-based wrapping.

Tradeoff:

- behavior is similar
- performance is worse
- editor warnings such as `NOT_GEN_WARNING` are expected signals that generated code is missing

## C# Calling Lua

`LuaFunction` and other `LuaBase` descendants wrap Lua registry references.

High-level call flow:

1. get the Lua function from the registry
2. push C# arguments through the translator
3. call `lua_pcall`
4. read return values if needed
5. restore stack state

This is the foundation used by project code such as `LuaBehaviourBridge` for `Update`, `Start`, and similar callbacks.

## Delegate Bridge Model

When a Lua function is converted into a C# delegate:

- XLua creates or reuses a bridge object tied to the Lua function registry reference
- it generates or selects an implementation method matching the target delegate signature
- the created delegate is cached per delegate type

Why this matters:

- delegate identity and Lua function identity need stable bridging
- repeated bridging should not create unlimited duplicate wrappers

This is the low-level reason the project also keeps its own `luaDelegateMap` in `EventCentre`.

## GC Cooperation

### Lua GC -> C# cleanup

When Lua drops userdata, XLua can run `__gc` callbacks that release the associated `ObjectPool` slot and reverse-map entry.

### C# GC -> Lua cleanup

When a `LuaBase`-derived wrapper is collected on the C# side, XLua cannot free Lua references directly from the finalizer thread. Instead it queues cleanup and later processes it on the Lua side.

Why this matters:

- leaked `LuaFunction` / `LuaTable` handles create retained registry references
- disposal discipline still matters even though XLua has deferred cleanup paths

## Hotfix Hook Model

Types marked with `[Hotfix]` can have method entry points routed through Lua-provided replacements.

High-level model:

- generated/injected code checks whether a hotfix delegate bridge is installed for a method
- if a hotfix bridge exists, Lua runs instead of the original C# body
- otherwise the original C# method executes

This is a runtime redirection mechanism, not just a source-level convention.

## Multi-`LuaEnv` Support

`ObjectTranslatorPool` lets XLua find the correct translator from a `lua_State*`.

Implication:

- multiple `LuaEnv` instances can exist in one process
- each environment keeps isolated object-pool and translation state

## AI Rules For XLua Low-Level Work

- Prefer understanding whether a problem belongs to project wrapper code or to XLua engine behavior before changing anything.
- Missing generated wrappers and missing type config are different failures; do not collapse them into one diagnosis.
- Be careful with delegate identity, object lifetime, and disposal when changing cross-language glue.
