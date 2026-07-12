# Build Repository 构建仓库

> 返回总览：[资源管理架构文档](./资源管理架构文档.md)

> **关联代码**
>
> `Assets/FYAsset/Scripts/Shared/Build/Repository/` · `Assets/FYAsset/Scripts/Shared/Build/Snapshots/` · `Assets/FYAsset/Scripts/AA/Build/Pipeline/Editor/Tasks/AA/TaskScanAddressableHotfixDiff.cs` · `Assets/FYAsset/Scripts/AB/Build/Pipeline/Editor/Tasks/AB/TaskScanABHotfixDiff.cs`

---

## 概述

Build Repository 是构建产物的版本化存储系统，采用类 git 的 HEAD/objects 模型记录每次构建的产物快照。它同时承接原 Snapshots 差异快照模块的说明：差异计算由 `ArtifactDigest` / `ArtifactDelta` / `ArtifactDiffer` 完成，Repository 负责保存快照、预览差异、提交 HEAD 和发布已构建包体。

核心设计决策：
- **JSON 唯一持久化格式**，所有写入通过 `FileHelper` 原子写保证一致性
- **先写 object 再交换 HEAD**，HEAD 交换失败时 object 作为孤立文件保留，不会丢数据
- **AA / AB 仓库空间隔离**，通过 ChannelKey 中的 BackendMode 段区分
- **提交级 CommitDiff 固化**：每个 commit 写入时记录它相对上一个同 Channel/Backend HEAD 的 `CommitDelta`，首个 commit 的 diff 是从空集合到当前产物的全量 Added
- **Push 发布已构建包体**：Target Path 是服务总根，AA/AB 分别发布到 `{Path}/AA` 与 `{Path}/AB`，每个后端根独立包含 `PackageIndex.json` 和 `{BuildPackagesFolderName}/{PackageName}`
- **Diff 不筛 Push 文件**：Repository Diff 负责识别变化、驱动 AA Hotfix Group、展示提交/预览；Push 始终发布已构建包体。AB Hotfix 的实际包体交付列表由 AB 构建 Task 按 Full baseline 计算。

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

### CommitDelta（提交差异）

`RepositoryCommit.CommitDelta` 是提交对象持久化的一部分，语义仿照 Git 的“一个提交相对它的父提交的变动”：

- `ParentVersion` 指向提交写入前的同 Channel/Backend HEAD；
- 首个提交没有父提交，`ParentVersion` 为空字符串；
- 首个提交的 `CommitDelta.Added.Count == Artifacts.Count`，表示空仓库到当前产物的全量新增；
- 后续提交的 `CommitDelta` 固定为 `ArtifactDiffer.Diff(parent.Artifacts, current.Artifacts)`；
- 旧 object JSON 没有 `CommitDelta` 时只在 UI 中显示“无持久化 diff”，不会加载后自动重写旧文件。

---

## 差异机制与打包关系

### 当前事实

- AA：`TaskScanAddressableHotfixDiff` 在构建前扫描 Addressables source，与 Repository HEAD 比较，并把 Added/Modified 写入 `ArtifactDelta`。`TaskMoveAddressableHotfixGroups` 根据该结果移动资源到 Hotfix Group，然后 Addressables 生成 catalog 和热更 bundles。
- AB：`TaskScanABHotfixDiff` 在 `TaskVerifyBuildResult` 后执行，从 `ABManifest.BundleEntries` 生成当前 bundle 指纹。它同时计算两类结果：`ArtifactDelta` 是 current-vs-Repository HEAD 的预览/提交差异；`ABDeliveryBundles` 是 current-vs-同 Channel/Backend/Major 的 Full baseline 的 Hotfix 实际交付列表。
- AB Full：`TaskOrganizeOutput` 复制 `ABManifest.BundleEntries` 中的全部 bundle，`ABManifest.DeliveryBundles` 保持空列表。
- AB Hotfix：`TaskOrganizeOutput` 只复制 `ABDeliveryBundles`，`TaskWriteABPackageManifest` 发布完整 `ABManifest`，其中 `BundleEntries` 仍是完整运行时索引，`DeliveryBundles` 记录本次远端包实际交付/下载的 bundle。
- Push：`FileBuildRepository.Push()` 发布指定 from/to 范围内的目标包体；`LocalDirectoryPushTarget` 发布已构建包体目录，不重新解释 catalog、AAManifest 或 ABManifest。Push 历史不再持久化。

### AA 机制边界

AA 受 Addressables 官方 catalog 生成机制约束。为了保证 catalog 指向的资源位置和文件名正确，AA Hotfix 需要让 Addressables 基于当前 Hotfix Group 状态完整生成本次热更输出。

