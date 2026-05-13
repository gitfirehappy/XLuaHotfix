---
title: Unity Resource Fundamentals — Types, Import Pipeline, and Directory Strategy
source: Zhihu Column "游戏资源管理" by 伽蓝之洞, Chapters 1-3
status: verified
---

# 1. Deconstructing Unity Game Resources

## 1.1 Introduction: Two Forms of Every Resource

Every asset in a Unity project exists in two fundamentally different forms:

- **Editor form** (what the developer sees): `.png`, `.fbx`, `.wav` files in the `Assets/` folder.
- **Runtime form** (what the engine and hardware consume): binary buffers, compressed blocks, GPU microcode.

This duality is the root cause of many common problems:

| Symptom | Underlying Cause |
|---|---|
| A 2 MB background PNG consumes 64 MB of memory at runtime | The compressed disk format is fully decompressed into raw pixel data |
| A low-poly model still overheats a phone | Every vertex consumes VRAM bandwidth, not just storage |
| Deleting a `.meta` file breaks all Prefab references | The GUID identity record is lost; Unity treats the file as a brand-new asset. The old Prefabs hold a stale GUID that now points to nothing. |

The data journey is: **Disk (storage) → RAM (system memory) → VRAM (GPU memory)**. Understanding each stage is critical for performance engineering.

---

## 1.2 Layer 1: Source Assets — Designed for Artists, Not Machines

**Source assets** are files produced by DCC (Digital Content Creation) tools: `.psd` (Photoshop), `.max` (3ds Max), `.wav` (raw audio).

### Why GPUs Cannot Read Source Files Directly

DCC tools and game engines serve opposing goals:

| | DCC Tools (Photoshop, 3ds Max) | Game Engines (Unity) |
|---|---|---|
| **Goal** | Editability — allow artists to freely modify | Real-time computation — feed the GPU as fast as possible |
| **Data structure** | Layers, masks, smart objects, undo history, loose vertex topology | Continuous binary buffers, tightly packed for cache-line efficiency |
| **Optimized for** | Human iteration speed | GPU memory bandwidth and instruction throughput |

A `.psd` file contains layers, masks, smart objects, and undo history — all completely useless to a GPU. A `.max` file stores loose, editable vertex structures designed for dragging and tweaking. The GPU has thousands of compute cores that can perform billions of matrix multiplications per second, but its instruction set has zero native support for complex data structures. It does not understand the concept of "files" or "layers." It only accepts one thing: **formatted, contiguous binary buffers**.

### The Import Process as Translation

Unity's Import process translates human-editable source data into machine-readable internal formats:

- **`Assets/` folder**: Stores source files (for humans).
- **`Library/` folder**: Stores converted engine data (for the machine).

When building a game, Unity **never** includes your `.psd` or `.fbx` source files in the final package. It bundles only the converted (and often encrypted) binary data from `Library/`.

This is why sending a project folder to another developer without `.meta` files forces Unity to re-import everything — the correspondence between source files and `Library/` data is lost, and Unity cannot reconstruct the mapping.

---

## 1.3 Layer 2: Runtime Assets — Engineered for Hardware

After the import pipeline, assets become engine-internal binary formats. This section examines what happens at the hardware level.

### 1.3.1 Geometry: From "Model" to Buffer Streams

**The core question**: How does a GPU understand a smooth sphere?

The GPU does not know what a "sphere" or even a "model" is. In the GPU's world, everything is triangles. To feed the GPU efficiently, Mesh data is decomposed into two core data streams:

#### A. Vertex Buffer and Memory Layout

Think of a giant, flat array. The engine disassembles the model and flattens all vertex data — position (Pos), normal (Norm), texture coordinates (UV) — into a single contiguous stream. To exploit CPU/GPU cache lines, modern engines typically use **interleaved storage**:

```
[Pos0, Norm0, UV0, Pos1, Norm1, UV1, Pos2, Norm2, UV2, ...]
```

Each vertex's attributes are stored together, so a single cache-line fetch retrieves everything needed to process that vertex in one go. This is a deliberate optimization: cache lines are typically 64 bytes on modern hardware, and fetching scattered data would waste bandwidth.

#### B. Index Buffer

To avoid repeating shared vertex coordinates (e.g., a cube corner shared by three faces), a lightweight integer list describes triangle composition:

