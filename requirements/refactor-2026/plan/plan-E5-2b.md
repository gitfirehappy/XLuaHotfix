# Sub-Plan E5-2b: Backbone Tasks (Phase 2 — 依赖 E6)

> **Risk**: Low
> **Dependencies**: E5-2a (BundleBuildInfo), E6 (ABManifest from TaskGenerateManifest)
> **Parent**: [plan-E5.md](plan-E5.md)
> **Status**: Realized — 2026-05-07, 4/4 tasks completed

---

## Objective

Implement 2 backbone pipeline Tasks that depend on E6's `ABManifest` output:
**TaskVerifyBuildResult** (6 项输出完整性校验), **TaskOrganizeOutput** (拷贝 + 序列化 + 摘要 + 清理).

---

## Task Specifications

### TaskVerifyBuildResult

```
ReadKeys:  ABManifest, BundleBuildResults, OutputRoot
WriteKeys: BuildVerificationResult
DependsOn: [TaskGenerateManifest]
在 TaskOrganizeOutput 之前执行

Logic (6 项检查):
  1. FILE EXISTENCE (Error): ABManifest 中每个 BundleEntry 对应的 .bundle 文件存在
  2. FILE INTEGRITY (Error): 每个 .bundle 文件大小 > 0，Unity header 可读
  3. ORPHAN CHECK (Warning): 输出目录中每个 .bundle → 有对应 Manifest BundleEntry
  4. HASH RE-VERIFY (Error): 用 HashGenerator.GenerateFileHash 重新计算 → 与 ManifestBundleEntry.FileHash 比对
  5. SIZE ANOMALY (Warning): 文件 < 1KB 或 > 500MB（默认可配置）
  6. COUNT CROSS-CHECK (Error): 输出 bundle 数量 == BundleBuildInfo 数量 == BundleEntries 数量

  Error → 构建中止；Warning → 继续执行，列在摘要中

  输出 BuildVerificationResult 写入 Context:
    bool Success / List<VerificationIssue> Issues
    VerificationIssue { CheckName, IssueLevel(Error/Warning), BundleName, Message }
```

### TaskOrganizeOutput

```
ReadKeys:  ABManifest, BundleBuildResults, OutputRoot, BuildVersion
WriteKeys: OutputPath
DependsOn: [TaskVerifyBuildResult]

### TaskOrganizeOutput

```
ReadKeys:  ABManifest, BundleBuildResults, OutputRoot
WriteKeys: OutputPath
DependsOn: [TaskVerifyBuildResult]

Logic:
  1. 创建输出目录: {OutputRoot}/{BuildVersion}/
  2. 拷贝所有 bundle 从临时构建目录到输出目录
  3. 序列化 ABManifest 到输出目录 (ABManifest.json)
  4. 生成构建摘要 ({OutputRoot}/{BuildVersion}/build_summary.txt):
     - 版本号 / 时间戳 / 平台 / 后端模式
     - Bundle 数量 / 总大小 / 资产数量
     - Warning/Error 摘要
  5. 清理 Unity 临时构建产物
  6. 写入 OutputPath → BuildContext
```

---

## New Files

| File | Path | Assembly | Lines (est.) |
|------|------|----------|-------------|
| BuildVerificationResult.cs | Build/Pipeline/Editor/ | Editor | ~40 |
| TaskVerifyBuildResult.cs | Build/Pipeline/Editor/Tasks/ | Editor | ~80 |
| TaskOrganizeOutput.cs | Build/Pipeline/Editor/Tasks/ | Editor | ~100 |

All paths relative to `Assets/FYAsset/Scripts/`.

---

## Modified Files

| File | Change | Risk |
|------|--------|------|
| HashGenerator.cs | 合并 CRC32 + HashAlgorithmType 枚举选择 + GenerateFileCRC 快捷方法 | Low — 内部重构，保持现有 API |
| CRC32Helper.cs | 删除（合并到 HashGenerator） | Low |
| TaskBuildBundles.cs | Hash128 → HashGenerator.GenerateFileHash | Low — 替换 Hash 来源 |
| TaskGenerateManifest.cs | CRC32Helper.Compute → HashGenerator.GenerateFileCRC | Low |

---

## Pre-execution Refactoring: HashGenerator Unification

```
HashAlgorithmType enum { MD5, CRC32 }  // SHA256 等后续扩展

HashGenerator (static)
  ├─ 快捷方法
  │   GenerateFileHash(path)      → string   (MD5)
  │   GenerateFileCRC(path)       → uint     (CRC32)
  │   GenerateStringHash(content) → string   (MD5)
  │
  ├─ 通用方法（枚举选择）
  │   ComputeFileHash(path, algo) → string   (hex)
  │
  └─ 内部：switch(algo) → MD5.Create / CRC32 查表
```

---

## Task Breakdown

| Task | Content | Depends On |
|------|---------|-----------|
| E5-2b-T0 | HashGenerator 合并 CRC32 + HashAlgorithmType 枚举；CRC32Helper 删除；TaskBuildBundles/TaskGenerateManifest 调用点更新 | — |
| E5-2b-T1 | 创建 BuildVerificationResult.cs + 创建 TaskVerifyBuildResult.cs | T0, E5-1 done, E6 done |
| E5-2b-T2 | 创建 TaskOrganizeOutput.cs | T1 |
| E5-2b-T3 | 编译验证 (dotnet build) | All above |

---

## Invariants

1. TaskVerifyBuildResult 6 项检查全部执行；Error→Fatal 中止，Warning→继续
2. TaskOrganizeOutput 创建正确输出目录结构
3. 临时构建产物在成功拷贝后清理
4. `dotnet build` 0 errors

---

## Change Log

| Date | Change |
|------|--------|
| 2026-05-06 | Split from original plan-E5-2; blocked by E6 |
| 2026-05-07 | E6 realized → unblocked; review: Hash algorithm → HashGenerator (merge CRC32+enum), BuildVerificationResult type defined, BuildVersion added to TaskOrganizeOutput ReadKeys, HashGenerator refactoring added as T0, TaskBuildBundles/TaskGenerateManifest call-site updates added |
