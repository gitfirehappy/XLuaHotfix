# Sub-Plan E6: ABManifest Generation

> **Risk**: Low (ABManifest runtime structure already implemented in B6; E6 only adds build-time assembly logic)
> **Dependencies**: E5-1 (IBuildTask + BuildContext + BuildContextKeys), E5-2a (BundleBuildInfo from TaskBuildBundles), E4 (BundleDependencyGraph), B6 (ABManifest + ManifestAssetEntry + ManifestBundleEntry)
> **Status**: Realized — 2026-05-07, 5/5 tasks completed
> **Task**: TaskGenerateManifest

---

## Objective

Implement `TaskGenerateManifest`, the E5 backbone node 5 that consumes `CollectedAssets` + `BundleBuildResults` from BuildContext and produces a fully populated `ABManifest`. This is the bridge between the new build pipeline (E5) and the existing runtime data structures (B6).

The ABManifest runtime structure was implemented in B6 and requires NO structural modification beyond changing `BundleType` from `int` to `string` (see D7).

---

## Confirmed Design Decisions

### D1: CollectedAssetInfo → ManifestAssetEntry

| CollectedAssetInfo | ManifestAssetEntry | Note |
|---------------------|---------------------|------|
| AssetGUID | EntryId | 直接映射 |
| Address | Address | 空 Address 直接传递（ImplicitDependency 用 EntryId 查找） |
| PrimaryType | PrimaryType | 直接映射 |
| Labels | Labels | `new List<string>(a.Labels)` 浅拷贝隔离生命周期 |
| AssetPath | SourcePath | 直接映射 |
| GroupName | Group | 直接映射 |
| — | AutoAddress | V1 固定 true，后续编辑器阶段加验证开关 |
| BundleName → index | BundleIndex | 通过 `bundleNameToIndex` 字典解析 |

### D2: BundleBuildInfo → ManifestBundleEntry

| Source | ManifestBundleEntry | Note |
|--------|---------------------|------|
| BundleBuildInfo.OutputFileName | BundleName | 含 hash + .bundle 后缀 |
| BundleBuildInfo.Hash | FileHash | Unity BuildPipeline 产出 |
| CRC32(OutputFileName) | FileCRC | 构建期实时计算 |
| BundleBuildInfo.Size | FileSize | 直接映射 |
| false | Encrypted | V1 不加密 |
| 主导 PrimaryType 名 或 "Mixed" | BundleType | string 类型，>80% 阈值推断（见 D4） |
| BundleDependencyGraph 解析 | DependBundleIndices | Name→Index 字典查表（见 D5） |
| 资产 Labels 并集 | Tags | Bundle 内所有资产 Labels 去重（见 D6） |

### D3: FileCRC — 构建期 CRC32 静态查表

CRC32 多项式 0xEDB88320（IEEE 802.3 标准），静态初始化 256 条目查表。`CRC32Helper.Compute(string filePath)` 读取文件字节计算。构建期算一次写入 Manifest，运行时直接读取比对。

### D4: BundleType 推断 — >80% 阈值，直接用 PrimaryType 名

对每个 Bundle，统计包含资产中 PrimaryType 的频次分布。主导类型占比 >80% → BundleType = 该 PrimaryType 字符串名（如 "Texture2D"）。否则 → BundleType = "Mixed"。

不使用枚举，不维护映射表。新增资产类型自动支持，零维护成本。

### D5: DependBundleIndices — 从 BundleDependencyGraph 解析

```
对每个 ManifestBundleEntry:
  查 BundleDependencyGraph.Edges → 找 FromBundle == 此 Bundle 逻辑名的 outgoing edges
  对每个匹配边: 用 bundleNameToIndex 解析 ToBundle → int index
  写入 DependBundleIndices = int[]
```

### D6: Bundle 级 Tags — 资产 Labels 并集

`ManifestBundleEntry.Tags` = 该 Bundle 内所有 `ManifestAssetEntry.Labels` 的去重并集。含义是 Bundle 级下载策略标签（如 "startup"、"on-demand"），后续增量下载阶段按此过滤。

### D7: ManifestBundleEntry.BundleType 类型变更 — int → string

`ManifestBundleEntry.BundleType` 从 `int` 改为 `string`（默认值 `""`）。`EBundleType` 枚举删除。`ManifestBundleEntry_BinarySerializer.cs` 重生成。`ABManifestRoundTripTest.cs` 同步修改。

---

## TaskGenerateManifest Specification

