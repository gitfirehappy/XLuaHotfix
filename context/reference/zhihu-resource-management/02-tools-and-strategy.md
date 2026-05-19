---
title: Resource Tools, Packaging Strategy, and Addressables Comparison
source: Zhihu Column "游戏资源管理" by 伽蓝之洞, Chapters 12-14
status: verified
---

# Resource Tools, Packaging Strategy, and Addressables Comparison

## Chapter Overview

Three interconnected chapters from the Zhihu series on Unity resource management:
- Chapter 12 (raw_ch_13): Building a runtime resource visualization/debugger panel
- Chapter 13 (raw_ch_14): AssetBundle packaging granularity strategy and engineering principles
- Chapter 14 (raw_ch_15): Comprehensive comparison between Unity Addressables and the self-built solution

---

## Part A: Resource Visualization Panel (Chapter 12)

### Design Principle: Zero Runtime Pollution

The observer pattern must not affect the observed system's performance. Adding a `List<string> ReferencedBy` field inside `BundleInfo` to track every reference would generate enormous GC pressure and memory waste across hundreds of thousands of objects.

The correct approach:

- **Runtime**: Keep only the minimal `int RefCount` field.
- **Editor**: At panel draw time, reverse-derive reference relationships using Unity's Manifest and the current loaded-bundle list. This is compute-on-demand, not continuous tracking.

### Data Interface Extensions (Read-Only)

The debugger needs read-only access to runtime internals. Two classes are extended.

**BundleManager** exposes three static accessors:

```csharp
// BundleManager.cs
public static AssetBundleManifest GetManifest() => _manifest;

public static System.Collections.Generic.List<BundleInfo> GetLoadedBundleInfos()
{
    return new System.Collections.Generic.List<BundleInfo>(_loadedBundles.Values);
}

public static System.Collections.Generic.List<string> GetLoadedBundlesThatDependOn(string targetBundleName)
{
    var result = new System.Collections.Generic.List<string>();
    if (_manifest == null) return result;

    foreach (var kvp in _loadedBundles)
    {
        var sourceBundle = kvp.Key;
        if (sourceBundle == targetBundleName) continue;

        // Reverse lookup: if Source depends on Target, then Source holds Target
        var deps = _manifest.GetAllDependencies(sourceBundle);
        foreach (var dep in deps)
        {
            if (dep == targetBundleName)
            {
                result.Add(sourceBundle);
                break;
            }
        }
    }
    return result;
}
```

Key design note: `GetLoadedBundlesThatDependOn` iterates all loaded bundles and checks manifest dependencies. This is O(n * d) but only runs in the editor when the panel repaints -- acceptable trade-off for zero runtime overhead.

**AssetSystem** exposes two editor-only accessors:

```csharp
// AssetSystem.cs
#if UNITY_EDITOR
public static System.Collections.Generic.List<AssetInternalNode> GetLoadedAssetNodes()
{
    return new System.Collections.Generic.List<AssetInternalNode>(_nodes.Values);
}

// Find all Assets belonging to a specific Bundle
public static System.Collections.Generic.List<string> GetLoadedAssetsInBundle(string bundleName)
{
    var result = new System.Collections.Generic.List<string>();
    foreach (var node in _nodes.Values)
    {
        if (node.BundleName == bundleName)
            result.Add(node.AssetName);
    }
    return result;
}
#endif
```

The `#if UNITY_EDITOR` guard ensures these accessors are completely stripped from builds.

### ResourceDebuggerWindow: Full Implementation

The debugger window lives under `Assets/Editor/` and is accessible via menu `GameTools > Resource Debugger`. Three core features:

1. **Left-right split layout**: Bundle list overview (40% width) on the left, dependency details (60% width) on the right.
2. **Dependency perspective**: Right panel shows "Referenced By" (who holds me) and "Dependencies" (who I depend on).
3. **Snapshot export**: One-click dump of all reference relationships to a `.txt` file for offline analysis.

