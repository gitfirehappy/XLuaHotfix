---
title: Build Graph Architecture — Data Model, Node System, and Execution Engine
source: Zhihu Column "游戏资源管理" by 伽蓝之洞, Chapters 15-17
status: verified
---

# Build Graph Architecture

This document consolidates Chapters 15 through 17 of the Zhihu column, covering the full architecture of the Build Graph system: its data model, 19-node type system, execution engine, visual editor, incremental build cache, and CI/headless support.

---

## 1. Core Design Philosophy

### 1.1 The Problem

Traditional Unity build pipelines face two failure modes:

- **Addressables mode**: Configure groups and labels in the Inspector. Build logic is hidden inside Addressables' Build Script black box. To understand why an asset ended up in a particular bundle, you must dig through thousands of log lines.
- **Script mode**: Write a single `BuildAssetBundles()` function with hundreds of lines. After three handovers, nobody dares to modify it.

Build Graph's answer: **visualize the build pipeline as a directed acyclic graph (DAG)**. Each node is an independent functional unit. Resource data flows left-to-right, and every intermediate result is previewable in-editor.

### 1.2 Comparison with Addressables

| Dimension | Addressables | Build Graph |
|---|---|---|
| Configuration | Inspector form-filling | Visual node graph |
| Build pipeline | Built-in Build Script (black box) | Customizable node graph (fully transparent) |
| Intermediate results | Invisible | Any node previewable |
| Resource types | AssetBundle only | AB / Zip / Copy hybrid pipeline |
| CI support | Cloud Content Delivery | HeadlessBuilder (full support) |
| Incremental build | Relies on Unity Cache Server | Custom cache + change detection + dependency propagation |

### 1.3 Three-Layer Architecture

1. **Data model layer**: Defines the minimum data units flowing between nodes, graph serialization format, and execution context.
2. **Core engine layer**: Handles graph execution — topological sort, data flow scheduling, build API wrapping, CI support, incremental caching.
3. **Visual editor layer**: A WYSIWYG editor built on Unity's GraphView API.

---

## 2. Data Model Design (Chapter 16)

### 2.1 AssetBuildInfo — The Assembly-Line Part

The minimum data unit flowing between nodes. Each node receives a list of `AssetBuildInfo`, processes them, and produces a new list.

```csharp
// Assets/Editor/BuildBundleTools/Data/AssetBuildInfo.cs
namespace YY.Build
{
    [Serializable]
    public class AssetBuildInfo
    {
        // Full project path (e.g., "Assets/Res/UI/Login.prefab")
        public string AssetPath;

        // Target bundle name (e.g., "ui/login.bundle")
        // Empty string = not yet assigned
        public string BundleName;

        // Addressable name for LoadAsset (defaults to filename)
        public string AddressableName;

        public AssetBuildInfo(string path)
        {
            AssetPath = path;
            AddressableName = Path.GetFileNameWithoutExtension(path);
            BundleName = string.Empty;
        }
    }
}
```

**Design points:**

- **AssetPath** is the unique identifier. All subsequent nodes (dependency analysis, deduplication, grouping) operate with path as the key.
- **BundleName is deferred** — resources scanned out initially have an empty `BundleName`. Only after passing through a GrouperNode is a bundle name assigned. This follows the pipeline order: collect resources, analyze dependencies, then decide grouping.
- **AddressableName** provides logical addressing decoupled from file paths. Default is filename minus extension. Allows later nodes to rename (e.g., "bg_01.png" -> "Background").
- AssetPath uses Unity project-relative paths (`Assets/Res/UI/Login.prefab`) rather than absolute paths, because all Unity Editor APIs use this format (`AssetDatabase.GetDependencies()`, `AssetDatabase.LoadAssetAtPath()`, `AssetImporter.GetAtPath()`). Absolute paths would break cross-machine sharing.

**Lifecycle through the pipeline:**

```
DirectoryNode scan      -> AssetPath = "Assets/Res/UI/Login.prefab", BundleName = ""
FilterNode filter       -> BundleName still empty
DependencyNode resolve  -> BundleName still empty
DeduplicatorNode dedup  -> BundleName still empty
GrouperNode assign      -> BundleName = "ui/login.bundle"   (only here)
BuildBundleNode build   -> reads BundleName, calls SBP
```

### 2.2 BuildContext — Execution Context

The container that carries data in and out of each node's execution. It bundles four categories of information:

```csharp
// Assets/Editor/BuildBundleTools/BuildContext.cs
namespace YY.Build
{
    public class BuildContext
    {
        // Core data: current list of assets in flight
        public List<AssetBuildInfo> Assets = new List<AssetBuildInfo>();

        // Execution log (each node appends; StringBuilder avoids
        // intermediate string allocations across many nodes)
        public StringBuilder Logs = new StringBuilder();

        // Build mode switch: false = preview, true = real build
        public bool IsBuildMode = false;

        // Build report items (only populated when IsBuildMode = true)
        public List<BuildReportItem> Reports = new List<BuildReportItem>();
    }
}
```

**IsBuildMode — the bridge between preview and build:**

The same node's `Execute` method takes different branches based on `IsBuildMode`:

```csharp
public override Dictionary<string, BuildContext> Execute(BuildContext context)
{
    if (context.IsBuildMode)
    {
        // Build mode: actually invoke SBP
        context.Logs.AppendLine("[BuildBundleNode] Building AssetBundles...");

        var bundlesByGroup = context.Assets
            .Where(a => !string.IsNullOrEmpty(a.BundleName))
            .GroupBy(a => a.BundleName)
            .ToDictionary(g => g.Key, g => g.ToList());

        bool success = PipelineLauncher.Build(OutputPath, TargetPlatform,
            BuildOptions, context.Assets, ManifestName);

        context.Logs.AppendLine(success
            ? "  Build Success!"
            : "  Build Failed!");
    }
    else
    {
        // Preview mode: log only, no real operations
        context.Logs.AppendLine("[BuildBundleNode] Ready to build. (Preview Mode)");
    }

    return new Dictionary<string, BuildContext> { { "Pass", context } };
}
```

This means the same graph serves both preview and build — only the runtime mode flag differs. In preview mode, all Source, Process, and Strategy nodes run their full logic (they never write to disk anyway). Only Export nodes (BuildBundleNode, BuildZipNode, BuildCopyNode) check `IsBuildMode` and skip real operations.

**Logs** use `StringBuilder` so every node appends to the same mutable object. After `GraphRunner` finishes all nodes, the final `BuildContext.Logs` contains the complete pipeline trace — a natural end-to-end tracking mechanism without extra instrumentation.

