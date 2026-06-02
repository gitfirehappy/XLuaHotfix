# Build Repository 构建仓库

> 返回总览：[资源管理架构文档](./资源管理架构文档.md)

> **关联代码**
>
> `Assets/FYAsset/Scripts/Build/Repository/` · `Assets/FYAsset/Scripts/Build/Snapshots/`

---

## 概述

Build Repository 是构建产物的版本化存储系统，采用类 git 的 HEAD/objects 模型记录每次构建的产物快照。支持 HEAD 查询、历史追溯、差异对比（Diff）和产物发布（Push）。

核心设计决策：
- **JSON 唯一持久化格式**，所有写入通过 `FileHelper` 原子写保证一致性
- **先写 object 再交换 HEAD**，HEAD 交换失败时 object 作为孤立文件保留，不会丢数据
- **AA / AB 仓库空间隔离**，通过 ChannelKey 中的 BackendMode 段区分
- **Push 发布已构建包体**：目标包体目录整体替换，包内 `PackageIndex.json` 由构建 Task 负责

---

## 核心接口

### IBuildRepository

```csharp
public interface IBuildRepository
{
    RepositoryStatus GetStatus(string channelKey);
    RepositoryCommit GetHeadCommit(string channelKey);
    List<RepositoryCommit> ListCommits(string channelKey);
    void Commit(RepositoryCommit commit);
    PushReceipt Push(string channelKey, VersionNumber fromVersion, VersionNumber toVersion, IPushTarget target);
    List<PushHistoryEntry> ListPushHistory(string channelKey);
}
```

当前唯一实现：`FileBuildRepository`。

---

## 文件布局

所有数据存储在项目根目录的 `BuildData/Snapshots/` 下：

```
BuildData/Snapshots/
├─ {BuildTarget}[-Channel]/
│  └─ {BackendMode}/               # "AA" 或 "AB"
│     ├─ HEAD.json                  # HEAD 指针 → objects/{HeadVersion}.json
│     ├─ PushHistory.json           # 推送历史记录
│     └─ objects/
│        ├─ 1.0.0+1.json
│        ├─ 1.0.1+2.json
│        └─ ...
```

示例 ChannelKey：`StandaloneWindows64/AB`、`StandaloneWindows64-MyChannel/AA`。

---

## 数据模型

### RepositoryCommit（提交对象）

每次构建成功后通过 `BuildRepositoryFacade.Commit()` 写入。JSON 序列化，存储在 `objects/{Version}.json`。

| 字段 | 类型 | JSON | 语义 |
|------|------|:----:|------|
| `Version` | VersionNumber | ✓ | 构建版本号 |
| `ChannelKey` | string | ✓ | 仓库 Channel 标识，包含 BuildTarget、可选 Channel、BackendMode |
| `BackendMode` | string | ✓ | 构建后端类型，值为 `"AA"` 或 `"AB"` |
| `BuildTarget` | string | ✓ | Unity BuildTarget（如 `"StandaloneWindows64"`） |
| `PackageName` | string | ✓ | 包体名称（如 `"Build_20250101123045_1.0.0"`） |
| `CreatedAtUtc` | string | ✓ | Commit 创建时间，UTC ISO-8601 字符串 |
| `GitCommitHash` | string | ✓ | 当前 git HEAD 的 commit hash，git 不可用时为空字符串 |
| `IsDirty` | bool | ✓ | 工作区是否有未提交的变更（`git status --porcelain` 非空） |
| `PackageRootDir` | string | ✓ | 最终包体输出目录的绝对路径，Push 时用于定位产物文件 |
| `Artifacts` | List\<ArtifactDigest\> | ✓ | 本次构建的产物指纹列表（AA 为源资产 GUID，AB 为 Bundle 文件） |

### RepositoryHeadState（HEAD 指针）

| 字段 | 类型 | JSON | 语义 |
|------|------|:----:|------|
| `HeadVersion` | string | ✓ | 当前 HEAD 指向的版本字符串（如 `"1.0.0+5"`） |

