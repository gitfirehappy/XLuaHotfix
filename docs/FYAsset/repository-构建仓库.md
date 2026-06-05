# Build Repository 构建仓库

> 返回总览：[资源管理架构文档](./资源管理架构文档.md)

> **关联代码**
>
> `Assets/FYAsset/Scripts/Build/Repository/` · `Assets/FYAsset/Scripts/Build/Snapshots/` · `Assets/FYAsset/Scripts/Build/Pipeline/Editor/Tasks/AA/TaskScanAddressableHotfixDiff.cs` · `Assets/FYAsset/Scripts/Build/Pipeline/Editor/Tasks/AB/TaskScanABHotfixDiff.cs`

---

## 概述

Build Repository 是构建产物的版本化存储系统，采用类 git 的 HEAD/objects 模型记录每次构建的产物快照。它同时承接原 Snapshots 差异快照模块的说明：差异计算由 `ArtifactDigest` / `ArtifactDelta` / `ArtifactDiffer` 完成，Repository 负责保存快照、预览差异、提交 HEAD 和发布已构建包体。

核心设计决策：
- **JSON 唯一持久化格式**，所有写入通过 `FileHelper` 原子写保证一致性
- **先写 object 再交换 HEAD**，HEAD 交换失败时 object 作为孤立文件保留，不会丢数据
- **AA / AB 仓库空间隔离**，通过 ChannelKey 中的 BackendMode 段区分
- **Push 发布已构建包体**：发布根包含 `PackageIndex.json` 和 `{BuildPackagesFolderName}/{PackageName}`，包体内容由构建 Task 负责
- **Diff 不直接筛包体**：当前 Diff 负责识别变化、驱动 AA Hotfix Group 和记录 PushHistory；正式包体由 AA/AB 构建 Task 决定

---

## 差异快照模型

### ArtifactDigest（产物指纹）

构建产物的最小内容指纹。JSON 可序列化，不参与 Binary 序列化。

| 字段 | 类型 | JSON | 语义 |
|------|------|:----:|------|
| `Name` | string | ✓ | 产物身份标识。AA 使用 Asset GUID，AB 使用 BundleName |
| `Hash` | string | ✓ | 内容 Hash，当前使用 MD5 字符串 |
| `Size` | long | ✓ | 产物大小，单位为 byte |
| `CRC` | uint | ✓ | CRC32 快速校验值 |

### ArtifactDelta（差异结果）

`ArtifactDiffer.Diff()` 的三段式输出。JSON 可序列化。

| 字段 | 类型 | JSON | 语义 |
|------|------|:----:|------|
| `Added` | List\<ArtifactDigest\> | ✓ | 目标侧存在、基准侧不存在的产物 |
| `Modified` | List\<ArtifactDigest\> | ✓ | 两侧 Name 相同但 Hash 不同的产物（取目标侧值） |
| `Removed` | List\<string\> | ✓ | 基准侧存在、目标侧不存在的产物，只保留 Name |
| `IsEmpty` | bool (computed) | — | 没有任何新增、修改或删除 |

### ArtifactDiffer

`ArtifactDiffer.Diff(from, to)` 是纯计算器：按 `Name` 配对并比较 `Hash`，不访问 Unity API，不读写文件，也不决定最终包体内容。

算法：

1. 遍历 `from` 建立 `Name -> ArtifactDigest` 索引，跳过 null 和空 Name。
2. 遍历 `to` 分类目标产物：新 Name 进入 `Added`，同名 Hash 不同进入 `Modified`，同名 Hash 相同不记录。
3. `from` 中存在但 `to` 中不存在的 Name 进入 `Removed`。

使用约束：

- 调用方必须保证 `from` 和 `to` 处于同一命名域。
- AA 命名域是 Asset GUID；AB 命名域是 BundleName。
- Hash 比较使用 Ordinal 字符串比较。

---

## 差异机制与打包关系

### 当前事实

当前没有“按差异筛最终包体文件”的通用机制。

- AA：`TaskScanAddressableHotfixDiff` 在构建前扫描 Addressables source，与 Repository HEAD 比较，并把 Added/Modified 写入 `ArtifactDelta`。`TaskMoveAddressableHotfixGroups` 根据该结果移动资源到 Hotfix Group，然后 Addressables 生成 catalog 和热更 bundles。
- AB：`TaskScanABHotfixDiff` 在 `TaskVerifyBuildResult` 后执行，从 `ABManifest.BundleEntries` 生成当前 bundle 指纹，与 Repository HEAD 比较，并写入 `ArtifactDelta` / `RepositoryArtifacts`。正式构建随后继续进入 `TaskOrganizeOutput`，当前实现会复制 `ABManifest.BundleEntries` 中的所有 bundle。
- Push：`FileBuildRepository.Push()` 使用 `ArtifactDiffer.Diff(fromCommit.Artifacts, toCommit.Artifacts)` 计算差异数量，仅用于 `PushHistory.DeltaFileCount` 展示。`LocalDirectoryPushTarget` 发布已构建包体目录，不重新解释 catalog、AAManifest 或 ABManifest。