### 2.3 BuildReportItem — Structured Build Reports

Logs are for humans; reports are structured data for programs and CI.

```csharp
public class BuildReportItem
{
    public string NodeTitle;       // Which node generated it
    public string Category;        // Artifact type: AssetBundle, Zip, Copy
    public string OutputPath;      // Output directory
    public int AssetCount;         // Assets processed
    public long OutputSizeBytes;   // Total output file bytes
    public double DurationSeconds; // Wall-clock seconds
    public bool IsSuccess;         // Success flag
    public string Message;         // Notes or error message
}
```

Each Export node generates its own report item in build mode:

```csharp
// In BuildBundleNode.Execute:
context.Reports.Add(new BuildReportItem
{
    NodeTitle = title,
    Category = "AssetBundle",
    OutputPath = OutputPath,
    AssetCount = context.Assets.Count,
    OutputSizeBytes = totalSize,
    DurationSeconds = watch.Elapsed.TotalSeconds,
    IsSuccess = success,
    Message = success ? $"OK ({changedCount} rebuilt, {skippedCount} skipped)"
                      : "PipelineLauncher Failed"
});
```

`HeadlessBuilder` (CI mode) iterates all Reports and outputs to Console:

```
[SUCCESS] Export: Build AssetBundles (AssetBundle): OK (3 rebuilt, 2 skipped)
  Output: StreamingRes/Bundles
  Assets: 156, Size: 12.4 MB
[SUCCESS] Export: Build Zip (Zip): OK
  Output: StreamingRes/Lua
  Assets: 48, Size: 2.1 MB
```

CI scripts parse these structured lines to determine build success without reading raw text logs.

### 2.4 BuildGraphAsset — Graph Persistence Format

The entire graph is serialized as a `ScriptableObject` asset file:

```csharp
// Assets/Editor/BuildBundleTools/BuildGraphAsset.cs
namespace YY.Build.Data
{
    [CreateAssetMenu(fileName = "NewBuildGraph",
                     menuName = "GameTools/Build Pipeline/Build Graph")]
    public class BuildGraphAsset : ScriptableObject
    {
        public List<BuildNodeData> Nodes = new List<BuildNodeData>();
        public List<BuildEdgeData> Edges = new List<BuildEdgeData>();
    }

    [Serializable]
    public class BuildNodeData
    {
        public string NodeGUID;       // Unique ID, stable across save cycles
        public string Title;          // Node title (user double-click to rename)
        public Vector2 Position;      // Canvas coordinates
        public string NodeType;       // Full C# class name (used for reflection)
        public string JsonData;       // Node-specific custom data as JSON
    }

    [Serializable]
    public class BuildEdgeData
    {
        public string BaseNodeGUID;   // Output-side node GUID
        public string BasePortName;   // Output port name
        public string TargetNodeGUID; // Input-side node GUID
        public string TargetPortName; // Input port name
    }
}
```

**Why ScriptableObject?**

1. Native Unity asset management — create, save, load, delete via standard Unity workflows.
2. Undo support — `Undo.RecordObject(_currentAsset, "Save Graph")` auto-records each snapshot; Ctrl+Z restores to prior state.
3. Double-click to open — `[OnOpenAsset]` callback opens the Build Graph editor when the `.asset` file is double-clicked in the Project window:

```csharp
[UnityEditor.Callbacks.OnOpenAsset(1)]
public static bool OnOpenAsset(int instanceID, int line)
{
    var asset = EditorUtility.InstanceIDToObject(instanceID) as BuildGraphAsset;
    if (asset != null)
    {
        Open(asset);
        return true;
    }
    return false;
}
```

**Why store node type as a string (`NodeType`) instead of an enum?**

Because enums are closed. Every new node type would require modifying the enum definition. With string + reflection, adding a new node requires only writing a new `BaseBuildNode` subclass — no existing code changes. On load:

```csharp
// First try current assembly
var nodeType = Type.GetType(nodeData.NodeType);

// Fallback: search all loaded assemblies
if (nodeType == null)
    nodeType = AppDomain.CurrentDomain.GetAssemblies()
        .SelectMany(a => a.GetTypes())
        .FirstOrDefault(t => t.FullName == nodeData.NodeType);

var node = Activator.CreateInstance(nodeType) as BaseBuildNode;
```

**Why NodeGUID?**

- `BuildEdgeData` uses `BaseNodeGUID` and `TargetNodeGUID` to record connections. Node object references cannot be serialized (GraphView.Node is not a UnityEngine.Object).
- GUID is stable across save cycles. On reload, edges are matched via GUID lookup.
- Copy-paste must generate a new GUID to avoid ID collision.

**JsonData** stores per-node custom state. `BaseBuildNode` declares two virtual methods:

```csharp
public virtual string SaveToJSON() => "{}";
public virtual void LoadFromJSON(string json) { }
```

Each node subclass overrides these. Example for `DirectoryNode`:

```csharp
[Serializable] private class NodeData { public string path; }

public override string SaveToJSON()
{
    return JsonUtility.ToJson(new NodeData { path = FolderPath });
}

public override void LoadFromJSON(string json)
{
    var data = JsonUtility.FromJson<NodeData>(json);
    if (data != null)
    {
        FolderPath = data.path;
        if (_textField != null) _textField.value = FolderPath;
    }
}
```

Different nodes store completely different JSON content:

| Node | JsonData Content |
|---|---|
| DirectoryNode | `{"path":"Assets/Res/UI"}` |
| GrouperNode | `{"mode":1,"key":"ui","suffix":".bundle"}` |
| BuildBundleNode | `{"outPath":"StreamingRes/Bundles","target":5,"options":256,"manifestName":"sys_manifest"}` |
| FilterNode | `[{"ruleType":"Extension","pattern":".prefab"}]` |

This keeps `BuildGraphAsset` agnostic to internal node structure — it only stores strings; nodes handle their own parse.

**Save flow (`SaveGraph`):**

```csharp
private void SaveGraph(string undoName = "Save Graph")
{
    if (_currentAsset == null) return;

    Undo.RecordObject(_currentAsset, undoName);

    _currentAsset.Nodes.Clear();
    _currentAsset.Edges.Clear();

    foreach (var node in _graphView.nodes.ToList().Cast<BaseBuildNode>())
    {
        _currentAsset.Nodes.Add(new BuildNodeData
        {
            NodeGUID = node.GUID,
            Position = node.GetPosition().position,
            NodeType = node.GetType().FullName,
            Title = node.title,
            JsonData = node.SaveToJSON()
        });
    }

    foreach (var edge in _graphView.edges.ToList())
    {
        var outputNode = edge.output.node as BaseBuildNode;
        var inputNode = edge.input.node as BaseBuildNode;
        _currentAsset.Edges.Add(new BuildEdgeData
        {
            BaseNodeGUID = outputNode.GUID,
            BasePortName = edge.output.portName,
            TargetNodeGUID = inputNode.GUID,
            TargetPortName = edge.input.portName
        });
    }

    EditorUtility.SetDirty(_currentAsset);
}
```