```
[0, 1, 2,    // Triangle 1: uses vertices 0, 1, 2
 0, 2, 3,    // Triangle 2: uses vertices 0, 2, 3
 ...]
```

The index buffer is typically `uint16` or `uint32` — dramatically smaller than duplicating full vertex data (which can be 32+ bytes per vertex including position, normal, UV, tangent).

**Critical performance insight for mobile**: Every vertex consumes VRAM bandwidth. On mobile devices, memory bandwidth is more precious than raw compute power. A distant, barely-visible object with tens of thousands of faces is literally draining the phone's battery by saturating the memory bus. Many beginners make models overly "smooth" in their DCC tool without considering this cost.

### 1.3.2 Textures: From Image Files to VRAM Blocks

**The core question**: Why do games almost never use JPG/PNG as runtime formats?

Consider a 4K (4096x4096) PNG background image:

| | Disk Size | VRAM Usage (4K RGBA) | Reading Mechanism | Use Case |
|---|---|---|---|---|
| **PNG/JPG** | Small (2 MB) | Massive (64 MB) | CPU must fully decompress → RAM → upload to VRAM | Source files only |
| **ASTC/ETC** | Medium (4 MB) | Minimal (4 MB) | GPU hardware directly reads compressed data, no decompression needed | Runtime |

**Why JPG/PNG is catastrophic at runtime**: JPG and PNG use prediction-based and frequency-domain compression (DCT for JPG, DEFLATE for PNG). These algorithms exploit spatial redundancy across the entire image. The consequence: to read a single pixel in the bottom-right corner of the image, the CPU must **decompress the entire image in full**. This instantly consumes 64 MB of RAM (4096 x 4096 x 4 bytes = 64 MB for RGBA). The CPU then uploads this entire 64 MB buffer to VRAM.

**Why block compression (ASTC/ETC) is efficient**: Block-compressed formats divide the image into small fixed-size blocks (e.g., 4x4 pixels for ASTC, 4x4 for ETC2). The GPU hardware contains dedicated circuitry that can **directly extract pixel colors from the compressed data** without any decompression step. The compressed data stays compressed in VRAM; the GPU samples it on-the-fly during rendering. This is a hardware-level optimization that no CPU decompression can match.

**Hard rule**: Never use uncompressed formats like RGB24/RGBA32 in Unity to reduce APK/IPA size, and never attempt to dynamically load JPG at runtime. Trading disk space for VRAM efficiency is a fundamental consensus in game development. Modern phones can easily store a 4 MB file, but consuming an extra 60 MB of VRAM may cause an outright crash (OOM).

### 1.3.3 Shaders: From Code to Micro-Instructions

**The core question**: Can a GPU directly read C# or C++?

No. GPUs have their own instruction set architecture (ISA). Shader assets (`.shader`) are compiled into **binary microcode** during the build process. This is not optional — it is a hardware requirement of every GPU on the market.

**The Variant explosion problem**: A shader containing `#pragma multi_compile` directives actually **splits into hundreds or thousands of independent micro-programs**. Each valid combination of `#pragma` keywords produces a separate compiled shader variant. For example, a shader with three independent multi_compile directives (each with 2 options) produces `2^3 = 8` variants. Real production shaders can easily generate thousands.

This has massive practical implications:

- Build time increases linearly with variant count (each variant is compiled separately for each target platform).
- Runtime shader loading cost grows with the number of variants that must be compiled and uploaded to the GPU.
- Shader stripping (removing unused variants via `ShaderVariantCollection` or `IPreprocessShaders`) becomes essential for production builds.

**Resource essence**: A Shader asset is fundamentally a **GPU program library**. Loading it means uploading these instruction sets into the GPU's instruction cache, where they can be dispatched by draw calls.

---

## 1.4 Layer 3: Structural Assets — The Data Glue

Meshes, Textures, and Shaders are individual parts. Prefabs and Scenes are the assembly instructions that bind them together. They contain **no image or model data themselves** — they are pure relational data.

### 1.4.1 Prefabs and Scenes as GUID Index Tables

**The core question**: Why does renaming a file inside Unity preserve all references, but deleting a `.meta` file breaks everything?

This comes down to Unity's foundational identity system.

#### A. GUID: The Asset's ID Card

