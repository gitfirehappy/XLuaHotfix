# Infrastructure Consistency Fix Plan (2026-05-11)

> **Source**: `requirements/refactor-2026/review/fyasset-infrastructure-consistency-review-20260511.md`
> **Status**: Executed (2026-05-11)
> **Scope**: 统一 FileHelper / build 产物处理 / 编排结果类型 / 错误码 / 资源路径常量——消除 FYAsset 基础设施旁路
> **Out of scope**: 新功能开发、管线架构重构、旧 BuildProjectManager 接口分离（已有独立 plan）

---

## Decisions Snapshot

| # | Review Finding | Severity | Decision |
|---|---------------|----------|----------|
| 1 | Hotfix 多处绕过 FileHelper 直调 `File.*` / `Directory.*` | High | 扩展 FileHelper（CopyFile / TryCopyFile / EnsureDirectory / DirectoryExists / GetDirectories），迁移 HotfixManager + LegacyHotfixBackend + PackageCleaner。`version_state.json` 写入改用原子写 |
| 2 | TaskOrganizeOutput / ABBuildBackend / BuildPathCustomizer 三处重复 build 产物处理逻辑 | High | 抽取 `BuildArtifactOrganizer` 共享模块，三处委托到共享层，写入走 FileHelper + SerializationUtility |
| 3 | IHotfixPipeline / IBuildBackend 顶层接口返回 `bool` + 裸字符串，无法承载结构化诊断 | High | 引入 `HotfixStepResult` / `BuildBackendResult` 结构化类型替代 `bool` + `BuildSummary`；编排层统一走结构化消息 |
| 4 | ABPackageBackend / TaskPrepareContext / TaskBuildBundles / TaskGenerateManifest / TaskVerifyBuildResult 硬编码错误码字符串 | Medium | 扩展 `RuntimeErrorCodes`（加 `InvalidArgument`）、扩展 `BuildErrorCodes`（加 `InvalidBackend` / `InvalidPlatform` / `BuildFailed` / `RawfileMultiAsset` / `RawfileCopyFailed` / `BundleFileNotFound` / `BundleNotFound_Build` / `ManifestInitFailed` / `VerificationFailed`），替换所有裸字符串 |
| 5 | TaskPrepareContext / TaskBuildBundles / PipelinePanel / VersionPanel / BuildProjectManager 硬编码 asset 路径 | Medium | 加 `FYAssetConstants.VERSION_DATABASE_ASSET_PATH`，替换 6 处硬编码路径为常量引用 |

---

## Task Breakdown

### P1: FileHelper 扩展 + Hotfix 迁移（Finding 1）

#### T1.1 — 扩展 FileHelper

**改** `Assets/FYAsset/Scripts/Helpers/FileHelper.cs`，新增 5 个方法：

```csharp
// 确保目录存在（纯目录，非文件父目录）
public static void EnsureDirectory(string dirPath)

// 目录存在性检查（跨平台安全）
public static bool DirectoryExists(string path)

// 文件拷贝（覆盖）
public static void CopyFile(string src, string dest, bool overwrite = true)

// 文件拷贝（不抛异常，失败返回 false）
public static bool TryCopyFile(string src, string dest, bool overwrite = true)

// 目录枚举（跨平台安全，返回绝对路径）
public static string[] GetDirectories(string path, string searchPattern = "*")
```

#### T1.2 — 迁移 HotfixManager.cs

**改** `Assets/FYAsset/Scripts/LegacyRuntime/HotfixManager.cs`，8 处直调替换：