**Load flow (`LoadGraph`):** Clears the GraphView, then:

1. Reflectively create all nodes from `BuildNodeData`, restore GUID/position/JsonData
2. Rebuild edges (must happen after all nodes exist, because `outPort.ConnectTo(inPort)` requires both ends already added to GraphView)
3. Bind `OnDataChanged` callback so UI changes trigger auto-save

Edge rebuilding must be a second pass because `outPort.ConnectTo(inPort)` needs both endpoint nodes already added to the GraphView.

### 2.5 End-to-End Data Flow

A simple graph `DirectoryNode -> FilterNode -> GrouperNode -> BuildBundleNode -> BatchBuildNode`:

1. **DirectoryNode** scans folder, creates `AssetBuildInfo` per file (all with empty `BundleName`)
2. **FilterNode** keeps only files matching extension rules, discards the rest
3. **GrouperNode** assigns `BundleName` (e.g., all to `"ui/login.bundle"` in OneBundle mode)
4. **BuildBundleNode** groups by `BundleName`, calls `PipelineLauncher.Build()` if `IsBuildMode`, generates `BuildReportItem`
5. On save, `BuildGraphAsset` persists all node types, GUIDs, and JsonData

---

## 3. BaseBuildNode — The Node Foundation (Chapter 17)

### 3.1 Inheritance Chain

```
UnityEditor.Experimental.GraphView.Node     <- Unity's visual node
    └── BaseBuildNode                        <- Our extended base
        ├── DirectoryNode
        ├── FilterNode
        ├── GrouperNode
        ├── BuildBundleNode
        └── ... (19 types total)
```

Inheriting from `GraphView.Node` provides for free: drag-move, single/multi-select, delete, title bar, content area, port containers, stylesheet support. We add: identity (GUID), upstream connection tracking, execution, and serialization.

### 3.2 Constructor

```csharp
// Assets/Editor/BuildBundleTools/Nodes/BaseBuildNode.cs
namespace YY.Build.Graph
{
    public class BaseBuildNode : Node
    {
        public string GUID;
        public Action OnDataChanged;

        // Key: this node's input port name -> Value: list of (upstream node, upstream output port name)
        private Dictionary<string, List<(BaseBuildNode, string)>> _upstreamConnections
            = new Dictionary<string, List<(BaseBuildNode, string)>>();

        public BaseBuildNode()
        {
            GUID = Guid.NewGuid().ToString();
            styleSheets.Add(Resources.Load<StyleSheet>("BuildGraphStyles"));
            capabilities |= Capabilities.Renamable;
        }
    }
}
```

Three operations only in the constructor: generate GUID, load stylesheet (`Assets/Editor/BuildBundleTools/Resources/BuildGraphStyles.uss`), set Renamable capability. These do not depend on any subclass fields. Everything else goes in `Initialize()`.

### 3.3 GUID — The Node's Identity Card

GUID serves three critical roles:

**1. Serialization anchor** — `BuildNodeData.NodeGUID` persists the node's identity across save/load cycles.

**2. Edge endpoints** — `BuildEdgeData` stores `BaseNodeGUID` and `TargetNodeGUID` rather than object references (Node is not a `UnityEngine.Object`, cannot be serialized as a reference). On load, GUIDs are used to look up nodes in a dictionary.

**3. CurrentDataMap key** — `GraphRunner` indexes execution results by GUID:
```csharp
CurrentDataMap[node.GUID] = outputs;
```

**Why not `GetInstanceID()`?** `GetInstanceID()` is a `UnityEngine.Object` method. `GraphView.Node` does not inherit from `UnityEngine.Object`, so it has no such method.

**Copy-paste must refresh GUID.** In `BuildGraphView.UnserializeAndPasteImpl`:
```csharp
var node = Activator.CreateInstance(nodeType) as BaseBuildNode;
node.Initialize();
node.GUID = Guid.NewGuid().ToString();  // Force refresh
node.LoadFromJSON(json);
```
Failure to refresh causes: `CurrentDataMap` key collision, edge mismatch on rebuild, mutual data corruption on save/load.

### 3.4 Port System

**Creating ports:**

```csharp
protected Port AddInputPort(string name, Port.Capacity capacity = Port.Capacity.Single)
{
    var port = InstantiatePort(Orientation.Horizontal, Direction.Input, capacity, typeof(bool));
    port.portName = name;
    port.name = name;
    inputContainer.Add(port);
    RefreshExpandedState();
    RefreshPorts();
    return port;
}

protected Port AddOutputPort(string name, Port.Capacity capacity = Port.Capacity.Single)
{
    var port = InstantiatePort(Orientation.Horizontal, Direction.Output, capacity, typeof(bool));
    port.portName = name;
    port.name = name;
    outputContainer.Add(port);
    RefreshExpandedState();
    RefreshPorts();
    return port;
}
```

Key parameters:

- **`Port.Capacity.Single`** vs **`Port.Capacity.Multi`** — Single allows only one connection (suitable for most input ports). Multi allows multiple connections (for aggregation nodes like `MergeNode` or `BatchBuildNode`).
- **`typeof(bool)`** is a placeholder type for `InstantiatePort`. Actual data flows through `BuildContext`, not the port's type parameter. The real compatibility check is in `GetCompatiblePorts` (opposing directions only).
- **`portName` and `name` are set identically** because `Q<Port>(name)` is used during graph load to find port objects by name:
```csharp
var outPort = outNode.outputContainer.Q<Port>(edgeData.BasePortName);
var inPort = inNode.inputContainer.Q<Port>(edgeData.TargetPortName);
```

**Deleting dynamic ports** (used by nodes like `FilterNode` that change port count at runtime):

```csharp
protected void ClearOutputPorts()
{
    // Disconnect all edges first
    foreach (var element in outputContainer.Children())
    {
        if (element is Port port && port.connected)
        {
            var edgesToDelete = port.connections.ToList();
            foreach (var edge in edgesToDelete)
            {
                if (edge.input != null) edge.input.Disconnect(edge);
                if (edge.output != null) edge.output.Disconnect(edge);
                edge.RemoveFromHierarchy();
            }
        }
    }
    outputContainer.Clear();
    RefreshPorts();
    RefreshExpandedState();
}
```

