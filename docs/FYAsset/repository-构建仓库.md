# 构建基线与发布

> **关联代码** | [Baseline](../../Assets/FYAsset/Scripts/Shared/Build/Baseline/) · [Publish](../../Assets/FYAsset/Scripts/Shared/Build/Publish/) · [BuildProjectRunner](../../Assets/FYAsset/Scripts/Shared/Build/Editor/BuildProjectRunner.cs)

本机制保存最近交付状态，用于 Diff、累计 Hotfix 和发布。它不是源码仓库，不保存 object、HEAD、分支或提交历史。流程图见 [HTML 建模文档](./fyasset-modeling.html)。

## 模型与差异

| 类型 | 作用 |
|---|---|
| `BuildDiffEntry` | `Name/Hash/Size/CRC` 构建比较条目；AA Name 是 Asset GUID，AB 是 BundleName |
| `ArtifactDiffer` | 区分大小写匹配 Name，只比较 Hash；Size/CRC 不影响修改判定 |
| `ArtifactDelta` | Added/Modified 保存目标条目引用，Removed 保存基准 Name |
| `BuildBaseline` | 版本、构建类型、包名、后端、物理包目录、ParentVersion、CommitDelta、Manifest 文件名与产物条目 |
| `BuildBaselineState` | `Latest` 和 `LatestFull` 双槽状态 |
| `PushTargetConfig` / `PushReceipt` | 发布目标配置与一次发布结果 |

基线存储于 `BuildData/Baselines/{BuildTarget}[-Channel]/{AA|AB}/baseline.json`。Save 更新 Latest，Full 还更新 LatestFull；Hotfix 的 ParentVersion 取 LatestFull。单文件原子写入不等于包、启动数据、索引和 VersionRecord 同时原子更新。

| 比较 | 基准 | 用途 |
|---|---|---|
| Changes / CommitDelta | Latest | 查看相邻构建差异 |
| AB Hotfix Delivery | 同后端、通道、Major 的 LatestFull | 确定 Hotfix 必须携带的累计变更 Bundle |

没有 Latest 时可按空集合显示 Changes；没有合法 LatestFull 时不能生成 AB 累计 Hotfix。完整 `BundleEntries` 与部分 `DeliveryBundles` 不可互换。未投递条目必须由 Full 基线提供同名同 Hash 的内容。Removed 不代表立即删除远端文件。

## 构建交付

AB 正式入口启用 attempt 布局：Task 先写 `BuildPathManager.AttemptPackagesRoot` 下的尝试目录。Runner 验证 Bundle 后将其 promote 到交付目录，依次更新本地启动数据、PackageIndex、baseline、VersionRecord；失败按逆序尝试补偿。Standalone 不更新 PackageIndex/baseline，最终目录是 `StreamingAssets/Standalone`。

AA 正式入口仍使用非 attempt 路径：构建后发布本地数据，再保存 baseline，成功返回后应用版本。不得把 AB 的补偿流程描述为 AA 已有的完整事务。补偿失败会记录日志，不保证磁盘状态必然恢复。

`HotfixOutput` 是构建输出默认根；`HotfixPublish` 是发布镜像/部署暂存根。保留两者可以区分“已构建”与“已发布”，且发布目标可以不止一个。

## 发布流程

1. `BuildPublisher.PushLatest` 加载 Latest，读取其 `PackageRootDir`，构造 PushPayload。
2. 目标将服务根按 AA/AB 分开；发布不重新构建，也不按 Diff 筛选文件。
3. `PackagePublishTransaction` 校验声明的 Manifest 文件，暂存包和 PackageIndex，再替换可见目录与索引。
4. Local 目标完成本地发布；Cloudflare 接入在更新本地镜像后执行 Wrangler 部署。
5. 失败尝试恢复本地包与索引。部署命令失败不能证明远端从未发生任何变化。

Shared 内置 LocalDirectory；Cloudflare 由 Compat 工厂注入。`PublishTargetPanel` 的 `Apply URL` 是独立显式动作：面板只计算 URL，由 AA/AB 窗口注入的回调写入各自 Settings。发布本身不修改运行时 URL。baseline 的 ManifestFileNames 由后端按本次 ManifestOutputFormat 提供：JsonOnly/BinaryOnly 只要求实际输出，JsonAndBinary 要求两份。

## 编辑器入口

- AB Diff：`ABBuildDiffPanel` 展示 Changes 与 Delivery。
- Publish Target：`PublishTargetPanel` 处理目标选择、发布及 URL 应用。
- AB 维护：`ABTestMaintenancePanel` 承载维护操作；删除和重置有副作用。
- AA Hotfix Groups：保留临时 Group 恢复和不可恢复记录处置，不与 Labels 编辑混同。
- 独立 Test Matrix：代码位于 `Scripts/Tests`，不属于 Compat 生产接入。

旧 RepositoryStatusPanel 与相关注入接口已经退役。`RepositoryPreview` 等仍存在的类型名不代表恢复了历史仓库模型。

## CLI 与安全

[BuildRepositoryCLI.cs](../../Assets/FYAsset/Scripts/Compat/Editor/Repository/BuildRepositoryCLI.cs) 提供 `FYAssetRepository.Status`、`Diff`、`Push`。Status/Diff 与 Push 副作用不同；Push 涉及文件写入，Cloudflare 还涉及外部部署，不用作只读验证。

操作前核实通道、后端、源包与目标根。不要根据目录名推断发布成功，不自动清空另一后端，不把失败补偿等同于无风险事务。包、运行时索引、启动数据及发布 URL 的一致性需分别验收。