因此 AA 不建议在 Push 阶段再过滤差异文件：

- catalog 可能引用被过滤掉的 bundle；
- 跳版本客户端无法仅凭最新目录补齐旧热更资源；
- 当前下载端已有 Hash 复用逻辑，能直接复制本地同 Hash bundle，机制更简单。

### AB 累积 Hotfix 交付

AB 是自研清单和加载链路，没有 Addressables catalog 限制。当前 AB Hotfix 采用“完整运行时清单 + Full-baseline 累积交付列表”：

- `ABManifest.BundleEntries` 保留完整运行时索引，运行时查找和依赖解析始终使用它；
- `ABManifest.DeliveryBundles` 记录远端 Hotfix 包实际发布/下载的 bundle；
- Full 构建发布全部 bundles，`DeliveryBundles` 为空；
- Hotfix 构建只发布相对当前 Major Full baseline 的 Added/Modified 物理 bundle；
- 运行时仍按“热更目录优先，StreamingAssets 回退”加载未变更的整包 baseline bundles。

约束：

- AB Hotfix 缺少同 Channel/Backend/Major 的 Full baseline commit 时直接失败，要求重新执行 AB Full build；
- 旧 commit 没有 `BuildType == "Full"` 时不会被推断为 Full baseline；
- 未交付的每个 bundle 必须在 Full baseline 中存在同名且同 Hash 的文件，否则 fallback 校验失败；
- Removed bundle 不会被发布，也不会在运行时删除；新的完整 `BundleEntries` 停止引用它即可；
- 交付列表不额外包含依赖闭包，依赖 bundle 若未变化则从 StreamingAssets baseline 加载，若自身 Hash 变化则会进入 `DeliveryBundles`。

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
│     └─ objects/
│        ├─ 1.0.0.json
│        ├─ 1.0.1.json
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
| `BuildType` | string | ✓ | 构建类型，值为 `"Full"` 或 `"Hotfix"`；AB Hotfix 用它查找同 Major Full baseline |
| `BuildTarget` | string | ✓ | Unity BuildTarget（如 `"StandaloneWindows64"`） |
| `PackageName` | string | ✓ | 包体名称（如 `"Build_20250101123045_1.0.0"`） |
| `CreatedAtUtc` | string | ✓ | Commit 创建时间，UTC ISO-8601 字符串 |
| `GitCommitHash` | string | ✓ | 当前 git HEAD 的 commit hash，git 不可用时为空字符串 |
| `IsDirty` | bool | ✓ | 工作区是否有未提交的变更（`git status --porcelain` 非空） |
| `PackageRootDir` | string | ✓ | 最终包体输出目录的绝对路径，Push 时用于定位产物文件 |
| `ParentVersion` | string | ✓ | 父提交版本；首个提交为空 |
| `CommitDelta` | ArtifactDelta | ✓ | 当前提交相对父提交的固定差异；首个提交为全量 Added |
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
| `HeadErrorReason` | string | HEAD 异常原因（HasHeadError 为 true 时） |

---

## Push 发布系统

### 数据模型

**PushTargetConfig** — 持久化在 `FYAssetSettings.PushTargets` 中，由仓库面板编辑：

| 字段 | 类型 | JSON | 语义 |
|------|------|:----:|------|
| `Id` | string | ✓ | 用户自定义目标标识 |
| `Type` | PushTargetType | ✓ | `LocalDirectory`(0) 或 `CloudflarePages`(1) |
| `Path` | string | ✓ | 服务总根；Push 根据 commit BackendMode 追加 `AA` 或 `AB` |
| `PublicBaseUrl` | string | ✓ | 服务公开根 URL；Repository 显式派生并应用当前后端 URL |

**PushPayload** — 由 Repository 组装，PushTarget 只消费已构建完成的包体目录：

| 字段 | 类型 | 语义 |
|------|------|------|
| `FromCommit` | RepositoryCommit | 基准提交 |
| `ToCommit` | RepositoryCommit | 目标提交 |

**PushReceipt** — Push 执行结果：

| 字段 | 类型 | 语义 |
|------|------|------|
| `Success` | bool | 是否成功 |
| `TargetId` | string | 目标标识 |
| `TargetLocation` | string | 目标路径 |
| `PushedAtUtc` | string | Push 执行时间 |
| `FailureReason` | string | 失败原因（Success=false 时） |

### IPushTarget 接口

```csharp
public interface IPushTarget
{
    string Id { get; }
    PushReceipt Push(PushPayload payload);
}
```

当前实现：`LocalDirectoryPushTarget` 和 `CloudflarePagesPushTarget`。

### Push 流程