Edges must be disconnected before ports are cleared. Deleting a port without disconnecting its edges leaves edge objects referencing destroyed Port objects, causing `NullReferenceException`. Edges do not auto-delete with ports — explicit `Disconnect` + `RemoveFromHierarchy` is required.

### 3.5 Upstream Connection Management

The core data structure enabling data flow:

```csharp
// Key: this node's input port name (e.g., "Source", "Input")
// Value: list of upstream connections
private Dictionary<string, List<(BaseBuildNode, string)>> _upstreamConnections;
```

Each tuple contains:
- `BaseBuildNode` — the upstream node instance
- `string` — the upstream node's output port name (needed because a node like `DeduplicatorNode` has multiple outputs: "Combined (Unique)" and "Deps Only (Unique)")

**Why not use `Edge` objects?** `Edge` is a UI-layer object that depends on the `GraphView` environment. Headless build mode has no `GraphView`, but still needs connection topology. `_upstreamConnections` is a pure data structure independent of UI.

**Three methods:**

```csharp
// Called by GraphRunner before each execution round
public void ResetConnections()
{
    _upstreamConnections.Clear();
}

// Called by PrepareConnectionsFromUI (Editor) or HeadlessBuilder (CI)
public void AddUpstreamConnection(string inputPortName,
    BaseBuildNode sourceNode, string sourcePortName)
{
    if (!_upstreamConnections.ContainsKey(inputPortName))
        _upstreamConnections[inputPortName] = new List<(BaseBuildNode, string)>();

    _upstreamConnections[inputPortName].Add((sourceNode, sourcePortName));
}

// Used by GraphRunner for topological sort
public Dictionary<string, List<(BaseBuildNode, string)>> GetUpstreamConnections()
{
    return _upstreamConnections;
}

// Used by GraphRunner for topological sort
public List<BaseBuildNode> GetInputNodes()
{
    var nodes = new List<BaseBuildNode>();
    foreach (var list in _upstreamConnections.Values)
        foreach (var (node, _) in list)
            nodes.Add(node);
    return nodes;
}
```

**Two injection paths for connections:**

**Editor mode** — `GraphRunner.PrepareConnectionsFromUI` traverses UI edges and converts to `_upstreamConnections`:

```csharp
// GraphRunner.cs
private static void PrepareConnectionsFromUI(BaseBuildNode node)
{
    node.ResetConnections();
    foreach (var element in node.inputContainer.Children())
    {
        if (element is Port inPort && inPort.connected)
        {
            foreach (var edge in inPort.connections)
            {
                var upstreamNode = edge.output.node as BaseBuildNode;
                if (upstreamNode != null)
                    node.AddUpstreamConnection(
                        inPort.portName,
                        upstreamNode,
                        edge.output.portName
                    );
            }
        }
    }
}
```

**Headless mode** — `HeadlessBuilder.Build` directly calls `AddUpstreamConnection` from `BuildEdgeData`:

```csharp
// HeadlessBuilder.cs
foreach (var edgeData in graphAsset.Edges)
{
    if (nodeMap.TryGetValue(edgeData.BaseNodeGUID, out var outNode) &&
        nodeMap.TryGetValue(edgeData.TargetNodeGUID, out var inNode))
    {
        inNode.AddUpstreamConnection(
            edgeData.TargetPortName,  // input port name
            outNode,                   // upstream node
            edgeData.BasePortName      // upstream output port name
        );
    }
}
```

Both paths produce identical `_upstreamConnections` structure — this is the key to Editor and Headless sharing `GraphRunner`.

### 3.6 GetInputContext — Data Pull

When a node needs upstream data, it calls `GetInputContext(portName)`:

```csharp
protected BuildContext GetInputContext(string portName)
{
    var context = new BuildContext();

    if (_upstreamConnections.TryGetValue(portName, out var connections))
    {
        foreach (var (upstreamNode, upstreamPort) in connections)
        {
            var map = GraphRunner.CurrentDataMap;

            if (map != null &&
                map.TryGetValue(upstreamNode.GUID, out var nodeOutputs) &&
                nodeOutputs.TryGetValue(upstreamPort, out var data))
            {
                context.Assets.AddRange(data.Assets);
                if (data.Logs.Length > 0)
                    context.Logs.AppendLine(data.Logs.ToString());
            }
        }
    }
    return context;
}
```

Workflow:
1. Look up all upstream connections for the given port name from `_upstreamConnections`
2. For each upstream connection, use `upstreamNode.GUID` to fetch that node's output from `GraphRunner.CurrentDataMap`
3. Use `upstreamPort` to index that node's specific output port data
4. Merge all upstream port data into a single `BuildContext`

**Two usage patterns:**

**Pattern 1 — Use GraphRunner's pre-aggregated context.** For nodes with a single input port that don't need to distinguish upstream sources, the `context` parameter of `Execute` already contains all upstream data (GraphRunner pre-aggregates it). The node uses it directly:

```csharp
// GrouperNode: single "Input" port
public override Dictionary<string, BuildContext> Execute(BuildContext context)
{
    foreach (var asset in context.Assets)
        asset.BundleName = "ui/login.bundle";
    return new Dictionary<string, BuildContext> { { "Output", context } };
}
```

**Pattern 2 — Ignore pre-aggregation, pull independently by port.** For nodes with multiple input ports where data from different ports must not be mixed, the node ignores the `context` parameter and calls `GetInputContext` separately for each port:

```csharp
// DeduplicatorNode: two input ports "Source" and "Reserved (Exclude)"
public override Dictionary<string, BuildContext> Execute(BuildContext ignoredContext)
{
    var sourceCtx = GetInputContext("Source");
    var reservedCtx = GetInputContext("Reserved (Exclude)");

    // sourceCtx.Assets: candidates for deduplication
    // reservedCtx.Assets: blacklist (assets to exclude from dependencies)

    // ... deduplication logic ...

    return new Dictionary<string, BuildContext>
    {
        { "Combined (Unique)",  combinedCtx },
        { "Deps Only (Unique)", depsOnlyCtx }
    };
}
```

The parameter name `ignoredContext` serves as an explicit reminder that this method does not depend on pre-aggregated data.

### 3.7 Execute — The Execution Contract

```csharp
public virtual Dictionary<string, BuildContext> Execute(BuildContext context)
{
    return new Dictionary<string, BuildContext> { { "Output", context } };
}
```

