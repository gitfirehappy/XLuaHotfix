# Draft: PlayMode 三模式设计（对标 Addressables Fast/Virtual/Packed）

> **Status**: Draft — 2026-05-06，待 E8 + E6 完成后细化为正式 Plan
> **Dependencies**: E8 (CollectorReverseIndex), E6 (TaskGenerateManifest), E5-2b (完整构建管线)
> **Replaces**: `FYAssetConstants.USE_AB_BACKEND` 编译期常量

---

## 动机

当前系统通过 `USE_AB_BACKEND` 常量在 Addressables 和自研 AB 之间切换。一旦完全移除 Addressables，开发期将失去"不打包直接 Play"的能力。需要对标 Addressables 三模式，在自研体系内提供等价的开发体验。

## 三模式定义

```csharp
public enum EPlayMode
{
    Editor = 0,      // 对标 Fast Mode: AssetDatabase 直接加载
    Simulate = 1,    // 对标 Virtual Mode: 模拟 Bundle 分组但不打包
    Runtime = 2,     // 对标 Packed Mode: 真实 AB 加载
}
```

| 模式 | 加载方式 | 需要打包 | 需要 Manifest | 开发期用途 |
|------|---------|:---:|:---:|-----------|
| Editor | AssetDatabase.LoadAssetAtPath | ❌ | ❌ | 日常开发，最快迭代 |
| Simulate | AssetDatabase + 虚拟 Manifest | ❌ | 虚拟生成 | 验证配置/分组/依赖 |
| Runtime | AssetBundle.LoadFromFile | ✅ | ✅ | 上线前测试，线上运行 |

## 架构设计

```
AssetPackageManager.LoadByAddress<T>("icon_home")
        │
        ▼
    IPackageBackend (已有接口)
        │
        ├─ EditorBackend (#if UNITY_EDITOR)
        │     └─ EditorAssetIndex: address → assetPath
        │           └─ AssetDatabase.LoadAssetAtPath<T>(path)
        │
        ├─ SimulateBackend (#if UNITY_EDITOR)
        │     └─ 虚拟 ABManifest (CollectionScanner 生成)
        │           └─ 走完整解析流程 → AssetDatabase 加载
        │
        ├─ ABPackageBackend (已实现)
        │     └─ ABManifest → ABBundleLoader → AssetBundle
        │
        └─ AddressablesBackend (过渡期保留，最终移除)
```

## EditorBackend 设计

### 核心：EditorAssetIndex

```csharp
#if UNITY_EDITOR
public class EditorAssetIndex : IAssetIndex
{
    // 正向索引：address → assetPath（加载用）
    private Dictionary<string, string> _addressToPath;
    
    // 与 E8 的 CollectorReverseIndex 共享扫描结果
    // ReverseIndex: assetPath → CollectorRef（Inspector 勾选用）
    // EditorAssetIndex: address → assetPath（加载用）
    // 两者在同一次 Rebuild 中同时构建
    
    public void Rebuild(CollectorSetting setting)
    {
        foreach Package → foreach Group → foreach Collector:
            foreach asset in CollectPath:
                string address = AddressRule.GetAddress(asset);
                _addressToPath[address] = assetPath;
    }
    
    // IAssetIndex 实现
    public RuntimeAssetEntry QueryByAddress(string address) { ... }
    public RuntimeAssetEntry QueryByEntryId(string guid) { ... }
}
#endif
```

### EditorBackend 实现

```csharp
#if UNITY_EDITOR
public class EditorBackend : IPackageBackend
{
    private EditorAssetIndex _index;
    
    public async Task<(Object, RuntimeMessage)> LoadAssetAsync(string key, Type type)
    {
        if (!_index.TryGetAssetPath(key, out string path))
            return (null, RuntimeMessage.LoadFailed(key, "Address not found in EditorAssetIndex"));
        
        var asset = AssetDatabase.LoadAssetAtPath(path, type);
        return asset != null 
            ? (asset, null) 
            : (null, RuntimeMessage.LoadFailed(key, $"Asset not found at {path}"));
    }
    
    // Unload 是空操作（Editor 下由 GC 管理）
    public void UnloadAsset(string key) { }
}
#endif
```

## SimulateBackend 设计

```csharp
#if UNITY_EDITOR
public class SimulateBackend : IPackageBackend
{
    private ABManifest _virtualManifest;
    
    public SimulateBackend(CollectorSetting setting)
    {
        // 运行 CollectionScanner + DependencyAnalyzer 生成虚拟 Manifest
        // 不调用 Unity BuildPipeline，不产出真实 Bundle 文件
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

## 模式切换

### 配置位置

在 `BuildPipelineConfig` SO 上新增字段（或独立 `RuntimeConfig` SO）：

```csharp
[Header("Play Mode")]
public EPlayMode PlayMode = EPlayMode.Editor;
```

Editor 下可在 Inspector 中下拉切换，无需重编译。

### Initialize 改造

```csharp
// AssetPackageManager.Initialize()
public async Task Initialize()
{
#if UNITY_EDITOR
    var config = LoadConfig();
    switch (config.PlayMode)
    {
        case EPlayMode.Editor:
            var editorIndex = new EditorAssetIndex(collectorSetting);
            _index = editorIndex;
            _backend = new EditorBackend(editorIndex);
            return;
            
        case EPlayMode.Simulate:
            var simBackend = new SimulateBackend(collectorSetting);
            _index = simBackend.Index;
            _backend = simBackend;
            return;
    }
#endif
    // Runtime 模式
    var manifest = await ManifestLoader.LoadAsync();
    _index = new ABAssetIndex(manifest);
    _backend = new ABPackageBackend(manifest, new ABBundleLoader(manifest));
}
```

## 与 E8 的关系

E8 的 `CollectorReverseIndex` 和 EditorBackend 的 `EditorAssetIndex` 可以合并为一个类：

```csharp
public class CollectorEditorIndex
{
    // 正向：address → assetPath（EditorBackend 加载用）
    Dictionary<string, string> _addressToPath;
    
    // 反向：assetPath → CollectorRef（Inspector 勾选用）
    Dictionary<string, CollectorRef> _pathToCollector;
    
    // 同一次 Rebuild 同时构建两个索引
    void RebuildIfDirty(CollectorSetting setting) { ... }
}
```

## 执行顺序

```
E8 Phase 1D (CollectorReverseIndex)
    → 扩展为 CollectorEditorIndex（加正向索引）
    → EditorBackend 实现
    → EPlayMode 枚举 + Initialize 改造
    → 移除 USE_AB_BACKEND 常量

E6 (TaskGenerateManifest)
    → SimulateBackend 实现（需要虚拟 Manifest 生成）

最终清理
    → 移除 AddressablesBackend
    → 移除 Addressables 包依赖
```

## 待确认问题

1. EditorBackend 是否需要支持异步？（AssetDatabase 是同步的，但接口是 async）
2. SimulateBackend 的虚拟 Manifest 是每次 Play 重新生成，还是缓存到磁盘？
3. PlayMode 配置放 BuildPipelineConfig 还是独立 SO？
4. 是否需要在 Game 视图顶部显示当前 PlayMode 标识（防止误以为在测真包）？