In the operating system, files are identified by their name. Inside Unity, the file name is merely a changeable nickname. Unity's true identification system is the **GUID (Globally Unique Identifier)**.

When a file enters the `Assets/` folder, Unity immediately generates a companion `.meta` file. Inside that file is a line like:

```
guid: 5a2b9c3d4e5f...
```

This GUID is the asset's true identity within the engine. The GUID is generated once and never changes — unless the `.meta` file is deleted, in which case a brand-new GUID is generated on re-import.

#### B. The Reference Graph

When you assign `Sword.png` as a texture on `Knight.prefab`, the Prefab file does **not** store the string `"Sword.png"`. It stores the GUID of `Sword.png`.

**Loading logic**: When the engine loads a Prefab, it reads instructions like: "Load the texture whose GUID is `5a2b...`, and apply it to the model whose GUID is `8c1d...`."

**Glue role**: Prefabs and Scenes are essentially **giant GUID index tables** that describe how assets reference each other.

**Critical workflows and warnings**:

- **Deleting `.meta` = tearing up the ID card**. Unity treats the original asset as "vanished" and the file as a brand-new import with a new GUID.
- **Result**: Old Prefabs holding the old GUID cannot find their target. They show purple "Missing" materials or lost script references.
- **Correct workflow**: All asset moves and renames **must** be done through Unity's Project panel. Unity updates the `.meta` GUID-to-path mapping correctly only when it orchestrates the operation itself. Moving files in the Windows/macOS file explorer bypasses this mechanism.

### 1.4.2 Why Scripts Are Also Assets

A script (`.cs` file) is not just code — in Unity's serialization system, it is a special kind of asset.

**The core question**: Where is `HP = 100` (set in the Inspector) actually stored?

It is **not** written into the C# code. It is **serialized** into the `.prefab` or `.unity` (Scene) file.

**Data-driven architecture**:

| Component | Role |
|---|---|
| **Script** (`.cs`) | Defines the data structure — "there is an `int` here, a `float` there" — the class definition |
| **Prefab** (`.prefab`) | Stores the concrete values — `int` = 100, `float` = 5.5 — the serialized instance data |

**Loading process**: When the engine loads a Prefab:
1. Read the serialized values from disk.
2. Use the script's GUID to look up the corresponding class definition (metadata).
3. Deserialize the object into memory, reconstructing it from the stored values and the class template.

**Performance concern**: Serialization and deserialization are **expensive CPU operations**. If a Prefab has an extremely complex structure (hundreds of nested GameObjects, hundreds of attached MonoBehaviour components), `Instantiate()` will cause a noticeable frame spike or freeze. This is why large open-world games often implement custom lightweight binary loading systems rather than relying entirely on Unity's native Prefab system.

### 1.4.3 Summary: Redefining How You See the Assets Folder

After this analysis, the game resource directory is not just a file tree — it is a map of data morphologies:

| Asset Type | Runtime Essence |
|---|---|
| **Mesh** | Geometry data stream optimized for GPU cache lines (Vertex Buffer + Index Buffer) |
| **Texture** | Compressed VRAM blocks that the GPU reads directly — not a PNG image |
| **Prefab** | Assembly instruction sheet that connects data via GUID references |
| **Shader** | GPU program library (compiled binary micro-instructions) |
| **Script** | Metadata class definition; concrete values are serialized into Prefabs/Scenes, not stored in the `.cs` file itself |

---

# 2. Unity Asset Import Pipeline Deep Dive

## 2.1 How Unity Imports Resources

When you drag files into Unity, you see a progress bar. Beneath the surface, Unity runs a complex **Asset Database** system.

### 2.1.1 Input and Output: Source vs Artifact

**The absolute core concept**: Unity's runtime **never reads your source files**.

| Stage | Description | Location |
|---|---|---|
| **Input (Source Asset)** | Your `.png`, `.fbx`, `.cs` files on disk, as authored by artists and programmers | `Assets/` folder |
| **Conversion (Importing)** | The process of translating source files into engine-readable binary format | The import pipeline |
| **Output (Artifact)** | The resulting binary — texture VRAM data blocks, model vertex buffers, compiled code | `Library/` folder |

**Determinism guarantee**: As long as the source file (e.g., `.png` bytes) and import settings (stored in `.meta`) remain unchanged, the generated Artifact is **unique and reproducible**. Deleting the `Library/` folder simply removes the cache — Unity will re-import from source and produce **identical** Artifacts. You never need to back up or version-control the `Library/` folder.