- **Input**: A `BuildContext` with `Assets` pre-aggregated from all upstream ports by GraphRunner. The node may use it directly or ignore it and call `GetInputContext` instead.
- **Output**: `Dictionary<string, BuildContext>` — key is output port name, value is the data produced on that port. GraphRunner writes the entire dictionary into `CurrentDataMap[node.GUID]`. Downstream nodes index by port name.

**Why a dictionary return?** Because a node can have multiple output ports. `DeduplicatorNode` has two:

```csharp
return new Dictionary<string, BuildContext>
{
    { "Combined (Unique)",  combinedCtx },   // Source + deduplicated deps (for main bundle)
    { "Deps Only (Unique)", depsOnlyCtx }    // Only deduplicated deps (for split packaging)
};
```

A downstream node connected to "Deps Only (Unique)" gets only that port's data when it calls `GetInputContext`.

**Default implementation is pass-through.** Nodes like `MergeNode` and `BatchBuildNode` that have one input, one output, and don't modify data can use the base default without overriding.

### 3.8 Serialization Rules

```csharp
public virtual string SaveToJSON() => "{}";
public virtual void LoadFromJSON(string json) { }
```

Each node subclass defines a `[Serializable] private class NodeData` as a DTO for JSON serialization.

**What gets stored:** user-configured parameters — folder path, filter rules, bundle name, compression options, etc.

**What does NOT get stored:** GUID (auto-generated or overwritten by `BuildNodeData.NodeGUID`), ports and edges (managed by `BuildEdgeData` separately), UI control references (recreated during `Initialize`).

**Standard pattern (GrouperNode example):**

```csharp
public class GrouperNode : BaseBuildNode
{
    public enum GroupingMode { OneBundle, ByFolder, ByFile, TopDirectory }

    public GroupingMode Mode = GroupingMode.OneBundle;
    public string MainKey = "assets";
    public string Suffix = ".bundle";

    // UI control references (not persisted, rebuilt on load)
    private EnumField _modeField;
    private TextField _mainKeyField;
    private TextField _suffixField;

    [Serializable]
    private class NodeData
    {
        public GroupingMode mode;
        public string key;
        public string suffix;
    }

    public override string SaveToJSON()
    {
        return JsonUtility.ToJson(new NodeData
        {
            mode = Mode,
            key = MainKey,
            suffix = Suffix
        });
    }

    public override void LoadFromJSON(string json)
    {
        var data = JsonUtility.FromJson<NodeData>(json);
        if (data == null) return;

        Mode = data.mode;
        MainKey = data.key;
        Suffix = data.suffix;

        // Restore UI controls (if already created)
        if (_modeField != null) _modeField.value = Mode;
        if (_mainKeyField != null) _mainKeyField.value = MainKey;
        if (_suffixField != null) _suffixField.value = Suffix;

        UpdateUIState();
    }
}
```

**Why check UI controls for null in `LoadFromJSON`?** In headless mode, nodes are created via `Activator.CreateInstance` and `LoadFromJSON` is called directly. `Initialize()` may not execute (headless mode does not need UI), so UI controls are not yet created. The null check is essential. In Editor mode, the sequence is `new NodeType()` -> `Initialize()` (creates UI controls) -> `LoadFromJSON()` (UI controls exist, can restore values).

### 3.9 Initialize() — Why Separate from Constructor

Subclass constructors run after the base constructor. If `Initialize()` were called from the base constructor, subclass fields would not yet be initialized:

```csharp
public class FilterNode : BaseBuildNode
{
    private List<FilterRule> _rules;      // Not yet assigned in constructor
    private TextField _patternField;      // Not yet created in constructor

    public FilterNode()
    {
        // _rules is still null here
        // _patternField is still null here
    }
}
```

The base constructor only does three things that don't depend on subclass fields: generate GUID, load stylesheet, set Renamable. Subclass `Initialize()` is called explicitly by the caller after construction:

```csharp
// BuildGraphWindow.CreateNode:
var node = new T();        // Construct first
node.Initialize();         // Then initialize (ports + UI)
```

### 3.10 UI Helpers

**OnDataChanged callback:**

```csharp
public Action OnDataChanged;

protected void NotifyChange()
{
    OnDataChanged?.Invoke();
}
```

Any UI control value change calls `NotifyChange()`. BuildGraphWindow binds it during node creation:

```csharp
node.OnDataChanged = () => SaveGraph("Node Data Change");
```

So changing a parameter in a node auto-saves the graph. Ctrl+Z restores the previous saved snapshot.

**Title bar double-click rename:**

`capabilities |= Capabilities.Renamable` tells GraphView the node is renamable, but GraphView does not auto-implement the behavior. It is manually implemented in `Initialize()`:

```csharp
public virtual void Initialize()
{
    titleContainer.RegisterCallback<MouseDownEvent>(evt =>
    {
        if (evt.button == 0 && evt.clickCount == 2)
        {
            OpenRenameTextField();
            evt.StopPropagation();
            focusController.IgnoreEvent(evt);
        }
    });
}

private void OpenRenameTextField()
{
    var textField = new TextField();
    textField.value = title;
    textField.style.position = Position.Absolute;
    textField.style.left = 0;
    textField.style.top = 0;
    textField.style.right = 0;
    textField.style.height = titleContainer.layout.height;
    textField.style.backgroundColor = new Color(0.1f, 0.1f, 0.1f);

    titleContainer.Add(textField);
    textField.Focus();
    textField.schedule.Execute(() => textField.SelectAll());

    void SaveAndClose()
    {
        if (!string.IsNullOrEmpty(textField.value) && textField.value != title)
        {
            title = textField.value;
            NotifyChange();
        }
        titleContainer.Remove(textField);
    }

    textField.RegisterCallback<KeyDownEvent>(evt =>
    {
        if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
        {
            SaveAndClose();
            evt.StopPropagation();
        }
        else if (evt.keyCode == KeyCode.Escape)
        {
            titleContainer.Remove(textField);
            evt.StopPropagation();
        }
    });

    textField.RegisterCallback<FocusOutEvent>(evt => SaveAndClose());
}
```

Flow: double-click -> TextField overlays title area -> user types -> Enter saves / Escape cancels / focus-out auto-saves -> `NotifyChange()` -> `SaveGraph()`.

### 3.11 GetCompatiblePorts — Connection Rules

This lives in `BuildGraphView` but is tightly coupled to the port system:

```csharp
// BuildGraphView.cs
public override List<Port> GetCompatiblePorts(Port startPort, NodeAdapter nodeAdapter)
{
    var compatiblePorts = new List<Port>();
    ports.ForEach(port =>
    {
        if (startPort != port
            && startPort.node != port.node
            && startPort.direction != port.direction)
        {
            compatiblePorts.Add(port);
        }
    });
    return compatiblePorts;
}
```

