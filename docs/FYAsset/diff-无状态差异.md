# 无状态差异

> 返回总览：[资源管理架构文档](./资源管理架构文档.md)

> **关联代码**
>
> `Assets/FYAsset/Scripts/Shared/Build/FileDigest.cs` · `FileDiff.cs` · `Shared/Build/Editor/CompleteBuildSummary.cs`（`SummaryContentFact`） · `Shared/Build/Editor/Summary/BuildSummaryStore.cs` · `Shared/Build/Editor/Summary/BuildArtifactReuseService.cs` · `Shared/Build/Editor/Summary/HotfixBaselineResolver.cs` · `Shared/Build/Publish/PublishCache.cs`

---

## 定位

差异比较是四段管线中的第二段，由两个纯数据结构承担：

```text
FileDigest  一份物理文件的摘要：Name + Hash(MD5) + CRC32 + Size
FileDiff    两份摘要集合的比较结果：Added / Modified / Unchanged / Removed
```

两者只依赖 `System` 与 `HashGenerator`，因此构建复用、发布流程和纯 .NET 场景都能直接引用。差异**无状态**：

1. 只输入两份摘要或文件集合，不读 Git、PackageIndex、版本库、缓存或任何历史状态；
2. 不持有生命周期、不产生副作用，调用方决定结果如何被消费；
3. `Name` 按 `Ordinal` 比较（大小写敏感）：包内文件名由构建侧统一产生，不做大小写折叠，避免在区分大小写的平台把两个不同文件当成同一个；
4. 同一次比较中忽略重复名称（保留首个），因为包内同名文件本身已经是错误状态。

本次比对的“上一份事实”永远来自可读文件：正式 Summary 里的内容复用记录、基准 Full 包内的 Manifest（AB）或 `AASourceScan.json`（AA）、或服务器上 `PackageIndex` 指向的 Manifest。

---

## FileDigest

```csharp
public readonly struct FileDigest
{
    public readonly string Name;   // 所属集合内的名称，不含目录
    public readonly string Hash;   // 内容 MD5，小写十六进制
    public readonly uint CRC;      // 内容 CRC32
    public readonly long Size;     // 字节数
}
```

- `TryCreate(filePath, name, out digest)` 一次流式遍历读取文件并计算摘要；文件缺失、被截断或不可读时返回 false，**不抛异常**（IO、权限、并发删除一律表达为“摘要不可用”，由调用方退化处理）。
- `IsComplete` 要求 Name 与 Hash 非空且 Size ≥ 0；不完整摘要不参与任何比较与复用。
- `Matches(other)` 要求四个字段全部一致才算同一份物理文件。
- 字段不含绝对路径与时间戳，因此可以跨 attempt 目录、跨构建、跨机器比较。

---

## FileDiff

```csharp
FileDiff diff = FileDiff.Compute(previous, current);
```

| 结果 | 含义 |
|---|---|
| `Added` | 名称只出现在 current |
| `Modified` | 同名但 Hash/CRC/Size 任一不同（保存 current 一侧摘要） |
| `Unchanged` | 同名且三个内容字段完全一致（保存 current 一侧摘要） |
| `Removed` | 名称只出现在 previous（只保存名称） |

- `previous` 为 null 按“没有任何历史文件”处理，current 全部记为 Added；`current` 为 null 按“目标集合为空”处理，previous 全部记为 Removed。
- `Hash/CRC/Size` 任一不同即视为内容变化，避免把“大小相同但内容不同”当成未变化。
- 辅助索引：`IndexByName`（按名称，重复保留首个）、`IndexByHash`（键为 `HashKey(hash, size)`，用于“Hash 命中即可复用服务器内容”）、`CollectChangedNames()`（Added + Modified 名称集合）。

---

## 三处事实的职责分离

三者复用 `FileDigest`，但存储位置、生命周期与是否影响正确性完全不同：

| 机制 | 位置 | 写入时机 | 失败后果 |
|---|---|---|---|
| 正式构建摘要 | `BuildData/Summaries/{AA\|AB}/{BuildId}.json`（不可变，`index.json` 是可重建的定位索引） | 交付事务中产物提升并应用启动数据之后、Index 之前 | 写入失败则整个交付回滚；历史摘要缺失只损失复用候选与版本定位加速 |
| 内容复用事实 | 摘要内 `SummaryContentFact`（内容身份 + 输入指纹 + 制品摘要 + 依赖事实）＋历史 `Build_*` 包目录的物理字节 | 交付成功并写出正式摘要之后才可被后续构建读到 | 没有候选或事实不完整 → 该内容重建，只损失优化 |
| 发布缓存 | `BuildData/PublishCache/{AA\|AB}/{TargetId}.json` | 发布成功后写入 | 读取失败按“没有缓存”处理，只提示与服务器事实的漂移，**不改变发布决定** |

