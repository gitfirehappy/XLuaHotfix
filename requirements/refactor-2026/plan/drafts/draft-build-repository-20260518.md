# Draft: Build Repository — Git-like 构建产物版本管理系统

> **Status**: Draft — 2026-05-18
> **Supersedes**: draft-E7-diff-snapshot-20260517.md, plan-smart-versioning-draft.md
> **Design philosophy**: 构建产物作为 repository 管理。完整 git 工作流隐喻——不是借用命名，而是同构的操作语义。Build Repository 只负责版本化管理，不关心构建过程和构建策略。
> **Dependencies**: E5-1 (IBuildTask + BuildContext + DAGScheduler), E5-2 (TaskBuildBundles), E6 (ABManifest)

---

## 核心定位

```
Build Pipeline（构建）→ 产出 artifacts
    ↓
Build Repository（本系统）→ 版本化管理 artifacts
    ↓
Build Report（记录）→ 构建元数据、耗时、环境
```

Build Repository 的职责边界：
- **管**：版本索引、状态流转、快照存储、差异计算、发布标记
- **不管**：构建过程、构建策略（如 AA 的 group 移动）、构建日志、版本号决策逻辑

---

## 操作集（7 个核心操作）

| 操作 | Git 等价 | 语义 | 修改状态 |
|------|---------|------|----------|
| `status` | `git status` | 查询 HEAD 版本、INDEX 是否存在、channel 状态 | 只读 |
| `add` | `git add` + write tree | 扫描构建产物 → 写 INDEX（暂存快照） | 写 INDEX |
| `diff` | `git diff` | 对比两个版本/状态 → 产出 delta | 只读 |
| `commit` | `git commit` | INDEX → objects/{version}，更新 HEAD | 写 objects/ + HEAD |
| `reset` | `git reset` | 丢弃 INDEX | 删 INDEX |
| `tag` | `git tag` | 标记版本为 published | 写 tags |
| `push` | `git push` | 发布 delta artifacts 到 CDN/远端 | 外部操作 |

**注意**：原草稿中的 `apply` 操作已移出 Repository。AA 旧管线的 group 移动由 `LegacyAddressableBuildBackend` 在构建流程中自己处理，不属于版本管理的通用操作。

---

## diff 支持的对比组合

| 对比 | 用途 |
|------|------|
| `diff(v1, v2)` | 任意两个已提交版本间对比（历史查询、回归分析） |
| `diff(INDEX, HEAD)` | 暂存区 vs 当前版本（本次构建产出了什么变化 → 发布决策） |

---

## 统一数据结构

### 设计原则

AA（Addressables）和 AB（自定义管线）共享同一套快照数据结构。快照只记录 artifact 的身份和内容标识，不记录构建策略相关信息（如 AA 的 group 归属）。

### ArtifactDigest（git blob 等价）

```csharp
[BinarySerializable]
public class ArtifactDigest
{
    [BinaryField(0)] public string Name;   // AA: AssetGUID, AB: BundleName
    [BinaryField(1)] public string Hash;   // MD5, 内容标识（diff/下载决策）
    [BinaryField(2)] public long Size;     // 字节大小
    [BinaryField(3)] public uint CRC;      // CRC32, 快速校验（下载验证/损坏检测）
}
```

**AA 和 AB 的差异只在于**：
- `Name` 的语义（GUID vs BundleName）
- `Hash` 的计算方式（AA: DeepHash/FileHash → MD5, AB: bundle 输出 FileHash → MD5）
- `CRC` 的计算对象（AA: bundle 输出文件, AB: bundle 输出文件 — 统一）
- `add` 时的扫描逻辑（AA 扫 Addressable entries，AB 扫输出目录）

**Hash 职责分工**：
- `Hash`（MD5）：内容标识 → diff、增量下载决策、版本对比
- `CRC`（CRC32）：快速校验 → 下载后验证、运行时损坏检测

**不存储在快照中的信息**：
- AA 的 group 归属（CurrentGroupName / OriginalGroupName / RemoteGroupName）→ 由 LegacyBuildBackend 自己管理
- Address / Labels → 构建策略信息，不属于版本管理

### Snapshot（git commit 等价）

