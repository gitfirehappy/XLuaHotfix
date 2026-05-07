# Draft: VersionNumber Extension Plan

> **Status**: Draft — 2026-05-07，发现 E6 ParseVersion 缺口后提取
> **Risk**: Low — 扩展已有类，不改变现有字段和序列化格式
> **Motivation**: E6 审批发现 `ABManifest.PackageVersion` (VersionNumber) 没有从 `BuildVersion` (string) 构造的路径；`ParseVersion()` 不存在。同时 VersionNumber 语义和配套函数不充分。

---

## Problem

1. **ParseVersion 缺失**：`BuildVersion` 是 string（如 `"1.2.3"` 或 `"20260507-120000"`），无法转为 `VersionNumber`
2. **语义不充分**：只有 `Major/Minor/Patch`，不支持 pre-release / build metadata
3. **配套函数零散**：`GetVersionString()` 有，但无 `ToString()` / `TryParse` / 比较运算符

---

## Scope (最小扩展，不改旧设计)

### FR-1: ParseVersion 工厂方法
- `VersionNumber.Parse(string version)` → VersionNumber
- `VersionNumber.TryParse(string version, out VersionNumber)` → bool
- 支持格式：`"X.Y.Z"` 或 `"X.Y"`
- 不支持的格式抛出/返回 false

### FR-2: ToString 标准化
- `override ToString()` → `"X.Y.Z"`（委托 `GetVersionString()`）

### FR-3: 比较运算符
- `operator >`, `<`, `>=`, `<=`
- 语义：逐级比较 Major → Minor → Patch

### FR-4: E6 集成（方案B）
- `TaskGenerateManifest` 中从 `VersionDataBase` SO 读取 `CurrentVersion` 写入 `ABManifest.PackageVersion`
- 回退：SO 不可用时用 `new VersionNumber { Major = 1, Minor = 0, Patch = 0 }`

---

## Not In Scope
- 不改变现有字段定义（Major/Minor/Patch 不变）
- 不改变 [BinarySerializable] 序列化格式
- 不增加 pre-release tag / SemVer 字段（V1 不需要）
- 不修改 VersionDataBase SO 结构

---

## Task Breakdown

| Task | Content | Lines (est.) |
|------|---------|-------------|
| VN-T1 | VersionNumber.Parse + TryParse | ~30 |
| VN-T2 | VersionNumber.ToString + 比较运算符 | ~30 |
| VN-T3 | E6 中改用 VersionDataBase SO 读取 | ~5 |
| VN-T4 | 编译验证 | — |

---

## Change Log

| Date | Change |
|------|--------|
| 2026-05-07 | Initial draft from E6 ParseVersion gap |