### 2.1.2 Parallel Import (Asset Database V2)

Older Unity versions used single-threaded import, causing multi-hour import times for large projects. Modern Unity (Asset Database V2) uses a **multi-process architecture**:

1. **Main Editor Process**: Scans for file changes and distributes tasks.
2. **Worker Processes**: Unity launches multiple background Unity Editor helper processes. These are independent processes, not threads, which avoids GIL-style bottlenecks.
3. The main process assigns `A.png` to Worker 1 and `B.fbx` to Worker 2, enabling truly parallel import across CPU cores.

**Rules for writing parallel-safe import code**:

- Do not contend for file locks during import (e.g., writing to a shared log file).
- Custom asset processing code must be **thread-safe** — no shared mutable state between import operations.
- Custom importers must **not depend on other assets** that are simultaneously being imported (no cross-asset dependencies during import). If you need to reference another asset's results, design it as a post-import step or use `AssetDatabase` APIs after import completes.

---

## 2.2 Special Folders

Unity's import pipeline does not treat all folders equally. Certain folder names carry special semantics that alter import order, compilation behavior, or build inclusion.

### 2.2.1 `Editor/` — Code That Never Ships

**Behavior**: Scripts placed here are treated as editor extension tools.

**Pipeline effects**:
- Code is compiled into `Assembly-CSharp-Editor.dll`, which only exists in the Editor.
- **Stripped at build time**: When building the game (APK/EXE), everything in this folder is **discarded entirely**.
- **Use case**: Helper tools, custom Inspector windows, asset validation scripts, and `AssetPostprocessor` classes (which should typically live here). Bugs in Editor code will never cause a game crash at runtime — they can only affect the Editor experience.

### 2.2.2 `StreamingAssets/` — The Sole Exception

**Behavior**: Assets retained in their original format for runtime streaming.

**Pipeline effects**:
- **Bypasses the import pipeline entirely**. Unity does not attempt to import these files, does not generate Artifacts for them, and does not modify them in any way.
- Files are copied **verbatim** into the final build package at a platform-specific path accessible via `Application.streamingAssetsPath`.
- **Use case**: Video files (MP4), raw configuration files (JSON/XML) that need runtime I/O access, or pre-built AssetBundle files themselves (when hosting bundles locally rather than downloading).

### 2.2.3 `Resources/` — The Newcomer's Trap

**Behavior**: Assets can be loaded at runtime via `Resources.Load("Path/To/Asset")` using string paths. All `Resources/` folders (regardless of where they appear in the project hierarchy) are merged at build time.

**Pipeline effects**: At build time, Unity merges the contents of **all** `Resources/` folders and their associated metadata (including file path mapping) into a single massive serialized file (`resources.assets`).

**The costs**:

| Problem | Explanation |
|---|---|
| **Fine-grained memory management is impossible** | All resources are bundled together with implicit, untracked dependencies. Unlike AssetBundles, you cannot precisely "unload a group of no-longer-needed resources." Memory peaks become uncontrollable because you cannot surgically free asset memory. |
| **Startup time penalty** | On app launch, Unity must read the massive serialized file header and build an in-memory index for **all** resource paths contained within it — even resources you may never load during that session. This adds measurable startup latency. |
| **Build time penalty** | The serialization of all `Resources/` folders into one monolithic file adds measurable build time, and it must happen every build regardless of whether resources changed. |

**Recommendation**: Only use `Resources/` for rapid prototyping or truly minimal configuration loading (a handful of very small assets). Avoid it entirely in production projects.

**Reference**: Unity's full special folders documentation: `https://docs.unity3d.com/6000.2/Documentation/Manual/SpecialFolders.html`

---

## 2.3 Extending the Pipeline: Teaching Unity New File Formats

Unity natively supports PNG, FBX, C#, and other standard formats. But when you drag in a custom data format — `.lua`, `.level`, or a custom `.mydata` extension — Unity treats it as an opaque binary blob. It is not previewable in the Inspector and not processed by any import pipeline stage.

### 2.3.1 ScriptedImporter: A Custom Text Importer

`ScriptedImporter` allows you to teach Unity to recognize and process new file extensions, bringing them into the full Asset Database management system with Cache Server and Artifact benefits.

