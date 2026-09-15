# Draft: PlayMode 三模式设计（对标 Addressables Fast/Virtual/Packed）

> **Status**: 部分转 Plan — 2026-07-24 → `requirements/plan/archive/plan-playmode-editor-20260724.md`（仅 Editor 模式；2026-07-28 签字，2026-09-04 归档）
> ~~Draft — 2026-05-06, converged 2026-05-12~~
> Simulate 模式仍留 Draft Residual，未进本期 Plan。
> **Dependencies**: E11 (FYAssetSettings SO), E5-1 (DAGScheduler + BuildContext)
> **Replaces**: `FYAssetConstants.USE_AB_BACKEND` 编译期常量 → FYAssetSettings SO 字段 (E11 迁移) + 本 draft 在 USE_AB_BACKEND=true 时生效

---

## 动机

当前系统通过 `USE_AB_BACKEND` 常量在 Addressables 和自研 AB 之间切换。一旦完全移除 Addressables，开发期将失去"不打包直接 Play"的能力。需要对标 Addressables 三模式，在自研体系内提供等价的开发体验。

## 两层开关模型

```
第一层: FYAssetSettings.UseABBackend (bool, E11)
  false → Addressables 体系 (AddressablesBackend + Addressables 自带的 PlayMode Script)
  true  → FYAsset 自研体系 (进入第二层 EPlayMode)

第二层: FYAssetSettings.PlayMode (EPlayMode, Editor-only)
  Editor   → EditorAssetIndex + AssetDatabase 直读, 零验证, 最快
  Simulate → 虚拟 ABManifest (构建管线跳过 TaskBuildBundles) + AssetDatabase, 完整验证
  Runtime  → 真实 ABManifest + ABBundleLoader, 真机加载路径
  
  真机强制 Runtime, EPlayMode 在非 Editor 编译时不可用
```

两层互不干扰。`UseABBackend=false` 时第二层不生效。`UseABBackend=true` 时第二层决定 Editor 下的迭代体验。

---

## 三模式定义

```csharp
public enum EPlayMode
{
    Editor = 0,      // 对标 Fast Mode: AssetDatabase 直接加载
    Simulate = 1,    // 对标 Virtual Mode: 虚拟 Manifest + AssetDatabase 加载
    Runtime = 2,     // 对标 Packed Mode: 真实 AB 加载
}
```

| 模式 | 加载方式 | 需要构建 | Manifest 来源 | 开发期用途 |
|------|---------|:---:|------|-----------|
| Editor | AssetDatabase.LoadAssetAtPath | ❌ | Collector→address→path 直查 | 日常开发，最快迭代 |
| Simulate | AssetDatabase + 虚拟 ABManifest | ⚠️ 跑管线, 跳过 BuildAssetBundles | 构建管线产出 (内存) | 验证配置/分组/依赖 |
| Runtime | AssetBundle.LoadFromFile | ✅ | ABManifest.json/.bin 文件 | 上线前测试，线上运行 |

---

## 架构总览

```
AssetPackageManager.Initialize()
        │
        ▼
    UseABBackend?
        │
    ┌───┴──── false → AddressablesBackend (现有, 不变)
    │
    └─── true → PlayMode?
                  │
              ┌───┼─── Editor → EditorBackend(EditorAssetIndex)
              │   │             └─ AssetDatabase.LoadAssetAtPath
              │   │
              │   ├── Simulate → EditorBackend(ABAssetIndex from virtual build)
              │   │               └─ AssetDatabase.LoadAssetAtPath
              │   │               └─ 加载前验证 asset 在 Manifest 声明的 Bundle 中
              │   │
              │   └── Runtime → ABPackageBackend(ABAssetIndex from real build)
              │                   └─ ABBundleLoader + AssetBundle.LoadFromFile
              │
              ▼
         IAssetIndex + IPackageBackend (现有接口, 不变)
```

**关键设计: EditorBackend 同时服务 Editor 和 Simulate 模式。** 区别仅在于传入的 IAssetIndex 实例不同:
- Editor 模式: `EditorAssetIndex` — 从 Collector 配置直接构建 address→path 映射 (~100 行)
- Simulate 模式: `ABAssetIndex` — 从虚拟 Manifest 构建 (复用已存在的 ABAssetIndex 代码, 零新代码)

