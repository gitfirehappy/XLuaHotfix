# Draft: AA-AB 管线统一与差异分析

> **Status**: Archived — 2026-05-19; promoted plan slices moved to executable plans
> **Purpose**: 系统梳理 AA（Legacy Addressables）和 AB（自研管线）在构建、版本管理、运行时热更三个维度的统一点与差异点，为 Build Repository 和后续重构提供决策依据。

---

## 总览：三层对比

| 层 | AA (Legacy) | AB (自研) | 统一程度 |
|---|---|---|---|
| **构建编排** | BuildProjectManager → IBuildBackend | 同左 | 已统一 |
| **构建执行** | Addressables BuildPlayerContent | DAGScheduler + 7 Tasks | 完全不同 |
| **版本管理** | BuildSnapshots SO + DifferentialProcessor | 无（待建） | 待统一 → Build Repository |
| **产物格式** | version_state.json + bundles/ | ABManifest.json + bundles/ | 结构相似，格式不同 |
| **运行时热更** | IHotfixPipeline → LegacyHotfixBackend | IHotfixPipeline → ABHotfixBackend | 已统一（接口层） |
| **CLI 入口** | BuildCommandLine.Build | 同左 | 已统一 |

---

## 第一层：已统一的部分

### 1.1 构建编排（IBuildBackend）

```
BuildProjectManager
  ├── CreateBackend() → UseABBackend ? ABBuildBackend : LegacyAddressableBuildBackend
  ├── BuildAsync(version, buildType, options)
  ├── OrganizeOutput(outputDir, version)
  └── GenerateVersionState(outputDir, version)
```

**统一点**：
- 入口统一（BuildFullPackage / BuildHotfix）
- 版本递增逻辑统一（VersionDataBase）
- 产物目录结构统一（HotfixOutput/Packages/Build_{date}_{version}/）
- manifest.json 更新统一

**决策已定**：IBuildBackend 接口不需要改动。

### 1.2 运行时热更（IHotfixPipeline）

```
HotfixManager（编排）
  └── IHotfixPipeline（后端）
        ├── LegacyHotfixBackend: version_state + catalog 下载
        └── ABHotfixBackend: ABManifest 下载
```

**统一点**：
- 5 步流程统一（Init → LoadLocal → FetchRemote → GetDownloadList → PostDownload）
- BundleDownloadItem 统一（BundleName + FileHash + FileSize）
- HotfixVersionInfo 统一视图

**决策已定**：IHotfixPipeline 不受 Build Repository 影响。

### 1.3 CLI 入口（BuildCommandLine）

已有 `-executeMethod BuildCommandLine.Build -buildType full/hotfix` 统一入口。Build Repository CLI 将作为独立入口并列。

---

## 第二层：结构相似但格式不同

### 2.1 产物清单

| | AA | AB |
|---|---|---|
| 文件 | `version_state.json` | `ABManifest.json` |
| Bundle 条目 | `BundleInfo { BundleName, FileHash, FileSize }` | `ManifestBundleEntry { BundleName, Hash, ... }` |
| 版本号 | `VersionState.Version` | 无（由外部管理） |
| 总大小 | `VersionState.TotalSize` | 运行时计算 |

**观察**：BundleInfo 和 BundleDownloadItem 字段几乎一致（Name + Hash + Size）。这正是 ArtifactDigest 的来源。

### 2.2 快照数据

| | AA | AB |
|---|---|---|
| 存储 | BuildSnapshots SO（Unity 序列化） | 待建（Build Repository 文件系统） |
| 粒度 | Asset 级（AssetGUID + Hash） | Bundle 级（BundleName + Hash） |
| 额外信息 | Group 归属、Labels、Address | 无 |
| HEAD/Stage | HeadIndex + StageSnapshot | 待建 |

**关键差异**：AA 快照是 asset 级（源文件），AB 快照是 bundle 级（输出文件）。Build Repository 的 ArtifactDigest 需要兼容两种粒度。

