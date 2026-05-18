# Post-Review Fix Plan (2026-05-10)

> **Source**: `requirements/refactor-2026/plan/drafts/draft-post-review-fix-20260509.md`
> **Promoted from**: `review/fyasset-three-plan-post-review-20260509.md` (gpt-5.5)
> **Status**: Executed (2026-05-11)
> **Depends on**: 已落地的 E5-1 / E5-2a / E5-2b / E6 / E9 / naming-unification / review-fix-20260509
> **Scope**: 8 项门禁审计修复 + BuildConfig 数据锁引入（新管线侧），不触及 BuildProjectManager 旧流程
> **Out of scope**: BuildProjectManager 接口分离（新/旧管线，类比 B4B9 HotfixManager），单独 plan 处理（见末尾"欠账"）

---

## Decisions Snapshot

| Item | Severity | Decision |
|------|----------|----------|
| H-1 | High | 引入不可变 `BuildConfig` struct，全收 5 个 BuildContext key（BackendMode / Version / BuildVersionString / OutputRoot / TargetPlatform）；新管线唯一来源为 SO；CLI `--version/--backend/--output/--platform` 走 SO 覆盖语义（CLI 写 SO 后再读，保持 SO 唯一来源原则） |
| H-2 | High | `VersionNumber` Channel 白名单（`alpha`/`beta`/`rc`/`""`），`TryParse` 与 setter 拒绝未知 channel；`GetChannelRank` default 抛异常 |
| M-3 | Medium | `VersionState.version → Version`（旧管线字段命名补齐），同步所有引用点 |
| M-4 | Medium | `AssetPackageManager`: (1) `GetKeysByType/Label` 返回 `IReadOnlyList<string>` 防外部篡改; (2) `_labelToKeys`/`_typeToKeys` 改 `OrdinalIgnoreCase`; (3) Legacy 初始化拷贝 List（不 alias SO 内部引用） |
| M-5 | Medium | `AssetPackageManager.Initialize()`: 开头 `Clear()` 三集合 + `_isInitialized` 幂等保护 |
| M-6 | Medium | `docs/FYAsset/字段语义参考表.md` 更新到当前字段实际状态（BundleInfo.Hash/BundleName 大写、Labels `IReadOnlyList`、新增 BuildContextKeys 条目、VersionState.Version 改名同步） |
| L-1 | Low | `ABAssetIndex.GetEntriesByAddressAndType()` 改返回预建结果数组，去掉每调用 `new List` |
| L-2 | Low | `VersionNumber.TryParse` 加 `>= 0` 范围校验，拒绝负数 |
| 附加 | — | TaskGenerateManifest 的 `OutputRoot` 漏声明补回 ReadKeys（E5-1 校验的漏网之鱼） |

---

## Task Breakdown

### T1: BuildConfig 数据锁（H-1 + 附加）

**新建** `Assets/FYAsset/Scripts/Build/BuildConfig.cs`（Runtime 程序集，与 `BuildContextKeys` 同级）

```csharp
/// <summary>
/// 构建管线运行环境的不可变快照。
/// TaskPrepareContext 是唯一构建者，下游 Task 只读消费。
/// 通过单 key 收口，DAG W-W 校验自然保护数据来源唯一性。
/// </summary>
public readonly struct BuildConfig
{
    public readonly BackendMode BackendMode;
    public readonly VersionNumber Version;            // 主版本数据（来自 SO）
    public readonly string BuildVersionString;        // 时间戳/构建标识（与 Version 解耦）
    public readonly string OutputRoot;
    public readonly BuildTarget TargetPlatform;

    public BuildConfig(BackendMode mode, VersionNumber version, string buildVersionString,
                       string outputRoot, BuildTarget platform) { ... }
}
```

**改动**：

