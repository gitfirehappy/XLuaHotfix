# Sub-Plan C: Lua Script Directory Auto-Management

> **Status**: C1+C2 completed and signed off, C3 pending Plan-B completion
> **Dependencies**: None (pure Editor tool, highest independence)
> **Recommendation**: Execute first, lowest risk
> **Sub-tasks**: C0 Manual mapping verification (prerequisite, completed) | C1 LuaAutoSyncConfig SO | C2 LuaDirectoryScanner | C3 Auto-tagging integration (execute after Plan-B)

---

## Prerequisite Step C0: Verify Current Directory-Container Mapping (Manual)

### Objective

Before coding, developer manually confirms the current 'Lua script directory <-> LuaScriptContainer SO' mappings are correct.
AI cannot directly inspect SO asset internal references; developer needs to verify in Unity Editor.

### Items to Confirm

1. Open Unity Editor, find all `LuaScriptContainer` type `.asset` files in Project window
2. Check each Container's `luaAssets` list, confirm referenced Lua files actually come from expected directories
3. Record actual directory-container mappings (will be used to populate LuaAutoSyncConfig initial configuration)

### Reference: Expected Mapping Table (Pending Developer Confirmation/Correction)

| Directory | Container SO | Status |
|-----------|-------------|--------|
| AboutXLua/LuaScripts/Core | Core.asset | Pending confirmation |
| AboutXLua/LuaScripts/Framework | Framework.asset | Pending confirmation |
| AboutXLua/LuaScripts/Game/Player | Player.asset | Pending confirmation |
| (Developer adds other mappings) | | |

**After this step is complete, notify confirmation results via ask_user, then begin C1.**

---

## Task C1: LuaAutoSyncConfig SO

### Objective

Create a configuration SO storing 'directory path -> LuaScriptContainer' mapping rules,
read by the scan tool, no hardcoded paths.

### Scope of Changes

| File | Change |
|------|--------|
| New: LuaAutoSyncConfig.cs | ScriptableObject with mapping list |

### Data Structure

```csharp
[CreateAssetMenu(menuName = 'XLua/Lua Auto Sync Config')]
public class LuaAutoSyncConfig : ScriptableObject
{
    [System.Serializable]
    public class DirectoryMapping
    {
        public string directoryPath;           // Scan directory (relative to Assets/)
        public LuaScriptContainer container;   // Corresponding Container SO (direct reference if existing)
        public string outputDirectory;         // Container SO generation directory (for new creation, relative to Assets/)
        public bool recursive = false;         // Whether to recursively scan subdirectories
    }
    public List<DirectoryMapping> mappings = new();
}
```

---

## Task C2: LuaDirectoryScanner Editor Tool

### Objective

Scan directories defined in LuaAutoSyncConfig, populate found .lua / .lua.txt files
into corresponding Container.luaAssets (union merge, never delete manual entries).

### Scope of Changes

| File | Change |
|------|--------|
| New: LuaDirectoryScanner.cs (Editor directory) | Scan + populate logic |
| LuaAddressableTagger.cs | Add 'Scan Directory' button (calls Scanner), integrated into existing window |

### Sync Strategy

- **Union merge**: Scan results + manually added files merged; manual entries never deleted
- **Manual trigger**: Triggered via Editor button; no file system monitoring
- **Optional pure auto mode**: When checked, scan results replace manual entries; off by default, requires secondary confirmation

### Current Directory-to-Container Default Mapping

Recommended initial config based on project state (pre-filled when writing LuaAutoSyncConfig.asset).
This mapping is fully configurable, not hardcoded; should list all existing container directories:

| Directory | Container | Recursive |
|-----------|-----------|-----------|
| AboutXLua/LuaScripts/Core | Core.asset | Yes |
| AboutXLua/LuaScripts/Framework | Framework.asset | Yes |
| AboutXLua/LuaScripts/Game/Player | Player.asset | Yes |

Note: Under Game/, scan all subdirectories that have containers; each subdirectory corresponds to one container (one-to-one).
When adding new subdirectories, manually add corresponding mapping entries in LuaAutoSyncConfig.

---

## Preservation Requirements (Must Pass)