---

## Simulate 模式: Task 跳过方案 (核心创新)

### 对标分析

Addressables Virtual Mode (`BuildScriptVirtualMode`) 的实现方式:

- **构建侧**: `PrepGroupBundlePacking()` 是 Packed 和 Virtual 两个模式共享的**同一个静态方法**——打包算法完全一致
- **区别**: Virtual 模式在打包后停止，不调用 `BuildPipeline.BuildAssetBundles()`, 而是产出 `VirtualAssetBundleRuntimeData` (内存中的虚拟 Bundle 描述)
- **加载侧**: `VirtualAssetBundleProvider` 和 `VirtualBundledAssetProvider` 是独立的 Provider——用 AssetDatabase 加载但模拟 Bundle 加载进度

**我们的 Simulate 模式更轻量**: 不需要独立的 Build Script 和 Provider, 只需在现有 DAG Task 上加模式开关。

### EBuildMode 枚举

```csharp
// 新增: BuildContext 或 BuildPipelineConfig 中
public enum EBuildMode
{
    Packed = 0,   // 完整构建, 产出 .bundle 文件 + ABManifest.json
    Virtual = 1,  // 模拟构建, 只产出内存 ABManifest, 不调 Unity BuildPipeline
}
```

### Task 跳过策略

```
管线 Task 序列 (Simulate / Virtual 模式):

  TaskPrepareContext      → 正常执行 (BuildMode 不影响)
  TaskCollectBuiltins     → 正常执行
  TaskCollectAssets       → 正常执行 (CollectionScanner 完整跑)
  TaskAnalyzeDependencies → 正常执行 (DependencyAnalyzer 完整跑)
  
  TaskBuildBundles        → [SKIP] BuildMode=Virtual 时 pass-through
                             不调用 BuildPipeline.BuildAssetBundles
                             保留 Context.Assets (asset→bundle 映射已有)
  
  TaskVerifyBuildResult   → [SKIP] 没有 .bundle 文件可验证
  
  TaskGenerateManifest    → 正常执行的核心逻辑
                             构建 ABManifest 对象 (Asset/Bundle entries, 依赖关系, Labels/Tags)
                             但不序列化到磁盘 (或输出到 Library/ 临时目录)
  
  TaskOrganizeOutput      → [SKIP] 没有 bundle 文件可整理
```

每个需要跳过的 Task 只需在 `Execute` 开头加:

```csharp
public override Dictionary<string, BuildContext> Execute(BuildContext context)
{
    if (context.BuildMode == EBuildMode.Virtual)
        return new Dictionary<string, BuildContext> { { "Output", context } };
    
    // 原有逻辑不变...
}
```

### 对比 Addressables Virtual Mode

| 维度 | Addressables Virtual | FYAsset Simulate |
|------|---------------------|-----------------|
| 构建脚本 | 独立 `BuildScriptVirtualMode` 类 | 复用 DAG + Task 跳过开关 |
| 打包算法 | `PrepGroupBundlePacking` (共享) | CollectionScanner + DependencyAnalyzer (共享) |
| 虚拟产物 | `VirtualAssetBundleRuntimeData` | 内存 ABManifest (同数据结构, 不序列化) |
| 加载 Provider | `VirtualAssetBundleProvider` (独立) | `EditorBackend` (Editor + Simulate 共用) |
| 代码增量 | ~1200 行 (全套 Virtual 体系) | ~200 行 (枚举 + skip 判断 + EditorBackend + EditorAssetIndex) |

---

## EditorBackend 设计 (复用)

### 单一 Backend 类