```csharp
[BinarySerializable(Magic = 0x42525053)] // 'BRPS' = Build Repository Snapshot
public class Snapshot
{
    [BinaryField(0)] public VersionNumber Version;
    [BinaryField(1)] public string GitCommitHash;
    [BinaryField(2)] public string Channel;
    [BinaryField(3)] public string Timestamp;
    [BinaryField(4)] public SourceDiffSummary SourceChanges;
    [BinaryField(5)] public List<ArtifactDigest> Artifacts;
}

public class SourceDiffSummary
{
    public int TotalFilesChanged;
    public int CodeFilesChanged;      // *.cs
    public int AssetFilesChanged;     // *.prefab, *.mat, *.png, ...
    public int ConfigFilesChanged;    // ProjectSettings/*, *.asset
    public string SuggestedBumpType;  // "Patch" / "Minor" / "Major"
}
```

**SourceDiffSummary** 由 Build Task 在构建前计算并传入，Repository 只负责存储。

### ArtifactDelta（git diff 等价）

```csharp
public class ArtifactDelta
{
    public List<ArtifactDigest> Added;
    public List<ArtifactDigest> Modified;
    public List<string> Removed;  // Name only
}
```

AA 和 AB 共享同一个 Delta 类型。消费方根据 backend 类型解读 Name 的语义。

---

## AA 旧管线的 Group 移动（Repository 外部）

AA 旧管线的 hotfix 策略（diff → 移动 group → 构建 Hotfix group → 还原 group）不属于 Build Repository 的职责。由 `LegacyAddressableBuildBackend` 在构建流程中自己处理：

```
LegacyAddressableBuildBackend.BuildHotfix():
  1. repo.diff(INDEX_or_HEAD, current_scan) → ArtifactDelta（哪些 asset 变了）
  2. 内部 PrepareHotfix：根据 delta 移动 group + 记录 undo log
  3. 执行 Addressables 构建
  4. 内部 RestoreGroups 或等待用户手动 reset

LegacyAddressableBuildBackend.ResetGroups():
  回放 undo log，还原 group 归属
```

Undo log 由 LegacyBuildBackend 自己管理，存储位置由 backend 决定（可以是 SO 内部字段或独立文件）。Repository 不感知。

---

## 多渠道：独立存储空间

每个 channel 是独立的 repository 实例（类似 git branch），拥有独立的 HEAD、INDEX、objects/、tags：

```
BuildData/Snapshots/
  ├── android/
  │     ├── HEAD
  │     ├── INDEX
  │     ├── objects/
  │     └── tags/
  ├── ios/
  │     ├── HEAD
  │     ├── INDEX
  │     ├── objects/
  │     └── tags/
  └── default/
        └── ...
```

channel 选择由构建参数决定，Repository 不做 channel 间的合并/同步。

---

## Commit 元数据（版本管理相关）

每个 commit（objects/{version}.bin）即 Snapshot 结构，包含：
- Version number
- Git commit hash（source 状态）
- Channel
- Timestamp
- SourceDiffSummary（变更文件数/类型/版本建议）
- Artifacts（ArtifactDigest 列表）

**不属于 commit 的信息**（归 Build Report）：
- 构建耗时、机器信息
- DAG 执行日志
- 错误/警告

两者通过 version number 关联。

---

## HEAD 文件格式

```json
{
  "Head": "v4.0.2",
  "Staged": "v4.0.3"
}
```

- `Head`: 当前已提交版本（最近一次 commit）
- `Staged`: 待提交版本（add 后、commit 前），null 表示无暂存

---

## Tag 机制

```
BuildData/Snapshots/{channel}/tags/
  └── published.json
```

```json
{
  "Tags": [
    { "Version": "v4.0.1", "TaggedAt": "2026-05-18T10:00:00Z", "PushTarget": "cdn-prod" },
    { "Version": "v4.0.2", "TaggedAt": "2026-05-18T14:00:00Z", "PushTarget": "cdn-prod" }
  ]
}
```

tag 标记"已发布"版本，区分 built-only vs published。push 成功后自动打 tag。

---

## Interface 设计

```csharp
public interface IBuildRepository
{
    RepositoryStatus Status();
    void Add(IArtifactScanner scanner);
    ArtifactDelta Diff(DiffTarget from, DiffTarget to);
    ArtifactDelta Diff(VersionNumber from, VersionNumber to);
    void Commit(CommitMetadata metadata);
    void Reset();
    void Tag(VersionNumber version, TagInfo info);
    void Push(VersionNumber version, IPushTarget target);
    
    // 查询 API
    Snapshot GetSnapshot(VersionNumber version);
    List<VersionNumber> ListVersions();
}

public interface IArtifactScanner
{
    List<ArtifactDigest> Scan();  // AA: 扫描 Addressable entries, AB: 扫描输出目录
}

public enum DiffTarget { HEAD, INDEX }
```