| 行号 | 原调用 | 替换为 |
|------|-------|--------|
| 153 | `File.Exists(localManifestPath)` | `FileHelper.Exists(localManifestPath)` |
| 304 | `Directory.CreateDirectory(targetBundleRoot)` | `FileHelper.EnsureDirectory(targetBundleRoot)` |
| 337 | `File.Exists(localPath)` | `FileHelper.Exists(localPath)` |
| 341 | `File.Copy(localPath, savePath, true)` | `FileHelper.CopyFile(localPath, savePath)` |
| 457 | `File.Exists(guidFilePath)` | `FileHelper.Exists(guidFilePath)` |
| 461 | `File.ReadAllText(guidFilePath).Trim()` | `FileHelper.ReadAllTextAsync` 同义（注意：这是同步路径，需确认是否可改为 async；若不可，加 `FileHelper.ReadAllText(string path)` 同步包装） |
| 492 | `Directory.Delete(aaCachePath, true)` | `FileHelper.TryDeleteDirectory(aaCachePath)` |
| 500 | `File.WriteAllText(guidFilePath, currentGuid)` | `FileHelper.WriteAllTextAtomic(guidFilePath, currentGuid)` |

#### T1.3 — 迁移 LegacyHotfixBackend.cs

**改** `Assets/FYAsset/Scripts/LegacyRuntime/LegacyHotfixBackend.cs`：

| 行号 | 原调用 | 替换为 |
|------|-------|--------|
| 70 | `File.Exists(localVersionStatePath)` | `FileHelper.Exists(localVersionStatePath)` |
| 149 | `File.WriteAllText(Path.Combine(...), _remoteVersionJson)` | `FileHelper.WriteAllTextAtomic(Path.Combine(...), _remoteVersionJson)` |
| 通用 | 确保 metadata 文件（version_state.json / guid）走原子写，与 ABBackend 对齐 |

#### T1.4 — 迁移 PackageCleaner.cs

**改** `Assets/FYAsset/Scripts/LegacyRuntime/PackageCleaner.cs`：

| 行号 | 原调用 | 替换为 |
|------|-------|--------|
| 14 | `Directory.Exists(PathManager.HotfixRoot)` | `FileHelper.DirectoryExists(PathManager.HotfixRoot)` |
| 16 | `Directory.Delete(PathManager.HotfixRoot, true)` | `FileHelper.TryDeleteDirectory(PathManager.HotfixRoot)` |
| 42 | `Directory.GetDirectories(PathManager.HotfixRoot, "Build_*")` | `FileHelper.GetDirectories(PathManager.HotfixRoot, "Build_*")` |
| 74 | `Directory.Delete(dirInfo.FullName, true)` | `FileHelper.TryDeleteDirectory(dirInfo.FullName)` |

#### T1.5 — 加 ReadAllText 同步方法

`FileHelper` 现有 `ReadAllTextAsync` 但 HotfixManager line 461 是同步调用。若上下文不允许改为 async，则在 FileHelper 新增：

```csharp
public static string ReadAllText(string path)  // 跨平台同步读取包装
```

---

### P2: Build 产物处理统一（Finding 2）

#### T2.1 — 新建 BuildArtifactOrganizer

**新建** `Assets/FYAsset/Scripts/Build/BuildManage/Editor/BuildArtifactOrganizer.cs`（Editor 程序集）：

```csharp
/// <summary>
/// 构建产物组织器 —— 所有 build artifact 的目录创建、文件拷贝、清理操作的唯一入口。
/// TaskOrganizeOutput / ABBuildBackend / BuildPathCustomizer 均委托到此。
/// </summary>
public static class BuildArtifactOrganizer
{
    public static void CreateOutputDirectory(string path);
    public static void DeleteOutputDirectory(string path);
    public static void CopyArtifact(string src, string dest);
    public static void WriteManifest(string path, string json);
    public static void WriteSummary(string path, string content);
    public static void CleanDirectory(string path);
}
```

内部实现统一走 `FileHelper`（EnsureDirectory / TryDeleteDirectory / CopyFile / WriteAllTextAtomic）和 `SerializationUtility`。

#### T2.2 — 迁移 TaskOrganizeOutput.cs

**改** `Assets/FYAsset/Scripts/Build/Pipeline/Editor/Tasks/TaskOrganizeOutput.cs`：

