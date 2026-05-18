# Draft: Diff Snapshot Adaptation — Git-like Artifact Store（E7）

> **Status**: ~~Draft~~ → **Archived 2026-05-18** — Superseded by `draft-build-repository-20260518.md`（统一 Build Repository 设计）
> **Design philosophy**: 显式采用 git 语义。E7 的 head/staged/history 本质是 HEAD/INDEX/objects——不再自己造词，对齐 git 命名体系。好处：自文档化、下游（BuilderPanel）消费无翻译成本、新成员零学习曲线
> **来源**: E7 plan review 打回后重新起草，吸收 review-E7-E13-plan-20260515 中 E7 相关 findings + E7 plan 8 个设计问题
> **E13 状态**: ✅ Executed（代码已落地，独立 plan-E13-legacy-sidebar.md，E7 不包含 E13 内容）
> **已吸收的 mistakes**: 见文末 Mistake Absorption Log
> **Dependencies**: E5-1 (IBuildTask + BuildContext + DAGScheduler), E5-2 (TaskBuildBundles), E6 (ABManifest)
> **与 E12 关系**: 独立草稿，与 E12（AB 管线编辑器工具）并行，但 CLI entry point 边界需对齐

---

## 第一部分：Diff Snapshot（原 E7 范围）

### Objective

将现有 `DifferentialProcessor`（与 Addressables asset groups 紧耦合）迁移到 Interface-Backend 分离架构。Diff 是 **build-time-only** 关注点——Runtime 不消费 diff 结果，runtime hotfix 通过 ABManifest 全量比对（local vs remote）跳过匹配 bundle、下载剩余。

### 已吸收的设计错误（8 项 → 修正）

| # | 原错误 | 根因 | 修正 |
|---|--------|------|------|
| 1 | DiffResult 多态不自洽 — 接口返回 DiffResult 但两个 backend 产出不同类型 delta | 强行统一不同类型的输出 | **不再定义统一 DiffResult**。每个 backend 的 PrepareDiff 产出写入 BuildContext，类型由 backend 自定。ApplyDiff 不作为独立接口方法——backend 内部消费自己的 delta |
| 2 | BundleDigest/BundleDigestList 放 Runtime assembly | 过度预判未来 runtime 用途 | **放 Editor assembly**。当前只有构建期使用。runtime 需要时再移 |
| 3 | 文件路径放 Collector/ | Diff 是 Pipeline 关注点，不是 Collector | **路径改为** `Build/Pipeline/Editor/Diff/` |
| 4 | Hash 算法指定 MD5 | 项目已有 HashGenerator + HashAlgorithmType 统一基础设施 | **使用 HashGenerator**（项目现有），不硬编码 MD5 |
| 5 | D5d channel 参数过度设计 | 为未实现功能污染接口签名 | **移除 channel 参数**。待多渠道需求实际出现时再加 |
| 6 | MaxHistoryVersions 放 BuildPipelineConfig | 执行配置与快照管理配置混在一起 | **放 IDiffPipeline 实现的常量默认值**，或独立的 DiffSettings SO（待讨论） |
| 7 | E7-T9 可选 ReadKeys | DAGScheduler 是否支持可选读取未确认 | **先确认 DAGScheduler contract**，再设计依赖 optional key 的 task |
| 8 | E7-T11 与 E12-2 边界模糊 | CLI vs Editor 入口未澄清 | **明确边界**：CLI entry（BuildCommandLine）归 E7，Editor UI entry 归 E12-2 |

### 已吸收的 review findings（P0-1, P1-2）

| Finding | 修正 |
|---------|------|
| P0-1: head.bin vs head.json 矛盾 | **统一使用 `HEAD`**（git 语义，无扩展名）。所有引用使用同一文件名 |
| P1-2: channel 参数未贯穿 interface | **移除 channel**（与 #5 一致） |

### Architecture: Interface-Backend Separation

```
IDiffPipeline (interface)
  ├── LegacyDiffBackend  — wraps DifferentialProcessor, Asset-level diff, BuildSnapshots SO
  └── ABDiffBackend      — Bundle-level diff, BundleDigestList + .bin/.json
```