**单一实现**：IBuildRepository 只有一个实现（文件系统存储）。AA/AB 的差异通过 IArtifactScanner 注入，不需要两个 Repository 实现。

---

## 与 Build Backend 的交互

```
Full Build 流程:
  backend.Build() → 产出 artifacts
  repo.Add(scanner) → 写 INDEX
  repo.Diff(INDEX, HEAD) → ArtifactDelta（记录变化）
  用户确认 → repo.Commit(metadata)
  发布 → repo.Push(version, target) → repo.Tag(version, info)

Hotfix Build 流程 (AB):
  backend.Build() → 全量构建（Unity 增量引擎自动优化）
  repo.Add(scanner) → 写 INDEX
  repo.Diff(INDEX, HEAD) → ArtifactDelta（哪些 bundle 变了 → push 决策）
  用户确认 → repo.Commit(metadata) → repo.Push(version, target)

Hotfix Build 流程 (AA):
  repo.Diff(INDEX_or_HEAD, current_scan) → ArtifactDelta
  backend.PrepareHotfix(delta) → 移动 group（backend 内部操作）
  backend.Build() → 构建 Hotfix group
  repo.Add(scanner) → 写 INDEX
  用户确认 → repo.Commit(metadata)
  放弃 → repo.Reset() + backend.RestoreGroups()
```

Build Task（如 TaskAnalyzeSource）从 Repository 读信息做版本建议，但版本建议逻辑不在 Repository 内。

---

## 破坏范围与新建范围

### 破坏范围（已落地代码需改动）

| 文件 | 改动 | 严重度 |
|------|------|--------|
| `BuildProjectManager.cs` | 4 处 DifferentialProcessor 调用 → 通过 IBuildRepository 路由 | 中 |
| `DifferentialProcessor.cs` | 重构为 LegacyBuildBackend 的内部实现 + 分离 diff/group 移动 | **高** |
| `BuildSnapshots.cs` | 替换为 Snapshot + ArtifactDigest（统一数据结构） | **高** |
| `LegacyAddressableBuildBackend.cs` | 增加 PrepareHotfix/RestoreGroups 逻辑（从 DifferentialProcessor 迁入） | 中 |

### 新建范围

| 新增 | 说明 |
|------|------|
| `IBuildRepository` 接口 + 实现 | 7 操作 + 查询 API，文件系统存储 |
| `IArtifactScanner` + 实现 | AA: AddressableScanner, AB: BundleOutputScanner |
| `ArtifactDigest` / `Snapshot` / `ArtifactDelta` | 统一数据结构（替代 AssetSnapshot + BundleDigest） |
| `SourceDiffSummary` | 版本建议元数据 |
| `IPushTarget` + 实现 | 发布目标抽象 |
| 存储布局 | `BuildData/Snapshots/{channel}/` |

### 不受影响

| 模块 | 原因 |
|------|------|
| E5 (DAGScheduler / BuildContext / Tasks) | Repository 是 Task 的消费者 |
| E6 (ABManifest) | 独立模块 |
| E9 (VersionNumber) | 被使用，不被改 |
| E13 (Legacy sidebar) | UI 骨架 |
| IHotfixPipeline / ABHotfixBackend | 已接口分离，hotfix 下载策略独立 |

### 关键风险

**DifferentialProcessor 拆分**：将 PrepareHotfix 拆为"diff（纯信息）" + "group 移动（副作用）"。当前 ScanCurrentProjectAssets 依赖 group 状态推断 OriginalGroupName。分离后：
1. diff 扫描只看 GUID + Hash（不关心 group）
2. group 移动从 ArtifactDelta 获取需要移动的 asset 列表
3. OriginalGroupName 记录从"快照字段"变为"backend 内部 undo log"

---

## 访问方式：GUI + CLI 双入口

### 设计原则

- **Editor 内**：通过 BuildPipelineWindow 面板按钮调用（GUI 驱动）
- **Editor 外**：通过 Unity `-executeMethod` 命令行调用（CI/BatchMode 驱动）
- 第一版同时支持两种入口，共享同一套 IBuildRepository 实现