| 行号 | 原调用 | 替换为 |
|------|-------|--------|
| 36 | `Directory.CreateDirectory(outputDir)` | `BuildArtifactOrganizer.CreateOutputDirectory(outputDir)` |
| 47 | `File.Copy(srcPath, destPath, true)` | `BuildArtifactOrganizer.CopyArtifact(srcPath, destPath)` |
| 54 | `File.WriteAllText(manifestPath, ..., Encoding.UTF8)` | `BuildArtifactOrganizer.WriteManifest(manifestPath, ...)` |
| 88 | `File.WriteAllText(summaryPath, ..., Encoding.UTF8)` | `BuildArtifactOrganizer.WriteSummary(summaryPath, ...)` |
| 93 | `Directory.Delete(tempDir, true)` | `BuildArtifactOrganizer.DeleteOutputDirectory(tempDir)` |

#### T2.3 — 迁移 ABBuildBackend.cs

**改** `Assets/FYAsset/Scripts/Build/BuildManage/Editor/ABBuildBackend.cs`：

| 行号 | 原调用 | 替换为 |
|------|-------|--------|
| 62 | `Directory.Delete(outputDir, true)` | `BuildArtifactOrganizer.DeleteOutputDirectory(outputDir)` |
| 65 | `Directory.CreateDirectory(outputDir)` | `BuildArtifactOrganizer.CreateOutputDirectory(outputDir)` |
| 76, 104 | `File.Copy(sourcePath, destinationPath, true)` | `BuildArtifactOrganizer.CopyArtifact(...)` |
| 95 | `File.WriteAllText(manifestPath, ..., Encoding.UTF8)` | `BuildArtifactOrganizer.WriteManifest(manifestPath, ...)` |

#### T2.4 — 迁移 BuildPathCustomizer.cs

**改** `Assets/FYAsset/Scripts/Build/BuildManage/Editor/BuildPathCustomizer.cs`：

| 行号 | 原调用 | 替换为 |
|------|-------|--------|
| 28 | `Directory.Delete(finalOutputDir, true)` | `BuildArtifactOrganizer.DeleteOutputDirectory(finalOutputDir)` |
| 30 | `Directory.CreateDirectory(finalOutputDir)` | `BuildArtifactOrganizer.CreateOutputDirectory(finalOutputDir)` |
| 46, 60, 66 | `File.Copy(file, targetPath, true)` | `BuildArtifactOrganizer.CopyArtifact(...)` |
| 85 | `Directory.Delete(serverDataPath, true)` | `BuildArtifactOrganizer.DeleteOutputDirectory(serverDataPath)` |

---

### P3: 结构化编排结果类型（Finding 3）

#### T3.1 — 新建 HotfixStepResult

**新建** `Assets/FYAsset/Scripts/LegacyRuntime/HotfixStepResult.cs`（Runtime 程序集）：

```csharp
public readonly struct HotfixStepResult
{
    public bool Success { get; }
    public RuntimeMessage Error { get; }  // success 时 null
    public static HotfixStepResult Ok { get; }
    public static HotfixStepResult Fail(RuntimeMessage error) { ... }
}
```

#### T3.2 — 新建 BuildBackendResult

**新建** `Assets/FYAsset/Scripts/Build/BuildManage/Editor/BuildBackendResult.cs`（Editor 程序集）：

```csharp
public class BuildBackendResult
{
    public bool Success { get; }
    public BuildMessage Error { get; }                       // fatal 失败时非 null
    public IReadOnlyList<BuildMessage> Diagnostics { get; }  // 所有 warning + error
    public static BuildBackendResult Ok(List<BuildMessage> diagnostics = null) { ... }
    public static BuildBackendResult Fail(BuildMessage error, List<BuildMessage> diagnostics = null) { ... }
}
```

#### T3.3 — 更新 IHotfixPipeline 签名

**改** `Assets/FYAsset/Scripts/LegacyRuntime/IHotfixPipeline.cs`：

| 行号 | 原签名 | 新签名 |
|------|-------|--------|
| 25 | `Task<bool> InitializeBackendAsync()` | `Task<HotfixStepResult> InitializeBackendAsync()` |
| 49 | `Task<bool> PostDownloadAsync(HotfixContext ctx)` | `Task<HotfixStepResult> PostDownloadAsync(HotfixContext ctx)` |