复用候选由 `BuildArtifactReuseService` 判定，全部条件同时满足才命中：

```text
后端相同 · 平台逐字符相同 · 构建配方指纹逐字符相同
内容身份（ContentIdentity）相同 · 输入指纹（InputFingerprint）相同
摘要记录完整（FileHash / FileCRC / FileSize）· 历史包目录存在且制品可读
复制到本次产物目录后重新计算的 Hash / CRC / Size 与记录一致
依赖事实存在（DependencyFileNames != null）
```

历史包目录只是字节来源，身份与事实一律取自正式 Summary；任一条件不满足即退化为重建，不阻断构建。

---

## 三处差异比较的实际用法

| 用途 | 上一份事实 | 本次事实 | 消费方 |
|---|---|---|---|
| AB 内容复用 | 历史正式 Summary 的 `SummaryContentFact`（`InputFingerprint` + `FileHash/CRC/Size`） | 本次输入指纹与产物 | `BuildABContentTask` + `BuildArtifactReuseService` |
| AB Hotfix 交付差异 | 基准 Full 包内 Manifest 声明的内容 | 本次 `ABManifest` 的内容集合 | `ExportABOutputTask` + `HotfixBaselineResolver` |
| AA Hotfix 源差异 | 基准 Full 包内 `AASourceScan.json`（GUID → Asset+meta 摘要） | 本次 Addressables source 摘要 | `PrepareAAInputTask` + `HotfixBaselineResolver`（决定哪些 entry 临时移入 `HotfixGroup`） |
| 发布上传集合 | 服务器 `PackageIndex` → 其指向包的 Manifest | 本地包目录扫描结果 | `PackagePublishTransaction` |

AA 源快照文件 `AASourceScan.json` 写在包根并随包交付：Full 与 Hotfix 都携带，下一次 Hotfix 从**基准 Full 包目录**里读它（`HotfixBaselineResolver` 按 Summary Index 作用域定位），不在任何共享目录里累积。

---

## Hotfix 基准 Full 与差异交付

Hotfix 不再有“固定累计目录”这种跨构建事实载体，基准由构建事实决定：`HotfixBaselineResolver` 从 `BuildData/Summaries/index.json` 的作用域（后端、平台、通道）取 `LatestFullSummaryId`，再按该摘要的 `ArtifactRelativePath` 定位基准 Full 包目录。

- 基准缺失、摘要不可读或基准包目录不存在时构建中止，而不是把“没有基准”当成“没有变化”。
- AB：与基准 Full 包内 Manifest 声明的内容做 `FileDiff`，Added/Modified 的内容由本次构建提供；包内只放变化内容，**完整目标 Manifest 始终随包交付**，未变化内容由客户端从当前包复用。
- AA：与基准 Full 包内 `AASourceScan.json` 做 `FileDiff`，据此决定哪些 entry 临时移入 `HotfixGroup`；当前交付这一轮 Addressables 构建的产出内容（相对 Full 裁剪属延期矩阵）。
- 每个包都交付到按包名隔离的独立目录（`Packages/Build_*`，Standalone 为 `StreamingAssets/Standalone`），不存在需要 Full 重置的“内容清零”目录，也不存在热更包之间的累计继承。

---

## 已删除的差异机制

| 已删除 | 现状 |
|---|---|
| `ArtifactDelta` / `ArtifactDiffer` / `BuildDiffEntry`（`Shared/Build/Snapshots/`） | 整个目录删除，能力由 `FileDigest` + `FileDiff` 承担 |
| `BuildData/Snapshots/` 与基于 Commit 链的差异快照 | 不再存在 |
| 基线（`BuildData/Baselines/**/baseline.json`）与 `Latest` / `LatestFull` 双槽 | 不再存在；上一份事实改为可读文件（正式 Summary 复用记录、基准 Full 包内 Manifest / `AASourceScan.json`、服务器 Manifest） |
| Diff Preview 页面与 `BuildPreviewRunner` | 已删除 |

历史归档（`requirements/**`）中出现的旧名称只是当时的记录，不代表现行机制。
