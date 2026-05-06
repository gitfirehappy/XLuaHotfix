# Plan: BuildMessage 语义化工厂对齐

## Metadata
- **Status**: Realized
- **Parent**: refactor-2026 (cross-cutting)
- **Created**: 2026-05-06

## Motivation

`RuntimeMessage` 有 9 个语义化工厂方法（`NotFound`、`Ambiguous`、`TypeMismatch` 等），每个封装错误码常量与消息模板。`BuildMessage` 只有通用工厂 `Error(code, msg, source)` / `Warning(code, msg, source)`，调用方需手写错误码与消息字符串。两层设计不一致。

## Scope

### 新增：BuildMessage.cs 语义化工厂（14 个）

```
SettingNull(source)
NoPackages(source)
EmptyPackage(packageName, source)
EmptyPackageName(source)
DuplicatePackageName(packageName, source)
EmptyGroupName(source)
DuplicateGroupName(groupName, packageName, source)
EmptyCollectPath(source)
PathNotFound(path, source)
CrossPackageOverlap(path, pkg1, pkg2, source)
SamePathConflict(path, source)
RuleNotFound(ruleName, source)
EmptyCollector(collectorPath, source)
DuplicateGuid(guid, source)
```

通用工厂 `Error()`/`Warning()` 保留。

### 更新调用点

| 文件 | 调用点数 | 改动 |
|------|---------|------|
| CollectorSettingValidator.cs | 15 | 替换为语义化工厂 |
| CollectionScanner.cs | 2 (helper 方法) | 替换 helper 内部调用 |

### 不改

- DependencyAnalyzer.cs (4 处) — 使用字符串字面量错误码，不在 BuildErrorCodes 中

## Tasks

- T1: BuildMessage.cs 添加 14 个语义化工厂方法
- T2: CollectorSettingValidator.cs 调用点替换
- T3: CollectionScanner.cs helper 替换
- T4: dotnet build 验证
- T5: 更新 context/ 知识库
- T6: 更新 docs/FYAsset/错误处理.md

## Change Log

| Date | Version | Description | Author |
|------|---------|-------------|--------|
| 2026-05-06 | 1.0.0 | Initial plan | AI |