同步改所有实现类（`ABHotfixBackend` / `LegacyHotfixBackend`）的返回类型。

#### T3.4 — 更新 IBuildBackend 签名

**改** `Assets/FYAsset/Scripts/Build/BuildManage/Editor/IBuildBackend.cs`：

| 行号 | 原签名 | 新签名 |
|------|-------|--------|
| 10 | `Task<bool> BuildAsync(VersionNumber, BuildType)` | `Task<BuildBackendResult> BuildAsync(VersionNumber, BuildType)` |
| 13 | `string BuildSummary { get; }` | **删除**（诊断信息进入 BuildBackendResult.Diagnostics） |

同步改所有实现类（`ABBuildBackend` / `LegacyAddressableBuildBackend`）。

#### T3.5 — 更新编排层

**改** `Assets/FYAsset/Scripts/LegacyRuntime/HotfixManager.cs`：

| 行号 | 原代码 | 改为 |
|------|-------|------|
| 58 | `private static void ReportError(string message)` | 保留但改签名为 `ReportError(HotfixStepResult result)`，内部从 `result.Error` 提取结构化信息 |
| 187 | `ReportError("[HotfixManager] 热更后端初始化失败")` | `ReportError(result)` |
| 377 | `ReportError("[HotfixManager] 存在下载失败的 bundle")` | `ReportError(result)` |

**改** `Assets/FYAsset/Scripts/Build/BuildManage/Editor/BuildProjectManager.cs`：

| 行号 | 原代码 | 改为 |
|------|-------|------|
| 123 | `bool buildSucceeded = ...` | `var result = backend.BuildAsync(...)` |
| 126 | `Debug.LogError($"[BuildProjectManager] 后端构建失败: {backend.BuildSummary}")` | 遍历 `result.Diagnostics` 输出结构化日志 |

---

### P4: 错误码 + 资源路径常量化（Finding 4 + 5）

#### T4.1 — 扩展 RuntimeErrorCodes

**改** `Assets/FYAsset/Scripts/Runtime/Models/RuntimeMessage.cs`（`RuntimeErrorCodes` 类内），新增：

```csharp
/// <summary>参数无效（null / 空字符串 / 越界等）</summary>
public const string InvalidArgument = "INVALID_ARG";
```

#### T4.2 — 迁移 ABPackageBackend.cs 裸字符串

**改** `Assets/FYAsset/Scripts/Runtime/Backends/AB/ABPackageBackend.cs`，4 处替换：

| 行号 | 原代码 | 替换为 |
|------|-------|--------|
| 100, 120, 143, 162 | `RuntimeMessage.Error("INVALID_ARG", ...)` | `RuntimeMessage.Error(RuntimeErrorCodes.InvalidArgument, ...)` |

#### T4.3 — 扩展 BuildErrorCodes

**改** `Assets/FYAsset/Scripts/Build/Editor/BuildMessage.cs`（`BuildErrorCodes` 类内），新增 9 个常量：

```csharp
public const string InvalidBackend = "INVALID_BACKEND";
public const string InvalidPlatform = "INVALID_PLATFORM";
public const string BuildFailed = "BUILD_FAILED";
public const string RawfileMultiAsset = "RAWFILE_MULTI_ASSET";
public const string RawfileCopyFailed = "RAWFILE_COPY_FAILED";
public const string BundleFileNotFound = "BUNDLE_FILE_NOT_FOUND";
public const string BundleNotFoundBuild = "BUNDLE_NOT_FOUND_BUILD";  // 区分 Runtime 的 BundleNotFound
public const string ManifestInitFailed = "MANIFEST_INIT_FAILED";
public const string VerificationFailed = "VERIFICATION_FAILED";
```

#### T4.4 — 迁移 5 个 Task 裸错误码

