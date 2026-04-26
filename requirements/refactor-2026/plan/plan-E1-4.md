# Sub-Plan E1-4: Editor UI — BuildPipelineWindow + Collector Panel

> **Risk**: Low (Editor-only UI, no runtime impact)
> **Dependencies**: E1-1 (data model, enums, **including IGroupRule**), E1-2 (default rules for dropdown), E1-3 (IgnorePatterns field on Collector)
> **Status**: ⚠️ Approved, needs plan update (2026-04-26 audit: add GroupRule dropdown + RuleDropdownHelper IGroupRule scanning) `[审计修正]`

---

## Objective

Create the BuildPipelineWindow editor window shell with sidebar navigation (5 functional areas), and fully implement the Collector area. The other 4 areas (Pipeline, Builder, Inspector, Settings) are placeholder panels for future sub-plans.

The Collector area provides a three-level tree editor (Package → Group → Collector) with drag-and-drop reordering, right-click context menus, property panel, rule dropdown selection, and save-time configuration validation.

---

## Confirmed Design Decisions

### D1: Window Form — Full Shell (Option A)

BuildPipelineWindow is the single entry point. Left sidebar with 5 buttons routes to content panels. Only Collector panel is implemented in E1-4; other 4 panels show placeholder text.

### D2: UI Technology — IMGUI TreeView

Uses Unity's built-in IMGUI TreeView API. Consistent with all existing editor code in the project. No UI Toolkit.

### D3: V1 Capabilities — Drag + Context Menu

- Drag-and-drop reordering (same-level only)
- Right-click context menus (Add/Delete/Duplicate)
- Collection preview deferred until E1-3 is implemented

### D4: Rule Selection — Dropdown Menu

