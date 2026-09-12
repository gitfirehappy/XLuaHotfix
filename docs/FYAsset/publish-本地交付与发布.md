# 本地交付与发布

> 返回总览：[资源管理架构文档](./资源管理架构文档.md)

> **关联代码**
>
> `Assets/FYAsset/Scripts/Shared/Build/Publish/`（`BuildPublisher`、`PackagePublishTransaction`、`PublishPlan`、`PublishRequest`、`PublishCache`、`PublishCacheStore`、`PublishMaintenance`、`PackageTargetAssembler`、`PublishPathGuard`、`LocalDirectoryPushTarget`、`PushTargetConfig`、`PackageFileScanner`、`PackageFileNames`、`PackageBuildIdentity`、`IPackageManifestReader`） · `Assets/FYAsset/Scripts/Shared/Build/Editor/BuildProjectRunner.cs` · `Release/Editor/LocalBuildDataExporter.cs` · `Editor/Summary/{BuildSummaryStore,BuildVersionPlanner}.cs`

---

## 两段不同的边界

“交付”和“发布”是两件事，写入的目录、事务范围和失败后果都不同：

| 阶段 | 入口 | 写什么 | 事务 |
|---|---|---|---|
| 本地交付 | `BuildProjectRunner` 的 attempt 交付事务 | 正式包目录、StreamingAssets 启动数据、正式构建摘要（`BuildData/Summaries`）与 Summary Index | 单机内的补偿事务：任一步失败按逆序回滚 live 状态 |
| 发布 | `BuildPublisher.Push` + `PackagePublishTransaction` | 服务器隔离目录、服务器 `PackageIndex` | 目录型目标的逆序补偿；不保证跨网络原子性 |

构建**不写** `PackageIndex`：`PackageIndex` 是发布事务的产物，由 `BuildPublisher` 在内容就位并校验通过后最后生成上传。

---

## 本地交付事务

`BuildProjectRunner` 的顺序（attempt 布局）：

```text
后端管线成功（Runner 已把 attempt 提升到交付目录，返回 IBuildDeliveryToken）
→ LocalBuildDataExporter.BeginDelivery（Full/Standalone：写 StreamingAssets 启动数据）
→ 写正式 Summary（BuildData/Summaries/{AA|AB}/{BuildId}.json，不可变）
→ 写 Summary Index（BuildData/Summaries/index.json）——最后一个可见身份提交点
→ Commit（释放备份）
```

- 任一步失败：按逆序补偿 Summary Index、正式 Summary、本地启动数据、产物提升；补偿本身失败会记录错误并尽力继续其余补偿。
- 复用事实不单独提交：它就是正式 Summary 的内容记录，随 Summary 与 Index 一起生效，没有独立的缓存目录需要维护。
- 失败包清理有明确的安全边界：attempt 布局只允许删除 attempt 根之下的目录；`Standalone` 只允许删除 `StreamingAssets/Standalone`；非 attempt 的普通包目录必须严格位于 `PackagesDir` 之下且目录名等于包名。不满足时改为写入 `FAILED_BUILD.json` 标记。
- 版本推进是事务性的：候选版本由 `BuildVersionPlanner` 在构建开始时算出并只在内存中传递；只有交付事务成功写出 Summary 与 Index 后它才成为项目当前版本，失败回滚不推进版本。

### StreamingAssets 启动数据

`LocalBuildDataExporter` 负责把 BuildIndex 与后端清单/内容落到 StreamingAssets：

- Full / Standalone 导出，Hotfix 不导出（Hotfix 不覆盖安装包 BuildIndex）；
- `RuntimeMode` 由构建类型推导后写进 `BuildIndexData`，运行时只读该字段；
- `BeginDelivery` 会把被覆盖的目标备份到工作目录，调用方必须在事务边界 `Commit` 或 `Rollback`，否则备份不会清除。

---

## 发布事务

### 总流程

```text
读取服务器 PackageIndex
→ 读取其指向包的 Manifest
→ 用清单声明的内容集合与本地包目录组装目标包集合（逐文件定字节来源）
→ 与服务器声明集做 FileDigest / FileDiff
→ 在服务器根下的隔离工作区组装新包目录（只读复用服务器当前包的字节）
→ 校验新目录逻辑完整性
→ 就位新包目录
→ 本地生成并最后上传新 PackageIndex
```

