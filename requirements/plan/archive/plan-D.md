# Plan-D: Modular Assembly Splitting

> **Status**: Archived — 2026-05-19; stale draft, not executed as written
> **Created**: 2026-03-22
> **Updated**: 2026-03-22 - Adjusted to independent modules + static glue layer architecture

---

## Background & Objectives

Split the project into **independent assemblies** by functional module, communicating through a **static glue layer** (static facade method calls), achieving:
- Support for extension packs to be published independently
- No direct dependencies between modules; each can evolve independently
- Lightweight decoupling; avoids complexity of runtime event systems

---

## Splitting Approach

### Assembly Division

| Assembly Name | Included Modules | File Count | Dependencies |
|--------------|-----------------|------------|-------------|
| **Hotfix.Build** | Hotfix build module | ~25 | No runtime dependencies |
| **Framework.UI** | UI management module | ~5 | No direct deps (calls through glue layer) |
| **Framework.Config** | Data/text conversion module | ~17 | Editor-only, no runtime dependencies |
| **Framework.Dialogue** | Dialogue system module | ~12 | No direct deps (calls through glue layer) |

### Independent Modules + Static Glue Layer Architecture

```
+-------------------------------------------------------------+
|                      Assembly-CSharp                         |
|                      (Main Assembly)                         |
|  +----------+  +----------+  +----------+  +----------+    |
|  |  Bridge  |  |  Glue    |  |   Core   |  |   Game   |    |
|  | (Bridge) |  | (Facade) |  |(Utility) |  | (Logic)  |    |
|  +----------+  +----+-----+  +----------+  +----------+    |
|                     |                                        |
+---------------------+----------------------------------------+
                      |
                      v
+-------------------------------------------------------------+
|                  Independent Module Assemblies                |
|  +--------------+  +--------------+  +--------------+       |
|  |Hotfix.Build  |  |Framework.UI  |  |Framework.    |       |
|  |  (Build)     |  | (UI Mgmt)    |  |Dialogue      |       |
|  +--------------+  +--------------+  | (Dialogue)   |       |
|                                      +--------------+       |
|  +--------------+                                           |
|  |Framework.    |                                           |
|  |Config        |                                           |
|  |(Data Conv.)  |                                           |
|  +--------------+                                           |
+-------------------------------------------------------------+
```

**Core principle**: Assemblies have **no direct references**; communicate through static glue layer (Facade) for indirect calls

---

## Static Glue Layer Design

### Glue Layer Structure

```
Assets/AboutXLua/Scripts/Core/Glue/
|-- BuildFacade.cs       // Hotfix build module facade
|-- UIFacade.cs          // UI management module facade
-- DialogueFacade.cs    // Dialogue system module facade
```

### Facade Interface Design

```csharp
// BuildFacade.cs - Hotfix build module facade
public static class BuildFacade {
    public static void StartBuild(BuildType type) {
        BuildProjectManager.Instance.StartBuild(type);
    }

    public static string GetLastBuildOutput() {
        return BuildProjectManager.Instance.LastOutputPath;
    }
}

// UIFacade.cs - UI management module facade
public static class UIFacade {
    public static void ShowPanel(string panelName, object data = null) {
        UIManager.Instance.Show(panelName, data);
    }

    public static void HidePanel(string panelName) {
        UIManager.Instance.Hide(panelName);
    }
}

// DialogueFacade.cs - Dialogue system module facade
public static class DialogueFacade {
    public static void StartDialogue(string dialogueId) {
        DialogueController.Instance.StartDialogue(dialogueId);
    }

    public static void ShowDialogueUI(object data) {
        UIFacade.ShowPanel('DialoguePanel', data);
    }
}
```

### Call Examples

```csharp
// DialogueController calling UI (through glue layer)
public void StartDialogue(string dialogueId) {
    var data = LoadDialogueData(dialogueId);
    DialogueFacade.ShowDialogueUI(data);  // Call UI through glue layer
}

// Lua script calling C# modules
-- In Lua
Glue.BuildFacade.StartBuild('Hotfix')
Glue.UIFacade.ShowPanel('SettingsPanel')
```

---

## Detailed Design per Module

### D1: Hotfix.Build (Hotfix Build Module)

**Path**: `Assets/AboutXLua/Scripts/Core/Hotfix_AssetPackageManage/`

**Included files** (~25):
- AssetPackageManager.cs
- ABAssetIndex.cs
- ABBundleLoader.cs
- ABPackageBackend.cs
- AddressablesBackend.cs
- CatalogUpdater.cs
- HotfixManager.cs
- IAssetIndex.cs
- IPackageBackend.cs
- NetworkDownloader.cs
- PackageCleaner.cs
- BuildManage/ all subdirectories (~13 files)

**asmdef config**:
```json
{
    'name': 'Hotfix.Build',
    'rootNamespace': 'Hotfix.Build',
    'references': [
        'Unity.Addressables',
        'Unity.ResourceManager'
    ],
    'includePlatforms': [],
    'excludePlatforms': []
}
```

---

### D2: Framework.UI (UI Management Module)

**Path**: `Assets/AboutXLua/Scripts/Framework/UI/`