RuleDropdownHelper scans all IAddressRule/IPackRule/IFilterRule/**IGroupRule** implementations via reflection, caches class name lists, and renders EditorGUI.Popup dropdowns. New Collectors auto-fill default rule names from Constants. `[审计修正]`

### D5: Validation Timing — On Save

Validation triggers automatically when SerializedObject.ApplyModifiedProperties() detects changes. Results displayed in bottom area of Collector panel.

---

## Window Architecture

```
BuildPipelineWindow (EditorWindow, menu: XLua/Build Pipeline)
├── Left Sidebar (~120px fixed width)
│   ├── [Collector]  ← E1-4 implemented
│   ├── [Pipeline]   ← placeholder
│   ├── [Builder]    ← placeholder
│   ├── [Inspector]  ← placeholder
│   └── [Settings]   ← placeholder
│
└── Right Content Area (flexible width)
    └── Active panel's OnGUI()
```

Each panel implements a simple interface:

```csharp
public interface IBuildPipelinePanel
{
    string PanelName { get; }
    void OnEnable(EditorWindow window);
    void OnGUI(Rect rect);
    void OnDisable();
}
```

BuildPipelineWindow holds a `IBuildPipelinePanel[]` array and routes to the active panel.

---

## Collector Panel Layout

```
┌──────────────────────┬──────────────────────────────────┐
│ Tree View (~40%)     │ Property Panel (~60%)             │
│                      │                                   │
│ ▼ 📦 hotfix         │ ┌ Collector ─────────────────────┐│
│   ▼ 📁 ui           │ │ CollectPath: [Assets/Art/UI  ]▪││
│     📄 prefabs       │ │ CollectorType: [Main       ▾] ││
│     📄 textures      │ │ ForcePayloadKind: [Auto    ▾] ││
│   ▶ 📁 audio        │ │ AddressRule: [AddressByFile ▾] ││
│ ▶ 📦 builtin        │ │ PackRule: [PackByCollect   ▾] ││
│                      │ │ FilterRule: [CollectAll    ▾] ││
│                      │ │ Tags: [+] ui, panel           ││
│                      │ │ IgnorePatterns: [+] *.bak     ││
│                      │ └───────────────────────────────┘│
├──────────────────────┴──────────────────────────────────┤
│ Validation Results (shown only when errors/warnings)     │
│ ❌ CROSS_PACKAGE_OVERLAP: hotfix/Assets/Art ↔ builtin   │
│ ⚠️ PATH_NOT_FOUND: Assets/Art/WIP does not exist        │
└─────────────────────────────────────────────────────────┘
```

### UX Optimizations (Approved)

1. **Draggable Splitter**: The vertical line between Tree View and Property Panel is draggable to adjust width ratios.
2. **Top Toolbar**: A toolbar above the TreeView provides a SearchField for filtering nodes and Expand All / Collapse All buttons.
3. **Directory Drag & Drop**: Users can drag a folder from the Project window into the Property Panel's `CollectPath` field or onto a node to automatically fill the path.
4. **Validation Interaction**: TreeView nodes display a red error badge if they (or their children) contain validation errors. Double-clicking an error in the Validation Results panel automatically selects and highlights the corresponding problematic node in the TreeView.

---

## TreeView Data Model

```csharp
public class CollectorTreeViewItem : TreeViewItem
{
    public enum NodeType { Package, Group, Collector }
    
    public NodeType Type;
    public int PackageIndex;      // index in CollectorSetting.Packages
    public int GroupIndex;        // index in Package.Groups (-1 for Package nodes)
    public int CollectorIndex;    // index in Group.Collectors (-1 for Package/Group nodes)
}
```

Tree depth mapping:
- depth 0 = Package
- depth 1 = Group
- depth 2 = Collector

Display names:
- Package: `📦 {PackageName}`
- Group: `📁 {GroupName}`
- Collector: `📄 {last segment of CollectPath}`

---

## Drag-and-Drop Rules

- Same-level reordering only (Package↔Package, Group↔Group within same Package, Collector↔Collector within same Group)
- Cross-level drag is rejected (DragAndDropVisualMode.Rejected)
- Implementation: SerializedProperty.MoveArrayElement for data reorder, automatic Undo support
- TreeView.SetupDragAndDrop / HandleDragAndDrop overrides

---

## Right-Click Context Menus

| Node Type | Menu Items |
|-----------|------------|
| Empty area | Add Package |
| Package | Add Group / — / Delete Package / Duplicate Package |
| Group | Add Collector / — / Delete Group / Duplicate Group |
| Collector | Delete Collector / Duplicate Collector |

- Delete shows EditorUtility.DisplayDialog confirmation
- Duplicate performs deep copy of all fields (including nested lists)
- Add operations insert at end of parent list with sensible defaults:
  - New Package: PackageName = "NewPackage"
  - New Group: GroupName = "NewGroup"
  - New Collector: CollectPath = "", CollectorType = Main, ForcePayloadKind = Auto, rules = default constants from Constants.cs

---

## Property Panel

Renders different fields based on selected node type. Uses SerializedObject/SerializedProperty for Undo support.

### Package Selected

| Field | Widget |
|-------|--------|
| PackageName | EditorGUILayout.TextField |
| SharePolicy | Disabled label: "Available after E4 implementation" |

### Group Selected

| Field | Widget |
|-------|--------|
| GroupName | EditorGUILayout.TextField |
| Tags | ReorderableList of string |

### Collector Selected

| Field | Widget |
|-------|--------|
| CollectPath | TextField + folder picker button (EditorUtility.OpenFolderPanel, relative to Assets/) |
| CollectorType | EditorGUILayout.EnumPopup (ECollectorType) |
| ForcePayloadKind | EditorGUILayout.EnumPopup (EForcePayloadKind) |
| AddressRuleName | RuleDropdownHelper.Popup (IAddressRule implementations) |
| PackRuleName | RuleDropdownHelper.Popup (IPackRule implementations) |
| FilterRuleName | RuleDropdownHelper.Popup (IFilterRule implementations) |
| GroupRuleName | RuleDropdownHelper.Popup (IGroupRule implementations) `[审计新增]` |
| Tags | ReorderableList of string |
| IgnorePatterns | ReorderableList of string |

---

## RuleDropdownHelper

```csharp
public static class RuleDropdownHelper
{
    // On first call: scan all assemblies for IAddressRule/IPackRule/IFilterRule/IGroupRule implementations
    // Cache: Dictionary<Type, string[]> interfaceType → class name array
    // Popup: EditorGUI.Popup with cached names, returns selected class name string
    
    public static string AddressRulePopup(Rect rect, string currentValue);
    public static string PackRulePopup(Rect rect, string currentValue);
    public static string FilterRulePopup(Rect rect, string currentValue);
    
    // Force re-scan (called when new rules are added)
    public static void ClearCache();
}
```

Scan filters: concrete classes only (not abstract, not interface), must have parameterless constructor.

---

## Configuration Validation

### CollectorSettingValidator

Static utility class. Called automatically when SO is modified (ApplyModifiedProperties returns true).

```csharp
public static class CollectorSettingValidator
{
    public static List<ValidationMessage> Validate(CollectorSetting setting);
}

public class ValidationMessage
{
    public ValidationSeverity Severity;  // Error, Warning
    public string Code;
    public string Message;
    public int PackageIndex;    // -1 if global
    public int GroupIndex;      // -1 if Package-level
    public int CollectorIndex;  // -1 if Group-level
}

public enum ValidationSeverity { Warning = 0, Error = 1 }
```

### Validation Rules (9 items)

| # | Condition | Severity | Code |
|---|-----------|----------|------|
| 1 | PackageName is empty | Error | `EMPTY_PACKAGE_NAME` |
| 2 | GroupName is empty | Error | `EMPTY_GROUP_NAME` |
| 3 | CollectPath is empty | Error | `EMPTY_COLLECT_PATH` |
| 4 | CollectPath directory does not exist | Warning | `PATH_NOT_FOUND` |
| 5 | Cross-Package path overlap | Error | `CROSS_PACKAGE_OVERLAP` |
| 6 | Same-depth same-path within Package | Error | `SAME_PATH_CONFLICT` |
| 7 | Rule class name cannot be resolved | Error | `RULE_NOT_FOUND` |
| 8 | Duplicate PackageName | Error | `DUPLICATE_PACKAGE_NAME` |
| 9 | Duplicate GroupName within same Package | Warning | `DUPLICATE_GROUP_NAME` |

### Validation Result Display

Bottom area of Collector panel. Only visible when messages exist. Each message shows severity icon (❌/⚠️) + code + description. Clicking a message selects the corresponding tree node.

---

## New Files

| File | Path | Assembly | Lines (est.) | Description |
|------|------|----------|-------------|-------------|
| BuildPipelineWindow.cs | Build/Editor/ | Editor | ~130 | Main window shell + sidebar + 5-area routing + menu entry |
| IBuildPipelinePanel.cs | Build/Editor/ | Editor | ~15 | Panel interface (PanelName, OnEnable, OnGUI, OnDisable) |
| PlaceholderPanel.cs | Build/Editor/ | Editor | ~25 | Generic placeholder panel for Pipeline/Builder/Inspector/Settings |
| CollectorPanel.cs | Build/Collector/Editor/UI/ | Editor | ~120 | Collector area coordinator: TreeView + PropertyPanel + Validator display |
| CollectorTreeView.cs | Build/Collector/Editor/UI/ | Editor | ~280 | IMGUI TreeView: 3-level tree + drag reorder + right-click menus |
| CollectorPropertyPanel.cs | Build/Collector/Editor/UI/ | Editor | ~220 | Property panel: Package/Group/Collector field editors |
| CollectorSettingValidator.cs | Build/Collector/Editor/ | Editor | ~100 | 9-rule validation + ValidationMessage + ValidationSeverity |
| RuleDropdownHelper.cs | Build/Collector/Editor/UI/ | Editor | ~70 | Rule reflection scan + cache + Popup rendering |

All paths relative to `Assets/FYAsset/Scripts/`.

---

## Modified Files

| File | Change | Risk |
|------|--------|------|
| Constants.cs | Add `BUILD_PIPELINE_WINDOW_MENU_PATH` menu path constant | Low — additive |

---

## Task Breakdown

| Task | Content | Depends On |
|------|---------|-----------|
| E1-4-T1 | Create `IBuildPipelinePanel.cs` interface | — |
| E1-4-T2 | Create `PlaceholderPanel.cs` (generic placeholder for 4 unimplemented areas) | T1 |
| E1-4-T3 | Create `BuildPipelineWindow.cs` (window shell + sidebar + routing + menu entry) | T1, T2 |
| E1-4-T4 | Create `CollectorTreeView.cs` — base: build 3-level tree from CollectorSetting + selection + collapse | E1-1 done |
| E1-4-T5 | Extend `CollectorTreeView.cs` — drag-and-drop same-level reordering | T4 |
| E1-4-T6 | Extend `CollectorTreeView.cs` — right-click context menus (Add/Delete/Duplicate) | T4 |
| E1-4-T7 | Create `RuleDropdownHelper.cs` (reflection scan + cache + Popup) | E1-1 done |
| E1-4-T8 | Create `CollectorPropertyPanel.cs` (3 property panels + rule dropdowns + FolderPicker + ReorderableList) | T7 |
| E1-4-T9 | Create `CollectorSettingValidator.cs` (9 validation rules + message data structure) | E1-1 done |
| E1-4-T10 | Create `CollectorPanel.cs` (coordinator: TreeView + PropertyPanel + Validator display + splitter) | T4, T8, T9 |
| E1-4-T11 | Integrate: register CollectorPanel in BuildPipelineWindow + Constants.cs menu path | T3, T10 |
| E1-4-T12 | Compilation verification (dotnet build) | All above |

---

## Invariants (Must Hold After E1-4)

1. BuildPipelineWindow opens via menu, sidebar 5 buttons switch panels, Collector area functional, other 4 show placeholder
2. TreeView correctly reflects CollectorSetting SO's 3-level structure (Package → Group → Collector)
3. Drag reorder is same-level only; SO data updates correctly with Undo support
4. Right-click Add creates node with sensible defaults; Delete shows confirmation; Duplicate deep-copies all fields
5. New Collector auto-fills AddressRuleName/PackRuleName/FilterRuleName with default values from Constants
6. Rule dropdown lists all implemented rule classes (at minimum the 3 defaults from E1-2)
7. Save-time validation triggers automatically; errors/warnings display in bottom area
8. Cross-Package path overlap correctly reports Error (CROSS_PACKAGE_OVERLAP)
9. All edit operations use SerializedObject/SerializedProperty, supporting Undo/Redo
10. `dotnet build XLuaHotfix.sln` passes with 0 errors

---

## Not In Scope

- Collection preview (depends on E1-3 CollectionScanner — add after E1-3 implementation)
- Pipeline/Builder/Inspector/Settings panel implementations (future sub-plans)
- Cross-level drag-and-drop (V1 uses Duplicate + Delete as workaround)
- SharePolicy editing on Package (E4 enables this)
- Build triggering (E5)
- Advanced pack rules in dropdown (E2 adds more options)

---

## Approval Checklist

- [x] Agree to BuildPipelineWindow as single entry point with sidebar 5-area routing
- [x] Agree to IMGUI TreeView for tree editing
- [x] Agree to same-level drag reordering only (no cross-level drag in V1)
- [x] Agree to right-click context menus (Add/Delete/Duplicate per node type)
- [x] Agree to RuleDropdownHelper reflection scan + Popup for rule selection
- [x] Agree to new Collector auto-filling default rule names
- [x] Agree to save-time validation (9 rules) with bottom-area result display
- [x] Agree to IBuildPipelinePanel interface for panel abstraction
- [x] Agree to 8 new files + 1 modified file
- [x] Agree to collection preview deferred until E1-3 is implemented