```csharp
#if UNITY_EDITOR
public class EditorBackend : IPackageBackend
{
    private IAssetIndex _index;
    
    public EditorBackend(IAssetIndex index)
    {
        _index = index;
    }
    
    public async Task<(T, RuntimeMessage)> LoadAssetAsync<T>(string key) where T : UnityEngine.Object
    {
        // 强制异步: 暴露 Editor 下的时序 bug (Q1 决策)
        await Task.Yield();
        
        var entry = _index.QueryByAddress(key);
        if (entry == null)
            return (null, RuntimeMessage.NotFound(key));
        
        var asset = AssetDatabase.LoadAssetAtPath<T>(entry.AssetPath);
        return asset != null
            ? (asset, null)
            : (null, RuntimeMessage.Error(RuntimeErrorCodes.AssetNotFound,
                $"Asset not found at path: {entry.AssetPath}"));
    }
    
    public (T, RuntimeMessage) LoadAssetSync<T>(string key) where T : UnityEngine.Object
    {
        // 同步路径不加 Yield, 对标 ABPackageBackend 的同步方法
        var entry = _index.QueryByAddress(key);
        if (entry == null)
            return (null, RuntimeMessage.NotFound(key));
        
        var asset = AssetDatabase.LoadAssetAtPath<T>(entry.AssetPath);
        return asset != null
            ? (asset, null)
            : (null, RuntimeMessage.Error(RuntimeErrorCodes.AssetNotFound, ...));
    }
    
    // Unload 是空操作 (Editor 下 AssetDatabase 不管理生命周期)
    public void UnloadAsset(string key) { }
    public void UnloadByEntryId(string entryId) { }
}
#endif
```

**增量: ~60 行。** 不区分 Editor/Simulate——通过构造函数注入不同 Index 实例来分化行为。

### EditorAssetIndex (Editor 模式专用)

```csharp
#if UNITY_EDITOR
public class EditorAssetIndex : IAssetIndex
{
    private Dictionary<string, RuntimeAssetEntry> _addressToEntry;
    
    // 从 CollectorSetting 直接构建正向索引
    // 不跑完整 CollectionScanner, 不做依赖分析
    // 只构建 address → EntryId/AssetPath/PrimaryType/Labels 的基本映射
    public void Rebuild(CollectorSetting setting)
    {
        foreach (var pkg in setting.Packages)
        foreach (var grp in pkg.Groups)
        foreach (var collector in grp.Collectors)
        {
            if (!collector.Enabled) continue;
            var assets = AssetDatabase.FindAssets("t:Object", new[] { collector.CollectPath });
            foreach (var guid in assets)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var address = AddressRule.GetAddress(path, collector.AddressRuleName);
                _addressToEntry[address] = new RuntimeAssetEntry
                {
                    EntryId = guid,
                    Address = address,
                    AssetPath = path,
                    PrimaryType = AssetDatabase.GetMainAssetTypeAtPath(path)?.Name ?? "Unknown",
                    Labels = grp.Tags.Concat(collector.Tags).ToArray(),
                    BundleName = "" // Editor 模式不需要 BundleName
                };
            }
        }
    }
}
#endif
```

**增量: ~100 行。** 每次进入 Play Mode 时重建 (Q2 决策: 不缓存, 保证一致性)。

### Simulate 模式使用 ABAssetIndex

Simulate 模式下不需要 EditorAssetIndex——直接复用已有的 `ABAssetIndex`, 用虚拟 Manifest 初始化:

```csharp
// AssetPackageManager.Initialize() 中的 Simulate 分支:
#if UNITY_EDITOR
case EPlayMode.Simulate:
    var virtualManifest = BuildVirtualManifest();  // 跑 DAG (跳过 TaskBuildBundles)
    _index = new ABAssetIndex(virtualManifest);    // 复用已有代码
    _backend = new EditorBackend(_index);           // 共用 EditorBackend
    break;
#endif
```

`BuildVirtualManifest()`: 构建 DAG, 设 `BuildMode = Virtual`, 跑完获取内存中的 ABManifest。**这是 Simulate 的核心——构建管线复用 + 零新 Backend。**

---

## 模式切换

### 配置位置

`FYAssetSettings` SO (E11 产物):

```csharp
public class FYAssetSettings : ScriptableObject
{
    // 第一层: 后端选择
    [Header("Backend")]
    public bool UseABBackend = false;
    
    // 第二层: Editor PlayMode (UseABBackend=true 时生效)
    #if UNITY_EDITOR
    [Header("Play Mode")]
    public EPlayMode PlayMode = EPlayMode.Editor;
    #endif
}
```

### Initialize 改造

