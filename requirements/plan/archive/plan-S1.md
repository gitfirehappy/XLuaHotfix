# Sub-Plan S1: Serialization Interface Layer + JsonCodec

> **Risk**: Low
> **Dependencies**: None (independent infrastructure)
> **Status**: Archived — 2026-05-19; approved and executed as part of serialization series

---

## Objective

Create a unified serialization tool layer (`ISerializationCodec` + `JsonCodec` + `SerializationUtility`) and replace all scattered `JsonUtility.FromJson/ToJson` + `File.ReadAllText/WriteAllText` call sites with the new API.

This establishes:

- A single entry point for all data serialization/deserialization
- Automatic format detection infrastructure (Binary header check → Binary codec, otherwise → JSON fallback)
- A codec registration mechanism for future Binary codec (S2) to plug in without changing call sites

S1 only introduces the JSON codec. Binary serialization is deferred to S2/S3.

---

## Background

Current codebase has 10 `JsonUtility.FromJson/ToJson` call sites scattered across 4 files:

| File | Calls | Layer |
|------|-------|-------|
| HotfixManager.cs (LegacyRuntime) | 5 | Legacy runtime |
| ABManifest.cs (Runtime/Models) | 2 | New runtime |
| BuildProjectManager.cs (Editor) | 2 | Build-time |
| LocalStatusExporter.cs (Editor) | 1 | Build-time |

Replacing the JSON library or adding a new format currently requires modifying each call site individually. The serialization utility layer eliminates this coupling.

---

## Confirmed Scope Boundaries

1. S1 only introduces `JsonCodec` — no binary serialization in this phase
2. `BinaryHeader` class is created for format detection infrastructure, but no Magic values are registered in S1 (DetectFormat always returns "json")
3. All 10 `JsonUtility` call sites are replaced in one batch
4. File format on disk remains identical JSON — zero behavioral change for existing files
5. `SerializationUtility` is placed in `Assets/AboutXLua/Scripts/Utility/Serialization/` as general-purpose infrastructure
6. No changes to data class definitions (`ABManifest`, `VersionState`, `Manifest`, `BuildIndexData`)

---

## Confirmed Design Decisions

### A. Interface Design

1. `ISerializationCodec` handles `byte[] <-> T` conversion only; file I/O is not its responsibility
2. `JsonCodec` wraps `JsonUtility.ToJson/FromJson` internally; external callers never touch `JsonUtility` directly
3. `SerializationUtility` is the high-level facade providing file-level read/write + automatic format detection
4. Convenience methods `DeserializeJson<T>(string)` and `SerializeToJson<T>(obj, prettyPrint)` are provided for call sites that pass raw JSON strings (e.g., `HotfixManager.ParseJson`, network responses)

### B. Format Detection

1. `SerializationUtility.Deserialize<T>(byte[])` (no codecId) triggers automatic detection
2. Detection logic: check first 4 bytes against registered Magic values → if match, use "binary" codec; otherwise, use "json" codec
3. In S1, no Magic values are registered → detection always returns "json" → behavior identical to current code
4. S2/S3 will register Magic values, at which point the same `ReadFromFile<T>` call automatically supports binary files

### C. Codec Registration

1. `JsonCodec` is registered in `SerializationUtility` static constructor as the default codec
2. Future codecs register via `SerializationUtility.RegisterCodec(codec)` — called during initialization (e.g., `[RuntimeInitializeOnLoadMethod]` or manual setup)
3. Same `codecId` overwrites previous registration (allows testing/mocking)

### D. Replacement Strategy

All 10 call sites replaced in one batch. Replacement patterns:

**Pattern 1 — File write (JSON):**
```csharp
// Before:
string json = JsonUtility.ToJson(obj, true);
File.WriteAllText(path, json);

// After:
SerializationUtility.WriteToFile(path, obj);
```

**Pattern 2 — File read + deserialize:**
```csharp
// Before:
string json = File.ReadAllText(path);
var obj = JsonUtility.FromJson<T>(json);

// After:
var obj = SerializationUtility.ReadFromFile<T>(path);
```

**Pattern 3 — String JSON deserialize (network/in-memory):**
```csharp
// Before:
var obj = JsonUtility.FromJson<T>(json);

// After:
var obj = SerializationUtility.DeserializeJson<T>(json);
```