BackendMode（SO default + build-parameter override）选择 backend。构建中途不切换。

### Git 语义映射

E7 的存储和操作模型与 git 完全同构。显式采用 git 术语消除概念翻译成本：

| E7 操作 | Git 等价 | 说明 |
|---------|---------|------|
| `Snapshot(ctx)` | `git add` + write tree | 全量构建：扫描产物 → 写 BundleDigestList 到 INDEX |
| `Diff(ctx)` | `git diff --stat` | 热更：对比 INDEX vs HEAD → 产出 BundleDelta 写入 BuildContext |
| `Commit()` | `git commit` | INDEX → objects/{version}.bin，更新 HEAD |
| `Reset()` | `git reset --soft HEAD~1` | 丢弃 INDEX，HEAD 不变 |
| `DiffVersions(v1, v2)` | `git diff v1 v2` | 任意两个版本间的 Bundle 差异查询（供 BuilderPanel 消费） |

### IDiffPipeline Interface（git 语义版）

```csharp
public interface IDiffPipeline
{
    void Snapshot(BuildContext ctx);     // Full build: scan built bundles → BundleDigestList → write to INDEX
    void Diff(BuildContext ctx);         // Hotfix: compare current vs HEAD → BundleDelta → write to BuildContext
    void Commit();                       // INDEX → objects/{version}.bin, update HEAD
    void Reset();                        // Discard INDEX, HEAD unchanged
}
```

**变化**: 5 方法 → 4 方法。ApplyDiff 移除——Diff() 直接将 delta 写入 BuildContext，消费方（TaskBuildBundles）从 BuildContext 读取。命名对齐 git：Snapshot/Diff/Commit/Reset。

**Delta 类型不统一**: LegacyDiffBackend.Diff() 写 AssetDelta 到 BuildContext；ABDiffBackend.Diff() 写 BundleDelta 到 BuildContext。消费者按 BackendMode 读取对应 key。不强求多态统一。

### Diff Granularity

| Backend | Data Source | Delta Type | BuildContext Key |
|---------|------------|------------|-----------------|
| LegacyDiffBackend | AssetSnapshot[] (per-asset hash) | AssetDelta | `LegacyAssetDelta` |
| ABDiffBackend | BundleDigestList (per-bundle hash) | BundleDelta | `BundleDelta` |

### Persistence

| Backend | Snapshot Format | Storage |
|---------|----------------|---------|
| LegacyDiffBackend | BuildSnapshots (SO) | `Assets/Build/Snapshots.asset` — unchanged |
| ABDiffBackend | BundleDigestList (.bin/.json) | `BuildData/Snapshots/` — file system（git-like 布局） |

Legacy SO 不迁移（legacy backend 已 parked）。

### Storage Layout（git 语义版）

对齐 `.git` 目录结构：

```
BuildData/Snapshots/          ← .git/
  ├── HEAD                    ← ref: v4.0.2（当前版本指针，含 Staged 信息）
  ├── INDEX                   ← 暂存区（pending snapshot，git index 等价物）
  └── objects/                ← 历史快照（git objects 等价物）
        ├── v4.0.0.bin
        ├── v4.0.1.bin
        └── v4.0.2.bin
```

**HEAD**（JSON 格式，原子写入）:
```json
{
  "Head": "v4.0.2",
  "Staged": "v4.0.3"
}
```
- `Head`: 当前已提交版本（`git rev-parse HEAD`）
- `Staged`: 待提交版本，null 表示无暂存（`git diff --cached` 为空）

**INDEX**（二进制，BundleDigestList 序列化）: 下一次 Commit 的内容。全量构建后写入，Commit 后清除（移至 objects/），Reset 后删除。

**objects/{version}.bin**: 已提交的不可变快照。按 VersionNumber 命名（非 SHA——构建场景下版本号比内容哈希更有语义价值）。

### Data Structures（修正：Editor assembly）