HEAD 指针文件路径：`{channelRoot}/HEAD.json`。Object 文件路径由目录布局推导（`objects/{HeadVersion}.json`），避免双重事实源。

### RepositoryStatus（状态摘要）

编辑器 UI 展示用，`GetStatus()` 组装返回，不直接持久化。

| 字段 | 类型 | 语义 |
|------|------|------|
| `ChannelKey` | string | 仓库 Channel 标识 |
| `HasHead` | bool | 是否存在有效 HEAD |
| `HasHeadError` | bool | HEAD 是否存在异常（文件损坏、指向的 object 不存在等） |
| `HeadVersion` | string | HEAD 版本字符串 |
| `PackageName` | string | HEAD 对应的包名 |
| `ArtifactCount` | int | HEAD 产物数量 |
| `LastPushTargetId` | string | 最近一次 Push 的目标 ID |
| `LastPushAtUtc` | string | 最近一次 Push 的时间 |
| `HeadErrorReason` | string | HEAD 异常原因（HasHeadError 为 true 时） |

---

## Push 发布系统

### 数据模型

**PushTargetConfig** — 持久化在 `SharedBuildSettings.PushTargets` 中，由 SettingsPanel 编辑：

| 字段 | 类型 | JSON | 语义 |
|------|------|:----:|------|
| `Id` | string | ✓ | 用户自定义目标标识 |
| `Type` | PushTargetType | ✓ | 目标类型，当前仅 `LocalDirectory`(0) |
| `Path` | string | ✓ | 目标路径（目录选择器编辑） |

**PushPayload** — 由 Repository 组装，PushTarget 只消费已构建完成的包体目录：

| 字段 | 类型 | 语义 |
|------|------|------|
| `FromCommit` | RepositoryCommit | 基准提交 |
| `ToCommit` | RepositoryCommit | 目标提交 |
| `ChangedArtifactCount` | int | 本次 from/to 差异数量，仅用于 PushHistory 展示 |

**PushReceipt** — Push 执行结果：

| 字段 | 类型 | 语义 |
|------|------|------|
| `Success` | bool | 是否成功 |
| `TargetId` | string | 目标标识 |
| `TargetLocation` | string | 目标路径 |
| `PushedAtUtc` | string | Push 执行时间 |
| `FailureReason` | string | 失败原因（Success=false 时） |

**PushHistoryEntry** — `PushHistory.json` 的条目：

| 字段 | 类型 | 语义 |
|------|------|------|
| `FromVersion` | string | 基准版本 |
| `ToVersion` | string | 目标版本 |
| `TargetId` | string | 推送目标 |
| `TargetLocation` | string | 推送路径 |
| `PushedAtUtc` | string | 推送时间 |
| `DeltaFileCount` | int | 推送的差异文件数 |

### IPushTarget 接口

```csharp
public interface IPushTarget
{
    string Id { get; }
    PushReceipt Push(PushPayload payload);
}
```

当前唯一实现：`LocalDirectoryPushTarget`。

### Push 流程

1. `FileBuildRepository.Push()` 加载 `fromCommit` 和 `toCommit`
2. 调用 `ArtifactDiffer.Diff(fromCommit.Artifacts, toCommit.Artifacts)` 计算差异
3. 组装 `PushPayload` 并调用 `target.Push(payload)`
4. `LocalDirectoryPushTarget` 整体替换目标 `{path}/{packageName}/` 目录
5. Push 成功后追加 `PushHistoryEntry` 并写入 `PushHistory.json`
6. **注意：当前版本 AA Push 被显式拒绝**（ChannelKey 含 `/AA` 时直接返回失败）

### LocalDirectoryPushTarget

将产物推送到本地目录：
- 清空目标 `{path}/{packageName}/` 目录
- 将 `RepositoryCommit.PackageRootDir` 下的已构建包体文件整体复制到目标包体目录
- 不重新解释、重写或校验包体内部 `PackageIndex.json`

---

## BuildRepositoryFacade（门面层）

`BuildRepositoryFacade` 是外部调用仓库的统一入口，内部持有 `FileBuildRepository` 实例。主要职责：

### ChannelKey 构造