Four rules: cannot connect to self, cannot connect within same node, directions must oppose (Output to Input or Input to Output). GraphView calls this during drag-connect; incompatible ports do not highlight.

### 3.12 BaseBuildNode Capability Summary

| Capability | Method/Field | Consumer |
|---|---|---|
| Identity | `GUID` | Serialization (`BuildNodeData`), edge matching (`BuildEdgeData`), data indexing (`CurrentDataMap`) |
| Port creation | `AddInputPort` / `AddOutputPort` | Subclass `Initialize()` |
| Port cleanup | `ClearOutputPorts` | Dynamic-port nodes (FilterNode) |
| Connection mgmt | `_upstreamConnections` + `AddUpstreamConnection` / `GetUpstreamConnections` / `GetInputNodes` | GraphRunner (topological sort + data aggregation), HeadlessBuilder |
| Data pull | `GetInputContext(portName)` | Subclass `Execute` methods |
| Execution | `Execute(BuildContext)` | GraphRunner |
| Serialization | `SaveToJSON()` / `LoadFromJSON()` | BuildGraphWindow (save/load), HeadlessBuilder |
| Change notify | `OnDataChanged` + `NotifyChange()` | UI controls -> BuildGraphWindow.SaveGraph |
| Rename | `OpenRenameTextField` | User double-click on title |

---

## 4. Node Type Catalog — All 19 Nodes (Chapter 15)

Nodes are organized into six functional categories:

### 4.1 Source Nodes

**DirectoryNode** — Scans a directory, outputs all file paths.

### 4.2 Process Nodes

**FilterNode** — Filters assets by extension/path rules. Has dynamic output ports that change based on filter rule configuration. Stored JsonData: `[{"ruleType":"Extension","pattern":".prefab"}]`.

**DependencyNode** — Resolves asset dependencies via `AssetDatabase.GetDependencies` recursively. Supports SpriteAtlas smart handling (recognizes atlas dependencies and treats them specially).

**MergeNode** — Merges multiple input streams into one output. Uses `Port.Capacity.Multi` for its input port. Default pass-through `Execute` from base class suffices.

**DeduplicatorNode** — Deduplicates dependencies. Two input ports: "Source" (candidates) and "Reserved (Exclude)" (blacklist). Two output ports: "Combined (Unique)" (source + deduplicated deps) and "Deps Only (Unique)" (deduplicated deps only). Must use Pattern 2 (ignore pre-aggregated context, pull each port independently).

### 4.3 Portal Nodes

**PortalSenderNode** — Broadcasts data via a `PortalID` string. No visual wire needed; receivers match by ID.

**PortalReceiverNode** — Receives data from a matching PortalSender. Establishes a virtual upstream connection via `AddUpstreamConnection("VirtualInput", sender, "Output")`.

### 4.4 Incremental Build Nodes

**IncrementalBuildNode** — Detects incremental changes. Outputs split into "Changed" and "Unchanged" ports based on `BuildCacheManager` comparison.

### 4.5 Strategy Nodes

**GrouperNode** — Assigns `BundleName` to each `AssetBuildInfo`. Four modes:
- `OneBundle`: All assets go to one bundle (key + suffix)
- `ByFolder`: One bundle per directory
- `ByFile`: One bundle per file
- `TopDirectory`: One bundle per top-level directory under the source path

This is the node where `BundleName` transitions from `string.Empty` to the final value.

**AnalyzerNode** — Automatically analyzes shared assets (assets referenced by multiple groups). Used to identify common/shared dependencies that should be extracted into a common bundle.

### 4.6 Export Nodes

**BuildBundleNode** — Calls SBP (Scriptable Build Pipeline) to build AssetBundles. In preview mode, only logs "Ready to build". In build mode, groups assets by `BundleName`, invokes `PipelineLauncher.Build()`, generates `BuildReportItem`. Has a prominent "BUILD Bundles" button in the editor.

**BuildZipNode** — Packages assets into encrypted Zip archives.

**BuildCopyNode** — Directly copies files to the output directory.

**BatchBuildNode** — Master execution switch. Red title bar for visual prominence. Has an "EXECUTE ALL" button that triggers the full graph execution from all upstream Export nodes. This is typically used as the start node for topological sort.

**ApplyToEditorNode** — Writes `BundleName` back to `AssetImporter` (`assetBundleName` property) so other systems can read the assignment.

### 4.7 SubGraph Nodes

**SubGraphNode** — Loads and executes a child `BuildGraphAsset`. When executed, pushes a new `DataMap` layer onto `GraphRunner`'s stack (enabling nested sub-graph isolation).

**SubGraphInputNode** — Entry point for data flowing into a sub-graph.

**SubGraphOutputNode** — Exit point for data flowing out of a sub-graph.

### 4.8 Debug/Report Nodes

**DebugViewerNode** — Pass-through node that opens an asset list preview window for inspection.

**RedundancyCheckNode** — Performs BFS dependency tree analysis to detect redundancy. Can block the build if redundancy is found.

**ReportNode** — Generates a Markdown-format build report.

---

## 5. GraphRunner — Core Execution Engine (Chapter 15)

### 5.1 Topological Sort

The engine performs a depth-first topological sort from a target node (usually `BatchBuildNode` or the user-selected preview node) backward along upstream connections:

```csharp
private static List<BaseBuildNode> TopologicalSort(BaseBuildNode startNode)
{
    var sorted = new List<BaseBuildNode>();
    var visited = new HashSet<BaseBuildNode>();
    var recursionStack = new HashSet<BaseBuildNode>();

    void Visit(BaseBuildNode node)
    {
        if (recursionStack.Contains(node)) return; // Cycle protection
        if (visited.Contains(node)) return;

        visited.Add(node);
        recursionStack.Add(node);

        // Recurse into all upstream nodes first (depth-first)
        foreach (var inputNode in node.GetInputNodes())
            Visit(inputNode);

        recursionStack.Remove(node);
        sorted.Add(node); // Post-order append = topological order
    }

    Visit(startNode);
    return sorted;
}
```

Key properties:
- Traces from sink toward sources: starts at the target node and follows upstream connections backward.
- **Cycle detection**: `recursionStack` catches cyclic dependencies. Cycles are skipped without infinite recursion.
- **Only necessary nodes execute**: Orphaned nodes not connected to the target are never visited.

### 5.2 CurrentDataMap — Global Data Registry