```csharp
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using YY;

public class ResourceDebuggerWindow : EditorWindow
{
    [MenuItem("GameTools/Resource Debugger")]
    public static void OpenWindow()
    {
        GetWindow<ResourceDebuggerWindow>("Res Debugger").Show();
    }

    private BundleInfo _selectedBundle;
    private Vector2 _scrollPosLeft;
    private Vector2 _scrollPosRight;
    private string _searchFilter = "";

    // Auto-refresh: repaint on every inspector tick for real-time data
    private void OnInspectorUpdate() => Repaint();

    private void OnGUI()
    {
        DrawToolbar();

        EditorGUILayout.BeginHorizontal();
        {
            // Left: Bundle list (40%)
            EditorGUILayout.BeginVertical(GUILayout.Width(position.width * 0.4f));
            DrawBundleListPanel();
            EditorGUILayout.EndVertical();

            // Divider
            GUILayout.Box("", GUILayout.Width(1), GUILayout.ExpandHeight(true));

            // Right: Dependency details (60%)
            EditorGUILayout.BeginVertical();
            DrawBundleDetailPanel();
            EditorGUILayout.EndVertical();
        }
        EditorGUILayout.EndHorizontal();
    }

    private void DrawToolbar()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        GUILayout.Label("Bundle Filter:", GUILayout.Width(80));
        _searchFilter = EditorGUILayout.TextField(_searchFilter, EditorStyles.toolbarTextField);

        GUILayout.FlexibleSpace();

        if (GUILayout.Button("Export Log", EditorStyles.toolbarButton, GUILayout.Width(80)))
        {
            ExportSnapshot();
        }

        if (GUILayout.Button("Force GC", EditorStyles.toolbarButton, GUILayout.Width(70)))
        {
            Resources.UnloadUnusedAssets();
            System.GC.Collect();
        }
        EditorGUILayout.EndHorizontal();
    }

    // --- Left: Bundle List ---
    private void DrawBundleListPanel()
    {
        DrawHeader("Loaded Bundles");
        _scrollPosLeft = EditorGUILayout.BeginScrollView(_scrollPosLeft);

        var bundles = BundleManager.GetLoadedBundleInfos();

        // Sort: referenced bundles first (focus on what matters), then by name
        var sortedBundles = bundles.OrderByDescending(b => b.RefCount > 0).ThenBy(b => b.Name);

        GUIStyle listBtnStyle = new GUIStyle("CN EntryBackEven");
        listBtnStyle.alignment = TextAnchor.MiddleLeft;
        listBtnStyle.padding = new RectOffset(10, 0, 0, 0);
        listBtnStyle.fixedHeight = 25;

        foreach (var bundle in sortedBundles)
        {
            if (!string.IsNullOrEmpty(_searchFilter) && !bundle.Name.Contains(_searchFilter)) continue;

            // Highlight logic
            if (_selectedBundle != null && _selectedBundle.Name == bundle.Name)
            {
                GUI.backgroundColor = Color.cyan; // Selected color
            }
            else
            {
                // Green for referenced bundles, white for unreferenced
                GUI.backgroundColor = bundle.RefCount > 0
                    ? new Color(0.6f, 1f, 0.6f) : Color.white;
            }

            if (GUILayout.Button($"{bundle.Name} ({bundle.RefCount})",
                listBtnStyle, GUILayout.ExpandWidth(true)))
            {
                _selectedBundle = bundle;
                GUI.FocusControl(null); // Deselect input field
            }

            GUI.backgroundColor = Color.white; // Restore
        }

        EditorGUILayout.EndScrollView();
    }

    // --- Right: Dependency Details ---
    private void DrawBundleDetailPanel()
    {
        DrawHeader("Dependency Inspector");

        if (_selectedBundle == null)
        {
            GUILayout.Label("Select a bundle from the list to view details.",
                EditorStyles.centeredGreyMiniLabel);
            return;
        }

        _scrollPosRight = EditorGUILayout.BeginScrollView(_scrollPosRight);

        // 1. Basic info
        EditorGUILayout.LabelField("Name", _selectedBundle.Name, EditorStyles.boldLabel);
        EditorGUILayout.LabelField("Ref Count", _selectedBundle.RefCount.ToString(),
            EditorStyles.boldLabel);

        EditorGUILayout.Space(10);

        // 2. Core analysis: Who references me?
        // Two sources: A. Assets inside this bundle; B. Other bundles that depend on it
        DrawSectionHeader("Referenced By (谁在引用我?)");

        // A. Check Assets (directly from AssetSystem)
        var assetsInBundle = AssetSystem.GetLoadedAssetsInBundle(_selectedBundle.Name);
        if (assetsInBundle.Count > 0)
        {
            GUILayout.Label($"[Assets] ({assetsInBundle.Count})", EditorStyles.miniBoldLabel);
            foreach (var assetName in assetsInBundle)
            {
                DrawItem($"Asset: {assetName}");
            }
        }

        // B. Check Bundles (reverse-derive via Manifest)
        var parentBundles = BundleManager.GetLoadedBundlesThatDependOn(_selectedBundle.Name);
        if (parentBundles.Count > 0)
        {
            GUILayout.Label($"[Parent Bundles] ({parentBundles.Count})",
                EditorStyles.miniBoldLabel);
            foreach (var parent in parentBundles)
            {
                DrawItem($"Bundle: {parent}");
            }
        }

        // Anomaly hints
        if (assetsInBundle.Count == 0 && parentBundles.Count == 0)
        {
            if (_selectedBundle.RefCount > 0)
                GUILayout.Label(
                    "Ref Count > 0 but no holders tracked.\n(Manual reference increment in code?)",
                    EditorStyles.helpBox);
            else
                GUILayout.Label("No references, safe to unload", EditorStyles.miniLabel);
        }

        EditorGUILayout.Space(10);

        // 3. Dependencies (downstream)
        DrawSectionHeader("Dependencies (我依赖了谁?)");

        var manifest = BundleManager.GetManifest();
        if (manifest != null)
        {
            string[] deps = manifest.GetAllDependencies(_selectedBundle.Name);
            if (deps.Length > 0)
            {
                foreach (var dep in deps) DrawItem($"Bundle: {dep}");
            }
            else
            {
                GUILayout.Label("No Dependencies", EditorStyles.miniLabel);
            }
        }
        else
        {
            GUILayout.Label("Manifest not loaded (Simulation Mode?)", EditorStyles.miniLabel);
        }

        EditorGUILayout.Space(10);

        // 4. Content preview
        if (_selectedBundle.Bundle != null)
        {
            DrawSectionHeader("Contains Assets");
            var assetNames = _selectedBundle.Bundle.GetAllAssetNames();
            foreach (var name in assetNames)
            {
                GUILayout.Label(System.IO.Path.GetFileName(name), EditorStyles.miniLabel);
            }
        }

        EditorGUILayout.EndScrollView();
    }

    // --- Snapshot Export ---
    private void ExportSnapshot()
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine($"Memory Snapshot - {System.DateTime.Now}");
        sb.AppendLine("========================================");

        var bundles = BundleManager.GetLoadedBundleInfos();

        sb.AppendLine($"Total Loaded Bundles: {bundles.Count}");
        sb.AppendLine($"Total Active Assets: {AssetSystem.GetLoadedAssetNodes().Count}");
        sb.AppendLine("========================================");
        sb.AppendLine("");

        foreach (var bundle in bundles)
        {
            sb.AppendLine($"[Bundle] {bundle.Name}");
            sb.AppendLine($"  RefCount: {bundle.RefCount}");

            var parents = BundleManager.GetLoadedBundlesThatDependOn(bundle.Name);
            var assets = AssetSystem.GetLoadedAssetsInBundle(bundle.Name);

            if (parents.Count > 0)
            {
                sb.AppendLine("  Referenced By Bundles:");
                foreach (var p in parents) sb.AppendLine($"    - {p}");
            }

            if (assets.Count > 0)
            {
                sb.AppendLine("  Referenced By Assets:");
                foreach (var a in assets) sb.AppendLine($"    - {a}");
            }

            sb.AppendLine("");
        }

        string path = Path.Combine(Application.dataPath, "..", "BundleMemoryDump.txt");
        File.WriteAllText(path, sb.ToString());

        EditorUtility.RevealInFinder(path);
        Application.OpenURL(path);
        Debug.Log($"Snapshot exported to: {path}");
    }

    // --- Helper drawing methods ---
    private void DrawHeader(string title)
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        GUILayout.Label(title, EditorStyles.boldLabel);
        EditorGUILayout.EndHorizontal();
    }

    private void DrawSectionHeader(string title)
    {
        var style = new GUIStyle(EditorStyles.boldLabel);
        style.normal.textColor = new Color(0.3f, 0.6f, 0.9f);
        GUILayout.Label(title, style);
        GUILayout.Box("", GUILayout.Height(1), GUILayout.ExpandWidth(true));
    }

    private void DrawItem(string label)
    {
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("↳", GUILayout.Width(15));
        GUILayout.Label(label);
        EditorGUILayout.EndHorizontal();
    }
}
```