```
ReadKeys:  CollectedAssets, BundleBuildResults, BundleDependencyGraph
WriteKeys: ABManifest
DependsOn: [TaskBuildBundles]

Execute(BuildContext ctx):
  ① require: CollectedAssets, BundleBuildResults, BuildVersion
  ② get(optional): BundleDependencyGraph
  
  ③ 构建 bundleNameToIndex 字典（BundleBuildInfo.BundleName → index）
  
  ④ 遍历 BundleBuildInfo → 创建 ManifestBundleEntry 列表
     基础字段: BundleName, FileHash, FileCRC, FileSize, Encrypted=false
     暂填空: BundleType, DependBundleIndices, Tags
  
  ⑤ 遍历 CollectedAssetInfo → 创建 ManifestAssetEntry 列表
     8 字段映射 + BundleIndex 解析
     BundleName 不匹配 → Error BUNDLE_NOT_FOUND
  
  ⑥ DependBundleIndices 解析
     for each bundleEntry:
       查 BundleDependencyGraph → outgoing edges
       bundleNameToIndex[ToBundle] → index[]
  
  ⑦ BundleType 推断
     按 BundleIndex 分组统计 PrimaryType
     >80% 同类型 → 赋该类型名；否则 "Mixed"
  
  ⑧ Tags 聚合
     按 BundleIndex 分组 → union of all asset Labels
  
  ⑨ 组装 ABManifest
     PackageName = "MainPackage" (V1)
     PackageVersion = ParseVersion(BuildVersion)
     BuildTimestamp = DateTime.UtcNow.ToString("o")
  
  ⑩ Initialize() 校验（异常 → Error MANIFEST_INIT_FAILED）
  
  ⑪ ctx.Set(ABManifest, manifest)
```

---

## New Files

| File | Path | Assembly | Lines (est.) | Description |
|------|------|----------|-------------|-------------|
| TaskGenerateManifest.cs | Build/Pipeline/Editor/Tasks/ | Editor | ~200 | IBuildTask: 映射 + BundleType推断 + DependBundleIndices解析 + Tags聚合 + ABManifest组装 |
| CRC32Helper.cs | Build/Pipeline/Editor/ | Editor | ~40 | 静态工具: Compute(string filePath) → uint，多项式 0xEDB88320 |

All paths relative to `Assets/FYAsset/Scripts/`.

---

## Modified Files

| File | Change | Risk |
|------|--------|------|
| ManifestBundleEntry.cs | `int BundleType` → `string BundleType = ""`；删除 `EBundleType` 枚举 | Medium — 序列化格式变更 |
| ManifestBundleEntry_BinarySerializer.cs | 重生成（适配 string 字段） | Low — 自动生成 |
| ABManifestRoundTripTest.cs | `(int)EBundleType.Xxx` → `"Xxx"` | Low |

---

## Task Breakdown

| Task | Content | Depends On |
|------|---------|-----------|
| E6-T0 | ManifestBundleEntry.BundleType int→string，删除 EBundleType 枚举 | — |
| E6-T1 | 重生成 ManifestBundleEntry_BinarySerializer.cs | T0 |
| E6-T2 | 修改 ABManifestRoundTripTest.cs 适配 string BundleType | T0 |
| E6-T3 | 创建 CRC32Helper.cs | — |
| E6-T4 | 创建 TaskGenerateManifest.cs — ⑤ ⑥ ⑦ ⑧ 数据映射 + BundleType推断 + DependBundleIndices + Tags聚合 | T3, E5-1 done, E5-2a done |
| E6-T5 | 编译验证 (dotnet build) | All above |

---

## Invariants

1. `TaskGenerateManifest` 正确实现 `IBuildTask`（ReadKeys/WriteKeys 与 E5 D7 一致）
2. 每个 `CollectedAssetInfo` 正确映射为一个 `ManifestAssetEntry`，BundleName 不匹配时报错
3. 每个 `BundleBuildInfo` 正确映射为一个 `ManifestBundleEntry`
4. `DependBundleIndices` 正确反映 BundleDependencyGraph 边关系
5. BundleType: >80% → PrimaryType 名；否则 → "Mixed"
6. FileCRC 从实际文件计算
7. `Initialize()` 无异常
8. `dotnet build` 0 errors

---

## Not In Scope

- ABManifest / ManifestAssetEntry / ManifestBundleEntry 数据结构（B6 已实现）
- 二进制序列化格式（B6 已实现）
- 运行时索引 / ABAssetIndex（B6/B7 已实现）
- V1 加密 / 压缩
- 手动 AutoAddress 覆写

---

## Approval Checklist

- [x] 同意 TaskGenerateManifest 作为单个 IBuildTask (D1)
- [x] 同意 CollectedAssetInfo→ManifestAssetEntry 8 字段映射 + BundleName→BundleIndex 字典查表
- [x] 同意 BundleBuildInfo→ManifestBundleEntry 映射 + CRC32 静态查表 (D2+D3)
- [x] 同意 BundleType 改为 string，>80% 阈值直接用 PrimaryType 名，删除 EBundleType 枚举和映射表 (D4+D7)
- [x] 同意 DependBundleIndices 从 BundleDependencyGraph 解析 (D5)
- [x] 同意 Bundle 级 Tags = 资产 Labels 去重并集 (D6)
- [x] 同意 AutoAddress = true V1 (D1)
- [x] 同意 2 新文件 + 3 修改文件 + 5 tasks
- [x] 同意 E6 不拆子计划；E5-2 拆为 E5-2a + E5-2b

---

## Change Log

| Date | Change |
|------|--------|
| 2026-04-26 | Initial draft |
| 2026-05-06 | Approved with refinements: BundleType int→string, 删除 EBundleType/映射表, E5-2 拆分为 2a+2b |
