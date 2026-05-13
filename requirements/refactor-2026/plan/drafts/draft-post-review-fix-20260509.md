# Draft: Three-Plan Post-Review Fixes

> **Source**: `review/fyasset-three-plan-post-review-20260509.md` (2026-05-09, gpt-5.5)
> **Scope**: E9 + naming-unification + review-fix 三项计划执行后的门禁审计修复
> **Status**: Promoted — 2026-05-10 已提升为 `plan/plan-post-review-fix-20260510.md`（trace 保留，本文不再更新）
> **Depends on**: 无（独立修复）

---

## Decisions Summary (8 items)

### H-1: Version 源分裂 — BuildConfig 数据锁 + DependsOn 保护

**问题**: TaskPrepareContext 写入 `BuildContextKeys.Version` 但 TaskGenerateManifest 未消费，自调 `ResolveVersion()` 重读 SO。CLI `--version` 只影响 string `BuildVersion`，不转为 VersionNumber 对象。

**方案**:
1. 引入不可变 `BuildConfig` struct 作为 Context 单一 key，收口 BackendMode / TargetPlatform / Version / OutputRoot 等构建环境参数
2. TaskPrepareContext 是唯一构建者（CLI 解析在此完成，SO 读取在此统一），Set 入 Context 后下游只读
3. DAG 自动保护：后续 Task 若声明 WriteKeys 含 BuildConfig → Write-Write 冲突
4. **附加保护**: TaskGenerateManifest.DependsOn 显式声明 TaskPrepareContext，确保执行顺序

**涉及文件**:
- `BuildContextKeys.cs` — 新增 `BuildConfig` key，可移除分散的 BackendMode/BuildVersion/Version/OutputRoot/TargetPlatform
- `TaskPrepareContext.cs` — CLI 解析 VersionNumber，构建 BuildConfig 快照
- `TaskGenerateManifest.cs` — ReadKeys 加 BuildConfig，`ResolveVersion()` 替换为 `ctx.Require<BuildConfig>().Version`
- **新建** `BuildConfig.cs` — 不可变 struct

### H-2: VersionNumber.CompareTo vs Equals 不一致

**问题**: `GetChannelRank()` 把未知 channel 映射到 rank 3（=release），导致 `"1.2.3-dev".CompareTo("1.2.3") == 0` 但 `Equals == false`。

**方案**: 白名单拒绝未知 channel。`TryParse` 和 Channel setter 只接受 `alpha` / `beta` / `rc` / `""`，其余抛 `FormatException`。

**涉及文件**:
- `VersionDataBase.cs` — `TryParse` 加 channel 白名单校验；`GetChannelRank` `_` default 分支抛异常

### M-3: VersionState.version 仍为 camelCase

**问题**: 旧管线命名统一计划任务表说 `version→Version`，但修改文件表又说保留小写，自相矛盾。实际代码 `public VersionNumber version;` 未改。

**方案**: 改 `version` → `Version`，同步更新所有引用点。

**涉及文件**:
- `VersionState.cs` — 字段改名
- `LegacyHotfixBackend.cs` — `versionState.version` → `versionState.Version`
- `Manifest.cs` — 如有引用一并更新

### M-4: AssetPackageManager 查询缓存三问题

**问题**:
1. `GetKeysByType`/`GetKeysByLabel` 返回内部 `List<string>` 引用，外部可篡改
2. `_labelToKeys`/`_typeToKeys` 字典默认 Ordinal 比较器，与 label 匹配的大小写不敏感语义不一致
3. Legacy 初始化路径 alias 了 SO 的 List（`_typeToKeys[item.Type] = item.Keys`）

**方案**:
1. 查询接口返回 `IReadOnlyList<string>` 或 `.ToList()` 拷贝
2. `_labelToKeys`/`_typeToKeys` 初始化为 `StringComparer.OrdinalIgnoreCase`
3. Legacy 初始化时拷贝 List：`_typeToKeys[item.Type] = new List<string>(item.Keys)`

**涉及文件**:
- `AssetPackageManager.cs` — 三处修改

### M-5: 查询缓存初始化前不清空

**问题**: `Initialize()` 不 Clear `_labelToKeys`/`_typeToKeys`/`_addressSet`。AB 失败回退 Legacy 时两套数据混合。

**方案**: `Initialize()` 开头 Clear 三个集合；加幂等保护（`_isInitialized` 已为 true 时直接 return）。

**涉及文件**:
- `AssetPackageManager.cs`

### M-6: 字段语义文档过时

**问题**: `docs/FYAsset/字段语义参考表.md` 仍描述旧字段名（`BundleInfo.hash`/`bundleName`），`RuntimeAssetEntry.Labels` 仍写 `List<string>`。

**方案**: 更新文档到当前代码实际状态，`VersionState.version` 改名后同步更新，添加 `BuildContextKeys` 条目。

**涉及文件**:
- `docs/FYAsset/字段语义参考表.md`

### L-1: ABAssetIndex.GetEntriesByAddressAndType 仍分配

**问题**: T7/T8 已预建 `_addressResults`/`_typeResults`，但 `GetEntriesByAddressAndType()` 每调用仍 new List。

**方案**: 改为返回预建结果数组或预建组合索引。

**涉及文件**:
- `ABAssetIndex.cs`

### L-2: VersionNumber.TryParse 接受负数

**问题**: `TryParse` 用 `int.TryParse`，接受负数 Major/Minor/Patch/Build。

**方案**: 加 `>= 0` 范围校验，负数时 TryParse 返回 false。

**涉及文件**:
- `VersionDataBase.cs`

---

## Execution Order (suggested)

```
M-3 (VersionState.version → Version)     ← 独立，纯重命名
M-5 (Init Clear + 幂等)                  ← 独立
L-1 (ABAssetIndex 预建)                  ← 独立
L-2 (TryParse 负数校验)                   ← 独立，可随 H-2
H-2 (拒绝未知 channel)                    ← 依赖 L-2 的校验思路
M-4 (查询缓存三修)                        ← 独立
M-6 (文档更新)                            ← 依赖以上代码修复完成后
H-1 (BuildConfig 数据锁)                  ← 架构改动最大，最后做
```

---

## Change Log

| Date | Change |
|------|--------|
| 2026-05-10 | Initial draft from three-plan post-review discussion. 8 items, all decisions confirmed |