`BuildPublisher.Push(PublishRequest, IPushTarget)` 按目标能力分两条路：

- 目标实现 `IDirectoryPushTarget` 并能解析出服务器后端根 → 走完整的 `PackagePublishTransaction`；
- 目标不能提供目录级事实访问 → `PushFullUpload`：按完整上传处理，但 `PackageIndex` 仍然由发布器最后交给目标上传。

### 事务步骤与保证

| 步骤 | 方法 | 内容 |
|---|---|---|
| 读事实 + 组装计划 | `CreatePlan` | 扫描本地包目录；读服务器 `PackageIndex` → 包目录 → 后端 Manifest；`PackageTargetAssembler` 把清单声明的内容集合与本地文件合并成目标包集合，逐文件确定字节来源（本地 / 服务器当前包 / 本地基准 Full）；`FileDiff.Compute(ServerFiles, TargetFiles)`；只读，不修改任何文件 |
| 组装 | `Stage` | 在 `{serverRoot}/.fyasset_push/{包名}_{8位GUID}/staged/{包名}` 组装；同名同摘要优先复用服务器当前包，其次 Hash 命中复用（Hash+Size 相同、名称不同），最后从本地或基准 Full 复制；每个文件落地后重新计算摘要并要求与目标声明一致 |
| 校验 | `VerifyStaged` | 必填清单齐全、文件数与内容与计划一致、包内容目录内每个文件都被后端清单声明且摘要一致 |
| 就位 | `Apply` | 把隔离目录移动为正式包目录；若目标目录正是当前 `PackageIndex` 指向的包，则视为已发布内容不可变——内容一致按幂等成功，内容不一致直接拒绝覆盖 |
| 写索引 | `WritePackageIndex` | 备份旧索引后原子写入新 `PackageIndex`（`LatestPackage` / `LatestVersion` / `BackendMode`） |
| 提交 / 回滚 | `Commit` / `Rollback` / `Dispose` | 成功时写发布缓存并清理工作区；异常时按逆序恢复 PackageIndex 与包目录，再清理工作区 |

关键约束：

- 服务器 `PackageIndex` 与其指向的 Manifest 是**唯一远端事实**；本机发布缓存只做辅助提示。
- AB Hotfix 包是稀疏包：清单已经是完整目标清单，本地缺失的未变化内容先在服务器当前包内按摘要只读复用，再从基准 Full 包（`Summary.BaseFullSummaryId` → `ArtifactRelativePath`）取；两处都取不到或字节与清单不一致时**来源不足即失败**，不写部分目标包、不写 PackageIndex、不改动服务器旧包。
- AA Hotfix 的清单只声明本次构建产出的内容，目标集合等于本地包目录（相对 Full 裁剪属延期矩阵）。
- 服务器查询失败、索引损坏、包目录缺失、Manifest 损坏或内容与 Manifest 漂移时，`Degrade` 清空服务器声明集合，目标集合退化为本地包目录（按完整上传处理）；退化的计划仍然遵循“校验通过后才写索引”，且来源不足仍会失败。
- 新包目录不可变；当前 `PackageIndex` 指向的目录不可被覆盖。
- 发布**不删除任何旧包**。
- 发布路径一律经 `PublishPathGuard` 校验：包集合名与包名必须是安全单段目录名，派生路径必须被约束在服务器根内，路径链上不得出现符号链接/重解析点。

### 包身份

`PublishRequest.SourcePackageDir` 由调用方显式给出（Full / Standalone 为完整包目录，Hotfix 为稀疏变化内容包目录，来源是所选正式 Summary 的 `ArtifactRelativePath`）。包名、版本与后端由发布 UI 从正式 Summary 解析后经 `PublishRequest.Identity` 注入（`TryResolveIdentity`），**包目录与源目录不承载身份**；`PackageBuildIdentity` 严格解析 `Build_{yyyyMMddHHmmss}_{VersionNumber}` 并校验包名是安全单段名、后端与请求一致。