- [ ] Existing Container SO luaAssets contents are never auto-cleared
- [ ] LuaDataBase structure unchanged
- [ ] LuaScriptContainer unchanged
- [ ] LuaAddressableTagger existing tag management functionality unchanged (only adds button)
- [ ] XLuaLoader unchanged

---

## Acceptance Criteria

1. Create LuaAutoSyncConfig.asset in Assets, configure mapping rules
2. Click 'Scan Directory' button
3. Core.asset's luaAssets shows all .lua files from Core/ directory
4. Manually added extra files in Core.asset are still preserved
5. Clicking 'Scan Directory' again produces no duplicate entries

---

## SO Container Generation Approach

Using Approach 2: Scripts and SO containers separated. LuaAutoSyncConfig stores 'scan directory -> container SO generation location' mapping.

LuaAutoSyncConfig DirectoryMapping structure adjustment:

```csharp
[System.Serializable]
public class DirectoryMapping
{
    public string directoryPath;           // Scan directory (relative to Assets/)
    public LuaScriptContainer container;   // Corresponding Container SO (direct reference if existing)
    public string outputDirectory;         // Container SO generation directory (for new creation, relative to Assets/)
    public bool recursive = false;         // Whether to recursively scan subdirectories
}
```

When the `container` field is empty, the tool auto-creates a Container SO named after the directory in `outputDirectory`.

---

## Task C3: Auto-Tagging Integration (Extension, Toggle-Controlled)

> **Note**: This task depends on AB package management refactoring (Plan-B) completion.
> Currently LuaAddressableTagger and SO batch tagging tools depend on AA group management.
> After Plan-B completion, these tools will switch to AB self-managed approach.
> **C3 executes after Plan-B completion; does not block C1/C2.**

### Objective

After scanning and populating Containers, automatically tag new Lua scripts with Addressable labels,
eliminating the manual step of calling the SO tagging tool. This feature is controlled by a configuration toggle.

### Design Approach

Current workflow:
```
1. Manual/tool -> Add Lua files to Container SO
2. Manual -> Open LuaAddressableTagger window, click tag button
```

Optimized workflow:
```
1. Click 'Scan Directory' button -> Auto-populate Container
2. (If toggle enabled) -> Auto-invoke tagging logic for new files
```

### Scope of Changes

| File | Change |
|------|--------|
| LuaAutoSyncConfig.cs | Add `bool autoTagAfterSync = false` toggle field |
| LuaDirectoryScanner.cs | After scan completes, if toggle enabled, call LuaAddressableTagger's tagging logic |
| LuaAddressableTagger.cs | Extract core tagging logic into externally-callable static method (e.g., `TagContainerAssets(LuaScriptContainer)`) |

### Configuration Notes

```csharp
// LuaAutoSyncConfig new field
[Header('Auto-tag after scan')]
[Tooltip('When enabled, auto-invoke Addressable tagging for new files after scan populates Container')]
public bool autoTagAfterSync = false;
```

- **Off by default**: Does not affect existing workflow; only activates when manually enabled
- **How it works**: After LuaDirectoryScanner completes scanning, checks this toggle; if true, iterates newly added files and calls `LuaAddressableTagger.TagContainerAssets()`
- **Console output**: After tagging completes, outputs summary to Console (N files tagged)

### Preservation Requirements

- [ ] autoTagAfterSync defaults to false; does not affect users who don't enable this feature
- [ ] LuaAddressableTagger window's manual tagging functionality unchanged
- [ ] Tagging rules identical to existing LuaAddressableTagger

### Acceptance Criteria

1. autoTagAfterSync = false: scanning does not trigger any tagging operations
2. autoTagAfterSync = true: newly added Lua files after scan automatically receive correct Addressable labels
3. Files with existing labels are not re-tagged or labels modified
4. Console outputs tagging summary

---

## Approval Checklist

- [x] Does directory-to-container mapping need to support 'one directory to multiple containers'?
  **Decision: One-to-one; one-to-many not supported.**
- [x] Game/ directory handling: scan only Game/Player/ subdirectory, or each subdirectory under Game/ gets its own container?
  **Decision: Not limited to Player/; scan all subdirectories that have containers. Mapping config maintained in LuaAutoSyncConfig.**
- [x] Should scanning auto-trigger on Unity asset save (AssetPostprocessor), or manual only?
  **Decision: Manual button trigger only.**