- `BuildContextKeys.cs`: 删除 `BackendMode / BuildVersion / Version / OutputRoot / TargetPlatform` 5 个常量，新增 `BuildConfig`
- `TaskPrepareContext.cs`:
  - CLI `--version` 在解析后**先写 SO**（与编辑器入口统一来源：SO 唯一），再读 SO 拿到 `VersionNumber`
  - CLI `--backend / --output / --platform` 同语义（覆盖 SO 或 Editor 设置后再读）
  - 构造 `BuildConfig` 一次性写入 `ctx.Set(BuildContextKeys.BuildConfig, cfg)`
  - WriteKeys 收敛为 `{ BuildConfig }`
- `TaskGenerateManifest.cs`:
  - 删除 `ResolveVersion()`，改读 `ctx.Require<BuildConfig>(BuildContextKeys.BuildConfig).Version`
  - ReadKeys 加 `BuildConfig`（同时覆盖 `OutputRoot`）
- `TaskBuildBundles / TaskCollectBuiltins / TaskVerifyBuildResult / TaskOrganizeOutput`: ReadKeys 中所有原 5 key 替换为 `BuildConfig`；Execute 内 `ctx.Require<string>(OutputRoot)` 等改为 `ctx.Require<BuildConfig>().OutputRoot` 等
- `TaskOrganizeOutput`: `BuildVersion` 字符串读取改为 `cfg.BuildVersionString`

**校验**：DAGScheduler `Validate` 自动检测——若有第二个 Task 声明 WriteKeys 含 BuildConfig → W-W 冲突；若 Task 读 BuildConfig 未声明 ReadKeys → Read-before-Write 冲突。

### T2: VersionNumber Channel 白名单（H-2 + L-2）

**改 `VersionDataBase.cs`**（`VersionNumber` 所在文件）：

- `TryParse`: channel 段必须命中 `{ "", "alpha", "beta", "rc" }`，否则返回 false
- `TryParse`: Major/Minor/Patch/Build 加 `>= 0` 校验
- Channel setter（若存在）同样白名单
- `GetChannelRank` 的 `_` default 分支：`throw new ArgumentException($"Unknown channel: {channel}");`

### T3: VersionState.version → Version（M-3）

- `Assets/FYAsset/Scripts/Hotfix/LegacyRuntime/VersionState.cs`: `public VersionNumber version;` → `Version`
- `LegacyHotfixBackend.cs`: 所有 `.version` → `.Version`
- `Manifest.cs`: 若有 `.version` 引用同步
- `BuildProjectManager.cs` Line 285 `version = version,` → `Version = version,`

### T4: AssetPackageManager 查询缓存（M-4 + M-5）

**改 `AssetPackageManager.cs`**：

1. `_labelToKeys = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);` （`_typeToKeys` 同理；`_addressSet` 同理）
2. Legacy 初始化路径：`_typeToKeys[item.Type] = new List<string>(item.Keys);`（不 alias SO List）
3. `GetKeysByType / GetKeysByLabel` 返回 `IReadOnlyList<string>`（接口签名同步改）
4. `Initialize()` 开头：
   ```csharp
   if (_isInitialized) return;
   _labelToKeys.Clear(); _typeToKeys.Clear(); _addressSet.Clear();
   ```
5. AB 失败回退 Legacy 路径前同样 Clear（已在开头统一 Clear，回退路径不必重复，但需确认中途失败不残留——若 AB 部分写入后失败，Legacy 路径开始前再 Clear 一次以防御）

### T5: ABAssetIndex 零分配补完（L-1）

**改 `ABAssetIndex.cs`**：

- `BuildIndex()` 阶段建立 `Dictionary<(string address, string primaryType), RuntimeAssetEntry[]> _addressTypeResults`
- `GetEntriesByAddressAndType(address, primaryType)` 改为查表返回预建数组（无命中返回 `Array.Empty<RuntimeAssetEntry>()`）
- 注释保留"零分配热路径"语义，去掉每调用 `new List`

### T6: 文档同步（M-6）

**改 `docs/FYAsset/字段语义参考表.md`**：

