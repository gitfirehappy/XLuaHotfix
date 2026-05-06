# Sub-Plan E5-2b: Backbone Tasks (Phase 2 — 依赖 E6)

> **Risk**: Low
> **Dependencies**: E5-2a (BundleBuildInfo), E6 (ABManifest from TaskGenerateManifest)
> **Parent**: [plan-E5.md](plan-E5.md)
> **Status**: Draft — split from original E5-2; blocked until E6 realized
> **Blocked by**: E6

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
  4. HASH RE-VERIFY (Error): 重新计算每个 bundle MD5 → 与 Manifest.FileHash 比对
  5. SIZE ANOMALY (Warning): 文件 < 1KB 或 > 500MB（默认可配置）
  6. COUNT CROSS-CHECK (Error): 输出 bundle 数量 == BundleBuildInfo 数量 == BundleEntries 数量

  Error → 构建中止；Warning → 继续执行，列在摘要中
```

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
| TaskVerifyBuildResult.cs | Build/Pipeline/Editor/Tasks/ | Editor | ~80 |
| TaskOrganizeOutput.cs | Build/Pipeline/Editor/Tasks/ | Editor | ~100 |

All paths relative to `Assets/FYAsset/Scripts/`.

---

## Task Breakdown

| Task | Content | Depends On |
|------|---------|-----------|
| E5-2b-T1 | 创建 TaskVerifyBuildResult.cs | E5-1 done, E6 done |
| E5-2b-T2 | 创建 TaskOrganizeOutput.cs | E5-1 done, E6 done |
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