```csharp
// Stack supports sub-graph nesting
private static Stack<Dictionary<string, Dictionary<string, BuildContext>>> _dataMapStack;

// Key: node GUID -> Key: output port name -> Value: port's data
public static Dictionary<string, Dictionary<string, BuildContext>> CurrentDataMap
{
    get
    {
        if (_dataMapStack.Count > 0) return _dataMapStack.Peek();
        return null;
    }
}
```

This is a two-level dictionary:

```
CurrentDataMap
  +-- "abc-123-def" (DirectoryNode GUID)
  |     +-- "Output" -> BuildContext { Assets: [Login.prefab, bg.png, ...] }
  +-- "456-ghi-jkl" (FilterNode GUID)
  |     +-- "Output" -> BuildContext { Assets: [Login.prefab, bg.png] }
  +-- "789-mno-pqr" (DeduplicatorNode GUID)
  |     +-- "Combined (Unique)" -> BuildContext { Assets: [...] }
  |     +-- "Deps Only (Unique)" -> BuildContext { Assets: [...] }
  +-- ...
```

**Why a Stack?** It supports sub-graph nesting. When a `SubGraphNode` executes, a fresh `DataMap` layer is pushed onto the stack. All nodes inside the sub-graph read/write from the stack top. The parent graph is completely isolated. After sub-graph execution completes, the layer is popped.

### 5.3 Two Operating Modes

```csharp
// Editor mode: must establish connections from UI edges
public static BuildContext Run(BaseBuildNode startNode,
    List<BaseBuildNode> allNodes, bool isBuildMode = false)
{
    _dataMapStack.Clear();
    foreach (var node in allNodes) PrepareConnectionsFromUI(node);
    LinkPortals(allNodes);
    return RunInternal(startNode, isBuildMode);
}

// Headless mode: connections already established by HeadlessBuilder
public static BuildContext RunHeadless(BaseBuildNode startNode,
    bool isBuildMode = true)
{
    return RunInternal(startNode, isBuildMode);
}
```

The difference is in `isBuildMode` default:
- **Editor Preview**: `isBuildMode = false`, `BuildBundleNode` only logs "Ready to build"
- **Editor Build / Headless Build**: `isBuildMode = true`, `BuildBundleNode` actually calls `PipelineLauncher.Build()`

### 5.4 Portal Virtual Connections

Portal connections bypass visual wires. When the graph is large, wires become spiderwebs. Portals match by ID string:

```csharp
private static void LinkPortals(List<BaseBuildNode> allNodes)
{
    // 1. Collect all PortalSenders
    var senderMap = new Dictionary<string, PortalSenderNode>();
    foreach (var node in allNodes)
        if (node is PortalSenderNode s && !string.IsNullOrEmpty(s.PortalID))
            senderMap[s.PortalID] = s;

    // 2. For each PortalReceiver, establish virtual upstream connection to matching sender
    foreach (var node in allNodes)
        if (node is PortalReceiverNode r && !string.IsNullOrEmpty(r.PortalID))
            if (senderMap.TryGetValue(r.PortalID, out var s))
                r.AddUpstreamConnection("VirtualInput", s, "Output");
}
```

This works identically in both Editor and Headless modes.

---

## 6. Visual Editor Layer

### 6.1 BuildGraphView — The Canvas

Inherits from Unity's `GraphView`. Provides:
- Zoom, pan, rectangle selector
- Grid background
- Port compatibility checking (opposing directions, different nodes only)
- Copy-paste (serializes node type + JSON, generates new GUID on paste, offsets by 20px)

```csharp
// Copy
private string SerializeGraphElementsImpl(IEnumerable<GraphElement> elements)
{
    var container = new CopyPasteContainer();
    foreach (var element in elements)
    {
        if (element is BaseBuildNode node)
        {
            container.NodeTypes.Add(node.GetType().FullName);
            container.JsonDatas.Add(node.SaveToJSON());
        }
    }
    return JsonUtility.ToJson(container);
}

// Paste
private void UnserializeAndPasteImpl(string operationName, string data)
{
    // ... reflectively create nodes, generate new GUIDs, restore data ...
    // each pasted node offset 20px to prevent complete overlap
}
```

### 6.2 BuildGraphWindow — Editor Window

The main editor window. Responsibilities:
- Open/create graph assets via `[OnOpenAsset]` callback
- Right-click context menu with all 19 node types organized by category
- Undo support via `Undo.RecordObject`
- Expandable preview panel at bottom showing execution logs and asset lists
- Full graph save/load

**Undo strategy:**
```csharp
private void SaveGraph(string undoName = "Save Graph")
{
    if (_currentAsset == null) return;
    Undo.RecordObject(_currentAsset, undoName);
    // ... serialize all nodes and edges into _currentAsset ...
    EditorUtility.SetDirty(_currentAsset);
}
```

When the user presses Ctrl+Z, Unity auto-restores `BuildGraphAsset` to the previous state. An `OnUndoRedo` callback detects the change and re-calls `LoadGraph()` to refresh the view.

### 6.3 Preview Mechanism

Select any node and click "Preview Selection". `GraphRunner` performs a topological sort + execution from that node backward (`isBuildMode = false`). Results display in the bottom panel:
- **Log stream**: Each node's `Logs` output concatenated
- **Asset list**: Final output assets grouped by `BundleName`
- **One-click copy**: Copy preview results to system clipboard

---

## 7. Headless / CI Mode

```csharp
// Assets/Editor/BuildBundleTools/Core/HeadlessBuilder.cs
public static void Build(
    string graphAssetPath,     // Path to the BuildGraphAsset
    string targetNodeName,     // Target node name (default "Batch")
    bool copyToLoadPath,       // Whether to copy to StreamingRes
    bool copyToStreamingAssets // Whether to copy to StreamingAssets
)
{
    // 1. Load BuildGraphAsset
    var graphAsset = AssetDatabase.LoadAssetAtPath<BuildGraphAsset>(graphAssetPath);

    // 2. Reflectively create all nodes (zero UI dependency)
    Dictionary<string, BaseBuildNode> nodeMap = new Dictionary<string, BaseBuildNode>();
    foreach (var nodeData in graphAsset.Nodes)
    {
        var nodeType = Type.GetType(nodeData.NodeType);
        // Fallback: cross-assembly search
        if (nodeType == null)
            nodeType = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a => a.GetTypes())
                .FirstOrDefault(t => t.FullName == nodeData.NodeType);

        var node = Activator.CreateInstance(nodeType) as BaseBuildNode;
        node.Initialize();
        node.GUID = nodeData.NodeGUID;
        node.title = nodeData.Title;
        node.LoadFromJSON(nodeData.JsonData);
        nodeMap[node.GUID] = node;
    }

    // 3. Rebuild logical connections from BuildEdgeData
    foreach (var edgeData in graphAsset.Edges)
    {
        inNode.AddUpstreamConnection(
            edgeData.TargetPortName,
            outNode,
            edgeData.BasePortName
        );
    }

    // 4. LinkPortals
    // 5. Find target node (prefer BatchBuildNode)
    // 6. GraphRunner.RunHeadless(targetNode, isBuildMode: true)
    // 7. Output build report
    // 8. Copy artifacts to specified directories
}
```