- `BundleInfo.hash/bundleName` → `Hash/BundleName`
- `RuntimeAssetEntry.Labels: List<string>` → `IReadOnlyList<string>`
- `VersionState.version` → `Version`
- 新增 `BuildContextKeys` 章节（含 BuildConfig 字段表）
- 删除已不存在的 5 个分散 key 条目

---

## Execution Order

```
T3 (VersionState rename)          ← 独立，纯重命名
T2 (Channel 白名单 + 负数校验)     ← 独立
T4 (AssetPackageManager 三修+幂等) ← 独立
T5 (ABAssetIndex 零分配)          ← 独立
T1 (BuildConfig 数据锁)           ← 架构改动最大，所有 Task 同步改
T6 (文档同步)                     ← 最后做，依赖前 5 项落地
```

---

## Invariants

1. `dotnet build XLuaHotfix.sln` 0 errors
2. BuildContext 中所有原 5 key 调用点全部消失（grep `BuildContextKeys.BackendMode|BuildVersion|Version|OutputRoot|TargetPlatform` 应只命中 `BuildConfig` 字段读取）
3. DAGScheduler `Validate` 通过（无 W-W / Read-before-Write）
4. `VersionNumber.TryParse("1.2.3-dev", out _)` 返回 false
5. `VersionNumber.TryParse("1.-2.3", out _)` 返回 false
6. `AssetPackageManager.GetKeysByType` 返回类型为 `IReadOnlyList<string>`，调用者无法 cast 回 `List<string>` 篡改
7. `ABAssetIndex.GetEntriesByAddressAndType` 不再每调用分配
8. 旧管线 `BuildProjectManager` 行为不变（仅字段重命名）

---

## Approval Checklist

- [ ] H-1 方案：`BuildConfig` 全收 5 个 key，CLI 走 SO 覆盖语义（CLI 写 SO 后再读，保持 SO 唯一来源）—— 同意？
- [ ] H-2 channel 白名单仅含 `alpha/beta/rc/""`—— 是否需要 `dev`/`snapshot` 等其他？
- [ ] M-3 VersionState rename 是否需要 `FormerlySerializedAs("version")` 防序列化丢数据？
- [ ] M-4 `GetKeysByType / GetKeysByLabel` 改 `IReadOnlyList<string>` —— 现有调用方是否能接受签名变更？
- [ ] M-5 `_isInitialized` 幂等保护是否足够？要不要加 `Reset()` 方法供测试？
- [ ] L-1 `_addressTypeResults` 字典大小预估（实测 Address 数 × 平均 Type 数），是否会显著增加内存？
- [ ] 附加 OutputRoot 漏声明随 T1 一起修—— 同意？
- [ ] BuildProjectManager 新/旧管线接口分离暂不在本轮，单独 plan—— 同意？

---

## 欠账：BuildProjectManager 接口分离

类比 B4B9 把 HotfixManager 拆为 `IHotfixPipeline + LegacyHotfixBackend + ABHotfixBackend`，构建侧 `BuildProjectManager` 目前仍是单体（Addressables `BuildPlayerContent` 直调），新管线 DAGScheduler 与旧管线没有共用入口。

**待开 plan**：`plan-buildprojectmanager-split.md`（或合并到 Phase 4 思路），范围预估：

- 抽 `IBuildPipeline` 接口（5-6 方法）
- `LegacyAddressableBuildBackend` 包装现有 Addressables 流程
- `ABBuildBackend` 调用 DAGScheduler
- CLI 入口 `BuildCommandLine` 统一到 `IBuildPipeline`
- 与本轮 BuildConfig 协作（BuildConfig 是 ABBackend 内部数据锁，Legacy 不感知）

下一轮讨论。

---

## Change Log

| Date | Change |
|------|--------|
| 2026-05-10 | 从 draft 提升为正式 plan。讨论确认：H-1 走方案 A（全收 5 key）+ CLI 走 SO 覆盖；OutputRoot 漏声明随 T1 一起修；BuildProjectManager 接口分离记为欠账 |
