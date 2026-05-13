# Review: E11 FYAssetSettings 执行效果

> **Review date**: 2026-05-13
> **Status**: Addressed — 2026-05-13 (F1: #if UNITY_EDITOR, F2: .asset versioned, F3: startup-config semantic confirmed, F4: comments updated)
> **Scope**: `requirements/refactor-2026/plan/plan-E11-settings.md` 对应落地代码
> **Reviewed files**:
> `Assets/FYAsset/Scripts/FYAssetSettings.cs`
> `Assets/FYAsset/Scripts/Build/Editor/SettingsPanel.cs`
> `Assets/FYAsset/Scripts/Build/Editor/BuildPipelineWindow.cs`
> `Assets/FYAsset/Scripts/Build/Pipeline/Editor/BuildPipelineConfig.cs`
> `Assets/FYAsset/Scripts/Build/Pipeline/Editor/Tasks/TaskPrepareContext.cs`
> `Assets/FYAsset/Scripts/Helpers/PathManager.cs`
> `Assets/FYAsset/Scripts/LegacyRuntime/HotfixManager.cs`
> **Non-scope**: 编辑器审美、配色、间距、视觉层级

## Findings

### 1. [High] `FYAssetSettings` 落在 Runtime 路径，但直接依赖 `UnityEditor.AssetDatabase`，与 E11 的“Runtime 配置源”目标冲突

**Evidence**

- `Assets/FYAsset/Scripts/FYAssetSettings.cs:1` 直接 `using UnityEditor;`
- `Assets/FYAsset/Scripts/FYAssetSettings.cs:71-79` 的 `LoadOrCreate()` 使用 `AssetDatabase.LoadAssetAtPath` / `AssetDatabase.CreateAsset` / `AssetDatabase.SaveAssets`
- 该类同时被多个 Runtime 代码直接读取：
  - `Assets/FYAsset/Scripts/Helpers/PathManager.cs:11`
  - `Assets/FYAsset/Scripts/LegacyRuntime/HotfixManager.cs:15`
  - `Assets/FYAsset/Scripts/Runtime/Compatibility/AssetPackageManager.cs:36`

**Why this matters**

E11 的核心决策是把 `PROJECTNAME / HOTFIX_URL / USE_AB_BACKEND` 收敛到 Runtime 程序集中的 `FYAssetSettings`，并要求“打包时包含在 build 中”。但当前实现把 Editor-only 的 `AssetDatabase` 放进了 Runtime 类型本体里。这不是单纯的代码风格问题，而是运行时边界被打穿了：

- 该类不能作为纯 Runtime 配置源成立
- Runtime 侧调用 `FYAssetSettings.Instance` 时，底层解析路径仍依赖 Editor API
- 即使在 Editor 下能通过 `dotnet build`，也不能证明 Unity Player 编译/运行链路满足 E11 目标

**Impact**

- E11 的“Runtime assembly + included in build”验收条件实际上没有被可靠满足
- 后续任何继续沿用 `FYAssetSettings.Instance` 的 Runtime 改动，都会扩大这个边界问题

**Recommendation**

- 把 `LoadOrCreate()` 和 `AssetDatabase` 逻辑移到 Editor-only helper 中，至少用 `#if UNITY_EDITOR` 包裹
- Runtime 类型只保留纯数据字段和无 Editor API 的读取路径
- 如果该配置必须进 Player，明确采用可运行时读取的载入方式，例如：
  - 预置资源引用
  - `Resources` / Addressables
  - 构建时生成纯 Runtime 配置快照

---

### 2. [High] E11-T6 声称创建并验证 `FYAssetSettings.asset`，但当前代码树里并不存在该资产文件

**Evidence**

- `requirements/refactor-2026/plan/plan-E11-settings.md` 要求：
  - `E11-T6: 创建 .asset + 编译验证`
  - Acceptance Criteria 3: `FYAssetSettings.asset 存在且字段默认值正确`
- 本次审查中执行 `Test-Path Assets/FYAsset/FYAssetSettings.asset` 返回 `False`
- `Assets/FYAsset/` 当前仅看到：
  - `CollectorData/`
  - `Scripts/`
  - 没有 `FYAssetSettings.asset`

**Why this matters**

当前实现把“配置资产存在”变成了一个运行时副作用，而不是计划要求的已落库结果。这样会带来两个直接问题：

- 新工作区 / 新机器 / CI 环境下，仓库本身并不自带这份配置资产
- E11 的默认值、路径、backend 开关无法作为版本化工件被稳定复现

换句话说，计划写的是“交付一个配置资产”，当前交付的是“如果有人在 Editor 中恰好触发 `Instance`，也许会帮你生成一个资产”。

**Impact**

- T6 的执行结果与计划描述不一致
- Acceptance Criteria 3 当前在仓库快照层面未满足
- 配置管理从“显式版本化”退化成“隐式副作用生成”

**Recommendation**

- 将 `Assets/FYAsset/FYAssetSettings.asset` 明确加入仓库
- 把“自动创建”降级为兜底，而不是主交付方式
- 若确实希望零配置启动，也应在 review/plan 中明确“资产可缺省”的真实行为，而不是宣称已创建

---

### 3. [Medium] 部分运行时配置并没有真正变成“可改即生效”的统一设置源，而是在类型初始化时被静态缓存

**Evidence**

- `Assets/FYAsset/Scripts/Helpers/PathManager.cs:11`
  - `public static readonly string PersistentRoot = Path.Combine(Application.persistentDataPath, FYAssetSettings.Instance.ProjectName);`
- `Assets/FYAsset/Scripts/LegacyRuntime/HotfixManager.cs:15`
  - `private static readonly string _hotfixUrl = FYAssetSettings.Instance.HotfixUrl;`

**Why this matters**

E11 的目标是把原先硬编码常量迁移到可配置的 SO 中。但这里的两个关键运行时值在类型首次初始化时就被 `static readonly` 固化了：

- `ProjectName`
- `HotfixUrl`

这意味着配置虽然“名义上”从常量迁到了 SO，但行为上仍然接近常量：

- 修改 `FYAssetSettings` 后，不会自动反映到已经初始化过的 `PathManager` / `HotfixManager`
- 必须依赖域重载或重新进入进程，才能拿到新值

这和 E11 想建立的“统一设置源”是有落差的，尤其是 plan 里还把“修改 SO 中 ProjectName / UseABBackend，调用方读取到新值”列成了验收语义。

**Impact**

- E11 对 Runtime 配置热切换/即时生效的承诺并不一致
- 维护者会误以为所有迁移后的字段都具备同等动态性

**Recommendation**

- 对需要运行时实时读取的设置，改成按需 getter，而不是 `static readonly` 缓存
- 如果确实只允许“进程启动时读取一次”，应在代码与计划中明确这是启动时配置，而不是动态设置

---

### 4. [Low] E11 清理不彻底，注释和语义说明仍保留 `DefaultBackendMode` 旧所有权

**Evidence**

- `Assets/FYAsset/Scripts/Build/BackendMode.cs:2-4`
  - 注释仍写着“由 `BuildPipelineConfig.DefaultBackendMode` 配置”
- `Assets/FYAsset/Scripts/Build/Pipeline/Editor/BuildPipelineConfig.cs:36-38`
  - summary 仍写“定义 Task 编排、后端模式、文件名风格等全局选项”
- 但实际 `BuildPipelineConfig` 中的 `DefaultBackendMode` 已删除，真实所有权已迁移到：
  - `Assets/FYAsset/Scripts/Build/Pipeline/Editor/Tasks/TaskPrepareContext.cs:23`
  - `FYAssetSettings.Instance.UseABBackend`

**Why this matters**

这不是审美问题，而是源码语义会误导后续维护者。E11 的目标之一就是明确 backend 开关的唯一所有权；当前代码注释还在把维护者指向旧入口，会让后续修改和 review 继续沿错误方向排查。

**Recommendation**

- 更新 `BackendMode.cs` / `BuildPipelineConfig.cs` 注释
- 明确 backend 模式的实际来源是 `FYAssetSettings.Instance.UseABBackend`，CLI `--backend` 只是局部覆盖

## Summary

E11 在“引用点迁移”“窗口入口合并”“Settings 面板落地”这几项上基本完成了表层目标，但执行效果存在两个实质性缺口：

1. `FYAssetSettings` 还没有真正成为可进入 Player 的 Runtime 配置源
2. 计划声称已创建的 `FYAssetSettings.asset` 在当前仓库快照中并不存在

因此，这个子计划更接近“Editor 侧入口统一已完成，但 Runtime 配置边界仍未收口”。如果后续要继续以 `FYAssetSettings` 作为全局设置中心，建议优先修复 Findings 1 和 2，再把它视为真正完成。
