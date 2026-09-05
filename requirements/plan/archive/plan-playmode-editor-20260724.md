# Plan: AB Editor PlayMode（AssetDatabase 直读）

> **Status**: SIGNED OFF — 2026-07-28
> **Created**: 2026-07-24
> **Source Draft**: `requirements/plan/drafts/plan-playmode-draft.md`
> **Scope**: 仅 Editor 模式（对标 Addressables Fast Mode）；Simulate / Runtime 选择 UI 预留，逻辑本期不做
> **Dependencies**: E11 (`FYAssetSettings`) ✅、AB runtime (`ABPackageManager`) ✅
>
> ## 执行备注
>
> 1. 未单独做 `EditorAssetIndex`：用 `CollectionScanner` + 内存 `ABManifest` + 复用 `ABAssetIndex`。
> 2. 最小内部接口 `IABLoadBackend` 统一 `ABPackageBackend` / `EditorPackageBackend`。
> 3. Settings 两区：`AB Editor PlayMode` 与 `Package Mode`。
> 4. Smoke 入口：AB Build Pipeline 的 `Test` 面板（若单例已初始化需 Domain Reload）。

---

## 收敛决策

| # | 决策 | 结论 |
|---|------|------|
| S1 | 范围 | **只做 Editor 模式**；Simulate 作 Residual |
| S2 | Settings 布局 | **独立两区**：`AB Editor PlayMode` vs `Package Mode`（Standalone/Online） |
| S3 | 初始化锚点 | 改 `ABPackageManager.InitializePackageAsync()`，不改 `AssetPackageManager` facade |
| S4 | 接口策略 | 不引入新 `IPackageBackend`；Editor 路径在 `ABPackageManager` 内 `#if UNITY_EDITOR` 分支 |
| S5 | 真机 | 非 Editor 编译强制走现有 Runtime 路径；`EPlayMode` 字段仅 Editor 序列化可用 |
| S6 | 与 Standalone | 正交：`StandaloneBuild` 控制离线包/热更短路；`PlayMode` 控制 Editor 内加载源 |

---

## 方案

```
UseABBackend=false → AA 路径不变（Addressables 自带 PlayMode）
UseABBackend=true  + UNITY_EDITOR:
  PlayMode.Editor  → EditorAssetIndex + AssetDatabase.LoadAssetAtPath
  PlayMode.Runtime → 现有 ABManifestLoader + ABPackageBackend（默认，本期零逻辑改动）
  PlayMode.Simulate→ Residual（本期选了也当 Runtime 或灰掉）

真机 / 非 Editor → 永远 Runtime 路径
```

### 加载路径（Editor 模式）

```
ABPackageManager.InitializePackageAsync()
  if Editor && PlayMode==Editor:
    _index = EditorAssetIndex.Rebuild(CollectorSetting)
    _backend = null  // 或轻量 Editor 加载器内嵌
    LoadAsset* → AssetDatabase.LoadAssetAtPath
  else:
    现有: ABManifestLoader → ABAssetIndex → ABPackageBackend
```

### 不改

- 热更 11 步状态机（除已落地的 Standalone 短路）
- AA 管线
- Simulate / Virtual Task 跳过
- 调试面板（独立 Draft）

---

## 任务拆分

### T1: `EPlayMode` 枚举

**文件**: `Assets/FYAsset/Scripts/Shared/Settings/EPlayMode.cs`（新建，Runtime 程序集也可，但值仅 Editor 使用）

```csharp
public enum EPlayMode
{
    Editor = 0,    // AssetDatabase 直读
    Simulate = 1,  // Residual：本期不实现
    Runtime = 2    // 真实 AB 加载（默认）
}
```

### T2: `FYAssetSettings.PlayMode` 字段

**文件**: `Assets/FYAsset/Scripts/Shared/Settings/FYAssetSettings.cs`

```csharp
#if UNITY_EDITOR
[Header("AB Editor PlayMode")]
public EPlayMode PlayMode = EPlayMode.Runtime;
#endif
```