**Pattern 4 — ManifestLoader async read (prepare for future binary):**
```csharp
// Before:
string json = await Task.Run(() => File.ReadAllText(path));
var manifest = ABManifest.DeserializeFromJson(json);

// After:
byte[] data = await Task.Run(() => File.ReadAllBytes(path));
var manifest = SerializationUtility.Deserialize<ABManifest>(data);
manifest.Initialize();
```

---

## New Files

| File | Path | Lines (est.) | Description |
|------|------|-------------|-------------|
| ISerializationCodec.cs | Utility/Serialization/ | ~20 | Codec interface: CodecId, Serialize<T>, Deserialize<T> |
| JsonCodec.cs | Utility/Serialization/ | ~35 | JsonUtility wrapper, PrettyPrint option |
| SerializationUtility.cs | Utility/Serialization/ | ~150 | High-level facade: codec registry, format detection, sync/async file I/O |
| BinaryHeader.cs | Utility/Serialization/ | ~40 | Header constants + Magic registry + HasValidMagic detection |

All paths relative to `Assets/AboutXLua/Scripts/`.

---

## Modified Files

| File | Change | Risk |
|------|--------|------|
| ABManifest.cs | `DeserializeFromJson` / `SerializeToJson` internal impl → SerializationUtility | Low — API signature unchanged |
| ManifestLoader.cs | `File.ReadAllText` + `DeserializeFromJson` → `File.ReadAllBytes` + `SerializationUtility.Deserialize` + `Initialize()` | Low — behavior identical, prepares for binary |
| HotfixManager.cs | `ParseJson<T>` internal impl → `SerializationUtility.DeserializeJson<T>`; `File.WriteAllText` + `JsonUtility.ToJson` → `SerializationUtility.WriteToFile`; `File.ReadAllText` + `JsonUtility.FromJson` → `SerializationUtility.ReadFromFile` | Low — all internal changes, external API unchanged |
| BuildProjectManager.cs | 2x `JsonUtility.ToJson` + `File.WriteAllText` → `SerializationUtility.WriteToFile` | Low — editor only |
| LocalStatusExporter.cs | 1x `JsonUtility.ToJson` + `File.WriteAllText` → `SerializationUtility.WriteToFile` | Low — editor only |

---

## Task Breakdown

| Task | Content | Depends On |
|------|---------|-----------|
| S1-T1 | Create `ISerializationCodec.cs` | — |
| S1-T2 | Create `JsonCodec.cs` | T1 |
| S1-T3 | Create `BinaryHeader.cs` (format detection infrastructure, no Magic registered) | — |
| S1-T4 | Create `SerializationUtility.cs` (codec registry + format detection + sync/async file I/O) | T1+T2+T3 |
| S1-T5 | Replace ABManifest.cs + ManifestLoader.cs call sites | T4 |
| S1-T6 | Replace HotfixManager.cs call sites (5 places) | T4 |
| S1-T7 | Replace BuildProjectManager.cs + LocalStatusExporter.cs call sites | T4 |
| S1-T8 | Compilation verification (dotnet build) + grep confirmation (zero remaining direct JsonUtility usage in target files) | T5+T6+T7 |

---

## Invariants (Must Hold After S1)

1. All existing JSON files remain readable without any modification
2. All written JSON files are byte-identical to current output (same JsonUtility.ToJson behavior)
3. Zero direct `JsonUtility.FromJson/ToJson` calls remain in the 5 target files
4. `SerializationUtility.DetectFormat` returns "json" for all existing files (no binary Magic registered)
5. `dotnet build XLuaHotfix.sln` passes with 0 errors

---

## Not In Scope

- Binary codec implementation (S2)
- ABManifest binary serialization (S3)
- Runtime binary format switching (S4)
- Data class field changes
- HotfixManager new/old backend split (separate plan, can be done before or after S1)
- Android StreamingAssets UnityWebRequest adaptation (deferred per existing decision)

---

## Approval Checklist

- [ ] Agree to create serialization tool layer in `Utility/Serialization/`
- [ ] Agree to replace all 10 JsonUtility call sites in one batch
- [ ] Agree that S1 produces zero behavioral change (JSON format preserved, same file output)
- [ ] Agree that `BinaryHeader` is created but inactive in S1 (no Magic registered)
- [ ] Agree to `ManifestLoader` switching from `ReadAllText` to `ReadAllBytes` (prepares for binary, functionally identical for JSON)
