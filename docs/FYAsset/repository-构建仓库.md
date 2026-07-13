# Build Repository 构建仓库

> 返回总览：[FYAsset 资源管理总览](./资源管理架构文档.md)

> **关联代码** | `Assets/FYAsset/Scripts/Shared/Build/Repository/`

Build Repository 为每个构建通道保存可追溯 HEAD、产物指纹和固定差异，并把已提交包体发布到 Local 或 Cloudflare Target。它不是源码 Git，也不负责在 Push 时重新构建或重新筛选 Bundle。

---

## 核心模型

| 模型 | 作用 |
|------|------|
| `ArtifactDigest` | 用 Name、Hash、Size、CRC 表示一个可比较产物 |
| `ArtifactDelta` | 当前集合相对基准的 Added / Modified / Removed |
| `RepositoryCommit` | 保存版本、后端、构建类型、父版本、包体位置、Git 元数据和产物全集 |
| `RepositoryHeadState` | 只保存当前 HeadVersion，object 路径由目录推导 |
| `PushTargetConfig` | 描述 Target Id、类型、服务根路径和公开根 URL |
| `PushReceipt` | 描述发布结果、目标位置与失败原因 |

AA 的 Artifact Name 使用稳定资产 GUID；AB 使用 BundleName。展示层可以把 AA GUID 解析为 Address/AssetPath，但不得改变仓库匹配键。

---

## 通道与文件布局

ChannelKey 固定为 `{BuildTarget}[-Channel]/{AA|AB}`。AA 与 AB 拥有独立 HEAD 和 objects。

```text
BuildData/Snapshots/{ChannelKey}/
├─ HEAD.json
└─ objects/{ReleaseVersion}.json
```

发布版本使用 `Major.Minor.Patch[-Channel]`；独立 Build 计数不进入 object 名。

---

## Commit 流程

1. 后端完成构建与产物校验。
2. Repository 读取旧 HEAD，作为新提交的 ParentVersion。
3. 对比旧/新 Artifact 全集，生成固定 CommitDelta。
4. 采集 Git hash 与 dirty 状态；Git 不可用不阻断资源提交。
5. 原子写入新 object，再原子交换 HEAD。
6. 只有 commit 成功后，版本数据库和对外包体指针才可继续更新。

首个提交以空集合为基准，因此全部产物都是 Added。损坏的 HEAD 不是“空仓库”，必须报告 Health 错误。

---

## 两种差异

| 视图 | 比较对象 | 用途 |
|------|----------|------|
| History CommitDelta | 已提交版本 vs 其父提交 | 稳定审计，不重新构建 |
| Changes | 当前工作状态 vs Repository HEAD | 提交前预览 |
| AB Hotfix Delivery | 当前 AB 产物 vs 同 Channel/Backend/Major 的 Full baseline | 决定 Hotfix 实际交付 Bundle |

Changes 与 Delivery 是两个问题。缺少 HEAD 时 Changes 可按空基准展示；缺少 AB Full baseline 时 Delivery 必须失败，但不能阻断普通 Changes。

AA Changes 读取 Addressables 源资产指纹，不移动 Group、不构建、不写 PackageIndex。AB Changes 使用临时目录运行到差异 Task，结束后清理；两者都不得修改正式 HEAD、objects 或输出。

---

## AB Full / Hotfix 规则

- Full 的 `BundleEntries` 是完整基线。
- Hotfix 的 `DeliveryBundles` 只包含相对同 Major Full baseline 新增或改变的物理 Bundle。
- 完整 `BundleEntries` 仍用于运行时查询和依赖解析。
- 未交付 Bundle 必须能由 Full baseline 以同名同 Hash 提供，否则构建失败。
- Removed Bundle 不主动远程删除；新 manifest 停止引用后由清理策略处理旧包。

---

## Push 流程

1. UI 发布当前 HEAD；CLI 也可显式指定 from/to。
2. Repository 从 commit 定位已完成的包体并组装 PushPayload。
3. Target 将服务总根扩展为 `{ServiceRoot}/{AA|AB}`。
4. 包目录与后端 `PackageIndex.json` 在同一事务中暂存、替换；失败恢复旧状态。
5. 返回 PushReceipt。Repository 不保留额外 PushHistory。

Push 不做 delta-copy，也不解释 catalog/manifest 内容。AB Hotfix 的 Bundle 筛选发生在构建阶段。

### Local Target

- 相对路径按项目根解析，绝对路径直接规范化。
- 源和目标相同时跳过自复制，避免删除当前产物。
- 失败时恢复旧包和旧 PackageIndex，不残留事务目录。

### Cloudflare Pages Target

- 先事务更新本地镜像，再部署整个服务根，保留另一后端目录。
- 只有显式 Cloudflare Push 才调用 Wrangler。
- Wrangler 缺失、未认证或部署失败均返回失败；部署失败恢复本地镜像。
- PackageIndex 禁止缓存，版本化 Packages 使用 immutable 缓存。

Target 的 PublicBaseUrl 只用于派生 `/AA/` 或 `/AB/` URL。Push 本身不修改客户端设置；必须由用户点击 `Apply URL`。

---

## Repository 面板

AA/AB Build Pipeline 各自注册固定后端的 Repository 面板：

- Status：HEAD、包名、产物数量和 Health。
- History：查看已提交 CommitDelta。
- Changes：显式刷新当前 staging diff。
- Artifact Detail：查看旧/新 Hash、CRC 和大小。
- Push Targets：编辑 Local/Cloudflare 目标，发布当前 HEAD，显式 Apply URL。
- Local Server：只监听 `127.0.0.1`，用于本地发布验证。
- Test Reset：测试用途的版本、Channel、输出和启动基线清理。
- AA Hotfix Groups：恢复 Addressables 临时分组；与 Repository Reset 相互独立。

右侧内容位于纵向 ScrollView。动态 Target 列表必须保持自身内容高度，不得用固定卡片高度压缩字段。

---

## CLI

| 命令 | 用途 | 关键参数 |
|------|------|----------|
| `Status` | 查看 HEAD 摘要 | `-channel -backend` |
| `Health` | 检查 HEAD/object 一致性 | `-channel -backend` |
| `Diff` | 生成当前 Changes | `-channel -backend [-json]` |
| `Push` | 发布指定提交范围 | `-channel -backend -from -to -target` |
| `ListCommits` | 列出历史提交 | `-channel -backend` |

成功退出码为 0，失败为 1。

---

## 安全边界

- AA/AB 的 HEAD、PackageIndex 和发布根不得共用。
- Commit 先写 object 后换 HEAD；Push 先写包体后换 PackageIndex。
- 预览不得污染正式输出或仓库状态。
- Push 不自动改 HotfixUrl。
- Reset、Clear Channel、Local Server 和 Cloudflare 部署都属于显式操作，不应在普通刷新中触发。
- 路径删除前必须确认目标位于配置根内；ChannelKey 的 `/` 是逻辑分隔符，不直接当本地路径拼接。