Key points for headless mode:
- **Zero UI dependency**: Nodes are created purely through reflection and code. No `EditorWindow` or `GraphView` required.
- **Connection rebuild**: Does not depend on `Edge` UI objects. Directly calls `AddUpstreamConnection` from `BuildEdgeData`.
- **Portal compatible**: `LinkPortals` logic is identical to Editor mode.
- **Command-line friendly**: Can be invoked as a Unity batch command:
```
Unity.exe -quit -batchmode -projectPath "G:\111\test\YYAsset" \
  -executeMethod YY.Build.Core.HeadlessBuilder.BuildWithDefaultConfig
```

---

## 8. BuildCacheManager — Incremental Build System

### 8.1 Cache Structure

```csharp
// BuildCacheData.cs
public class BuildCacheData
{
    public string Version;
    public string UnityVersion;
    public string BuildTarget;                    // Platform change -> full rebuild

    public Dictionary<string, AssetHashInfo> AssetHashes;     // Per-asset hash
    public Dictionary<string, BundleCacheInfo> BundleCaches;  // Per-bundle cache
    public Dictionary<string, List<string>> DependencyCache;  // Dependency cache

    public List<string> ChangedAssets;    // Assets that changed this run
    public List<string> AffectedBundles;  // Bundles affected by changes
    public bool RequiresFullBuild;        // Full rebuild flag

    public IncrementalBuildStats Stats;
}

public class AssetHashInfo
{
    public string Path;
    public string MD5;
    public long FileSize;
    public long LastWriteTime;
    public string BundleName;

    // Quick dirty check: size or write-time change
    public bool IsQuickDirty(string fullPath)
    {
        var fi = new FileInfo(fullPath);
        if (!fi.Exists) return true;
        return fi.Length != FileSize || fi.LastWriteTimeUtc.Ticks != LastWriteTime;
    }
}
```

### 8.2 Two-Tier Detection Strategy

1. **Quick detection (O(1))**: Compare file size (`Length`) and last write time (`LastWriteTimeUtc.Ticks`). This catches 99% of changes.
2. **Precise verification**: If quick detection finds a change, compute MD5 hash to confirm actual content change. Some operations (e.g., Git checkout) can change timestamps without changing content.

### 8.3 Dependency Propagation

If `Texture/tree.png` changes, `environment/forest.bundle` (its direct container) must rebuild. But if `ui/hud.bundle` also references `Texture/tree.png`, it must rebuild too. This cross-bundle dependency propagation ensures all affected bundles are included in the incremental build.

### 8.4 Cache Storage

Cache is persisted to `Library/BuildCache/BuildCache.json`. This directory is in `.gitignore` (Unity's `Library` directory is typically ignored), so each developer's local cache is independent.

```csharp
public static void Save()
{
    string dir = "Library/BuildCache";
    if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

    string json = JsonUtility.ToJson(Cache, true);
    File.WriteAllText(Path.Combine(dir, "BuildCache.json"), json);
}
```

---

## 9. Typical Build Pipeline Walkthrough

A realistic example: build UI prefabs into `ui.bundle`, scenes into `scenes.bundle`, and extract shared dependencies into `common.bundle`.

When `BatchBuildNode`'s "EXECUTE ALL" is clicked:

1. **Topological sort** traces from `BatchBuildNode` back to all sources
2. **DirectoryNode** scans directories, produces all file paths
3. **FilterNode** filters by extension, keeps only `.prefab` or `.unity`
4. **DependencyNode** calls `AssetDatabase.GetDependencies` recursively for each asset
5. **AnalyzerNode** compares dependencies across branches, identifies shared assets (textures/materials referenced by both UI and Scenes)
6. **DeduplicatorNode** excludes shared assets already covered by `common.bundle` from the UI branch
7. **GrouperNode** assigns `BundleName` to each asset
8. **BuildBundleNode** calls SBP for actual build
9. Build report output to Console and `BuildReportItem` list

---

## 10. Design Principles Summary

- **Data and view separation**: `BuildGraphAsset` is pure data; `BuildGraphView` is pure UI. The same data is consumable by both Editor and Headless modes.
- **Node as functional unit**: Each node is an independent, testable module. New nodes require only a new `BaseBuildNode` subclass — no changes to core engine.
- **Execution and preview unification**: The same graph supports both preview and build via the `IsBuildMode` switch.
- **Flexible connection methods**: Visual wires, Portal virtual connections (cross-region ID matching), and code-injected connections in Headless mode.
- **Precise incremental build**: Two-tier detection (quick + MD5 confirmation) + dependency propagation = only bundles that actually need rebuilding get rebuilt.

---

## 11. Full File Index

| Module | File Path |
|---|---|
| Data Model | `Assets/Editor/BuildBundleTools/BuildGraphAsset.cs` |
| Data Unit | `Assets/Editor/BuildBundleTools/Data/AssetBuildInfo.cs` |
| Execution Context | `Assets/Editor/BuildBundleTools/BuildContext.cs` |
| Node Base Class | `Assets/Editor/BuildBundleTools/Nodes/BaseBuildNode.cs` |
| Graph Execution Engine | `Assets/Editor/BuildBundleTools/Core/GraphRunner.cs` |
| SBP Wrapper | `Assets/Editor/BuildBundleTools/Core/PipelineLauncher.cs` |
| CI Build | `Assets/Editor/BuildBundleTools/Core/HeadlessBuilder.cs` |
| Incremental Cache | `Assets/Editor/BuildBundleTools/Core/BuildCacheManager.cs` |
| Zip Builder | `Assets/Editor/BuildBundleTools/Core/ZipBuilder.cs` |
| Editor Window | `Assets/Editor/BuildBundleTools/BuildGraphWindow.cs` |
| Canvas | `Assets/Editor/BuildBundleTools/BuildGraphView.cs` |
| Node Implementations | `Assets/Editor/BuildBundleTools/Nodes/*.cs` (19 nodes) |

Repository: https://github.com/djswzw/YYAsset