**Example**: A `.level` file extension for level designer data:

```csharp
using UnityEngine;
using UnityEditor.AssetImporters;
using System.IO;

[ScriptedImporter(1, "level")]
public class LevelFileImporter : ScriptedImporter
{
    public override void OnImportAsset(AssetImportContext ctx)
    {
        // 1. Read the source file text from disk
        string text = File.ReadAllText(ctx.assetPath);

        // 2. Parse logic goes here — e.g., parse text into a ScriptableObject or custom data structure
        TextAsset subAsset = new TextAsset(text);

        // 3. Register the product (Artifact) into the import context
        // This transforms a plain text file on disk into a managed TextAsset object in the Asset Database
        ctx.AddObjectToAsset("MainText", subAsset);

        // 4. Set as the main asset — this is what appears in the Project window when you click the file
        ctx.SetMainObject(subAsset);
    }
}
```

Key details:
- `[ScriptedImporter(1, "level")]`: The first parameter (`1`) is a **version number**. Increment it to force Unity to re-import all files of this extension (useful when you change the importer logic). The second parameter (`"level"`) is the file extension Unity should associate with this importer.
- `AssetImportContext` is the bridge: you read source data from disk, process it, and register the resulting Artifact object(s). Multiple sub-assets can be added to a single source file.
- Once registered, the Artifact becomes part of Unity's dependency tracking, GUID system, and Cache Server — identical to how a `.png` is handled.

Together with `ScriptedImporter`, you are not merely a consumer of the pipeline — you become a **pipeline definer**, bringing arbitrary file formats into Unity's managed asset ecosystem.

---

## 2.4 Enforcement: Automating Import Rules with AssetPostprocessor

Relying on verbal conventions ("remember to disable Mipmap on UI images") is unreliable at team scale. `AssetPostprocessor` provides **code-level enforcement** by intercepting the import pipeline — either before or after Artifact generation.

### 2.4.1 Pre-process vs Post-process Timing

| Timing | When It Fires | Cost | Best Use |
|---|---|---|---|
| **Pre-process** (`OnPreprocessTexture`, `OnPreprocessModel`, etc.) | After Unity reads the `.meta` configuration but **before** the expensive Artifact generation begins | Zero extra cost — you modify import settings, then Unity proceeds with a single clean import using those settings | Setting compression format, toggling Mipmap, changing texture type, forcing Sprite mode |
| **Post-process** (`OnPostprocessModel`, `OnPostprocessTexture`, etc.) | After the Artifact is fully generated — the asset already exists as a runtime object (`Texture2D`, `Mesh`, `GameObject`) | High cost — the asset is already in memory. Modifying properties at this stage may require re-uploading to VRAM or triggering a secondary import pass | Read-only validation: logging warnings, checking for missing materials, verifying file naming conventions |

**Pre-process is the optimal modification window.** Post-process should be reserved for validation and diagnostics that do not modify the asset.

### 2.4.2 Implementation: Automated Import Standardization Script

This script should be placed in `Assets/Editor/`:

```csharp
using UnityEngine;
using UnityEditor;

public class ProjectResourceRule : AssetPostprocessor
{
    // -----------------------------------------------------------
    // Scenario: UI folder images → auto-disable Mipmap, auto-set Sprite type
    // -----------------------------------------------------------
    // Called: After Unity reads .meta config, BEFORE Artifact generation
    void OnPreprocessTexture()
    {
        // 1. Get the importer instance for this specific texture
        TextureImporter importer = (TextureImporter)assetImporter;

        // 2. Path-based rule matching (string-based, simple but effective)
        if (assetPath.Contains("Assets/Art/UI/"))
        {
            // 3. Enforce settings — overrides whatever the artist may have set manually
            if (importer.mipmapEnabled)
            {
                importer.mipmapEnabled = false;
            }

            if (importer.textureType != TextureImporterType.Sprite)
            {
                importer.textureType = TextureImporterType.Sprite;
            }
        }
    }

    // -----------------------------------------------------------
    // Scenario: Model post-import validation — check for missing material slots
    // -----------------------------------------------------------
    // Called: AFTER Artifact is generated, the full GameObject hierarchy is constructed
    void OnPostprocessModel(GameObject g)
    {
        Renderer[] renderers = g.GetComponentsInChildren<Renderer>();
        foreach (var r in renderers)
        {
            if (r.sharedMaterial == null)
            {
                Debug.LogWarning(
                    $"[Asset Warning] Model {assetPath} has a Renderer with missing material: {r.name}"
                );
            }
        }
    }
}
```