---

## 第三层：根本性差异

### 3.1 Hotfix 构建策略

```
AA Hotfix:
  1. 扫描所有 Addressable entries → 计算 Hash
  2. 与 HEAD 快照对比 → 找出变更 assets
  3. 将变更 assets 移入 Hotfix Group（副作用！）
  4. 执行 Addressables 构建（只构建 Hotfix Group）
  5. 还原 Group 或等待 ConfirmRelease

AB Hotfix:
  1. 执行 DAG 全量构建（Unity 增量引擎自动优化）
  2. 构建完成后对比 INDEX vs HEAD → 找出变更 bundles
  3. Push 只上传变更的 bundles
  （无 Group 移动，无副作用）
```

**根本差异**：
- AA 的 diff 在构建**前**（决定构建什么）
- AB 的 diff 在构建**后**（决定发布什么）
- AA 有副作用（Group 移动）；AB 无副作用

### 3.2 扫描对象

| | AA | AB |
|---|---|---|
| 扫描时机 | 构建前 | 构建后 |
| 扫描对象 | Addressable entries（源文件） | 输出目录（bundle 文件） |
| 标识符 | AssetGUID | BundleName |
| Hash 计算 | DeepHash（递归依赖）/ FileHash | bundle 文件 hash |
| 依赖 | AddressableAssetSettings | 文件系统 |

### 3.3 Group 移动（AA 独有）

AA 的 Group 移动是其 hotfix 机制的核心：
- Addressables 按 Group 打包 → 只有 Hotfix Group 的 bundle 会被构建
- 移动 = 改变构建范围
- 需要 undo（RestoreOriginalGroups）
- 需要 confirm（ConfirmRelease → Stage 转正为 HEAD）

AB 没有等价概念：全量构建 + 增量发布。

---

## Build Repository 统一策略

### 统一的部分（IBuildRepository 直接覆盖）

| 操作 | AA | AB | 统一方式 |
|---|---|---|---|
| `status` | 读 BuildSnapshots.HeadIndex + StageSnapshot | 读 HEAD 文件 | 统一为 HEAD 文件 |
| `add` | ScanCurrentProjectAssets → 写 Stage | 扫描输出目录 → 写 INDEX | IArtifactScanner 注入 |
| `diff` | FindModifiedAssets | 对比 INDEX vs HEAD | 统一 ArtifactDelta |
| `commit` | ConfirmRelease（Stage → Head） | INDEX → objects/ + 更新 HEAD | 统一语义 |
| `reset` | 清空 StageSnapshot | 删除 INDEX | 统一 |
| `tag` | 无 | 无 | 新增，统一 |
| `push` | 无（手动上传） | 无（手动上传） | 新增，统一 |

### 差异通过注入解决（IArtifactScanner）

```csharp
// AA Scanner: 扫描 Addressable entries
public class AddressableAssetScanner : IArtifactScanner
{
    public List<ArtifactDigest> Scan()
    {
        // 遍历 AddressableAssetSettings.groups
        // 对每个 entry: Name=GUID, Hash=DeepHash/FileHash, Size=文件大小
    }
}

// AB Scanner: 扫描构建输出目录
public class BundleOutputScanner : IArtifactScanner
{
    public List<ArtifactDigest> Scan()
    {
        // 遍历输出目录的 .bundle 文件
        // 对每个 bundle: Name=BundleName, Hash=FileHash, Size=文件大小
    }
}
```

### 差异保留在 Backend 内部（不进 Repository）

| AA 独有逻辑 | 归属 |
|---|---|
| Group 移动（PrepareHotfix） | LegacyAddressableBuildBackend 内部 |
| Group 还原（RestoreOriginalGroups） | LegacyAddressableBuildBackend 内部 |
| OriginalGroupName 记录 | Backend 内部 undo log |
| Hotfix Group 创建/管理 | Backend 内部 |
| ConfirmRelease 的 Group 清理 | Backend 内部 |

