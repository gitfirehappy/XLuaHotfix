# 本地交付与发布

> 返回总览：[资源管理架构文档](./资源管理架构文档.md)

> **关联代码**
>
> `Assets/FYAsset/Scripts/Shared/Build/Publish/`（`PackagePublisher`、`PackageRemoteReader`、`PackageContentResolver`、`PackageUploader`、`PublishRequest`、`PublishTargetConfig`、`PublishTargetContracts`、`PackageRetentionCleaner`、`PackageBuildIdentity`、`PublishPathGuard`、`PackageFileScanner`、`IPackageManifestReader`） · `Shared/Build/Runner/Editor/BuildProjectRunner.cs` · `Shared/Build/Delivery/Editor/BuildOutputDelivery.cs` · `Shared/Build/Release/Editor/LocalBuildDataExporter.cs`

## 边界

本地构建交付和远端发布是两个独立事务：

| 阶段 | 入口 | 事实 | 事务范围 |
|---|---|---|---|
| 本地交付 | `BuildProjectRunner` | `BuildRequest`、后端结果、`CompleteBuildSummary` | 临时构建目录、正式输出、本地启动数据和 Summary 的补偿 |
| 远端发布 | `PackagePublisher.Publish` | 本地包、实时远端事实、用户选择的正式 Summary 身份 | 目标包目录与最后写入的 `PackageIndex` |

构建不写 `PackageIndex`。构建成功后由 `BuildOutputDelivery` 将 `BuildRequest.TemporaryOutputDir` 交付到 `FinalOutputDir`；发布才负责目标服务器的 `PackageIndex`。

## 本地交付

```text
执行后端 Task
→ BuildOutputDelivery.Deliver(temporary, final)
→ Full/Standalone 导出本地启动数据
→ 写不可变 CompleteBuildSummary
→ 写可重建 Summary Index
→ Commit
```

`BuildDeliveryResult` 由调用方显式 `Commit` 或 `Rollback`。正式输出存在时先移动到 `BuildWorkRoot/_temp/_recovery/{target}`，提交时删除 recovery，失败时恢复旧目录。临时目录已存在即失败，不自动删除或复用。

`BuildRunContext` 只保存请求、配置和阶段 DTO。构建请求唯一来自 `BuildRequest`；最终对外结果是 `BuildResult`，只包含成功状态、错误、Summary 和报告路径。`CompleteBuildSummary` 是完整事实来源，`BuildSummaryIndex` 只是可重建索引。

## 发布主链

```text
PackagePublisher
  → PackageFileScanner
  → PackageRemoteReader
  → PackageContentResolver
  → PackageUploader
```

- `PackageRemoteReader` 只读取实时远端事实，返回 `Empty`、`Usable`、`Invalid` 或 `Inaccessible`。
- `PackageContentResolver` 只决定目标文件集合和来源：本地包 → 已验证远端包 → 显式注入的基准 Full。
- `PackageUploader` 负责临时目录、复制校验、不可变同名检查、目标目录切换、回滚和最后写 `PackageIndex`。
- 远端 `PackageIndex` 缺失表示首次完整上传；损坏或内容漂移禁止复用远端字节并执行完整修复；权限和 I/O 失败立即失败。
- 同名同内容是幂等成功；同名不同内容一律拒绝。
- 发布临时目录固定为 `{BackendRoot}/_temp/{PackageName}`，已存在即拒绝覆盖。
- Hotfix 缺失内容只能从已验证远端包或显式注入的 Full 基准补齐；来源不足时无副作用失败。

`PublishRequest.Identity` 必须来自用户选定的正式 Summary。`PackageBuildIdentity` 集中生成和严格解析 `Build_{yyyyMMddHHmmss}_{VersionNumber}`，包目录名不能替代身份事实。

## 发布目标

`PublishTargetConfig` 直接实现 `IPublishTarget`，只保存 `TargetId`、`Name`、`Path` 和可选 `PublicBaseUrl`，并解析后端根目录和公开 URL。目标不复制文件、不读写 `PackageIndex`、不决定来源、不拥有回滚。

旧 Target 枚举、Local/Cloudflare 专用目标类、工厂注册器和外部部署负载已删除。目录目标和未来外部目标都通过同一目标配置描述路径或 URL；当前发布事务由 Shared `PackageUploader` 统一承担。

## 旧包维护

`PackageRetentionCleaner` 是独立维护入口。发布成功不会删除旧包；清理时仅删除当前 `PackageIndex` 未指向的合法 `Build_*` 直接子目录。索引不可读、路径越界或遇到重解析点时拒绝清理。

## 验证边界

纯 .NET 场景覆盖构建管线、Summary 复用、Hotfix 来源、运行时资源边界、发布路径安全和资源导出边界。真实 Unity Editor 构建、LocalDirectory 发布和外部远端部署仍需在对应环境中单独执行；本地 fake runner 不代表真实网络部署。