### Key Architectural Decisions in the Debugger

1. **OnInspectorUpdate for auto-refresh**: `Repaint()` is called every inspector tick, making the panel show real-time data without manual refresh buttons. This is acceptable because the heavy work (reverse dependency calculation) only runs when something is actually drawn.

2. **Sorting strategy**: Bundles with `RefCount > 0` come first, then alphabetical. This immediately surfaces bundles that might be leaking (still held when they should have been released).

3. **Color coding**: Cyan = currently selected; Green = has references (potentially alive for a reason); White = zero references (safe to unload). This is a quick visual triage system.

4. **Anomaly detection**: When `RefCount > 0` but no upstream holders are detected, the panel flags it as a potential manual reference bug. This catches cases where code increments the count without going through the normal dependency system.

5. **Snapshot export**: Uses `StringBuilder` for efficient text construction, writes to `BundleMemoryDump.txt` in the project root, and auto-opens the file. This is designed for sending to another programmer for offline analysis.

6. **Bundle Mode Only**: The panel only works in the editor's Bundle mode (not Simulation mode). A later chapter plans to add remote debugging for device builds.

---

## Part B: AssetBundle Packaging Strategy (Chapter 13)

### Engineering Definition of AssetBundle Build

Resource packaging is fundamentally a **serialization + platform compilation + file archiving** process:

1. **Data structure serialization**: Converts complex in-memory object graphs (GameObject + attached Components) into flat binary streams for fast deserialization at runtime.

2. **Hardware-level format conversion**: Transforms source files into target-hardware-readable formats. Examples: `.png` to ASTC (Android) or PVRTC/ASTC (iOS); `.shader` to platform-specific graphics API code (Vulkan/Metal/GLES).

3. **File system archiving**: Merges discrete resources into one or more archive files with an internal file table (index) for offset-based access.

### Three Core Engineering Benefits

**I/O Performance Optimization**

- Problem: Reading thousands of small scattered files (UI icons, etc.) at runtime incurs massive I/O latency and CPU overhead from repeated system calls and disk seeking.
- Solution: AssetBundle merges logically related small files into one physical file. A single file handle open, then offset-based reads, dramatically increases loading throughput.

**Memory Layout and Management**

- Fast loading: Loading an AssetBundle is closer to a memory copy than parsing XML/JSON or decoding PNG at runtime.
- Lifecycle control: Explicit `Load` and `Unload` interfaces allow precise control over managed heap and native heap memory allocation and deallocation, preventing memory leaks.

**Dynamic Delivery and Hot Updates**

- Code/resource separation: AssetBundles decouple resource content from the main application binary.
- Incremental updates: Developers can distribute new AssetBundles over the network without recompiling the executable, enabling hot-update replacement of old resources or addition of new content.