### 2.4.3 Available Callback Methods (Partial List)

| Callback | Asset Type | Timing |
|---|---|---|
| `OnPreprocessTexture()` | Texture | Before texture Artifact generation |
| `OnPostprocessTexture(Texture2D t)` | Texture | After texture Artifact generation |
| `OnPreprocessModel()` | Model (FBX, etc.) | Before model import |
| `OnPostprocessModel(GameObject g)` | Model | After model is imported as GameObject |
| `OnPreprocessAudio()` | Audio | Before audio clip import |
| `OnPostprocessAudio(AudioClip clip)` | Audio | After audio clip is generated |
| `OnPreprocessAsset()` | Any asset | Generic pre-process, fires for any asset type |

### 2.4.4 What These Mechanisms Enable

| Capability | Mechanism |
|---|---|
| **Eliminate uncertainty** | Understand Source → Artifact transformation to diagnose missing references and GUID conflicts |
| **Build performance optimization** | Avoid `Resources/` indexing overhead; leverage `StreamingAssets` for raw file pass-through delivery |
| **Pipeline extensibility** | `ScriptedImporter` to bring custom data formats (level configs, DSL scripts) into Unity's caching and management ecosystem |
| **Automated standardization** | `AssetPostprocessor` to enforce project-wide rules (texture compression format, Mipmap policy, texture type) via code, eliminating human error and drift |

---

# 3. Directory Planning and Packaging Strategy

## 3.1 Pipeline-Role-Based Directory Structure

In commercial projects, directory structure is not merely a file classification system — it is a **direct mapping of the Build Pipeline**. Where files live determines how they are processed, whether they ship, and how they are bundled.

The traditional "classify by resource type" approach (e.g., `Assets/Textures/`, `Assets/Models/`, `Assets/Audio/`) is **inefficient for automated builds** because it fragments the cohesion of functional modules. A single UI panel might draw assets from 5 different type-based folders, making it hard to reason about dependencies and bundle boundaries.

### 3.1.1 Recommended Top-Level Structure

```
Assets/
├── 3rd/                   # [Isolation Zone] Third-party plugins and SDKs
├── Editor/                # [Tool Layer] Project-level build scripts, pipeline tools
├── GameResources/         # [Raw Production Zone] Artist-submitted original files
├── GameAssets/            # [Build Zone] Runtime assets, build pipeline whitelist
├── Scenes/                # [Scene Zone] Launch scenes, art review scenes, etc.
└── Scripts/               # [Code Layer] C# source code
```

### 3.1.2 Core Zone Details

#### A. `GameResources/` (Raw Production Zone)

- **Contents**: Artist-submitted `.fbx`, `.psd`, `.wav` and other raw DCC source files.
- **Rule — Referenced only**: These files exist only as **dependencies** of Prefabs inside `GameAssets/`. They are never directly bundled or loaded at runtime. Think of them as "raw materials" that get consumed by the build pipeline.
- **Automation entry point**: The `AssetPostprocessor` (from Chapter 2) primarily monitors this directory to enforce compression standards on everything artists import. This is where automated import rules are most impactful.

The conceptual model is clean: `GameResources/` = raw materials, `GameAssets/` = finished products ready for packaging.

#### B. `GameAssets/` (Build Zone)

- **Contents**: Prefabs, ScriptableObjects, Unity Assets (Timeline, Animator Controller), Textures, Materials — everything that a runtime scene actually loads.
- **Rule — Build whitelist**: Build scripts scan **only** this directory for packaging. Nothing outside `GameAssets/` is directly bundled unless pulled in as a dependency of something inside `GameAssets/`.
- **Modular structure**: Subdirectories under `GameAssets/` **directly map to AssetBundle granularity** — the directory tree is the bundle design document.

**Example structure with rationale**:

```
GameAssets/
├── Common/               # [Shared Bundle] Foundational assets referenced by many consumers
│   ├── Shaders/          # [Shader Bundle] Shader files, ShaderVariantCollections
│   └── Fonts/            # [Font Bundle] Font assets used throughout the game
├── Scenes/               # [Scene Bundles] .unity files — each scene maps to one bundle
├── Configs/              # [Data Bundle] Binary configs, text data, ScriptableObject game data
├── Textures/             # [Standalone Images] Large standalone images loaded independently (backgrounds, splash)
├── Prefabs/              # [Dynamic Loading]
│   ├── Hero/             # Loadable character prefabs — each hero gets its own bundle
│   └── Monster/          # Loadable enemy prefabs — each monster type gets its own bundle
└── UIModules/            # [UI Functional Modules]
    ├── UILogin/          # → UILoginPanel.prefab packaged as uiloginpanel.bundle
    └── UIMain/           # → UIMainPanel.prefab packaged as uimainpanel.bundle
```

#### C. `3rd/` (Isolation Zone)

- **Contents**: Third-party plugins downloaded from the Asset Store or other sources.
- **Rule — Assembly Definition (`.asmdef`) isolation**: Create an Assembly Definition file for each major plugin. This prevents plugin code changes from triggering full-project recompilation and cascading into business code compilation.
- **Rule — Build stripping**: Build scripts should **forcefully ignore** non-code resources in this directory to prevent plugin demo content (sample scenes, test textures, example prefabs) from leaking into the production build.

---

## 3.2 Packaging Granularity Strategies

AssetBundle granularity directly determines runtime I/O efficiency, memory footprint, and hotfix update size. This is an engineering trade-off requiring careful balance — there is no one-size-fits-all answer.

### 3.2.1 Three Strategies Compared

| Strategy | Description | Advantages | Disadvantages |
|---|---|---|---|
| **Fine-grained (per file)** | Each individual Prefab or Texture becomes its own AssetBundle | Minimal hotfix footprint — changing one asset downloads only one tiny bundle. Zero redundant data in memory. | I/O bottleneck — opening thousands of files consumes file handles and OS resources. Memory bloat — each bundle has a header occupying several KB; thousands of bundles create cumulative overhead. |
| **Coarse-grained (per type/category)** | An entire folder or asset category becomes one massive AssetBundle | Fewest I/O operations — one file open per category. Highest compression ratio — more data means better LZ4/LZMA compression. | Hotfix disaster — changing 1 KB inside the bundle requires users to re-download hundreds of MB. Impractical for live games with frequent updates. |
| **Logical granularity (per directory/function)** [Recommended] | Group assets by functional module and lifecycle — assets that load and unload together live together | Balanced solution. Same-lifecycle resources load together (amortized I/O) and unload together (clean memory release). | Requires strict directory conventions and discipline from the entire team. Cannot be retrofitted onto a messy project. |

### 3.2.2 Recommended Scheme: Lifecycle-Based Logical Partitioning

Based on the `GameAssets/` directory structure, apply the following packaging rules:

**UI / Functional Modules — Per-panel bundling**:

- Rule: Each panel under `GameAssets/UIModules/UILogin/` gets its own bundle named after the panel (`uiloginpanel.bundle`).
- Rationale: UI panels have well-defined, predictable load/unload boundaries. When a panel closes, its entire bundle can be unloaded cleanly with no dangling references.

**Shared Resources — Per-type bundling**:

- Rule: `GameAssets/Common/Fonts/` → `fonts.bundle`. `GameAssets/Common/Shaders/` → `shaders.bundle`.
- Rationale: Shared resources are typically resident in memory for long durations (often for the entire session). Independent bundling by type simplifies reference-count management and prevents cross-reference circularity that leads to unload errors (where bundle A references something in bundle B and vice versa).

**Scenes — Independent bundling**:

- Rule: `Level_01.unity` → `scene_level_01.bundle`. One bundle per scene, no exceptions.
- Rationale: `SceneManager.LoadScene` is a natural, engine-level resource load/unload boundary. When a scene is unloaded, Unity can release all assets exclusively owned by that scene's bundle.

---

## 3.3 Dependency Management: Eliminating Redundancy

**Implicit dependency duplication is the primary cause of build size bloat.** Understanding and preventing it is perhaps the most impactful optimization a technical artist or build engineer can make.

### 3.3.1 The Problem: Implicit Dependency Duplication

**Scenario**: `UILoginPanel.prefab` and `UIShopPanel.prefab` both have a `RawImage` component that references `GameResources/UI/BackBg.png`.