---

## 迁移路径

### Phase 1: Build Repository 基础（AA/AB 共用）

```
新建:
  IBuildRepository + FileSystemBuildRepository
  IArtifactScanner + AddressableAssetScanner + BundleOutputScanner
  ArtifactDigest / Snapshot / ArtifactDelta
  BuildRepositoryCLI

替换:
  BuildSnapshots SO → Repository 文件系统存储
  DifferentialProcessor.ConfirmRelease → repo.Commit()
  DifferentialProcessor.ReBuildSnapShots → repo.Add() + repo.Commit()
```

### Phase 2: AA Hotfix 重构（高风险）

```
拆分 DifferentialProcessor.PrepareHotfix:
  diff 部分 → repo.Diff(HEAD, current_scan)  // 纯信息
  group 移动 → LegacyAddressableBuildBackend.PrepareHotfix(delta)  // 副作用

拆分 DifferentialProcessor.RestoreOriginalGroups:
  → LegacyAddressableBuildBackend.RestoreGroups()  // 从内部 undo log 还原

ScanCurrentProjectAssets:
  → AddressableAssetScanner.Scan()  // 不再记录 Group 信息
  Group 信息由 Backend 自己维护（undo log）
```

### Phase 3: AB 管线接入（低风险）

```
ABBuildBackend.BuildAsync 完成后:
  repo.Add(new BundleOutputScanner(outputDir))
  repo.Diff(INDEX, HEAD) → 决定 push 哪些 bundles
  repo.Commit(metadata)
```

---

## 风险矩阵

| 风险 | 影响 | 缓解 |
|---|---|---|
| AA Scanner 去掉 Group 信息后，Backend 如何知道移动哪些 asset？ | 高 | Backend 从 ArtifactDelta 获取变更列表（Name=GUID），自己查 Addressable entry 获取当前 Group |
| BuildSnapshots SO 替换后，旧快照数据丢失 | 中 | 迁移工具：读旧 SO → 写入新 Repository 格式 |
| DeepHash 计算耗时（大项目扫描慢） | 低 | 已有实现，性能已验证 |
| OriginalGroupName 不再存储在快照中 | 高 | Backend 维护独立 undo log（Dict<GUID, OriginalGroup>），每次 PrepareHotfix 前记录 |

---

## 决策建议

| # | 决策点 | 建议 | 理由 |
|---|---|---|---|
| D1 | ArtifactDigest 粒度 | AA=asset 级, AB=bundle 级，Name 语义不同但结构统一 | 两条管线的 diff 对象本质不同，强制统一粒度会丢失信息 |
| D2 | AA Group 信息存储 | Backend 内部 undo log，不进 Repository | Group 是构建策略，不是版本信息 |
| D3 | 迁移策略 | 提供一次性迁移工具（旧 SO → 新格式），不做运行时兼容 | 个人项目，旧数据可废弃 |
| D4 | Scanner 触发时机 | AA: 构建前（决定构建范围）+ 构建后（记录结果）；AB: 仅构建后 | AA 需要 pre-build diff 驱动 Group 移动 |
| D5 | Repository 是否感知 backend 类型 | 不感知。Scanner 注入差异，Repository 只看 ArtifactDigest | 保持 Repository 的通用性 |

---

## 与 Build Repository Draft 的关系

本分析确认了 `draft-build-repository-20260518.md` 中的核心设计决策：
- IBuildRepository 单一实现 ✓
- IArtifactScanner 注入差异 ✓
- Group 移动在 Repository 外部 ✓
- ArtifactDigest 最小化（Name + Hash + Size）✓

新增补充：
- AA 需要 pre-build scan（构建前）+ post-build scan（构建后记录），AB 只需 post-build scan
- Backend 需要维护独立的 undo log 用于 Group 还原
- 迁移工具需求确认

