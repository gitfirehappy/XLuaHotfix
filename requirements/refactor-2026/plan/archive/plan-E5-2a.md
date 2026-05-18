# Sub-Plan E5-2a: Backbone Tasks (Phase 1 — 不依赖 E6)

> **Risk**: Medium
> **Dependencies**: E5-1 (IBuildTask + BuildContext + DAGScheduler), E1-3 (CollectionScanner), E4 (DependencyAnalyzer)
> **Parent**: [plan-E5.md](plan-E5.md)
> **Status**: Realized — 2026-05-06, BundleBuildInfo + TaskPrepareContext + TaskCollectBuiltins + TaskBuildBundles + BundleCompression landed. Review fixes 2026-05-07 (scene output collapse + folder guard + rawfile multi-file)
> **Status**: Approved — 2026-05-06, 4/4 checklist items confirmed with refinements
> **Refinements**: TaskCollectBuiltins→extensible Categories array (Shader+Resources+Builtin, $shared GroupName + per-category PackKey); TaskBuildBundles→BundleCompression from BuildPipelineConfig SO (LZ4/LZMA/Uncompressed)
> **Blocks**: E6 (TaskGenerateManifest needs BundleBuildInfo from TaskBuildBundles)

---

## Objective

Implement 3 backbone pipeline Tasks + 1 data contract that do NOT depend on E6:
**TaskPrepareContext** (init build environment), **TaskCollectBuiltins** (auto-collect Shaders), **TaskBuildBundles** (group by BundleName + build AssetBundles). Also define **BundleBuildInfo** as the data contract for E5-2a output → E6 input.

The other 2 backbone Tasks (TaskVerifyBuildResult, TaskOrganizeOutput) depend on E6 and are deferred to **E5-2b**.

---

## Confirmed Design Decisions

### D8: BundleBuildInfo — TaskBuildBundles Output

```csharp
public class BundleBuildInfo
{
    public string BundleName;       // 逻辑名（不含 hash/后缀）
    public string OutputFileName;   // 实际文件名（含 hash + .bundle）
    public string Hash;             // Unity BuildPipeline 产出
    public long Size;               // 文件大小（字节）
    public List<string> AssetPaths; // 此 Bundle 包含的资产路径
    public EPayloadKind PayloadKind; // 主导载荷类型
}
```

---

## Task Specifications

### TaskPrepareContext (管线起点)

```
ReadKeys:  —
WriteKeys: BackendMode, BuildVersion, OutputRoot, TargetPlatform
DependsOn: —

Logic:
  1. 读 BuildPipelineConfig SO → DefaultBackendMode
  2. 检查命令行 --backend 覆盖（"LegacyAddressable" | "ABManifest"）
  3. BackendMode 写入 BuildContext → 锁定
  4. 解析 BuildVersion: CLI --version > yyyyMMdd-HHmmss 时间戳
  5. 解析 TargetPlatform: CLI --platform > EditorUserBuildSettings.activeBuildTarget
  6. 解析 OutputRoot: CLI --output > {dataPath}/../Build/{platform}/
  7. 全部写入 BuildContext
```

### TaskCollectBuiltins (Shader 自动收集)

```
ReadKeys:  CollectedAssets
WriteKeys: CollectedAssets (augmented)
DependsOn: [TaskCollectAssets]
在 TaskAnalyzeDependencies 之前执行

Logic:
  1. AssetDatabase.FindAssets("t:Shader")
  2. 对每个不在 CollectedAssets 中的 Shader:
     - CollectorType = Implicit
     - GroupName = "$shared"
     - PackKey = "shaders"
     - BundleName = BundleNameBuilder.Build(pkg, "$shared", "shaders")
     - Address = AssetAddressGenerator.GenerateShortAddress(...)
  3. 追加到 CollectedAssets
```

### TaskBuildBundles (AssetBundle 构建)

```
ReadKeys:  CollectedAssets, BundleDependencyGraph, OutputRoot, BackendMode
WriteKeys: BundleBuildResults
DependsOn: [TaskAnalyzeDependencies]

Logic:
  1. 按 BundleName 分组 CollectedAssetInfo
  2. 每组按 EPayloadKind 分流:
     - Serialized → AssetBundleBuild 条目
     - Scene → 独立 Scene AssetBundleBuild 条目
     - RawFile → 直接拷贝文件，记录 BundleBuildInfo
  3. Serialized + Scene 组: BuildPipeline.BuildAssetBundles(outputDir, builds, options, targetPlatform)
  4. 每个构建产物收集: BundleName, OutputFileName, Hash, Size, AssetPaths, PayloadKind
  5. 写入 List<BundleBuildInfo> → BuildContext
```

---

## New Files

| File | Path | Assembly | Lines (est.) |
|------|------|----------|-------------|
| BundleBuildInfo.cs | Build/Pipeline/Editor/ | Editor | ~25 |
| TaskPrepareContext.cs | Build/Pipeline/Editor/Tasks/ | Editor | ~80 |
| TaskCollectBuiltins.cs | Build/Pipeline/Editor/Tasks/ | Editor | ~50 |
| TaskBuildBundles.cs | Build/Pipeline/Editor/Tasks/ | Editor | ~180 |

All paths relative to `Assets/FYAsset/Scripts/`.

---

## Task Breakdown

| Task | Content | Depends On |
|------|---------|-----------|
| E5-2a-T1 | 创建 BundleBuildInfo.cs | — |
| E5-2a-T2 | 创建 TaskPrepareContext.cs | E5-1 done |
| E5-2a-T3 | 创建 TaskCollectBuiltins.cs | E5-1 done, E1-3 done |
| E5-2a-T4 | 创建 TaskBuildBundles.cs | E5-1 done, E1-3 done, E4 done, T1 |
| E5-2a-T5 | 编译验证 (dotnet build) | All above |

---

## Invariants

1. 3 个 Task 正确实现 IBuildTask
2. TaskPrepareContext 写入 4 个 Key 到 BuildContext
3. TaskCollectBuiltins 发现所有 Shader 并追加到 CollectedAssets
4. TaskBuildBundles 按 BundleName 分组并调用 BuildPipeline.BuildAssetBundles
5. Serialized/Scene/RawFile 三种载荷分别走正确的构建路径
6. BundleBuildInfo 包含 E6 需要的全部字段
7. `dotnet build` 0 errors

---

## Not In Scope

- TaskVerifyBuildResult / TaskOrganizeOutput (E5-2b)
- TaskGenerateManifest (E6)
- Builder panel UI / build trigger button
- 增量构建

---

## Modified Files (from other plans)

| File | Change | Risk |
|------|--------|------|
| BuildPipelineConfig.cs | 新增 `BundleCompression` 枚举 + `Compression` 字段 | Low — additive |

---

## Approval Checklist

- [x] 同意 BundleBuildInfo 6 字段结构
- [x] 同意 TaskPrepareContext CLI > SO > 默认值 三级优先级
- [x] 同意 TaskCollectBuiltins 可扩展 Categories 数组（Shader + Resources + Builtin），$shared GroupName + 每类独立 PackKey
- [x] 同意 TaskBuildBundles 三路 PayloadKind 分流 + LZ4 默认压缩 + BuildPipelineConfig 可配置

---

## Change Log

| Date | Change |
|------|--------|
| 2026-05-06 | Split from original plan-E5-2; scope reduced to 3 tasks + BundleBuildInfo (no E6 dependency) |
| 2026-05-06 | Approved with refinements: extensible Categories, BundleCompression configurable |
