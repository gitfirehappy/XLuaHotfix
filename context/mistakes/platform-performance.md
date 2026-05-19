# Platform & Performance Pitfalls

> Editor/Runtime boundary violations, platform assumptions, allocation hot paths, and wasted computation.

## PL-01: Runtime Assembly Directly Using UnityEditor APIs

**Symptom:** Runtime type uses `using UnityEditor;` and calls `AssetDatabase.*`. Multiple Runtime classes depend on this type. Player build path not functional.

**Root cause:** Editor-only APIs placed into Runtime type without `#if UNITY_EDITOR` guards.

**Prevention:** Any Runtime assembly type must not use `UnityEditor` APIs without `#if UNITY_EDITOR`. Enforce via asmdef or build checks.

---

## PL-02: File.Exists on Android StreamingAssets

**Symptom:** `File.Exists(fallbackPath)` always returns `false` under `Application.streamingAssetsPath` on Android (APK is archive, not filesystem). Fallback path never entered.

**Root cause:** Platform-specific filesystem semantics not accounted for.

**Prevention:** Never use `File.Exists`/`File.ReadAllText` on `streamingAssetsPath` without consulting platform docs. Use `UnityWebRequest` path for StreamingAssets on mobile.

---

## PL-03: Mixed Platform-Specific and Cross-Platform I/O for Same Data

**Symptom:** Bundle fallback uses `File.Exists` + `AssetBundle.LoadFromFile` (platform-specific) while manifest loads via `FileHelper.ReadAllBytesAsync` (cross-platform). Inconsistent within same subsystem.

**Root cause:** Different I/O abstractions for similar data types.

**Prevention:** Within the same runtime subsystem, I/O for similar data must use consistent cross-platform abstractions.

---

## PL-04: Unity Main-Thread Assumptions Implicit

**Symptom:** Async methods call Unity APIs requiring main thread, but no assertion or documentation enforces this.

**Root cause:** Implicit thread affinity without enforcement.

**Prevention:** Every async API with thread affinity must explicitly document AND enforce the expected synchronization context.

---

## PL-05: Per-Frame new GUIStyle()

**Symptom:** `new GUIStyle()` in OnGUI/draw methods. Allocates GC memory every Editor repaint.

**Root cause:** Inline style creation instead of caching.

**Prevention:** Any `new GUIStyle()` in OnGUI/draw must be cached as `static readonly`.

---

## PL-06: AssetDatabase.LoadAssetAtPath Every Frame

**Symptom:** SO loaded from disk + migrator run on every `OnHeaderGUI` call (every repaint).

**Root cause:** No caching of loaded asset reference.

**Prevention:** `AssetDatabase.LoadAssetAtPath` in OnGUI paths must be cached, invalidated only on asset change (`AssetPostprocessor` or `Undo.undoRedoPerformed`).

---

## PL-07: LINQ Contains on Small Arrays

**Symptom:** `Enumerable.Contains` allocates `IEnumerator<T>` for arrays of 1-4 elements.

**Root cause:** LINQ convenience over static array method.

**Prevention:** For small arrays (≤10 elements), use `Array.IndexOf(arr, val) >= 0` — zero allocation.

---

## PL-08: Unused AssetDatabase API Call in Hot Path

**Symptom:** `AssetDatabase.GetMainAssetTypeAtPath(dep)` called in BFS loop, result assigned to local variable never read. 100K wasted expensive calls.

**Root cause:** Leftover debugging code not cleaned up.

**Prevention:** Every `AssetDatabase.*` call must be reviewed for actual consumption of its result. Never call with result unused.

---

## PL-09: Pipeline Re-Iterates Same Data

**Symptom:** Two separate loops over same groups. First pass results discarded; second pass rebuilds from scratch.

**Root cause:** Second pass added without extending first pass.

**Prevention:** When iterating a collection multiple times, check if first pass can produce results for later passes. Each additional O(n) pass must be justified.

---

## PL-10: Hash Re-Computed From Disk When Already Available

**Symptom:** Verification re-reads entire bundle files to compute hash already produced during build step. 500MB bundles read twice.

**Root cause:** Intermediate result does not carry pre-computed hash.

**Prevention:** Pipeline must flow computed results forward. If a later stage recomputes, data flow is missing a key.

---

## PL-11: BFS Cycle Detection O(n) Scan Instead of O(1) HashSet

**Symptom:** `for` loop over entire `bfsStack` on each dependency check. O(stack_depth × deps_count).

**Root cause:** Used `List` for traversal without parallel `HashSet`.

**Prevention:** "Check if element exists in current traversal path" → always use `HashSet` for membership test when using BFS/DFS.

---

## PL-12: DateTime.Now for Version Timestamps

**Symptom:** `DateTime.Now` as version fallback. Different timezones → different version strings for same code.

**Root cause:** Local time chosen for sortable identifier.

**Prevention:** Version strings, build timestamps, sortable IDs must use `DateTime.UtcNow`. `DateTime.Now` is for user-display only.

---

## PL-13: CLI Parsing Missing Bounds Check

**Symptom:** `args[i + 1]` accessed without checking `i + 1 < args.Length`. `--backend` as last arg → `IndexOutOfRangeException`.

**Root cause:** Assumes every flag has a value.

**Prevention:** Every CLI parser must validate flag-value pair completeness before value access. Bounds check mandatory.

---

## PL-14: Quit-Time Fire-and-Forget Async

**Symptom:** Async re-init triggered without `await` during application quit. Task may be faulted.

**Root cause:** Async called without lifecycle awareness.

**Prevention:** Quit-time code must avoid fire-and-forget async. Either await with timeout, or use synchronous cleanup.

---

## PL-15: Unity 2022.3 UI Toolkit Cursor API Mismatch

**Symptom:** Editor build fails when assigning `new StyleCursor(MouseCursor.ResizeHorizontal)` or similar values to `VisualElement.style.cursor`.

**Root cause:** In Unity `2022.3.62f3`, `style.cursor` expects a UI Toolkit `Cursor` value shape, not an IMGUI `MouseCursor` enum wrapped by `StyleCursor`.

**Fix:** Remove the cursor style assignment for splitter handles and keep pointer-event drag behavior intact.

**Prevention:** Do not port IMGUI cursor enums directly into UI Toolkit styles. Verify editor UI API assumptions with `dotnet build` after migration.