---

## 命名统一决策

> Promotion note: the VersionState rename and HelperBuildData fusion direction has been promoted into executable master plan `../../archive/plan-aamanifest-helperbuilddata-20260518.md`. This draft remains the analysis trace.

### 已确认

| 当前名 | 新名 | 职责 | 状态 |
|---|---|---|---|
| `Manifest`（类名 + manifest.json） | `PackageIndex` | 下载入口定位（LatestPackage + LatestVersion） | 可执行 |
| `ABManifest` | 保持不变 | AB 完整资源清单（Asset + Bundle 映射） | 不动 |
| `VersionState`（类名 + version_state.json） | `AAManifest` (AAManifest.json) | AA 完整资源清单（Bundle 列表 + 资源索引），对标 ABManifest | 与 HelperBuildData 融合同步执行 |

### 命名歧义消除后的全景

```
运行时下载链路:
  PackageIndex.json (原 manifest.json)
    → 定位最新包名 + 版本号
    → 拼接 CDN URL

  AA: AAManifest.json (原 version_state.json)
    → Bundle 下载清单 (BundleName + Hash + CRC + Size)
    → 资源索引 (key/type/labels) — 原 AddressableLabelsConfig 融入
    → 配合 catalog.json 做 Addressables 资源定位

  AB: ABManifest.json/.bin (ABManifest)
    → 完整资源清单 (Asset→Bundle 映射 + 依赖 + Labels)
    → 同时承担下载清单 + 资源定位双职责
```

### 对称性

| | AA | AB |
|---|---|---|
| 清单类名 | `AAManifest` | `ABManifest` |
| 清单文件 | `AAManifest.json` | `ABManifest.json` |
| Bundle 条目 | `BundleInfo` | `ManifestBundleEntry` |
| 资源条目 | `PackageEntry` | `ManifestAssetEntry` |
| 下载入口 | `PackageIndex.json` | `PackageIndex.json` |

---

## Hash 统一决策

> Promotion note: the executable first-step plan for this section is `../../archive/plan-hash-unification-20260518.md`. The rest of this draft remains non-executable planning material.

### 使用场景分析

| 场景 | 需求 | 适用算法 |
|------|------|----------|
| 构建时 diff（是否重新构建） | 高精度内容标识 | MD5 ✓ |
| 运行时增量下载决策（本地 vs 远端） | 高精度内容标识 | MD5 ✓ |
| 下载后完整性校验 | 快速校验 | CRC32 ✓（~10x faster than MD5） |
| 运行时加载前损坏检测 | 快速校验 | CRC32 ✓ |

### 当前状态

| 管线 | MD5 | CRC32 | 问题 |
|------|-----|-------|------|
| AA（BundleInfo） | ✓ FileHash | ✗ 无 | 缺少快速校验能力 |
| AB（ManifestBundleEntry） | ✓ FileHash | ✓ FileCRC | 存了但未消费 |

### 决策：统一添加 CRC32（方案 A）

**结论**：两条管线统一为 MD5（内容标识）+ CRC32（快速校验）双 Hash 方案。

**改动范围**：

| 改动 | 位置 | 说明 |
|------|------|------|
| AA BundleInfo 加 `FileCRC` 字段 | `VersionState.cs` | uint，与 AB 对齐 |
| AA 构建时计算 CRC | `LegacyAddressableBuildBackend.OrganizeOutput` | `HashGenerator.GenerateFileCRC` |
| 运行时下载后 CRC 校验 | `HotfixManager`（AA/AB 共用） | 下载完成后快速校验 |
| 运行时加载前损坏检测 | `ABBundleLoader` / AA 等价路径 | 可选，性能敏感场景可跳过 |

**ArtifactDigest 更新**：

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

**职责分工**：
- `Hash`（MD5）：内容标识 → 用于 diff、增量下载决策、版本对比
- `CRC`（CRC32）：快速校验 → 用于下载后验证、运行时损坏检测
- 两者互补，不互相替代

