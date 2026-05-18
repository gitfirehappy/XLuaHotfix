# Plan-E9: VersionNumber SemVer+Build Extension

> **Status**: Executed 2026-05-09
> **Priority**: Before E7 (Diff snapshot adaptation) — E7 depends on finalized version semantics
> **Insertion Point**: After Phase 6 (E4/E5/E6 realized), before E7/Phase 7
> **Supersedes**: `drafts/plan-versionnumber-extension.md` (minimal extension draft)
> **Related**: `drafts/plan-version-management-draft.md` (broader version management — VersionPanel/BuildHistory/一键化, deferred to later)

---

## Motivation

1. 当前 `VersionNumber` 只有 Major.Minor.Patch，无法区分同版本多次构建、无法表达预发布阶段
2. E7 (Diff snapshot) 需要明确的版本比较语义来判断"是否需要更新"
3. 旧管线 `BuildProjectManager` / `DifferentialProcessor` 直接使用 VersionNumber，扩充后统一受益
4. 个人学习项目，不需要兼容层，直接改

---

## Design Decision (2026-05-08 confirmed)

- **字段**: Major.Minor.Patch + Build (元数据，不参与比较) + Channel (参与比较)
- **Platform 不进 VersionNumber** — 由 PathManager 路径隔离 + BuildIndexData.Platform 承载
- **Channel 比较语义**: alpha(0) < beta(1) < rc(2) < ""(3, release)
- **Build 号**: 仅元数据，用于日志/文件名区分同版本多次构建，不影响客户端更新判断

---

## Task Breakdown

### E9-T1: VersionNumber 类扩充

**File**: `Assets/FYAsset/Scripts/Build/BuildManage/VersionDataBase.cs`

Changes to `VersionNumber` class:
1. 新增字段: `[BinaryField(3)] public int Build;` + `[BinaryField(4)] public string Channel;`
2. 实现 `IComparable<VersionNumber>` — 比较顺序: Major → Minor → Patch → ChannelRank
3. 新增 `GetFullVersionString()` → `"1.2.3-beta+42"` 格式（SemVer 2.0 标准: `Major.Minor.Patch-PreRelease+BuildMetadata`）
4. 新增 `RequiresForceUpdate(VersionNumber baseline)` → Major 不同时返回 true
5. 新增 `static int GetChannelRank(string channel)` — 内部辅助
6. 新增 `Parse(string)` + `TryParse(string, out VersionNumber)` — 支持 `"X.Y.Z"` / `"X.Y.Z-channel"` / `"X.Y.Z-channel+B"` / `"X.Y.Z+B"`（SemVer 2.0 顺序）
7. 新增 `override ToString()` → 委托 `GetVersionString()`
8. 新增 `operator >`, `<`, `>=`, `<=` — 委托 CompareTo
9. 更新 `Equals` / `GetHashCode` — 加入 Channel（Build 不参与 equality）

**Est.**: ~80 lines net addition

### E9-T2: VersionDataBase.IncrementVersion 适配

**File**: `Assets/FYAsset/Scripts/Build/BuildManage/VersionDataBase.cs`

Changes:
1. `IncrementVersion` 新增可选参数 `string channel = ""`
2. Build 号映射到 `DailyBuildCount`（每次构建 Build = DailyBuildCount）
3. Channel 写入 `CurrentVersion.Channel`
4. 日志输出改用 `GetFullVersionString()`

**Est.**: ~10 lines changed

### E9-T3: BinarySerializer 兼容性处理

**File**: `Assets/Tools/Scripts/Serialization/BinaryReflectionSerializer.cs`, `Assets/Tools/Scripts/Serialization/Generated/VersionNumber_BinarySerializer.cs`

- `BinaryReflectionSerializer.ReadObjectBody` 按 `[BinaryField]` 顺序逐字段读取，无 EOF 检测或缺失字段容错
- 旧 .bin 文件只有 field 0/1/2 (Major/Minor/Patch)，新增 field 3/4 (Build/Channel) 后读取旧数据会抛 `EndOfStreamException`
- **方案（个人项目，不需向后兼容）**: 扩充后删除所有旧 .bin 快照文件，重新生成。不在序列化器中加容错逻辑
- **Action**: 
  1. 确认 VersionNumber 的 `[BinaryField]` 标注正确（0=Major, 1=Minor, 2=Patch, 3=Build, 4=Channel）
  2. 删除 `BuildData/Snapshots/` 下所有旧 .bin 文件（如有）
  3. 重新运行 S2 代码生成器验证生成产物正确

### E9-T4: 引用点适配验证

确认以下文件无破坏性变更（新字段有默认值 0/null，旧代码不受影响）：