1. 编辑器面板调用 `BuildRepositoryFacade.PushHead()` 发布当前 Repository HEAD，不提供可编辑 From/To。
2. `BuildRepositoryFacade.PushHead()` 走文件仓库实现的 UI 专用入口，不改变 `IBuildRepository.Push(...)` 和 CLI 的显式 from/to 契约。
3. `FileBuildRepository.PushHead()` 读取 HEAD commit，并从 `ParentVersion` 推导 `FromCommit`；首个提交的 From 为空。
4. CLI 仍调用 `FileBuildRepository.Push(channelKey, fromVersion, toVersion, target)`，保留显式 `-from` / `-to` 参数契约。
5. Repository 组装 `PushPayload` 并调用 `target.Push(payload)`。
6. Target 将 `PushTargetConfig.Path` 解析为服务总根，再按 commit BackendMode 得到 `{ServiceRoot}/{AA|AB}` 后端发布根；空 Path 使用当前 `BuildPathManager.OutputRoot`
7. 发布 `{BackendRoot}/PackageIndex.json` 和 `{BackendRoot}/{BuildPackagesFolderName}/{PackageName}/...`
8. Push 成功后返回 `PushReceipt`；不写持久化 PushHistory。
9. URL 由 Target 的 `PublicBaseUrl` 加 `AA/` 或 `AB/` 派生；只有显式 `Apply URL` 会写当前后端 Settings，Push 本身不改 URL。

### LocalDirectoryPushTarget

将产物推送到本地服务根下的后端隔离目录：
- `PushTargetConfig.Path` 为空时使用当前 `BuildPathManager.OutputRoot`
- `PushTargetConfig.Path` 是服务总根语义；相对路径按项目根解析，绝对路径原样规范化
- 后端发布根固定为 `{ServiceRoot}/{AA|AB}`，包体目录固定为 `{BackendRoot}/{BuildPackagesFolderName}/{PackageName}/`
- 每个后端根的 `PackageIndex.json` 根据 `toCommit` 的 PackageName、Version、BackendMode 独立写入
- 如果目标包体目录就是当前构建输出包体目录，则通过规范化路径比较识别并跳过自复制，避免删除自身产物
- 不重新解释、重写或校验 catalog、AAManifest、ABManifest 等包体内容
- 包目录和 PackageIndex 通过同一事务暂存/备份；失败时恢复旧包和旧指针，不留下 `.fyasset_push` 目录

### CloudflarePagesPushTarget

- 先按同一事务规则更新本地 Cloudflare 服务镜像，再部署整个服务总根，另一后端目录不会被覆盖
- 只在用户显式选择 Cloudflare Target 并执行 Push 时调用 `wrangler pages deploy`
- Pages project name 复用 `FYAssetSettings.ProjectName`，生产分支固定为 `main`；该字段同时影响运行时 persistentData 根
- Wrangler 缺失、未认证或部署失败时 Push 返回失败；预检失败不会创建镜像，部署失败会恢复本地镜像
- 服务根 `_headers` 禁止缓存 AA/AB 的 PackageIndex，并为版本化 Packages 配置 immutable 缓存
- 当前生产配置已验证 `https://firehappy-cfy.com/AA/`：`PackageIndex.json`、AAManifest、catalog 和 7 个 Bundle 与本地镜像哈希一致；未发布 AB 时 `/AB/PackageIndex.json` 返回 404
- Windows 系统代理不会自动传给 Node/Wrangler。使用仅配置在 Windows Internet Settings 的本地代理时，应在启动 Unity 的同一进程环境中设置 `HTTP_PROXY`、`HTTPS_PROXY` 和 Node 24 的 `NODE_USE_ENV_PROXY=1`，否则 Wrangler 可能走直连并在 10 秒连接超时

路径处理规则：
- 服务总根、后端根、包体目录和后端 `PackageIndex.json` 都按本地文件系统路径拼接。
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

## Diff Preview / Staging（预览差异）

`RepositoryPreviewRunner` 提供只读 staging diff，不修改 HEAD、objects 或 PackageIndex。它只在 Repository 面板 `Changes` 视图点击 `Refresh Changes`，或 CLI `Diff` 命令中执行。

### AA Diff Preview

- runner 白名单：仅 `TaskScanAddressableHotfixDiff`
- Stop-after：`TaskScanAddressableHotfixDiff`
- 不产生临时文件，纯读取 `AddressableAssetSettings` 的当前资产状态
- 对比当前源资产 GUID 指纹与 Repository HEAD

### AB Diff Preview