| 文件 | 行号 | 原代码 | 替换为 |
|------|------|-------|--------|
| TaskPrepareContext.cs | 30 | `Fail("INVALID_BACKEND", ...)` | `Fail(BuildErrorCodes.InvalidBackend, ...)` |
| TaskPrepareContext.cs | 58 | `Fail("INVALID_PLATFORM", ...)` | `Fail(BuildErrorCodes.InvalidPlatform, ...)` |
| TaskBuildBundles.cs | 121 | `Fail("BUILD_FAILED", ...)` | `Fail(BuildErrorCodes.BuildFailed, ...)` |
| TaskBuildBundles.cs | 240 | `Fail("RAWFILE_MULTI_ASSET", ...)` | `Fail(BuildErrorCodes.RawfileMultiAsset, ...)` |
| TaskBuildBundles.cs | 251 | `Fail("RAWFILE_COPY_FAILED", ...)` | `Fail(BuildErrorCodes.RawfileCopyFailed, ...)` |
| TaskGenerateManifest.cs | 46 | `Fail("BUNDLE_FILE_NOT_FOUND", ...)` | `Fail(BuildErrorCodes.BundleFileNotFound, ...)` |
| TaskGenerateManifest.cs | 73 | `Fail("BUNDLE_NOT_FOUND", ...)` | `Fail(BuildErrorCodes.BundleNotFoundBuild, ...)` |
| TaskGenerateManifest.cs | 166 | `Fail("MANIFEST_INIT_FAILED", ...)` | `Fail(BuildErrorCodes.ManifestInitFailed, ...)` |
| TaskVerifyBuildResult.cs | 159 | `Fail("VERIFICATION_FAILED", ...)` | `Fail(BuildErrorCodes.VerificationFailed, ...)` |

#### T4.5 — 加 VERSION_DATABASE_ASSET_PATH 常量

**改** `Assets/FYAsset/Scripts/FYAssetConstants.cs`，在 `PIPELINE_CONFIG_ASSET_PATH` 旁新增：

```csharp
public const string VERSION_DATABASE_ASSET_PATH = "Assets/Build/VersionDataBase.asset";
```

#### T4.6 — 替换硬编码路径

| 文件 | 行号 | 原代码 | 替换为 |
|------|------|-------|--------|
| TaskPrepareContext.cs | 22 | `"Assets/Build/BuildPipelineConfig.asset"` | `FYAssetConstants.PIPELINE_CONFIG_ASSET_PATH` |
| TaskPrepareContext.cs | 40 | `"Assets/Build/VersionDataBase.asset"` | `FYAssetConstants.VERSION_DATABASE_ASSET_PATH` |
| TaskBuildBundles.cs | 33 | `"Assets/Build/BuildPipelineConfig.asset"` | `FYAssetConstants.PIPELINE_CONFIG_ASSET_PATH` |
| PipelinePanel.cs | 60, 95 | `"Assets/Build/BuildPipelineConfig.asset"` | `FYAssetConstants.PIPELINE_CONFIG_ASSET_PATH` |
| VersionPanel.cs | 9 | `"Assets/Build/VersionDataBase.asset"` | `FYAssetConstants.VERSION_DATABASE_ASSET_PATH` |
| BuildProjectManager.cs | 15 | `"Assets/Build/VersionDataBase.asset"` | `FYAssetConstants.VERSION_DATABASE_ASSET_PATH` |

---

## Execution Order

```
P4.5 (FYAssetConstants 加常量)    ← 独立，新增 1 行，无依赖
P4.3 (BuildErrorCodes 扩展)      ← 独立，只加常量定义
P4.1 (RuntimeErrorCodes 扩展)    ← 独立，只加常量定义
    ↓
P4.6 (替换硬编码路径)             ← 依赖 P4.5
P4.4 (迁移 Task 裸错误码)         ← 依赖 P4.3
P4.2 (迁移 ABPackageBackend)      ← 依赖 P4.1
    ↓
T1.1 (扩展 FileHelper)            ← 独立，新增方法
    ↓
T1.5 (FileHelper 同步 ReadAllText) ← 依赖 T1.1
T1.2 (HotfixManager 迁移)         ← 依赖 T1.1 + T1.5
T1.3 (LegacyHotfixBackend 迁移)   ← 依赖 T1.1
T1.4 (PackageCleaner 迁移)        ← 依赖 T1.1
    ↓
T3.1 (HotfixStepResult)           ← 独立，新建类型
T3.2 (BuildBackendResult)         ← 独立，新建类型
    ↓
T3.3 (IHotfixPipeline 签名更新)   ← 依赖 T3.1
T3.4 (IBuildBackend 签名更新)     ← 依赖 T3.2
T3.5 (编排层更新)                 ← 依赖 T3.3 + T3.4
    ↓
T2.1 (BuildArtifactOrganizer)     ← 独立，新建模块（内部依赖 FileHelper）
    ↓
T2.2 (TaskOrganizeOutput 迁移)    ← 依赖 T2.1
T2.3 (ABBuildBackend 迁移)        ← 依赖 T2.1
T2.4 (BuildPathCustomizer 迁移)   ← 依赖 T2.1
```