**DeepHash 说明**：AA Scanner 内部对 SO/Prefab 使用 DeepHash（递归依赖 hash），对普通文件使用 FileHash。最终都存为 `ArtifactDigest.Hash` 字符串，Scanner 外部不感知差异。

---

## 路径管理精简决策

### 当前状态

| 类 | 职责 | 环境 | AA/AB |
|---|---|---|---|
| `PathManager` | 运行时设备端路径（persistentDataPath 下） | Runtime | 共用 ✓ |
| `BuildProjectManager.OutputRoot` | 构建侧根路径 + 包目录名计算 | Editor | 共用 ✓ |
| `BuildPathCustomizer` | AA 中间产物整理（ServerData → 最终目录） | Editor | AA 独有 |
| `TaskOrganizeOutput` | AB 中间产物整理（_temp → 版本目录） | Editor | AB 独有 |

**统一点**：
- 包目录名 `Build_{date}_{version}` 构建侧和运行时一致
- `bundles/` 子目录结构一致
- PathManager 已 AA/AB 共用（不区分管线）

### 决策

| # | 改动 | 理由 |
|---|------|------|
| P1 | 删除 `BuildPathCustomizer`，逻辑内联到 `LegacyAddressableBuildBackend.OrganizeOutput` | 与 ABBuildBackend.OrganizeOutput 对称，消除多余间接层 |
| P2 | 新建 `BuildPathManager`（Editor only），统一暴露 OutputRoot / PackagesDir / GetPackageDir / GetBundlesDir | 构建侧路径计算统一入口，IBuildBackend 不再需要外部传 outputDir |
| P3 | `PathManager` → 改名（候选：`RuntimePathManager` / `DevicePathManager`） | 消除与构建侧 BuildPathManager 的歧义 |

### 预期效果

- 净减少 ~70 行代码
- 构建侧路径有统一入口（BuildPathManager）
- 运行时路径有明确命名（不再与构建侧混淆）
- BuildPathCustomizer 删除后，AA/AB 的 OrganizeOutput 实现对称

### 执行时机

延后执行。可与 Build Repository 实现同期进行（Repository 需要知道存储路径，正好由 BuildPathManager 提供）。

---

## HelperBuildData 融合决策

### 背景

HelperBuildData 是 AA 管线的补丁机制 — Addressables 原生不提供的索引能力，通过 SO 打包进独立 bundle 来补充。AB 管线的 ABManifest 已内置等价能力，不需要 HelperBuildData。

### 当前 HelperBuildData 组成

| SO | 内容 | 运行时消费方 | 语义 |
|---|---|---|---|
| `AddressableLabelsConfig` | 所有 AA entry 的 key/type/labels 索引 | `AssetPackageManager`（Legacy 路径） | 资源索引（对标 ABManifest.AssetEntries） |
| `LuaScriptsIndex` | Lua 脚本名 → Container 地址映射 | `XLuaLoader` | Lua 路由表（与构建辅助无关） |

### 决策

| # | 改动 | 理由 |
|---|------|------|
| H1 | `AddressableLabelsConfig` 数据融入 `AAManifest.json` | 让 AA 管线也变为"一个清单文件自包含"，对标 ABManifest。消除 SO 依赖，简化热更链路 |
| H2 | `LuaScriptsIndex` 保留为独立 SO，且作为普通 Addressable asset 进入索引；不从 `AAManifest.AssetEntries` 过滤 | 语义是"Lua 路由表"，不是索引构建桥接对象；加载仍通过普通资源路径完成 |
| H3 | `HelperBuildData` 退场分阶段执行：AAM-2 先抽出索引构建职责，AAM-5 才删除 group/exporter/旧配置 | 避免在破坏性计划中同时移动 runtime source、asset placement 和 group 结构 |
| H4 | `HelperBuildDataExporter` 不再拥有索引构建逻辑；`AAAssetIndexBuilder` 是 `AAManifest` 与临时 `AddressableLabelsConfig` fallback 的唯一索引构建来源 | 防止 AAManifest 与旧 SO fallback 双源漂移 |