**Incorrect configuration**: `BackBg.png` lives in `GameResources/` and is **not explicitly assigned** to any AssetBundle. It is only a dependency — not a bundle member itself.

**What Unity's build pipeline does**: Unity detects that `BackBg.png` is an "orphan" dependency — it is needed by two bundles but belongs to neither. To ensure that the Login bundle and Shop bundle can each function independently (the fundamental design contract of AssetBundles), Unity **duplicates** the full binary data of `BackBg.png` into **both** `uiloginpanel.bundle` and `uishoppanel.bundle`.

**The cascading costs**:

1. **Build size inflation**: The same texture data exists twice (or N times for N referencing bundles) in the shipped build.
2. **Runtime memory waste**: When both panels are loaded, two separate copies of the identical texture exist in VRAM with different internal Unity object IDs. They do not share memory.
3. **Batching broken**: Because the textures have different IDs, GPU draw-call batching cannot merge them. Each panel's UI elements using `BackBg.png` are drawn in separate batches.

### 3.3.2 The Solution: Dependency Counting Algorithm

Introduce dependency analysis into the build pipeline as a pre-build validation step.

**Algorithm logic (three steps)**:

1. **Collect**: Traverse all assets marked for packaging under `GameAssets/`. For each asset, recursively gather its full dependency chain via `AssetDatabase.GetDependencies()`.
2. **Count**: Build a map: `asset_path → reference_count`. Every dependency gets a count of how many distinct bundles reference it.
3. **Decide**:
   - **Reference count = 1**: No action needed. Let the asset be implicitly included in its sole referencing bundle. This reduces fragmentation and keeps the number of bundles manageable.
   - **Reference count >= 2**: This asset MUST be handled explicitly. It is a shared dependency that will cause duplication.

**Two approaches for handling multi-referenced assets**:

| Approach | Description | Verdict |
|---|---|---|
| **A: Auto-extraction** | Build script automatically detects the shared asset, creates a new bundle for it (`shared_backbg_texture.bundle`), and adds it to the Manifest. | **Not recommended.** Can produce thousands of fragmented micro-bundles over time, causing the Manifest to become extremely large and unwieldy. The bundle graph becomes inscrutable. |
| **B: Mandatory enforcement** [Recommended] | Build script **throws an exception** with a clear error message and **halts the entire build pipeline**: `"Error: [BackBg.png] is referenced by 2 bundles. Please move it to 'GameAssets/Textures'!"` | **Recommended.** Forces developers to make a conscious, deliberate decision about where the shared asset belongs. Prevents the uncontrollable entropy of automated splitting. |

**Why mandatory enforcement wins**: Auto-extraction appears convenient but introduces hidden costs:

- Each auto-generated bundle adds a header entry to the Manifest, growing Manifest parsing time at startup.
- Thousands of micro-bundles create runtime loading overhead (file handles, header parsing, decompression).
- The dependency graph becomes opaque — no one on the team can explain why a particular bundle exists or when it can be safely unloaded.

Forcing a build error with a clear diagnostic message preserves structural clarity. The developer must consciously move the shared asset to `GameAssets/Textures/` (or another appropriate location) where it becomes a first-class, explicitly managed shared bundle with a clear lifecycle and owner.

---

## 3.4 Chapter Summary

The three engineering pillars that solve resource architecture at commercial scale:

| Pillar | Mechanism | Why It Matters |
|---|---|---|
| **Directory isolation** | `GameResources/` (raw materials) separated from `GameAssets/` (finished products) with `3rd/` (isolated third-party) | Defines a clear, narrow build pipeline input boundary. Everything outside these zones is either tooling or ignored. |
| **Logical granularity packaging** | Rejects extreme per-file and per-type bundling. Adopts lifecycle-based directory mapping where directory structure IS the bundle design. | Balances I/O overhead and memory management. Resources that load together, bundle together. Resources that unload together, bundle together. |
| **Dependency governance** | Reference-counting dependency analysis eliminates resource duplication at its root, before it enters the build. | The #1 cause of build bloat is silent duplication. Catching it in CI prevents it from ever reaching players. |

A well-designed upfront directory and packaging plan prevents entropy from growing uncontrollably. Commercial projects are invariably more complex than the examples here, but the principles scale — early discipline is the foundation of long-term maintainability.