### AA 机制边界

AA 受 Addressables 官方 catalog 生成机制约束。为了保证 catalog 指向的资源位置和文件名正确，AA Hotfix 需要让 Addressables 基于当前 Hotfix Group 状态完整生成本次热更输出。

因此 AA 不建议在 Push 阶段再过滤差异文件：

- catalog 可能引用被过滤掉的 bundle；
- 跳版本客户端无法仅凭最新目录补齐旧热更资源；
- 当前下载端已有 Hash 复用逻辑，能直接复制本地同 Hash bundle，机制更简单。

### AB 当前机制与待优化方向

AB 是自研清单和加载链路，没有 Addressables catalog 限制。当前实现仍是“构建后全量复制当前 manifest bundles”。后续讨论稿 `requirements/plan/drafts/draft-ab-cumulative-hotfix-delivery-20260604.md` 计划把 AB 调整为：

- `ABManifest.BundleEntries` 保留完整运行时索引；
- 新增实际发布/下载列表，例如 `DeliveryBundles`；
- Full 构建发布全部 bundles；
- Hotfix 构建只发布相对当前 Major Full baseline 的累积变化 bundles；
- 运行时仍按“热更目录优先，StreamingAssets 回退”加载未变更的整包 baseline bundles。

该方向尚未进入可执行 plan。

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

**PushTargetConfig** — 持久化在 `FYAssetSettings.PushTargets` 中，由仓库面板编辑：

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
4. `LocalDirectoryPushTarget` 将 `PushTargetConfig.Path` 作为发布根；空路径解析为当前 `BuildPathManager.OutputRoot`
5. 发布 `{PublishRoot}/PackageIndex.json` 和 `{PublishRoot}/{BuildPackagesFolderName}/{PackageName}/...`
6. Push 成功后追加 `PushHistoryEntry` 并写入 `PushHistory.json`
7. **当前状态**：AA/AB Push 均走同一发布根语义，Push 成功后记录对应 Channel 的 `PushHistory.json`。

### LocalDirectoryPushTarget

将产物推送到本地发布根：
- `PushTargetConfig.Path` 为空时使用当前 `BuildPathManager.OutputRoot`
- `PushTargetConfig.Path` 是发布根语义；相对路径按项目根解析，绝对路径原样规范化，不硬编码 `HotfixOutput`
- 包体目录固定为 `{PublishRoot}/{BuildPackagesFolderName}/{PackageName}/`
- 根部 `PackageIndex.json` 根据 `toCommit` 的 PackageName、Version、BackendMode 写入
- 如果目标包体目录就是当前构建输出包体目录，则通过规范化路径比较识别并跳过自复制，避免删除自身产物
- 不重新解释、重写或校验 catalog、AAManifest、ABManifest 等包体内容

路径处理规则：
- 发布根、包体目录、根部 `PackageIndex.json` 都按本地文件系统路径拼接。
- 递归复制时通过统一相对路径计算生成目标路径，避免 Windows `\` 与 `/` 混用导致相对路径截取错误。
- ChannelKey 中的 `/` 是仓库逻辑分隔符，不是本地目录拼接规则；落盘前会映射到隔离目录。

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

Diff Preview 是只读诊断能力。它不会提交 HEAD、不会写 `PackageIndex.json`，也不会替代正式构建中的打包输出规则。

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
       └─ Push 由 Repository CLI/UI 触发；ConfirmRelease wrapper 当前不负责 Push
```

---

## 注意事项

- **AA/AB Push 同级**：当前 Push 发布根部 `PackageIndex.json` 和已构建 package 目录，并记录 PushHistory；不重新解释 catalog、AAManifest 或 ABManifest
- **Diff 不筛 Push 文件**：Push 中的差异只用于 PushHistory 数量统计，不用于 delta-copy bundle
- **AB Delivery 优化未落地**：AB 相对 Full baseline 的累积热更包仍是 draft，不是当前代码行为
- **HEAD 损坏保护**：`GetStatus()` 区分"无 HEAD"和"HEAD 损坏"，损坏原因在 `HeadErrorReason` 字段
- **ChannelKey 不可跨后端混用**：AA 和 AB 的 Repository 空间完全隔离
- **Git 元数据非必需**：git 不可用时 Commit 仍正常进行，`GitCommitHash` 为空
