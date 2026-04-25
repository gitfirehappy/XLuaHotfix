# Sub-Plan A: UI Framework Optimization

> **Status**: A1/A2 completed, A3 not executed
> **Dependencies**: None (can execute independently)
> **Sub-tasks**: ~~A1 UIAnimation parameterization~~ Done | ~~A2 DynamicGroup responsibility expansion~~ Done | A3 UIViewModel (optional, not executing this round)

---

## Current State Analysis

### Multi-Canvas Coordination (Existing Mechanism, Needs Understanding)

Current UIManager supports multiple Canvases through UIResourceConfigSO:

```
UIResourceConfigSO
  -- UIRegistrationGroup[]          (each corresponds to a scene Canvas, e.g., MainCanvas / PopupCanvas)
       |-- parentCanvasName         Name of the Canvas GameObject in the scene
       -- UIGroupDefinition[]       Groups under the Canvas
            |-- groupID             Group identifier (used for dynamic panel mounting)
            |-- manualUIForms       Static panel prefabs (directly registered)
            -- additionalPreloadForms  Template panel prefabs (preloaded but not immediately registered)
```

**Current multi-Canvas limitations**:
- Static panels are bound to Canvas via formToCanvasMap at initialization; cannot be switched later
- Dynamic panels look up parent Canvas via groupID -> canvasGroups, but groupID is currently only used for 'batch-generating same-type panels' (e.g., buff cards)
- Two concepts (static formToCanvasMap + dynamic canvasGroups) have unclear responsibility boundaries

---

## Task A1: UIAnimation Parameterization

### Design Approach

UIAnimation methods already have optional duration parameters (e.g., FadeIn(form, callback, duration=0.5f)),
but UIFormBase calls use hardcoded values and don't read from UIFormConfigSO.

Just need to add parameter fields to UIFormConfigSO and pass them when UIFormBase calls.
UIAnimation.cs mostly needs no changes (method signatures already have parameters).

### Scope of Changes

| File | Change |
|------|--------|
| UIFormConfigSO.cs | Add 4 fields under [Header('Animation Parameters')] |
| UIFormBase.cs | Pass corresponding parameters when calling UIAnimation in OpenAnim / CloseAnim |
| UIAnimation.cs | Mostly unchanged (methods already have params); parameterize fromOffset for FadeSlideIn |

### New Fields (UIFormConfigSO)

```csharp
[Header('Animation Parameters (0 = use built-in default)')]
[Tooltip('Fade in/out duration (seconds)')]
public float fadeInDuration = 0f;
public float fadeOutDuration = 0f;

[Tooltip('Scale/pop-in target multiplier (Pop/Zoom animations)')]
public float zoomScale = 0f;

[Tooltip('Slide/FadeSlide initial offset (pixels, 0 = use screen width/height)')]
public float slideOffset = 0f;
```

### Preservation Requirements

- [ ] Existing UIFormConfigSO.asset files need no modification (new fields default to 0, falling back to hardcoded values)
- [ ] UIAnimation static method external signatures unchanged (internal parameter passing adjusted)

---

## Task A2: DynamicGroup Responsibility Expansion & Clarification

### Design Approach

DynamicGroup's original design was for 'batch-generating same-type panels' (e.g., buff cards),
but its mechanism is essentially 'a group of dynamically instantiated panels sharing the same Canvas, identified by groupID'.
This mechanism can serve broader scenarios; need to clarify responsibilities and moderately expand.

### Current DynamicGroup Capabilities

```
UIManager
  |-- dynamicFormGroups: Dictionary<groupID, List<UIFormBase>>
  |   Manages all dynamic panel instances within a group
  |-- canvasGroups: Dictionary<groupID, Canvas>
  |   Each groupID's corresponding parent Canvas
  |-- CreateDynamicForm<T>()      Instantiate + register to group
  |-- ShowDynamicForm()           Show (not pushed to showFormStack)
  |-- HideDynamicForm()           Hide
  |-- ClearDynamicFormsInGroup()  Batch clear group
  -- SetGroupPanelsAlpha()       Batch set transparency (highlight selected item)
```

**Existing but unclear design semantics**:
- groupID serves as both 'Canvas binding key' and 'instance group key'; confusing during initialization config
- additionalPreloadForms field name is unintuitive (actually means 'dynamic panel templates')

### Scope of Changes

| Change | Description |
|--------|-------------|
| UIResourceConfigSO.cs | Rename additionalPreloadForms to dynamicFormTemplates (more semantic) |
| UIManager.cs | Add GetDynamicFormCount(groupID) query method |
| UIFormBase.cs | Add /// comments clarifying IsDynamicForm / DynamicGroupID usage scenarios |

**Note**: Field renaming affects existing .asset files. Approach: keep old field + add [FormerlySerializedAs] attribute;
Unity serialization system auto-migrates, no manual .asset file modifications needed.

### DynamicGroup Usage Scenarios (Written as Code Comments)

```
Scenario 1 (original): Batch-generate same-type panels
  E.g., buff cards: one BuffCardGroup, corresponding BuffCanvas,
  each card is a UIFormBase instance, data injected via SO

Scenario 2 (expanded): List/Grid UI
  E.g., inventory slots, skill bar icons; batch generate + unified show/hide management

Scenario 3 (expanded): Toast / Notification popup queue
  Same groupID manages same-type notifications; ClearDynamicFormsInGroup clears all at once
```

### Multi-Canvas Coordination Notes (Clarify Boundaries, No Logic Changes)

Current multi-Canvas support is already complete; this round only adds documentation and comments:
- **Static panels** -> Use UIRegistrationGroup.parentCanvasName to bind to specified Canvas
- **Dynamic panels** -> Use groupID to find parent Canvas in canvasGroups (groupID registered in UIResourceConfigSO)
- Same Canvas can have multiple groupIDs (UIGroupDefinition[] is an array)

> **Future expansion directions (not in this round's scope, file as separate issue/requirement)**:
> - In-group sorting (SortGroup)
> - Cross-Canvas panel migration
> - Group capacity limits
>
> This round only: responsibility clarification + field renaming + documentation comments.

### Preservation Requirements

- [ ] Dynamic panel creation/show-hide/clear logic unchanged
- [ ] SetGroupPanelsAlpha and other existing methods unchanged
- [ ] Field renaming uses FormerlySerializedAs; .asset files auto-migrate, no manual changes needed

---

## Task A3: UIViewModel

Not executing this round. Will be added when concrete ViewModel requirements arise.

---

## Approval Checklist

- [x] A1: Should slideOffset field be split into X/Y directions, or use a single value?
  **Decision: Single float value, no X/Y split.**
- [x] A2: Confirm renaming additionalPreloadForms (affects existing .asset, FormerlySerializedAs auto-migration)?
  **Decision: Confirmed renaming to dynamicFormTemplates; use FormerlySerializedAs for auto-migration.**
- [x] A2: Should DynamicGroup's other capabilities be expanded (e.g., in-group sorting, cross-Canvas panel migration)?
  **Decision: No additional capability expansion this round. Clarify responsibilities and add comments only. Expandable optimization items filed as future tickets.**
- [x] A3: Include in this refactoring round, or add as-needed later?
  **Decision: Not included in this refactoring round; add as-needed later.**