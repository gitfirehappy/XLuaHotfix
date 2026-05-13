# Draft: 旧管线字段命名 PascalCase 统一

> **Status**: Draft — 2026-05-08
> **Priority**: Low — 纯命名重构，不影响功能
> **Trigger**: field-semantics.md 审查发现旧管线 camelCase 与新管线 PascalCase 不一致
> **Depends on**: 无前置依赖，可独立执行

---

## 问题

旧管线数据结构使用 camelCase 字段命名（`hash`, `bundleName`, `size`），新管线统一使用 PascalCase（`FileHash`, `BundleName`, `FileSize`）。同语义字段命名不一致增加混淆风险。

## 改动范围

### VersionState.cs
```
version    → Version
hash       → FileHash
totalSize  → TotalSize
bundles    → Bundles
```

### BundleInfo（嵌套在 VersionState.cs）
```
bundleName → BundleName
hash       → FileHash
size       → FileSize
```

### Manifest.cs
```
latestPackage  → LatestPackage
latestversion  → LatestVersion
```

## 影响分析

- **序列化兼容性**: 这些类用 JSON 序列化。字段重命名后旧 JSON 文件将无法反序列化。
  - **方案 A**: 加 `[JsonProperty("hash")]` 或 `[FormerlySerializedAs("hash")]` 兼容属性
  - **方案 B**: 个人学习项目，直接改，旧数据作废
- **引用点**: BuildProjectManager.cs、DifferentialProcessor.cs、HotfixManager.cs 等使用这些字段的地方需要批量重命名

## 估算

- 3 个文件结构改动
- ~10 个引用点批量重命名
- 工作量: < 30 分钟

---

## Change Log

| Date | Change |
|------|--------|
| 2026-05-08 | Initial draft from field-semantics review |