真机 SO 无此字段序列化差异：字段用 `#if UNITY_EDITOR` 包住即可（Editor-only 序列化字段在 player 中不存在；若担心 SO 兼容，也可用始终存在字段 + 运行时忽略）。

**建议**: 字段始终存在于 SO（避免 asset 序列化漂移），运行时非 Editor 强制忽略：

```csharp
[Header("AB Editor PlayMode")]
public EPlayMode PlayMode = EPlayMode.Runtime;
```

### T3: `EditorAssetIndex`

**文件**: `Assets/FYAsset/Scripts/AB/Runtime/Editor/EditorAssetIndex.cs`（`#if UNITY_EDITOR`）

职责：从 Collector 配置扫 address → path，不跑完整构建管线。

- 输入：当前 AB CollectorSetting（与正式管线同一配置源）
- 输出：address → `RuntimeAssetEntry`（AssetPath、Address、Labels；BundleName 可空）
- 每次进入 Play / Initialize 时 `Rebuild()`，不缓存

估计 ~100 行。

### T4: `ABPackageManager` Editor 分支

**文件**: `Assets/FYAsset/Scripts/AB/Runtime/ABPackageManager.cs`

在 `InitializePackageAsync()` 开头：

```csharp
#if UNITY_EDITOR
if (FYAssetSettings.Instance.UseABBackend
    && FYAssetSettings.Instance.PlayMode == EPlayMode.Editor)
{
    return InitializeEditorMode();
}
#endif
// 现有 Runtime 路径不变
```

`LoadAssetAsync` / `LoadAssetSync` / `UnloadAsset`：
- Editor 模式：`AssetDatabase.LoadAssetAtPath`；Unload 空操作
- Async 路径加 `await Task.Yield()`（暴露时序问题，Draft Q1）

Scene 加载若已有 API：Editor 下走 `EditorSceneManager.LoadSceneAsyncInPlayMode`；若当前无 scene API，本期只做 Object 资源加载。

### T5: Settings 面板独立两区

**文件**: `Assets/FYAsset/Scripts/Shared/Build/Editor/Settings/SettingsPanel.cs`

1. **Package Mode**（已有）：Standalone / Online 快切 + `StandaloneBuild` 字段
2. **AB Editor PlayMode**（新增）：
   - `PlayMode` PropertyField（Editor / Simulate / Runtime）
   - Simulate 选项旁注：`未实现，等同 Runtime`
   - 仅当 `UseABBackend=true` 时显示完整说明

不把两个开关混在一个「Play Mode」标题下。

### T6: 最小自检

- Editor 下切 `PlayMode=Editor`，Play，加载一个已知 address 成功
- 切回 `Runtime`，依赖已有 StreamingAssets/热更路径（不强制本期 E2E）
- 无新 batch 矩阵项（Editor-only，Player 测不了 AssetDatabase 路径）

可选：AB Test 面板的 `Editor PlayMode Smoke` 调一次 `InitializePackageAsync` + Load 夹具 address。

---

## 涉及文件

| 文件 | 操作 | 估计 |
|------|------|:---:|
| `EPlayMode.cs` | 新增 | ~10 |
| `FYAssetSettings.cs` | +PlayMode 字段 | +3 |
| `EditorAssetIndex.cs` | 新增 | ~100 |
| `ABPackageManager.cs` | Editor 分支 + Load 分流 | ~40 |
| `SettingsPanel.cs` | AB Editor PlayMode 区 | ~30 |
| 可选 smoke menu | 新增 | ~30 |

**总计: ~180 行，2 新文件，3–4 文件修改。**

---

## Residual

- Simulate 模式（Task 跳过 + 虚拟 ABManifest + 校验）
- Runtime 模式在 Editor 下的状态条/调试面板集成（归 Debug Panel Draft）
- AA 侧 Editor PlayMode（Addressables 自带，不重复做）
- Scene 加载完整对齐（若现网无 scene API 则后续补）

---

## 执行顺序

1. T1–T2 枚举 + SO 字段
2. T3 EditorAssetIndex
3. T4 ABPackageManager 分支
4. T5 Settings UI
5. T6 手动/菜单 smoke