```csharp
// 格式：{BuildTarget}[-Channel]/{BackendSegment}
// BackendSegment：AB 后端 → "AB"，AA 后端 → "AA"
GetChannelKey(version, backendMode)
GetChannelKey(channel, backendMode)
```

### Commit 流程

1. 从 `BuildPackageRequest` 提取 Version、BackendMode、PackageName、OutputDir
2. 调用 `git rev-parse HEAD` 和 `git status --porcelain` 采集 Git 元数据
3. 组装 `RepositoryCommit` 并调用 `Repository.Commit()`
4. Git 元数据采集失败不阻断 Commit（GitCommitHash 置空，IsDirty 置 false）

---

## Diff Preview（差异预览）

`RepositoryPreviewRunner` 提供只读差异预览，不修改 HEAD、objects 或 PackageIndex。

### AA Diff Preview

- DAG 白名单：仅 `TaskScanAddressableHotfixDiff`
- Stop-after：`TaskScanAddressableHotfixDiff`
- 不产生临时文件，纯读取 `AddressableAssetSettings` 的当前资产状态
- 对比当前源资产 GUID 指纹与 Repository HEAD

### AB Diff Preview

- DAG 白名单：`TaskPrepareContext` → `TaskCollectAssets` → `TaskCollectBuiltins` → `TaskAnalyzeDependencies` → `TaskBuildBundles` → `TaskGenerateManifest` → `TaskVerifyBuildResult` → `TaskScanABHotfixDiff`
- Stop-after：`TaskScanABHotfixDiff`
- 使用 `Temp/BuildRepositoryPreview/{guid}/` 临时输出目录
- `finally` 块清理临时目录
- 对比当前构建产物指纹与 Repository HEAD

---

## 编辑器面板

### RepositoryStatusPanel

位于 Build Pipeline 窗口的 MANAGE 分组中。功能：

- **状态栏**：显示当前 HEAD 版本、包名、产物数量、最近 Push 信息
- **Diff 按钮**：触发 AA 或 AB Diff Preview，结果以 Added/Modified/Removed 列表展示
- **Push 按钮**：选择 Push Target、填写 From/To 版本号，执行 Push
- **Push History**：展示该 Channel 的推送历史

---

## CLI 入口

`BuildRepositoryCLI` 提供四个批处理/CI 命令：

| 命令 | 方法 | 关键参数 |
|------|------|---------|
| `Status` | `BuildRepositoryCLI.Status()` | `-channel`, `-backend` |
| `Diff` | `BuildRepositoryCLI.Diff()` | `-channel`, `-backend`, `-json`（可选输出路径） |
| `Push` | `BuildRepositoryCLI.Push()` | `-channel`, `-backend`, `-from`, `-to`, `-target` |
| `ListCommits` | `BuildRepositoryCLI.ListCommits()` | `-channel`, `-backend` |

所有命令通过 `EditorApplication.Exit()` 返回退出码（0 成功 / 1 失败）。

---

## 与构建流程的关系

```
BuildFullPackage / BuildHotfix
  → backend.BuildAsync()
  → CommitBuildRepository()
       ├─ BuildRepositoryFacade.GetChannelKey()
       ├─ BuildRepositoryFacade.Commit(request, artifacts)
       │    ├─ 采集 git metadata
       │    ├─ 组装 RepositoryCommit
       │    └─ FileBuildRepository.Commit()
       │         ├─ 写入 objects/{Version}.json（原子写）
       │         └─ 交换 HEAD.json（原子写）
       └─ (future: Push via ConfirmRelease)
```

---

## 注意事项

- **AA Push 暂不支持**：当前版本 ChannelKey 含 `/AA` 的 Push 会直接返回失败
- **HEAD 损坏保护**：`GetStatus()` 区分"无 HEAD"和"HEAD 损坏"，损坏原因在 `HeadErrorReason` 字段
- **ChannelKey 不可跨后端混用**：AA 和 AB 的 Repository 空间完全隔离
- **Git 元数据非必需**：git 不可用时 Commit 仍正常进行，`GitCommitHash` 为空