### CLI 入口设计

```csharp
// Editor/CLI/BuildRepositoryCLI.cs
public static class BuildRepositoryCLI
{
    // Unity -batchmode -executeMethod BuildRepositoryCLI.Status -channel=android
    public static void Status() { ... }

    // Unity -batchmode -executeMethod BuildRepositoryCLI.Add -channel=android -backend=ab
    public static void Add() { ... }

    // Unity -batchmode -executeMethod BuildRepositoryCLI.Diff -channel=android -from=HEAD -to=INDEX
    public static void Diff() { ... }

    // Unity -batchmode -executeMethod BuildRepositoryCLI.Commit -channel=android
    public static void Commit() { ... }

    // Unity -batchmode -executeMethod BuildRepositoryCLI.Reset -channel=android
    public static void Reset() { ... }

    // Unity -batchmode -executeMethod BuildRepositoryCLI.Tag -channel=android -version=v4.0.2
    public static void Tag() { ... }

    // Unity -batchmode -executeMethod BuildRepositoryCLI.Push -channel=android -version=v4.0.2
    public static void Push() { ... }
}
```

### 参数传递

Unity BatchMode 通过 `System.Environment.GetCommandLineArgs()` 获取参数：

```
Unity -batchmode -executeMethod BuildRepositoryCLI.Diff -channel=android -from=v4.0.1 -to=v4.0.2
```

内部解析为 key-value 对，映射到 IBuildRepository 方法参数。

### GUI 入口

BuildPipelineWindow 面板中的按钮直接调用同一套 IBuildRepository API：
- Status 按钮 → 刷新面板状态显示
- Diff 按钮 → 弹窗展示 ArtifactDelta
- Commit 按钮 → 确认后提交
- 与构建按钮（Build Full / Build Hotfix）并列，但独立于构建流程

### 与构建 CLI 的关系

```
构建 CLI:
  Unity -batchmode -executeMethod BuildProjectManager.BuildFromCommandLine ...
  → 内部自动调用 repo.Add() + repo.Commit()

Repository CLI:
  Unity -batchmode -executeMethod BuildRepositoryCLI.Status
  → 独立调用，不触发构建
```

构建 CLI 包含 Repository 操作（构建完自动 add+commit），Repository CLI 可独立使用（查状态、手动 diff、补打 tag 等）。

---

## Open Questions

| # | 问题 | 备选 |
|---|------|------|
| Q1 | BuildSnapshots SO 完全替换为 ArtifactDigest 文件系统存储？ | 路线 1 确认：完全替换 |
| Q2 | push 的具体实现——直接调用 CDN SDK？还是产出 delta artifact 列表，由外部脚本上传？ | 待 CDN 方案确认 |
| Q3 | AA 扫描时 Hash 计算方式（DeepHash vs FileHash）是否需要统一？ | 当前 AA 用 DeepHash（递归依赖），AB 用 bundle 输出 hash |
| Q4 | reset 是否需要区分 soft/hard？soft 只删 INDEX，hard 同时通知 backend 还原外部状态？ | 当前设计 reset 只删 INDEX，backend 自己管理外部状态还原 |

---

## 与旧草稿的关系

| 旧草稿 | 处置 |
|--------|------|
| draft-E7-diff-snapshot-20260517.md | Superseded。核心设计被本草稿吸收并重构为统一 Build Repository |
| plan-smart-versioning-draft.md | Superseded。版本建议逻辑作为 TaskAnalyzeSource + SourceDiffSummary 纳入 |

---

## Change Log

| Date | Change |
|------|--------|
| 2026-05-18 | 初始草稿：合并 E7 + Smart Versioning 为统一 Build Repository |
| 2026-05-18 | 讨论迭代：移除 apply 操作（AA group 移动由 backend 自己处理）；统一数据结构 ArtifactDigest（Name+Hash+Size）替代 AssetSnapshot/BundleDigest 双类型；补充破坏范围分析；明确 AA/AB hotfix 策略差异；IBuildRepository 单一实现 + IArtifactScanner 注入差异 |
| 2026-05-18 | 补充 GUI + CLI 双入口设计：Editor 内用 GUI 按钮，Editor 外用 -executeMethod 命令行；第一版即支持 BatchMode；CLI 7 操作完整暴露 |