```csharp
// AssetPackageManager.Initialize()
public async Task Initialize()
{
#if UNITY_EDITOR
    var settings = FYAssetSettings.Instance;
    if (settings.UseABBackend)
    {
        switch (settings.PlayMode)
        {
            case EPlayMode.Editor:
                _index = new EditorAssetIndex(collectorSetting);
                _backend = new EditorBackend(_index);
                return;
                
            case EPlayMode.Simulate:
                var virtualManifest = BuildVirtualManifest(collectorSetting);
                _index = new ABAssetIndex(virtualManifest);
                _backend = new EditorBackend(_index);
                return;
                
            case EPlayMode.Runtime:
                // 走真实 AB 加载, fall through
                break;
        }
    }
    else
    {
        // Addressables 路径, 不变
    }
#endif
    // Runtime 模式 (真机 + Editor 下 PlayMode.Runtime)
    var manifest = await ManifestLoader.LoadAsync();
    _index = new ABAssetIndex(manifest);
    _backend = new ABPackageBackend(manifest, new ABBundleLoader(manifest));
}
```

---

## Scene 加载处理

Editor/Simulate 模式下场景加载使用 `EditorSceneManager.LoadSceneAsyncInPlayMode`:

```csharp
#if UNITY_EDITOR
public async Task LoadSceneAsync(string sceneAddress, LoadSceneMode mode)
{
    var entry = _index.QueryByAddress(sceneAddress);
    var parameters = new LoadSceneParameters(mode);
    await EditorSceneManager.LoadSceneAsyncInPlayMode(entry.AssetPath, parameters);
}
#endif
```

---

## PlayMode 标识

不在 Game 视图左上角独立显示（Q4 决策: 整合到调试面板）。调试面板 (缺口 2) 的顶部状态栏显示:

```
┌──────────────────────────────────────────────┐
│ FYAsset Runtime Debugger                      │
│ Mode: Editor | Backend: EditorBackend         │
│ Bundles: 0 loaded | Handles: 12 active       │
└──────────────────────────────────────────────┘
```

---

## 与 EditorStagedVerification 的关系

`draft-editor-staged-verification-20260510.md` 中 Stage 4 的 Scan Preview 功能天然支持 Simulate 模式:

- Scan Preview 按钮 → 跑 CollectionScanner → 展示地址分配结果
- Simulate 模式 Play → 跑完整构建管线 (跳过 BuildAssetBundles) → 用虚拟 Manifest 加载

两者共享 CollectionScanner, Simulate 多跑了 DependencyAnalyzer + PackRule + TaskGenerateManifest。

---

## 增量统计

| 文件 | 操作 | 估计行数 |
|------|------|:---:|
| `EBuildMode.cs` | 新增 | 5 |
| `EPlayMode.cs` | 新增 | 10 |
| `EditorBackend.cs` | 新增 | 60 |
| `EditorAssetIndex.cs` | 新增 | 100 |
| `TaskBuildBundles.cs` | 修改 (+skip) | 5 |
| `TaskVerifyBuildResult.cs` | 修改 (+skip) | 5 |
| `TaskOrganizeOutput.cs` | 修改 (+skip) | 5 |
| `TaskGenerateManifest.cs` | 修改 (内存模式) | 10 |
| `AssetPackageManager.cs` | 修改 (Initialize 分支) | 20 |
| `FYAssetSettings.cs` | 修改 (+PlayMode 字段) | 5 |

**总计: ~225 行, 4 新文件, 7 文件修改。零新 IBackend 接口, 零新 构建流程。**

---

## 执行顺序建议

```
E11 (FYAssetSettings SO + Settings 面板)
  → EPlayMode 枚举 + PlayMode 字段随 E11 一起落地
  → 此时 PlayMode 字段在 SO 上可见, 但逻辑未实现 (默认 Runtime)
  
E11 之后:
  → EBuildMode 枚举落地 + Task 跳过开关
  → EditorAssetIndex 落地
  → EditorBackend 落地
  → AssetPackageManager.Initialize 加分支
  → 调试面板加 PlayMode 状态显示
```

---

## 已收敛决策