### AAM-2 落地约束

- Reuse existing `PackageEntry` directly. Do not rename it and do not introduce a bridge/wrapper DTO in this phase.
- Index construction is separate: `AAAssetIndexBuilder` builds `PackageEntry`, `TypeToKeys`, and `LabelToKeys` from `AddressableAssetSettings`.
- `HelperBuildData` is explicitly retiring. AAM-2 removes index-building ownership from `HelperBuildDataExporter`; removal of `AddressableLabelsConfig`, `LuaScriptsIndex` placement changes, and group deletion remain later approved sub-plans.
- Because the follow-up changes are destructive, implementation must stay aligned with the promoted plan and this draft trace. No unrelated cleanup, asset movement, runtime source switch, or Addressables group deletion should happen without a sub-plan approval.

### 融合后 AAManifest.json 结构

```json
{
  "Version": { "Major": 4, "Minor": 0, "Patch": 2 },
  "FileHash": "abc123...",
  "TotalSize": 12345678,
  "Bundles": [
    { "BundleName": "...", "FileHash": "...", "FileCRC": 0, "FileSize": 0 }
  ],
  "AssetEntries": [
    { "key": "UI/MainPanel", "Type": "Prefab", "Labels": ["UI", "Startup"] }
  ],
  "KeysByType": [
    { "Type": "Prefab", "Keys": ["UI/MainPanel", ...] }
  ],
  "KeysByLabel": [
    { "Label": "UI", "Keys": ["UI/MainPanel", ...] }
  ]
}
```

### 对标关系

| AA (AAManifest.json 融合后) | AB (ABManifest) |
|---|---|
| `Bundles` (BundleInfo 列表) | `BundleEntries` (ManifestBundleEntry 列表) |
| `AssetEntries` (PackageEntry 列表) | `AssetEntries` (ManifestAssetEntry 列表) |
| `KeysByType` / `KeysByLabel` | Labels 索引查询 API |
| `Version` | 外部管理 |
| `FileHash` + `TotalSize` | 运行时计算 |

### 改动范围

| 改动 | 位置 | 风险 |
|---|---|---|
| `AAManifest` 类加入索引字段 | `AAManifest.cs` | 低 — 新增字段，旧 JSON 反序列化缺字段时当前 runtime 仍不消费索引 |
| 构建时导出索引到 AAManifest | `LegacyAddressableBuildBackend.GeneratePackageManifest` | 中 — 需要读 AddressableAssetSettings |
| 运行时从 AAManifest 读索引 | `AssetPackageManager.cs`（Legacy 路径） | 高 — 加载时序变化，留到 AAM-3 |
| `XLuaLoader` 不受影响 | — | — LuaScriptsIndex 仍通过 AssetPackageManager 加载 |
| 删除 `HelperBuildDataExporter` 的 Labels 导出 | `HelperBuildDataExporter.cs` | 低 |
| LuaScriptsIndex 导出保留（移到独立工具或简化） | `HelperBuildDataExporter.cs` | 低 |
| 删除 HelperBuildData Group | Addressables 配置 | 低 — 确认无其他 SO 依赖 |

### 运行时加载时序变化

```
当前（AA）:
  HotfixManager 下载 AAManifest.json → 获取 bundle 列表 → 下载 bundles
  → Addressables 初始化 → 加载 AddressableLabelsConfig SO → 索引可用

融合后（AA）:
  HotfixManager 下载 AAManifest.json → 获取 bundle 列表 + 索引数据（同时可用）
  → 下载 bundles → Addressables 初始化（索引已提前可用）
```

融合后索引**更早可用**（不需要等 Addressables 初始化），是正向改进。