**并行组**：P4.5 ∥ P4.3 ∥ P4.1（三个常量定义互不依赖，可同时做）。T3.1 ∥ T3.2（两个结果类型互不依赖）。T2.2 ∥ T2.3 ∥ T2.4（迁移到同一个共享层，互不依赖）。

---

## Invariants

1. `dotnet build XLuaHotfix.sln` 0 errors
2. `grep -rn "File\.Exists\|File\.Copy\|File\.WriteAllText\|File\.ReadAllText\|File\.Delete\|Directory\.CreateDirectory\|Directory\.Delete\|Directory\.Exists\|Directory\.GetDirectories" Assets/FYAsset/Scripts/LegacyRuntime/` 在 HotfixManager / LegacyHotfixBackend / PackageCleaner 三个文件中 0 命中（编辑器 Assembly 文件除外）
3. `grep -rn "File\.Copy\|File\.WriteAllText\|Directory\.CreateDirectory\|Directory\.Delete" Assets/FYAsset/Scripts/Build/Pipeline/Editor/Tasks/TaskOrganizeOutput.cs` 0 命中
4. `grep -rn "File\.Copy\|File\.WriteAllText\|Directory\.CreateDirectory\|Directory\.Delete" Assets/FYAsset/Scripts/Build/BuildManage/Editor/ABBuildBackend.cs` 0 命中
5. `grep -rn "File\.Copy\|Directory\.CreateDirectory\|Directory\.Delete" Assets/FYAsset/Scripts/Build/BuildManage/Editor/BuildPathCustomizer.cs` 0 命中
6. `grep -rn '"INVALID_ARG"' Assets/FYAsset/Scripts/` 0 命中（裸字符串，不含 RuntimeErrorCodes 定义行）
7. `grep -rn '"BUILD_FAILED"\|"RAWFILE_MULTI_ASSET"\|"RAWFILE_COPY_FAILED"\|"BUNDLE_FILE_NOT_FOUND"\|"BUNDLE_NOT_FOUND"\|"MANIFEST_INIT_FAILED"\|"VERIFICATION_FAILED"\|"INVALID_BACKEND"\|"INVALID_PLATFORM"' Assets/FYAsset/Scripts/Build/` 在 `.cs` 任务文件中 0 命中（不含 BuildErrorCodes 定义行）
8. `grep -rn '"Assets/Build/BuildPipelineConfig.asset"' Assets/FYAsset/Scripts/` 0 命中
9. `grep -rn '"Assets/Build/VersionDataBase.asset"' Assets/FYAsset/Scripts/` 0 命中（不含 FYAssetConstants 定义行）
10. IHotfixPipeline 所有实现类的 `InitializeBackendAsync` 返回 `Task<HotfixStepResult>`（非 `Task<bool>`）
11. IBuildBackend 所有实现类的 `BuildAsync` 返回 `Task<BuildBackendResult>`（非 `Task<bool>`）
12. `IBuildBackend.BuildSummary` 属性已删除，无残留引用
13. 旧管线 BuildProjectManager 行为不变（仅日志输出方式从裸字符串改为结构化消息遍历）

---

## Change Log

| Date | Change |
|------|--------|
| 2026-05-11 | 从 review 创建正式 plan。5 个 finding → 4 个优先级 → 17 个子任务，按执行顺序编排 |