| File | Expected Impact |
|------|----------------|
| `ABManifest.cs` | PackageVersion 字段自动获得新字段，无需改动 |
| `TaskGenerateManifest.cs` | 从 BuildContext 读取 VersionNumber，透传即可 |
| `VersionState.cs` | `version` 字段自动获得新字段 |
| `Manifest.cs` | `latestversion` 字段自动获得新字段 |
| `BuildIndexData.cs` | `Version` 字段自动获得新字段 |
| `BuildSnapshots.cs` | `BuildSnapshot.Version` 自动获得新字段 |
| `DifferentialProcessor.cs` | 传递 VersionNumber，不做字段级操作 |
| `BuildProjectManager.cs` | 调用 IncrementVersion，签名兼容 |
| `LocalStatusExporter.cs` | 传递 VersionNumber |
| `IHotfixPipeline.cs` | 接口中 VersionNumber 参数，透传 |
| `LegacyHotfixBackend.cs` | 同上 |
| `ABManifestRoundTripTest.cs` | 测试数据需补充新字段（可选，默认值也能通过） |

**Action**: 编译验证，确认零报错

### E9-T5: 旧管线 BuildProjectManager 输出适配

**File**: `Assets/FYAsset/Scripts/Build/BuildManage/Editor/BuildProjectManager.cs`

Changes:
1. `ExecuteBuildFlow` 中构建前设置 `version.Build = versionData.DailyBuildCount`
2. `currentPackageName` 格式改用 `GetFullVersionString()` 或保持现有格式（决策点）
3. 日志输出改用完整版本字符串

**Est.**: ~5 lines changed

### E9-T6: TaskPrepareContext 写入 VersionNumber 对象

**File**: `Assets/FYAsset/Scripts/Build/Pipeline/Editor/Tasks/TaskPrepareContext.cs`

当前 TaskPrepareContext 只写入 `BuildContextKeys.BuildVersion` 为 string（时间戳或 CLI 参数）。E7/E9 设计要求 BuildContext 持有 VersionNumber 对象。

Changes:
1. 新增 `BuildContextKeys.Version` 键（类型 VersionNumber）
2. TaskPrepareContext 从 VersionDataBase SO 读取 CurrentVersion，写入 BuildContext
3. 保留原有 string `BuildVersion` 键不变（用于输出目录命名等）
4. 新增 WriteKeys: `BuildContextKeys.Version`

**Est.**: ~10 lines added

---

## Expected Consumers (新增 API 的预期使用者)

| API | 预期消费者 | 接入时机 |
|-----|-----------|----------|
| `CompareTo` / operators | E7 ABDiffBackend.PrepareDiff（比较 head vs current 版本） | E7 落地时 |
| `RequiresForceUpdate` | 运行期 HotfixManager（判断是否需要强制更新安装包） | E7 后续或独立 hotfix 优化 |
| `GetChannelRank` | CompareTo 内部使用 | E9 自身 |
| `Parse` / `TryParse` | CLI 参数解析、version_state.json 读取 | E9-T5 + E7 |
| `GetFullVersionString` | 日志输出、文件命名、ABManifest.BuildTimestamp 旁注 | E9-T5 |

```
E9-T1 (VersionNumber 类) 
  → E9-T2 (IncrementVersion)
    → E9-T3 (BinarySerializer 兼容性)
      → E9-T4 (编译验证)
        → E9-T5 (BuildProjectManager 适配)
          → E9-T6 (TaskPrepareContext 写入 VersionNumber)
```

全部顺序执行，无并行。

---

## Acceptance Criteria

1. `VersionNumber` 支持 `Parse("1.2.3")` / `Parse("1.2.3-beta+42")` 往返（SemVer 2.0 格式）
2. `new VersionNumber { Major=1, Minor=2, Patch=3, Channel="alpha" } < new VersionNumber { Major=1, Minor=2, Patch=3 }` 为 true
3. Build 字段不影响 `==` / `CompareTo` 结果
4. 旧管线 `BuildProjectManager.BuildHotfix()` 正常执行，version_state.json 包含新字段
5. 编译零错误
6. TaskPrepareContext 写入 VersionNumber 对象到 BuildContext

---

## Not In Scope (deferred to later plan)

- VersionPanel 重做 (see `drafts/plan-version-management-draft.md`)
- BuildHistory 记录
- GitInfoCollector
- 一键构建流程整合
- CI 环境变量注入 Channel/Build

---

## Change Log

| Date | Change |
|------|--------|
| 2026-05-08 | Initial plan from design discussion. Supersedes minimal extension draft |
| 2026-05-08 | Audit fixes: (1) Version format corrected to SemVer 2.0 `X.Y.Z-channel+build`; (2) E9-T3 clarified — BinaryReflectionSerializer has no missing-field fallback, solution: delete old .bin files; (3) Added E9-T6 — TaskPrepareContext must write VersionNumber to BuildContext; (4) Added Expected Consumers table; (5) Added acceptance criterion #6 |