```csharp
// BundleDigest.cs — Editor assembly, Build/Pipeline/Editor/Diff/
[BinarySerializable]
public class BundleDigest
{
    [BinaryField(0)] public string BundleName;
    [BinaryField(1)] public string Hash;       // via HashGenerator, not hardcoded MD5
    [BinaryField(2)] public long Size;
}

// BundleDigestList.cs — Editor assembly, Build/Pipeline/Editor/Diff/
[BinarySerializable(Magic = 0x42444C53)] // 'BDLS'
public class BundleDigestList
{
    [BinaryField(0)] public VersionNumber Version;
    [BinaryField(1)] public string Timestamp;
    [BinaryField(2)] public List<BundleDigest> Digests;
}

// BundleDelta.cs — Editor assembly, Build/Pipeline/Editor/Diff/
public class BundleDelta
{
    public List<BundleDigest> AddedBundles;
    public List<BundleDigest> ModifiedBundles;
    public List<string> RemovedBundles;
}
```

### Commit / Reset Flow

**Commit**（`git commit`）:
1. Read HEAD → get Staged version
2. Guard: if `objects/{version}.bin` exists → error（不静默覆盖）
3. Atomic rename: `INDEX` → `objects/{version}.bin`
4. Atomic write HEAD: `Head = version, Staged = null`
5. Best-effort GC（retain most recent N, N 来自 FYAssetSettings 或独立 DiffSettings SO）

**Reset**（`git reset --soft HEAD~1`）:
1. Read HEAD
2. Delete INDEX
3. Atomic write HEAD: `Staged = null`

**Atomic write**: 使用 `FileHelper.WriteAllBytesAtomic`（write .tmp → rename）。

### E5 DAG Integration

```
Full Build:
  ... → TaskBuildBundles → TaskSnapshot → TaskGenerateManifest → ...

Hotfix Build:
  ... → TaskDiff → TaskBuildBundles (reads BundleDelta from BuildContext, rebuilds changed only) → ...
```

TaskSnapshot / TaskDiff 是 backbone nodes。

### Diff API（供 BuilderPanel / CLI 消费）

```csharp
// ABDiffBackend 公开查询方法
BundleDelta DiffVersions(VersionNumber from, VersionNumber to);  // git diff v1 v2
BundleDigestList GetSnapshot(VersionNumber version);             // git show v1
List<VersionNumber> ListVersions();                               // git log --oneline
VersionInfo GetVersionInfo(VersionNumber version);                // git log -1 v1
```

- `DiffVersions`: 从 `objects/{from}.bin` 和 `objects/{to}.bin` 加载两个 BundleDigestList，逐项比对 → BundleDelta
- `ListVersions`: 扫描 `objects/` 目录 + 文件名解析，按 VersionNumber 降序
- `GetVersionInfo`: 加载单个 .bin 文件，提取元数据（Bundle 数量、总大小、时间戳）
- 这些是**查询 API**（不修改状态），IDiffPipeline 接口不强制——ABDiffBackend 公开方法，供 UI plan 调用

### Resolved Questions（已确认）

| # | 问题 | 结论 | 依据 |
|---|------|------|------|
| Q1 | Hash 算法选择 | **使用 HashGenerator**（项目统一基础设施，HashAlgorithmType 枚举），后续统一对齐 ManifestBundleEntry | 开发者确认 |
| Q2 | Optional ReadKeys | **DAGScheduler 支持，但语义是 Warning 而非 Error**。源码 `DAGScheduler.cs:159-181`：Read-before-Write 产生 `UNSATISFIED_READ_KEY` Warning（非阻塞）。`BuildContext` 提供 `Has(key)` + `Get<T>(default)` + `Require<T>(throw)` 三级 API。TaskBuildBundles 声明 `BundleDelta` 为 ReadKey，Execute 内部 `ctx.Has("BundleDelta")` 判断增量/全量 | 源码审计：`DAGScheduler.cs` + `IBuildTask.cs` + `BuildContext.cs` |
| Q3 | MaxHistoryVersions 归属 | **FYAssetSettings SO 或独立 DiffSettings SO**（待定二选一）。用于 GC 策略：超过 N 个 objects/ 时删除最旧 | 开发者确认 |
| Q6 | git 语义对齐 | **E7 显式采用 git 语义**。命名：HEAD/INDEX/objects/ + Snapshot/Diff/Commit/Reset/DiffVersions。git 与 E7 模型同构 | 开发者确认（方案 A） |