| # | 决策 | 结论 | 日期 |
|---|------|------|------|
| 1 | SO 形式 | FYAssetSettings SO (Runtime 程序集) | 2026-05-11 |
| 2 | PlayMode 配置位置 | FYAssetSettings.PlayMode (Q3) | 2026-05-12 |
| 3 | 异步支持 | EditorBackend 加 `await Task.Yield()` (Q1) | 2026-05-12 |
| 4 | 虚拟 Manifest 缓存 | 每次 Play 重新生成, 不缓存 (Q2) | 2026-05-12 |
| 5 | PlayMode 标识 | 整合到调试面板 (Q4) | 2026-05-12 |
| 6 | USE_AB_BACKEND 过渡 | 两层开关: UseABBackend + EPlayMode, 不冲突 | 2026-05-12 |
| 7 | Simulate 构建方案 | Task 加 EBuildMode 开关跳过 BuildBundles, 复用管线 | 2026-05-12 |
| 8 | 加载后端方案 | 单一 EditorBackend, Editor/Simulate 通过不同 Index 区分 | 2026-05-12 |
| 9 | 实现顺序 | Editor + Simulate 一起做, 共享 EditorBackend + EditorAssetIndex | 2026-05-12 |
| 10 | 虚拟 Manifest 深度 | 等同真实 ABManifest (Asset/Bundle entries + 依赖), 仅不写磁盘 | 2026-05-12 |
| 11 | Addressables 参考 | `PrepGroupBundlePacking` 共享模式 + `VirtualAssetBundleRuntimeData` 设计 | 2026-05-12 |

---

## 变更记录

| Date | Change |
|------|--------|
| 2026-05-06 | Initial draft — 三模式定义 + EditorBackend/SimulateBackend 设计 + E8 依赖 |
| 2026-05-12 | 重大收敛: Task 跳过方案替代 SimulateBackend; EditorBackend 统一 Editor+Simulate; 两层开关模型; Addressables Virtual Mode 参考; 10→11 决策收敛 |

---

## 附录: 原始草案 (2026-05-06, 已归档)

> 以下为 2026-05-06 原始草案内容, 保留作为设计演变记录。
> 主要差异: (1) Simulate 模式使用独立 SimulateBackend (现改为 Task 跳过 + 共用 EditorBackend);
> (2) 依赖 E8 CollectorReverseIndex (现依赖已存在组件);
> (3) 4 个待确认问题 (现已全部收敛)。
> 原始文本不再作为有效设计参考——以上收敛后内容为准。

<details>
<summary>点击展开原始草案</summary>

### 原始: 三模式定义

```csharp
public enum EPlayMode
{
    Editor = 0,      // 对标 Fast Mode: AssetDatabase 直接加载
    Simulate = 1,    // 对标 Virtual Mode: 模拟 Bundle 分组但不打包
    Runtime = 2,     // 对标 Packed Mode: 真实 AB 加载
}
```

### 原始: SimulateBackend 设计

```csharp
#if UNITY_EDITOR
public class SimulateBackend : IPackageBackend
{
    private ABManifest _virtualManifest;
    
    public SimulateBackend(CollectorSetting setting)
    {
        // 运行 CollectionScanner + DependencyAnalyzer 生成虚拟 Manifest
        _virtualManifest = CollectionScanner.BuildVirtualManifest(setting);
    }
    
    public async Task<(Object, RuntimeMessage)> LoadAssetAsync(string key, Type type)
    {
        // 1. 用 _virtualManifest 解析 address → entry → bundle
        // 2. 日志输出模拟的 Bundle 加载顺序（含依赖）
        // 3. 实际用 AssetDatabase 加载
        // 4. 验证资产确实存在于声称的 Bundle 中
    }
}
#endif
```

### 原始: 待确认问题 (已于 2026-05-12 全部收敛)

1. ~~EditorBackend 是否需要支持异步？~~ → 必须, 加 `await Task.Yield()` (Q1)
2. ~~SimulateBackend 的虚拟 Manifest 是每次 Play 重新生成，还是缓存到磁盘？~~ → 每次重新生成 (Q2)
3. ~~PlayMode 配置放 BuildPipelineConfig 还是独立 SO？~~ → FYAssetSettings (Q3)
4. ~~是否需要在 Game 视图顶部显示当前 PlayMode 标识？~~ → 整合到调试面板 (Q4)

</details>
