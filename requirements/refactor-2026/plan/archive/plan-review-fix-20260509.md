# Sub-Plan: GPT Review Fix — Data Structures / Redundancy / Naming (2026-05-09)

> **Source**: Three-dimension GPT review 2026-05-08
>   - `review/fyasset-data-structures-review-20260508.md`
>   - `review/fyasset-redundancy-architecture-review-20260508.md`
>   - `review/fyasset-naming-boundaries-maintainability-review-20260508.md`
> **Status**: Executed 2026-05-09
> **Risk**: Low-Medium (API surface changes, legacy path preservation required)

---

## Objective

Fix the highest-priority findings from the three-dimension GPT review. Attack surface: data-structure contract completeness, architecture redundancy removal, and naming convention stabilization.

## Principle

- 旧设计适应新设计：AB 路径不再背负 legacy string-key 查询方法
- 旧管线持续运行：`AddressableLabelsConfig` 独立，一个字段不动
- 语义 struct 趁小补全：`CollectorRef` / `AssetClassification` 加 value semantics
- 命名 typo 趁引用少现在修

---

## Tasks

| # | Source | Level | Task | Files |
|---|--------|-------|------|-------|
| T1 | Data P1 | 🔴 | `RuntimeAssetEntry` Labels 收口：`public IReadOnlyList<string>` + `SetLabels()` 内部管缓存失效，`InvalidateLabelCache()` 改 private | 1 |
| T2 | Redundancy P1 + Naming P1 | 🔴 | `IAssetIndex` 接口分离：砍 4 个 legacy 方法（GetKeysByLabel/GetKeysByType/GetLabels/ContainsKey），4 个 default NotSupportedException 体删除；`ABAssetIndex` 删 legacy 方法实现 | 2 |
| T3 | Redundancy P1 | 🔴 | `AssetPackageManager` 初始化改从 `GetAllEntries()` 自建 `_labelToKeys` + `_typeToKeys` 缓存；查询方法读本地缓存不穿透 `_index`。Legacy 路径从 `AddressableLabelsConfig.keysByLabel/keysByType` 直接填缓存 | 1 |
| T4 | Redundancy P1 | 🔴 | `AddressableLabelsConfig` 去掉 `: IAssetIndex`，独立存在 | 1 |
| T5 | Redundancy P1 | 🟡 | 提取 `CollectorPathUtility`（NormalizePath + PathDepth + IsPathContained + MatchesIgnorePattern），7 个文件各删私有副本改调用 | 8 (1新+7改) |
| T6 | Data P1 | 🟡 | `CollectorRef` + `AssetClassification` 加 `IEquatable<T>` + `Equals` + `GetHashCode` + `ToString` | 2 |
| T7 | Data P1 | 🟡 | `ABAssetIndex.BuildIndex()` 预建 `_addressResults` / `_typeResults` 字典，查询真正零分配。注释保留"零分配热路径" | 1 |
| T8 | Data P1 | 🟡 | `ABAssetIndex` 注释"零分配热路径"保真（T7 已做到真正零分配，注释和实现对齐）| — (随 T7) |
| T9 | Naming P1 | ⚪ | 修 typo：`DEAULT_XLUA_TYPE_CONFIG_LOAD_LABEL` → `DEFAULT_XLUA_TYPE_CONFIG_LOAD_LABEL`（3 文件） | 3 |
| T10 | Naming P1 | ⚪ | 修 typo：`ScriptObjectDataBse` → `ScriptObjectDataBase`（类名 + 文件名 + 4 处引用） | 2 |
| T11 | Naming P1 | ⚪ | PascalCase 命名规则：新代码公共字段 PascalCase，旧代码碰到改。以 `FYAssetConstants.cs` 为范本 | 政策 |

---

## Task Dependencies

```
T2 (IAssetIndex cut) ──┬── T3 (Manager 自建缓存)
                        │
T4 (LabelsConfig 独立) ─┘

T1 (RuntimeAssetEntry) ── 独立
T5 (CollectorPathUtility) ── 独立
T6 (CollectorRef + AssetClassification) ── 独立
T7 (ABAssetIndex 预建) ── 独立
T9 (DEAULT typo) ── 独立
T10 (ScriptObjectDataBse typo) ── 独立
T11 (PascalCase 政策) ── 独立
```

T2 → T3：`IAssetIndex` 砍方法后，`AssetPackageManager` 必须同时改初始化逻辑，否则编译不过。

---

## Modified Files Summary

| File | Task | Change |
|------|------|--------|
| `RuntimeAssetEntry.cs` | T1 | Labels → IReadOnlyList<string> property; +SetLabels(); InvalidateLabelCache → private |
| `IAssetIndex.cs` | T2 | 删 4 legacy 方法签名 + 4 default NotSupportedException 体（~48 行删） |
| `ABAssetIndex.cs` | T2,T7 | 删 legacy 方法实现（~40 行）；BuildIndex() 预建 _addressResults/_typeResults |
| `AssetPackageManager.cs` | T3 | 初始化改从 GetAllEntries() / keysByLabel/keysByType 建缓存；查询读本地缓存 |
| `AddressableLabelsConfig.cs` | T4 | 去掉 `: IAssetIndex` |
| `CollectorPathUtility.cs` | T5 | **新建**：NormalizePath + PathDepth + IsPathContained + MatchesIgnorePattern |
| `CollectionScanner.cs` | T5 | 删私有 NormalizePath/PathDepth/MatchesIgnorePattern，改用 CollectorPathUtility |
| `CollectorReverseIndex.cs` | T5 | 同上 |
| `CollectorSettingValidator.cs` | T5 | 删私有 NormalizePath/IsPathContained，改用 CollectorPathUtility |
| `CollectorContextMenu.cs` | T5 | 删私有 NormalizePath，改用 CollectorPathUtility |
| `CollectorAssetInspectorGUI.cs` | T5 | 同上 |
| `CollectorPanel.cs` | T5 | 同上 |
| `CollectorTargetPickerPopup.cs` | T5 | 同上 |
| `CollectorRef (in CollectorReverseIndex.cs)` | T6 | 加 IEquatable/GetHashCode/ToString |
| `AssetClassification.cs` | T6 | 加 IEquatable/GetHashCode/ToString |
| `FYAssetConstants.cs` | T9 | DEAULT → DEFAULT |
| `GameLauncher.cs` | T9 | 引用更新 |
| `XluaTypeConfigLoader.cs` | T9 | 引用更新 |
| `ScriptObjectDataBse.cs` | T10 | 类名 + 文件名重命名 |
| `SOAddressableTagger.cs` | T10 | 4 处类型引用更新 |

---

## Invariants

1. `dotnet build XLuaHotfix.sln` passes with 0 errors
2. Legacy 管线（Addressables 路径）持续运行：`AddressableLabelsConfig` 自身方法和数据不变
3. AB 管线 resolve/load 行为不变
4. Collector editor 扫描/校验/UI 行为不变
5. `RuntimeAssetEntry` 外部只读 `Labels`，缓存自动失效
6. `ABAssetIndex` 查询真正零分配（预建结果数组）

---

## Change Log

| Date | Change |
|------|--------|
| 2026-05-09 | Initial plan: 11 tasks derived from 3-dimension GPT review 2026-05-08 |