`PackageFileScanner` 把包目录整棵树展开成 `FileDigest` 集合，包目录只含发布内容，扫描不再需要排除构建元数据；`PackageFileNames` 只保留发布工作区目录名 `.fyasset_push`。`PackagesFolderName` 必须与运行时读取远端包时使用的 `FYAssetSettings.BuildPackagesFolderName` 一致——发布布局与下载布局是同一份契约。

`IPackageManifestReader` 是 Shared 读取后端清单的唯一入口：后端通过 `RequiredPackageFileNames`（必填清单/索引文件，不参与内容 Diff）、`ContentDirectoryName`（内容目录，例如 `bundles`）、`TryReadContentDigests` 注入自己的格式，Shared 不引用 `AAManifest` / `ABManifest` / `catalog`。

### 发布缓存

`PublishCache`（`BuildData/PublishCache/{AA|AB}/{TargetId}.json`）记录上一次发布完成后服务器包目录内的文件摘要、包名与时间：

1. 只有发布成功后写入，写入失败只记录 Warning；
2. 读取失败一律按“没有缓存”处理，不影响发布正确性；
3. 与服务器事实不一致时只输出提示（“仅提示，不改变发布决定”）；
4. 不参与任何写包、写索引或删除旧包的决定。

它与正式构建摘要（`BuildData/Summaries`）完全分离：摘要属于项目内构建事实（也不进入包目录），发布缓存只做提示。

---

## 旧包清理是独立维护入口

`PublishMaintenance.DeleteUnreferencedPackages(serverBackendRoot, packagesFolderName)`：

1. 发布本身永不删除旧包；
2. 只删除服务器包目录集合中**不被当前 `PackageIndex` 指向**的包；
3. 当前 `PackageIndex` 无法读取时**拒绝清理**（无法确定当前包时宁可不动）；
4. 只处理形如 `Build_*` 的直接子目录，不递归、不跟随符号链接、不触碰其他文件；
5. 删除失败记入 `SkippedEntries`，不视为整体失败。

`ABTestMaintenancePanel`（Test 面板）提供本机维护入口：本地热更服务器 Start/Stop/Status、Reset Version、Clear Local State。

---

## 发布目标与 URL

| 组成 | 说明 |
|---|---|
| `PushTargetConfig` | `TargetId`（稳定 GUID）/ `Name`（唯一显示名）/ `Type` / `Path`（服务目录根）/ `PublicBaseUrl`；持久化在 `FYAssetSettings.PushTargets` |
| 后端子目录 | 服务根下按后端隔离：`{serviceRoot}/{AA\|AB}`；两个后端不共享根部 `PackageIndex` |
| `LocalDirectoryPushTarget` | Shared 内置的目录型目标，具备目录级事实访问能力 |
| `CloudflarePagesPushTarget` | Compat 工厂注入的 CDN 接入：更新本地镜像后执行 Wrangler 部署；旧同名包目录先备份到服务根之外，失败时把包目录、`PackageIndex` 与 `_headers` 原样恢复 |

`PublishTargetPanel` 的 AB 页面把 `CurrentABTargetId` 立即写入 `FYAssetSettings`；TargetId 只用于稳定身份和缓存隔离，Name 只用于显示。AB 页面不再提供 `Apply URL`，也不把 URL 写回 `FYAssetABSettings`。AB runtime 与发布校验共用 TargetId、Name、PublicBaseUrl 的严格解析：缺失目标、重复身份、非法 HTTP/HTTPS 地址、查询字符串或片段都会阻断。AA 页面仍保留自己的 HotfixUrl 配置。

本地镜像服务由 `CommandLine/hotfix_server.py` 提供（只读静态服务，支持 `__fyasset_health` 健康检查与请求日志），默认根目录为 `HotfixPublish/Local/{AA|AB}`。

---

## 失败与验证边界

- 发布失败按逆序补偿本地包目录与索引；部署命令失败不能证明远端从未发生任何变化。
- 中断上传不影响旧 `PackageIndex` 与旧包。
- 分布在不同机器/网络上的服务器事实与本地缓存可能不一致，缓存漂移只提示不阻断。
- 真实远端发布需要单独的授权与网络验证；默认用隔离本地目标模拟。