### 执行时机

延后执行。与 VersionState 改名讨论关联 — VersionState 改名为 `AAManifest`，融合后结构对标 ABManifest。

---

## Bundle 条目统一决策

### 分析

| 字段 | AA `BundleInfo` | AB `ManifestBundleEntry` | 原因 |
|---|---|---|---|
| BundleName + FileHash + FileCRC + FileSize | ✓ | ✓ | 下载清单必需 |
| Encrypted / BundleType / Tags / DependBundleIndices | ✗ | ✓ | AB 手搓体系需要自己管理依赖/加载/策略；AA 由 Addressables 自动管理 |

### 决策：保持独立

- `BundleInfo`：AA 的"下载清单条目"（只回答"下载什么"）
- `ManifestBundleEntry`：AB 的"运行时资源管理条目"（还回答"怎么加载"）
- `BundleDownloadItem`：热更统一消费视图（两条管线的 Backend 各自转换）

**不做基类抽取**。理由：
1. 语义不同（下载清单 vs 运行时管理）
2. 变化方向不同（AB 会继续扩展，AA 不会）
3. 节省代码量极少（~15 行）
4. BundleDownloadItem 已是统一消费层，耦合风险 > 收益

---

## 序列化格式统一决策

### 当前状态

| | AA | AB |
|---|---|---|
| 格式 | JSON only | JSON + Binary |
| 热更下载 | 下载 .json | 优先 .bin，回退 .json |

### 决策：AA 也支持 Binary

融合 HelperBuildData 后 AAManifest 体积增大（~30-50KB），支持 Binary 可减半。且 `[BinarySerializable]` + `SerializationUtility` 已有现成工具。

**改动**：
1. `AAManifest` 类（原 VersionState）加 `[BinarySerializable]` 标记 + `[BinaryField]` 注解
2. 构建时同时输出 `AAManifest.json` + `AAManifest.bin`
3. `LegacyHotfixBackend` 优先读 `.bin`，回退 `.json`（与 ABHotfixBackend 对称）

**对称性**：

| | AA | AB |
|---|---|---|
| 清单文件 | `AAManifest.json` / `.bin` | `ABManifest.json` / `.bin` |
| 优先格式 | Binary | Binary |
| 回退格式 | JSON | JSON |
| 序列化工具 | SerializationUtility | SerializationUtility |

---

## Change Log

| Date | Change |
|------|--------|
| 2026-05-18 | 初始草稿：系统分析 AA-AB 统一与差异，确认 Build Repository 设计决策 |
| 2026-05-18 | 补充命名统一决策：Manifest→PackageIndex 确认；VersionState→AAManifest 确认 |
| 2026-05-18 | 补充 Hash 统一决策：方案 A 确认（统一添加 CRC32），MD5=内容标识 + CRC32=快速校验 |
| 2026-05-18 | Hash 统一决策部分提升为正式小任务计划：`../../archive/plan-hash-unification-20260518.md` |
| 2026-05-18 | 补充路径管理精简决策：删 BuildPathCustomizer + 新建 BuildPathManager + PathManager 改名 |
| 2026-05-18 | 补充 HelperBuildData 融合决策：AddressableLabelsConfig 融入 AAManifest，LuaScriptsIndex 独立为普通 SO，删除 HelperBuildData Group |
| 2026-05-18 | 补充 Bundle 条目决策：保持独立（语义不同、变化方向不同），不做基类抽取 |
| 2026-05-18 | 补充序列化格式决策：AA 也支持 Binary（对标 AB），AAManifest.json + .bin 双输出 |
| 2026-05-18 | VersionState→AAManifest + HelperBuildData 融合方向提升为正式分步计划：`../../archive/plan-aamanifest-helperbuilddata-20260518.md` |
| 2026-05-19 | 归档：已提升的 Hash、AAManifest/HelperBuildData、path management 切片均进入正式 plan 或已执行 |