- runner 白名单：`TaskPrepareContext` → `TaskCollectAssets` → `TaskCollectBuiltins` → `TaskAnalyzeDependencies` → `TaskBuildBundles` → `TaskGenerateManifest` → `TaskVerifyBuildResult` → `TaskScanABHotfixDiff`
- Stop-after：`TaskScanABHotfixDiff`
- 使用 `Temp/BuildRepositoryPreview/{guid}/` 临时输出目录
- `finally` 块清理临时目录
- 同时展示两组信息：HEAD Diff 是 current-vs-Repository HEAD；Hotfix Delivery 是 current-vs-Full baseline 的交付 bundle 数量、大小和列表

Staging diff 是只读诊断能力。它不会提交 HEAD、不会写 `PackageIndex.json`，也不会替代正式构建中的打包输出规则。

---

## 编辑器面板

### RepositoryStatusPanel

AA 与 AB Build Pipeline 窗口各自注册固定后端的 Repository。`RepositoryStatusPanel` 通过固定 `BackendMode` 构造，AA 仓库始终查看 AA Channel，AB 仓库始终查看 AB Channel，不跟随当前 `UseABBackend` 切换。Repository 左/中/右三栏之间使用两条可拖动分隔线，栏宽按 backend 分别持久化。

AA 的 `ArtifactDigest.Name` 是稳定的 Addressables GUID，不是 BundleName。AA Changes 和 History 会将它解析为 `Address | AssetPath` 供阅读，详情仍显示 GUID；GUID 无法解析时会明确提示未解析状态。该展示不改变仓库 JSON 或差异匹配键。AB 继续显示 BundleName。

AA Repository 右侧另有 `Hotfix Groups` 区域，用于处理尚未还原的 Addressables 热更分组记录。它显示可恢复、会回退 DefaultGroup 与无法恢复的记录数量；恢复后只保留失败项。`Discard Unrestorable` 只删除确认无法恢复的撤销记录，不移动资源、不删除 HotfixGroup，也不影响 Repository、包体或 `Test Reset`。

功能：

- **状态栏**：显示当前 HEAD 版本、包名、产物数量和 Health 状态
- **History**：显示 commit 列表，选择 commit 后展示该 commit 持久化的 `CommitDelta`；不会执行 `RepositoryPreviewRunner`
- **Changes**：显示 staging diff，只有点击 `Refresh Changes` 才执行 AA/AB preview runner
- **Artifact Detail**：展示 Added/Modified/Removed 的 `ArtifactDigest` 元数据；Modified 优先显示 old/new hash、CRC、size
- **Push 按钮**：选择 Push Target 后发布当前 Repository HEAD；面板不再提供可编辑 From/To 版本号

---

## CLI 入口

`BuildRepositoryCLI` 提供批处理/CI 命令：

| 命令 | 方法 | 关键参数 |
|------|------|---------|
| `Status` | `BuildRepositoryCLI.Status()` | `-channel`, `-backend` |
| `Health` | `BuildRepositoryCLI.Health()` | `-channel`, `-backend` |
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
       │         ├─ 读取旧 HEAD 作为 parent
       │         ├─ 写入 ParentVersion 和 CommitDelta
       │         ├─ 写入 objects/{Version}.json（原子写）
       │         └─ 交换 HEAD.json（原子写）
       └─ Push 由 Repository CLI/UI 触发；ConfirmRelease wrapper 当前不负责 Push
```

---

## 注意事项

- **AA/AB 发布根隔离**：同一个 Target 下 AA 与 AB 分别使用 `/AA/PackageIndex.json` 和 `/AB/PackageIndex.json`，不会争用远端入口
- **URL 应用显式执行**：Target 的 PublicBaseUrl 不会因 Push 自动写入运行时设置；Repository 的 Apply URL 只修改当前 AA/AB Settings
- **Diff 不筛 Push 文件**：Push 不做 delta-copy bundle；AB Hotfix 的交付筛选发生在构建 Task 内，不发生在 Push 阶段
- **提交 diff 与 staging diff 分离**：History 展示已持久化的 `CommitDelta`；Changes/CLI Diff 才运行当前 preview output vs HEAD 的主动预览
- **编辑器 Push 只发布 HEAD**：Target 是发布位置；From/To 在 UI 中不再作为人工输入。CLI Push 继续保留显式 from/to 参数。
- **AB Hotfix 依赖 Full baseline**：AB Hotfix 必须能找到同 Channel/Backend/Major 且 `BuildType == "Full"` 的 commit，否则失败
- **HEAD 损坏保护**：`GetStatus()` 区分"无 HEAD"和"HEAD 损坏"，损坏原因在 `HeadErrorReason` 字段
- **ChannelKey 不可跨后端混用**：AA 和 AB 的 Repository 空间完全隔离
- **Git 元数据非必需**：git 不可用时 Commit 仍正常进行，`GitCommitHash` 为空