### Packaging Granularity: Three Strategies

This is the central trade-off problem in packaging design. The axes being balanced: loading efficiency, hot-update payload size, and memory footprint.

#### Strategy A: Fine-Grained (Per-File) Packaging

Each individual resource file (each Prefab, each Texture) gets its own AssetBundle.

- **Advantages**:
  - Best hot-update efficiency: modifying a single file only updates the corresponding micro-bundle, minimizing user download.
  - True load-on-demand: load exactly what you use, zero memory waste.
- **Disadvantages**:
  - I/O bottleneck: loading a complex UI may require concurrent reads of hundreds of bundles, causing severe I/O blocking and stutter.
  - Header overhead: each AssetBundle file has several KB of header data; excessive file counts cause significant storage waste.

#### Strategy B: Coarse-Grained / Directory-Based Packaging

All resources under the same physical directory are merged into one bundle.

- **Advantages**:
  - I/O friendly: merged files reduce fragmentation, fast loading.
  - Intuitive management: matches developer file organization habits (e.g., `GUI/MainMenu` -> one bundle).
- **Disadvantages**:
  - Memory redundancy risk: if you only need one resource from the bundle, you must still load the entire bundle's header (the SerializedFile handle is held, even if asset content is lazily loaded).
  - Hot-update coupling: modifying any single file in the directory changes the entire bundle's hash, forcing users to re-download the whole thing.

#### Strategy C: Logical Aggregation with Dependency Separation (Hybrid -- Recommended)

This is the strategy most projects should adopt. It groups resources by reference topology and lifecycle, not physical directory structure.

**Implementation approach**:

- **Business resources**: Aggregated by module or function (e.g., `Scene_Level1`, `UI_Login`).
- **Shared dependencies**: Through dependency analysis, resources used by multiple bundles (like `Common_Shaders`, `Base_Textures`) are extracted into independent shared bundles.

**Engineering value**:

- Eliminates redundancy: prevents the same texture from being embedded in multiple business bundles.
- Memory reuse: shared resources stay resident, business resources load and unload on demand.

### Technical Challenges in Packaging Pipelines

These are the problems that motivate building visualization and analysis tooling:

