# Plan: Bundle 命名约定统一

## Metadata
- **Status**: Realized
- **Parent**: refactor-2026 (cross-cutting)
- **Created**: 2026-05-06

## Motivation

`BundleNameBuilder.SanitizeSegment` 用 `_` 替换非法字符，同时 `Build()` 用 `_` 做段分隔符。`_` 既是替换符又是分隔符，导致不同输入可产生相同输出（"my group" 和 "my_group" 都变成 "my_group"），无法从文件名反推原始段值。

`PackByLabel` 用 `--` 连接标签，与顶层 `_` 分隔符风格不统一。

## Design

### 命名约定

```
Bundle 格式：  {package}_{group}_{packKey}
Labels 格式：  label1~label2~label3
```

- 顶层分隔符：`_`（进黑名单，段值中不允许出现）
- 标签连接符：`~`

### 黑名单（11 个字符）

```
/  \  :  *  ?  <  >  "  ~  $  _
```

段值包含黑名单字符 → `BuildMessage.Error`，阻断构建。不静默替换。

### 安全字符

所有不在黑名单中的字符直接通过，不做转换。小写保留。

## Tasks

- T1: `SystemIdentifiers.cs` 新增分隔符常量
- T2: `BundleNameBuilder.cs` SanitizeSegment 改为黑名单校验
- T3: `PackByLabel.cs` 标签连接符 `--` → `~`
- T4: `CollectionScanner.cs` 段值黑名单校验
- T5: `dotnet build` 验证
- T6: 更新 `docs/FYAsset/collector-规则系统.md` 命名章节

## Change Log

| Date | Version | Description | Author |
|------|---------|-------------|--------|
| 2026-05-06 | 1.0.0 | Initial plan | AI |
