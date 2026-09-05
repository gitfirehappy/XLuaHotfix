# 构建基线与发布（Build Baseline & Publish）

> 返回总览：[FYAsset 资源管理总览](./资源管理架构文档.md)

> **关联代码** | `Assets/FYAsset/Scripts/Shared/Build/Baseline/` · `Shared/Build/Publish/` · `Shared/Build/Editor/UI/RepositoryStatusPanel.cs`

本机制记录每个构建通道的"交付基线"（最近一次成功交付的包体身份与产物指纹），支撑 diff 预览与后续 hotfix 的差异判定；并通过 Push 把已构建完成的包体发布到 Local 镜像或 Cloudflare Pages。它**不是源码 Git，不保存版本历史**——历史与审计交给源码 Git；这里只保存"当前可用状态"。

---

## 核心模型

| 模型 | 作用 |
|------|------|
| `ArtifactDigest` | 用 Name、Hash、Size、CRC 表示一个可比较产物 |
| `ArtifactDelta` | 当前集合相对基线的 Added / Modified / Removed |
| `BuildBaseline` | 一次成功交付：Version、BuildType、PackageName、BackendMode、PackageRootDir、ParentVersion、CommitDelta、ManifestFileNames、Artifacts |
| `BuildBaselineState` | 同通道的双槽状态：`Latest`（任何类型的最新交付）与 `LatestFull`（最近的 Full，hotfix 的累积基准） |
| `PushTargetConfig` | 发布目标：Id、类型（LocalDirectory / CloudflarePages）、服务根路径、公开根 URL |
| `PushReceipt` | 发布结果：成功与否、目标位置、失败原因 |

AA 的 Artifact Name 使用稳定资产 GUID；AB 使用 BundleName。展示层可解析为 Address/AssetPath，不改变匹配键。

---

## 通道与文件布局

ChannelKey 形如 `{BuildTarget}[-Channel]/{AA|AB}`。基线随源码版本管理（可审计、可随分支演进）：

```text
BuildData/Baselines/{ChannelKey}/baseline.json   （原子写入，双槽一个文件）
```

`Save` 时：

- 任何成功交付都覆盖 `Latest`；
- Full 交付同时覆盖 `LatestFull`；
- hotfix 交付的 `ParentVersion` 自动取当时的 `LatestFull` 版本（链回其累积基准）。

基线**只在构建+本地启动数据发布全部成功后**写入，因此没有"半提交状态"需要做回滚提交。

---

## 两种差异

| 视图 | 比较对象 | 用途 |
|------|----------|------|
| Changes | 当前工作状态 vs 基线 `Latest` | 构建前预览（AA/AB 各自 preview 提供数据源） |
| AB Hotfix Delivery | 当前 AB 产物 vs 同 Channel/Major 的 `LatestFull` | 决定 hotfix 实际投递的 Bundle |

缺少基线时 Changes 按空基准展示；缺少 `LatestFull` 时 AB Delivery 必须失败（不阻断 Changes）。
AA Changes 读 Addressables 源资产指纹，不移动 Group、不构建；AB 用临时目录跑到差异 Task 后清理。两者都不写正式输出与基线。

## AB Full / Hotfix 规则

- Full 的 `BundleEntries` 是完整基线；Hotfix 的 `DeliveryBundles` 只含相对同 Major Full 新增或改变的 Bundle。
- 未投递 Bundle 必须能由 `LatestFull` 以同名同 Hash 提供，否则构建失败。
- Removed Bundle 不主动远程删除；新 manifest 停止引用后由清理策略处理旧包。

---

## Push（发布）

Push 的唯一职责：把**已构建完成**的包体发布到远端镜像。它不再构建、不筛选 Bundle。

1. 面板或 CLI 调用 `BuildPublisher.PushLatest(channelKey, target)`。
2. 从 `BuildBaselineStore` 载入 `Latest` baseline，取 `PackageRootDir` 组装 `PushPayload`。
3. 服务根扩展为 `{ServiceRoot}/{AA|AB}`（AA 与 AB 物理隔离，互不清除对方）。
4. `PackagePublishTransaction` 在同一事务内暂存、替换包目录与 `PackageIndex.json`；异常自动回滚。
5. 发布前按 baseline 携带的 `ManifestFileNames` 校验 manifest 完整（文件名由后端 handler 注入、随基线落盘，事务不感知后端类型）。

### Local 目标（机制在 Shared）

- 相对路径按项目根解析；源=目标时跳过自复制。
- 失败时恢复旧包与旧 PackageIndex，不留事务目录。

### Cloudflare Pages 目标（部署胶水在 Compat）

- `CloudflarePagesPushTarget` 由 `CompatPushTargetFactory` 注入创建；Shared 面板只认 Local 类型，遇到扩展类型会给明确错误（生产 CDN 推荐走 CLI）。
- 先事务更新本地镜像，再 Wrangler 部署；失败恢复本地镜像。
- `PackageIndex` 禁缓存，版本化 Packages 用 immutable 缓存。

Push 不修改客户端设置；需要在面板显式 `Apply URL`（落盘能力由组合窗口注入 `IRepositorySettingsSink`）。

---

## Repository 面板

`RepositoryStatusPanel`（`Shared/Build/Editor/UI/`）为中性能力面板，由 AA/AB Build Pipeline 窗口注入后端：

- Header：Channel、`Latest`/`LatestFull`、包名与产物数；基线损坏时显示精确错误。
- Changes：显式刷新 staging diff（数据源由 `IRepositoryPreviewProvider` 注入：AA 走 `AARepositoryPreviewProvider`，AB 走 `ABRepositoryPreviewProvider`）。
- Delivery（仅 AB）：当前输出 vs `LatestFull` 的投递预估。
- Push Targets：编辑 Local 目标、发布、Apply URL；Local Server 本地发布验证（仅 `127.0.0.1`）。
- AA Hotfix Groups（注入的 `IRepositoryMaintenancePanel`）：恢复 Addressables 临时分组。

后端专属测试面板已迁出后端窗口：`FYAsset/Build/Test Matrix`（Compat 宿主）承载双后端 Test 页。

---

## CLI

`Compat/Editor/Repository/BuildRepositoryCLI.cs`（`FYAssetRepository`）：

| 命令 | 用途 |
|------|------|
| `Status` | 显示 `Latest`/`LatestFull` 摘要（历史另用 `git log`） |
| `Diff` | 生成当前 Changes（`-json` 可选） |
| `Push [-target NAME]` | 发布当前基线（扩展 target 经 Compat 注入工厂） |

成功 0 / 失败 1。

---

## 安全边界

- AA/AB 的基线、PackageIndex 与发布根不共用。
- 基线原子写入；Push 先写包体后换 PackageIndex。
- 预览不污染正式输出或基线。
- Push 不自动改 HotfixUrl；Test 重置、Local Server、Cloudflare 部署都是显式操作。
- 路径删除前必须确认目标位于配置根内；ChannelKey 的 `/` 是逻辑分隔符。