### Open Questions（待讨论）

*全部 5 个问题已解决。*

| # | 问题 | 结论 |
|---|------|------|
| Q4 | Diff 历史可视化 | **不在 E7 范围**。后续独立 plan（利用 E7 提供的 API: ListSnapshots / GetSnapshot） |
| Q5 | changelog.jsonl | **移除**。不搞 git-like 审计设计融合。操作审计不属于 Diff 系统核心关注点 |

---

## E7 与上下游边界

| 事项 | 归属 |
|------|------|
| BuildCommandLine DAG entry point | E7 |
| PipelinePanel 构建触发按钮 | E12-2 |
| BuildProjectManager → IDiffPipeline 路由 | E7 |
| PipelinePanel DAG 可视化 | E12 |
| LegacyReportPanel Diff 功能 | E7 完成后独立 plan（E13 已提供占位面板） |

---

## Mistake Absorption Log

从 `plan-E7.md`（打回）和 `review-E7-E13-plan-20260515.md`（已归档）吸收的 E7 相关问题（E13 相关 M11/M12 已在 plan-E13-legacy-sidebar.md 执行中处理）：

| # | 来源 | 严重度 | 问题 | 本草案修正 |
|---|------|--------|------|-----------|
| M1 | E7 review | P0 | DiffResult 多态不自洽 | 移除 ApplyDiff，backend 自产自消 delta |
| M2 | E7 review | P0 | 数据结构放错 assembly | BundleDigest* → Editor assembly |
| M3 | E7 review | P1 | 文件路径错误 | Diff 文件 → Pipeline/Editor/Diff/ |
| M4 | E7 review | P1 | Hash 算法未对齐 | 使用 HashGenerator |
| M5 | E7 review | P1 | channel 过度设计 | 移除 |
| M6 | E7 review | P1 | MaxHistoryVersions 归属不当 | 常量默认值，待讨论 |
| M7 | E7 review | P1 | ReadKeys contract 未确认 | 标记为 open question |
| M8 | E7 review | P1 | E7-T11 / E12-2 边界模糊 | 明确 CLI/Editor 边界 |
| M9 | combined review | P0 | head.bin vs head.json 文件名矛盾 | 统一为 HEAD（git 语义），存储布局全面 git 化 |
| M10 | combined review | P1 | channel 未贯穿 interface | 移除（与 M5 一致） |

---

## Change Log

| Date | Change |
|------|--------|
| 2026-05-17 | 初始草案：吸收 E7 10 项 review findings（M1-M10）。E7 5→4 方法接口，BundleDigest→Editor assembly，路径→Pipeline/Editor/Diff/，Hash→HashGenerator，移除 channel，统一 head.json，移除 changelog.jsonl。5 个 open questions |
| 2026-05-17 | 5 个 open questions 全部解决：Q1 HashGenerator 统一→确认；Q2 optional ReadKeys→源码审计 DAGScheduler（Warning 语义+BuildContext.Has 三级 API）→确认；Q3 MaxHistoryVersions→FYAssetSettings 或独立 SO→确认；Q4 Diff 可视化→不在 E7 范围，后续独立 plan；Q5 changelog.jsonl→移除 |
| 2026-05-17 | Q6 git 语义对齐→方案 A 确认。全草稿 git 化：head.json→HEAD, staged.bin→INDEX, history/→objects/, ConfirmRelease→Commit, RollbackHotfix→Reset, GenerateSnapshot→Snapshot, PrepareDiff→Diff。新增 Diff API（DiffVersions/GetSnapshot/ListVersions/GetVersionInfo）供 BuilderPanel 消费 |