**Implicit Dependencies and Resource Redundancy (Killer Problem #1)**

- Scenario: Asset A and Asset B both reference Asset C. If A and B are packaged separately and C is not explicitly assigned a bundle name, the build pipeline silently copies C into both A's and B's bundles.
- Consequences: bloated package size, two runtime instances of C in memory, batching failure (can't combine draw calls since they're different objects).
- Solution direction: mandatory dependency analysis to identify shared resources and perform deduplication.

**Circular Dependencies**

- Scenario: Bundle A references a resource in Bundle B, and Bundle B references a resource in Bundle A.
- Consequences: some build pipelines error out; at runtime, deadlocks or load failures occur.
- Solution direction: topological sorting before packaging; design conventions to avoid bidirectional references.

**Build Determinism**

- Requirement: with unchanged source files, repeated builds must produce binary-identical output (identical hashes).
- Challenge: certain Unity versions or AA pipelines (`BuildPipeline`) may include non-deterministic data (e.g., randomly ordered serialization), causing patch packages to grow meaninglessly. The Scriptable Build Pipeline (SBP) made significant improvements here.

**Dependency Tracking and Load Order**

- Principle: before loading AssetBundle A, all of A's dependency bundles must be loaded first.
- Challenge: runtime must maintain a complete dependency table (Manifest) and recursively load all dependencies. If dependency relationships are messy at build time, runtime faces "missing material (purple blocks)" or "missing script" critical errors.

### Summary of Packaging Goals

A well-designed packaging strategy should pursue:

- Minimize redundancy (through dependency analysis)
- Optimal I/O throughput (through reasonable aggregation)
- Minimal hot-update cost (through static/dynamic separation)

---

## Part C: Addressables vs. Self-Built Solution (Chapter 14)

### Unity Addressables: The Industry Standard

Unity Addressables System (AA) provides a unified resource addressing layer. Its core design is **logical-physical separation** -- a Catalog mapping layer sits between file paths and runtime references, enabling dynamic switching across platforms and storage locations. It includes built-in reference counting and a provider pattern for automated lifecycle management.

#### Addressables Advantages

**Decoupled Addressing**

- Business logic requests resources by string address or `AssetReference` without knowing which AssetBundle they live in or whether they are local or remote.
- Designers/artists can independently adjust resource grouping and optimize AssetBundle partitioning in the editor without requiring a single line of code change from programmers. This is critical for late-stage package optimization.

**Industrial-Grade Async Task Scheduling**

- **Request Collapsing**: The internal `_inflightTasks` mechanism ensures multiple concurrent requests for the same resource trigger only one actual I/O operation. All waiters share the same load result, preventing duplicate loads and I/O congestion.
- **Chain Operations (ChainOperation)**: Addressables automatically orchestrates complex async operation chains (initialize Catalog -> check remote catalog -> load AssetBundle -> load internal Asset), reducing the need for complex nested coroutines in business code.

**Robust Lifecycle Management**

- Automated reference counting via `IncrementReferenceCount` / `DecrementReferenceCount` tracks every resource. As long as paired `Load/Instantiate` and `Release` calls are maintained, memory leak risk is significantly reduced.
- **Safe handles (AsyncOperationHandle)**: Includes version number validation. After an async operation completes, even if the corresponding resource was accidentally destroyed, stale handle operations are detected and prevented from causing crashes.

**Modern Programming Paradigms and Extensibility**

- Multi-paradigm: natively supports `IEnumerator`, traditional callbacks (`Completed` event), and modern C# `async/await` (via `Task` property).
- **Provider pattern (IResourceProvider)**: The extensible `IResourceProvider` interface allows custom loading logic (encrypted files, database reads, etc.) without modifying Addressables core code.

#### Addressables Limitations

**Architecture Overhead: Memory and GC**

- **Resident memory burden**: To maintain the address abstraction layer, Addressables keeps complex Catalogs, ResourceLocators, and numerous internal InternalNodes resident. For resource-heavy mobile projects, the Catalog alone may consume several MB to 10+ MB of memory -- significant on memory-constrained low-end devices.
- **Object fragmentation and GC pressure**: Every async load request produces a chain of heap objects (AsyncOperationHandle, ProviderOperation, delegates, etc.), potentially triggering frequent GC on low-end devices.

**"Black Box" Debugging Challenges**

- Complex stack tracing: when a load fails or a memory leak occurs, the multi-layer abstraction (ResourceManager -> AddressablesImpl -> Providers) makes tracing the root cause through stack traces extremely difficult and time-consuming.
- Device debugging difficulty: while the Addressables Event Viewer provides some editor debugging capability, precisely diagnosing reference leaks for specific resources on device is far more challenging than with a transparent, direct system.

**Hot-Update and Initialization Coupling**

- Startup delay: the Addressables load flow is mandatorily dependent on Catalog initialization and remote catalog checking. For games requiring extreme startup speed, this forced async chain introduces unnecessary delay.
- Rigid hot-update strategy: AA tightly couples "resource downloading" with "resource loading". For domestic mobile game requirements like phased downloads, background silent downloading, and custom validation, the default Addressables flow is inflexible and requires extensive adapter code. While `PreloadDependencies` exists, customization is painful.

**Synchronous Loading Performance Risk**

- `WaitForCompletion` is unsupported on some platforms (WebGL) and excessive mobile usage causes main thread blocking, contradicting fully-async loading best practices and potentially introducing runtime stutter.

### Self-Built Solution: Explicit Control, Extreme Transparency

The self-built system is designed around "explicit control, extreme transparency" from the start.

#### Runtime Loading Advantages (Comparison Table)

- **Memory footprint**: Addressables = high (complex Catalog resident); Self-built = **extremely low** (only BundleInfo, AssetInternalNode, and reference counts). Critical for memory-sensitive mobile games.
- **GC pressure**: Addressables = high (frequent object creation); Self-built = **extremely low** (Chapter 7's Zero-GC async/await implementation). Improves low-end device smoothness.
- **Addressing**: Addressables = complex Catalog mapping; Self-built = **direct mapping** (AssetKey -> BundleName -> physical path). Simple, efficient, less lookup overhead.
- **Async mechanism**: Addressables = complex OperationHandle abstraction; Self-built = **lightweight Awaiter**, directly wrapping Unity AsyncOperation. Higher code readability, less performance overhead.
- **Hot-update decoupling**: Addressables = download and loading tightly bound; Self-built = **completely decoupled** (download module independent, AssetSystem only handles loading). Supports any hot-update strategy: background download, split-package download, etc.
- **Debugging transparency**: Addressables = black box, complex trace; Self-built = **fully transparent** (visualization debug panel directly maps to logic layer). Quickly locate memory leaks, lower investigation cost.
- **Lifecycle**: Addressables = automatic reference counting via handles; Self-built = **handle-based reference counting** with explicit Dispose. Clearer responsibility; combined with debug panel, precise management possible.

#### Build Graph Pipeline Advantages (Planned)

Relative to Addressables' "grouping list" model, the planned Build Graph tool offers:

**Visual Logic Flow**

- Addressables relies on static group configuration and hidden dependency analysis.
- Build Graph renders the entire packaging flow as a node graph. Developers see resources flow from source (DirectoryNode) through filtering (FilterNode), deduplication (DeduplicatorNode), grouping (GrouperNode), finally forming AssetBundles, Zips, or copies. This "what you see is what you get" pipeline is something Addressables cannot provide.

**Multi-Modal Hybrid Resource Handling**

- Addressables centers on AssetBundle.
- Build Graph is designed as a hybrid pipeline. In a single graph:
  - Prefabs and textures -> AssetBundle (BuildBundleNode)
  - Lua scripts and config files -> encrypted Zip (BuildZipNode)
  - Videos and audio -> direct copy to output (BuildCopyNode)
- All different output types converge at a ReportNode for a unified build report. This flexibility is not natively available in Addressables without extensive custom Build Scripts.

**Preemptive Dependency Governance and Deduplication**

- Addressables automatically analyzes dependencies at build time and merges, but redundancy detection and elimination is relatively passive (requires post-build analysis).
- Build Graph implements "prevention beforehand" via DeduplicatorNode and AnalyzerNode. Developers explicitly define resource ownership and exclusion rules through connections (e.g., "Common package has highest priority; UI package must exclude all resources owned by Common"). This proactive strategy eliminates implicit redundancy.

**Extreme Customizability and Extensibility**

- Addressables extension is based on Provider and Build Script.
- Build Graph extension is node-based. Developers write new nodes (encryption, image compression, custom format processing) and insert them into the pipeline without modifying core scheduling logic. The tool adapts infinitely to project-specific needs.

### Summary: The Metaphor

- **Addressables** is a "fully-furnished apartment": complete amenities, standardized, suitable for most projects to move in quickly. But if you want to remodel the layout, the cost is enormous.
- **Self-built system** is "modular building blocks": requires self-assembly, but you can build any custom pipeline shape matching project needs. It provides extreme control, performance transparency, and architectural flexibility.

The self-built system, especially in runtime memory control, GC optimization, and packaging pipeline customization/visualization, demonstrates advantages that Addressables struggles to match. This makes it a better choice for medium-to-large mobile projects pursuing high performance, deep customization, and multi-type resource hybrid processing.