**Included files** (5):
- UIAnimation.cs
- UIFormBase.cs
- UIFormConfigSO.cs
- UIManager.cs
- UIResourceConfigSO.cs

**asmdef config**:
```json
{
    'name': 'Framework.UI',
    'rootNamespace': 'Framework.UI',
    'references': [],
    'includePlatforms': [],
    'excludePlatforms': []
}
```

---

### D3: Framework.Config (Data/Text Conversion Module)

**Path**: `Assets/AboutXLua/Scripts/Framework/ConfigConvertTool/`

**Included files** (~17):
- Core/ all (6 files)
- Editor/ all (2 files)
- Reader/ all (5 files)
- Writer/ all (4 files)
- SimpleParser/ all (2 files)

**asmdef config**:
```json
{
    'name': 'Framework.Config',
    'rootNamespace': 'Framework.Config',
    'references': [],
    'includePlatforms': ['Editor'],
    'excludePlatforms': []
}
```

---

### D4: Framework.Dialogue (Dialogue System Module)

**Path**: `Assets/AboutXLua/Scripts/Framework/Dialogue/`

**Included files** (~12):
- DialoguePanel.cs
- CharacterConfig.cs
- CsharpOnly/ all (10 files)

**asmdef config**:
```json
{
    'name': 'Framework.Dialogue',
    'rootNamespace': 'Framework.Dialogue',
    'references': [],
    'includePlatforms': [],
    'excludePlatforms': []
}
```

**Decoupling tasks (must complete before executing D4)**:
1. `DialogueFacade` as glue layer; dialogue module does not directly reference UI
2. `DialogueController` calls `DialogueFacade.ShowDialogueUI(data)` to display panel
3. Lua scripts call `DialogueFacade.StartDialogue(id)` to start dialogue

---

## Execution Order

**Synchronized strategy**: As each module is decoupled, simultaneously update glue layer to keep system runnable throughout

```
Module splitting synchronized with glue layer
|-- D0: Create glue layer directory + base Facades
|-- D1: Split Hotfix.Build + BuildFacade
|-- D2: Split Framework.UI + UIFacade
|-- D3: Split Framework.Config (Editor-only, no runtime deps)
-- D4: Decouple Dialogue + DialogueFacade + split Framework.Dialogue
```

**Steps for each module split**:
1. Create corresponding Facade
2. Move module code into new assembly
3. Verify glue layer calls work correctly
4. Proceed to next module

---

## Execution Task List

### D0: Glue Layer Creation
- [ ] Create `Assets/AboutXLua/Scripts/Core/Glue/` directory
- [ ] Create `BuildFacade.cs`
- [ ] Create `UIFacade.cs`
- [ ] Create `DialogueFacade.cs`

### D1: Hotfix.Build Assembly Creation
- [ ] Create `Hotfix.Build.asmdef`
- [ ] Move all .cs under Hotfix_AssetPackageManage/ into new assembly
- [ ] Place Editor scripts in `Hotfix.Build.Editor/` subdirectory

### D2: Framework.UI Assembly Creation
- [ ] Create `Framework.UI.asmdef`
- [ ] Move all .cs under Framework/UI/ into new assembly
- [ ] Ensure UIManager no longer directly references other modules

### D3: Framework.Config Assembly Creation
- [ ] Create `Framework.Config.asmdef`
- [ ] Move all .cs under ConfigConvertTool/ into new assembly
- [ ] Place Editor scripts in `Framework.Config.Editor/` subdirectory

### D4: Dialogue Decoupling + Framework.Dialogue Assembly Creation
- [ ] **Decouple**: Ensure Dialogue module calls UI through DialogueFacade
- [ ] Create `Framework.Dialogue.asmdef`
- [ ] Move all .cs under Dialogue/ into new assembly

---

## Preservation (Unchanged)

1. **Core/Utility/** — Base utility classes remain in main assembly
2. **Bridge/** — XLua bridge code remains in main assembly
3. **Glue/** — Glue layer in main assembly, sole entry point for inter-module calls
4. **Game/** — Game logic remains in main assembly
5. **Global/** — Startup logic remains in main assembly

---

## Advantages

| Comparison | Event Communication | Static Glue Layer |
|-----------|-------------------|--------------------|
| Coupling | Loose coupling | Medium coupling (through Facade) |
| Runtime overhead | Yes (event system) | None (direct calls) |
| Debug difficulty | Harder (async event flow) | Easy (sync calls) |
| Type safety | Weak (string event names) | Strong (compile-time checks) |
| Learning cost | Must understand event system | Similar to utility class calls |

---

## Risks & Considerations

1. **Assembly GUID changes**: GUIDs change after splitting
   - Unity auto-fixes most references
2. **Facade bloat**: As module interactions grow, Facades may become bloated
   - Solution: Facades only do simple forwarding, no business logic
3. **Circular dependency risk**: Must ensure Facades don't introduce circular dependencies

---

## Future Expansion

After splitting, each assembly can be independently published as Unity Packages:
- `com.hotfix.build@1.0.0.unitypackage`
- `com.framework.ui@1.0.0.unitypackage`
- `com.framework.config@1.0.0.unitypackage`
- `com.framework.dialogue@1.0.0.unitypackage`
