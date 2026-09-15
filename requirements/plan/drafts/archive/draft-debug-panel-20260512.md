# Draft: FYAsset Runtime Debugger — 运行时资源调试面板

> **Status**: Draft — 2026-05-12 (知识库审计讨论收敛)
> **Dependencies**: 无硬依赖 (运行时类已有, 只需加 `#if UNITY_EDITOR` 访问器)
> **Ref**: Zhihu Ch.12 ResourceDebuggerWindow, Unity Profiler/Frame Debugger 布局分析

---

## 动机

运行时资源泄漏排查目前完全靠 Debug.Log + 重新构建 + 复现——每次泄漏都要走一遍"猜 → 加 log → 构建 → 复现 → 看 log"的循环。需要一个零成本的观测入口: 打开面板就能看到所有已加载 Bundle 的引用计数、Handle 状态、依赖关系。

## 设计原则

1. **观测不污染被观测系统** — 运行时只维护最小状态 (RefCount), 面板打开时反向推导引用关系, compute-on-demand
2. **零 build 开销** — 所有访问器 `#if UNITY_EDITOR` 剥离, 真机零增量
3. **类型安全** — 禁止反射, 用编译期类型安全的访问器方法
4. **对标 Unity 内置工具 UX** — 布局参照 Profiler/Frame Debugger, 降低学习成本

---

## 一、布局设计 (对标 Frame Debugger)

```
┌──────────────────────────────────────────────────────────┐
│ [▶ Recording] [⏸ Pause] │ [Export ▼] [Force GC]        │ ← 工具栏
│ Mode: Runtime | Bundles: 42 loaded | Handles: 128 active│ ← 状态摘要行
├──────────────────────┬───────────────────────────────────┤
│ [Bundles] [Handles]  │ 选中项详情:                       │
│                       │                                   │
│ Search: [__________] │ BundleName: ui_login.bundle       │
│                       │ RefCount: 3     Status: Loaded    │
│ Name        │Ref│St │ Size: 2.4 MB    Assets: 15         │
│ ui_login.b  │ 3 │✓  │                                   │
│ fonts.b     │ 2 │✓  │ ▶ Referenced By (3)               │
│ shaders.b   │ 1 │✓  │   Handle → UILoginPanel (EntryId)  │
│ scene_main.b│ 0 │⚠  │   Handle → UIButton.prefab         │
│ audio.b     │ 0 │✓  │   Bundle ← ui_shop.bundle          │
│ ...         │   │   │                                   │
│               │     │ ▶ Dependencies (2)                 │
│               │     │   common_tex.bundle                │
│               │     │   shaders.bundle                   │
│               │     │                                   │
│               │     │ ▶ Contained Assets (15)            │
│               │     │   Assets/UI/Login/LoginPanel.prefab│
│               │     │   Assets/UI/Login/LoginBG.png      │
│               │     │   ...                              │
├──────────────────────┴───────────────────────────────────┤
│ ⚠ Anomaly: audio.bundle has RefCount=0 but still loaded  │ ← 异常提示 (条件显示)
└──────────────────────────────────────────────────────────┘
```

### Tab 切换

- **Bundles Tab**: 已加载 Bundle 列表 (多列), 选中后右侧显示详情
- **Handles Tab**: 活跃 Handle 列表 (多列), 选中后右侧显示 Handle 详情 (EntryId / BundleName / RefCount / Generation / Error)

### 列定义 (Bundles)

| 列 | 宽度 | 排序 | 说明 |
|---|:---:|------|------|
| Name | 自适应 | 字典序 | Bundle 文件名 (如 `ui_login.bundle`) |
| RefCount | 60px | 降序 | 引用计数, 粗体。降序优先 — 有引用的排前面 |
| Status | 60px | — | ✓=Loaded, ⟳=Loading, ✗=Error, ⚠=ZeroRef |
| Size | 80px | — | 文件大小 (从 Manifest 读取, 不可用时显示 `-`) |
| Assets | 60px | — | 包含资源数 (从 Manifest 读取) |

### 列定义 (Handles)

| 列 | 宽度 | 排序 | 说明 |
|---|:---:|------|------|
| EntryId | 自适应 | — | 资源 GUID (截断显示前 8 位) |
| BundleName | 100px | 字典序 | 所属 Bundle |
| RefCount | 60px | 降序 | Handle 持有计数 |
| Address | 150px | — | 加载时使用的 Address |
| Error | 80px | — | 错误码 (正常为空) |

### 颜色编码

| 颜色 | Hex | 条件 | 含义 |
|------|-----|------|------|
| 浅绿 | `#C8E6C9` | RefCount > 0, Loaded | 有活跃引用, 正常使用中 |
| 浅黄 | `#FFF9C4` | RefCount == 0, Loaded | 零引用但仍在内存 — **泄漏嫌疑** |
| 浅红 | `#FFCDD2` | LoadError | 加载失败, 需排查原因 |
| 浅蓝 | `#BBDEFB` | 选中 | 高亮当前选中行 |
| 默认 | — | RefCount == 0, Unloaded | 生命周期正常 (通常不会出现在列表中) |

---

## 二、数据刷新策略

### Auto-Refresh + Pause 模型 (对标 Profiler Record)

```
工具栏按钮:
  [▶ Recording] / [⏸ Paused]     ← 切换

Recording 状态:
  - OnInspectorUpdate ~10Hz 自动刷新列表
  - 所有运行时访问器实时调用
  - 适合"看着状态变化"的实时诊断

Paused 状态:
  - 冻结当前快照, 停止调用访问器
  - 列表和详情不更新
  - 底部状态栏显示 "[PAUSED] Snapshot at 15:30:42"
  - 适合"仔细分析某个瞬间的状态"
```

### 性能考虑

- 每次刷新: Dictionary→List copy (非反射), 对于 <100 个 Bundle 开销 <1ms
- 详情面板的"谁在引用我"反向推导: 仅在选中 Bundle 时计算, 非列表刷新时
- Pause 时完全停止所有数据访问
- `OnInspectorUpdate` 不是 `Update` — 只在 Inspector 可见时刷新, 面板不可见时零开销

---

## 三、异常检测

### 规则 1: 零引用未卸载

```
Bundle RefCount == 0 && Bundle.Status == Loaded → 浅黄标记 ⚠
```

可能原因: UnloadBundle 未被调用, 或 Bundle 刚释放但 GC 尚未回收。如果持续超过 30 秒, 大概率是泄漏。

### 规则 2: 有引用无追踪者

```
Bundle RefCount > 0 && (无 Handle 引用此 Bundle) && (无其他 Bundle 依赖此 Bundle)
→ 底部异常条显示警告
```

可能原因: 代码直接调了 `ABBundleLoader.LoadBundle` 绕过 `AssetPackageManager`, 或手动增加了 BundleInfo.RefCount。

### 规则 3: 加载失败

```
Bundle.Status == Error → 浅红标记 ✗
```

详情面板显示错误码 + 错误消息。提供"从此 Bundle 的依赖链向上追溯"的能力——如果 `ui_login.bundle` 依赖 `fonts.bundle`, 而 `fonts.bundle` 加载失败, 可以快速定位根因。

---

## 四、快照导出

### 格式选择

工具栏 `[Export ▼]` 下拉:

| 选项 | 格式 | 文件名 | 用途 |
|------|------|------|------|
| Export as TXT | 带缩进文本 | `BundleMemoryDump_{timestamp}.txt` | 发给同事, bug report 附件 |
| Export as JSON | 结构化 JSON | `BundleMemoryDump_{timestamp}.json` | CI 脚本分析, 回归测试 |

### JSON Schema

```json
{
  "timestamp": "2026-05-12T15:30:42+08:00",
  "playMode": "Runtime",
  "summary": {
    "totalLoadedBundles": 42,
    "totalActiveHandles": 128,
    "totalAssetCacheEntries": 56,
    "bundlesWithZeroRef": 3,
    "handlesWithErrors": 0
  },
  "bundles": [
    {
      "name": "ui_login.bundle",
      "refCount": 3,
      "status": "Loaded",
      "sizeBytes": 2516582,
      "assetCount": 15,
      "dependencies": ["fonts.bundle", "common_tex.bundle"]
    }
  ],
  "handles": [
    {
      "entryId": "abc123def456",
      "bundleName": "ui_login.bundle",
      "refCount": 1,
      "address": "UILoginPanel",
      "error": null
    }
  ],
  "anomalies": [
    {
      "type": "ZeroRefNotUnloaded",
      "bundleName": "audio.bundle",
      "duration": "PT45S"
    }
  ]
}
```

JSON 的持续集成价值: 可写脚本对比两次快照, `bundlesWithZeroRef` 持续增长 → 泄漏告警。

---

## 五、Runtime 访问器 (零侵入 = 零反射)

用 `#if UNITY_EDITOR` 条件编译 + 类型安全的结构体, 不用反射。

### ABBundleLoader 新增 (~15 行)

```csharp
#if UNITY_EDITOR
public readonly struct BundleDebugInfo
{
    public string BundleName;
    public int RefCount;
    public bool IsLoaded;
    public string[] DependencyBundleNames;
}

public List<BundleDebugInfo> GetLoadedBundlesDebugInfo()
{
    var result = new List<BundleDebugInfo>(_bundleCache.Count);
    foreach (var kv in _bundleCache)
        result.Add(new BundleDebugInfo
        {
            BundleName = kv.Key,
            RefCount = kv.Value.RefCount,
            IsLoaded = kv.Value.Bundle != null,
            DependencyBundleNames = kv.Value.DependencyBundleNames
        });
    return result;
}
#endif
```

### HandleRegistry 新增 (~15 行)

```csharp
#if UNITY_EDITOR
internal readonly struct HandleDebugInfo
{
    public string EntryId;
    public string BundleName;
    public int RefCount;
    public int Generation;
    public bool HasError;
    public string ErrorCode;
}

internal static List<HandleDebugInfo> GetActiveHandlesDebugInfo()
{
    var result = new List<HandleDebugInfo>();
    for (int i = 0; i < _count; i++)
    {
        if (_slots[i].RefCount > 0)
            result.Add(new HandleDebugInfo
            {
                EntryId = _slots[i].EntryId,
                BundleName = _slots[i].BundleName,
                RefCount = _slots[i].RefCount,
                Generation = _slots[i].Generation,
                HasError = _slots[i].Error != null,
                ErrorCode = _slots[i].Error?.Code
            });
    }
    return result;
}
#endif
```

### ABPackageBackend 新增 (~10 行)

```csharp
#if UNITY_EDITOR
public readonly struct AssetCacheDebugInfo
{
    public string EntryId;
    public string BundleName;
    public bool IsCached;
}

public List<AssetCacheDebugInfo> GetAssetCacheDebugInfo()
{
    var result = new List<AssetCacheDebugInfo>(_assetCache.Count);
    foreach (var kv in _assetCache)
        result.Add(new AssetCacheDebugInfo
        {
            EntryId = kv.Key,
            BundleName = kv.Value.BundleName,
            IsCached = kv.Value.Asset != null
        });
    return result;
}
#endif
```

### AssetPackageManager 桥接 (HandleRegistry 是 internal)

```csharp
#if UNITY_EDITOR
public static List<HandleDebugInfo> GetActiveHandlesDebugInfo()
{
    return HandleRegistry.GetActiveHandlesDebugInfo();
}
#endif
```

### 总 Runtime 增量

| 类 | 方法 | 行数 |
|---|------|:---:|
| ABBundleLoader | GetLoadedBundlesDebugInfo() | ~15 |
| HandleRegistry | GetActiveHandlesDebugInfo() | ~15 |
| ABPackageBackend | GetAssetCacheDebugInfo() | ~10 |
| AssetPackageManager | GetActiveHandlesDebugInfo() (桥接) | ~5 |

**总计: ~45 行 Runtime 代码, 全部 `#if UNITY_EDITOR` 剥离, 零 build 开销, 零反射。**

---

## 六、"谁在引用我" 反向推导

选中 Bundle 时, 详情面板显示引用者——反向推导, 不存储在运行时状态中:

```csharp
List<string> FindReferencers(string bundleName)
{
    var refs = new List<string>();

    // 1. 活跃 Handle → 检查其关联 Entry 的 BundleName
    var handles = AssetPackageManager.GetActiveHandlesDebugInfo();
    foreach (var h in handles)
    {
        if (h.BundleName == bundleName)
            refs.Add($"Handle → EntryId={h.EntryId[..8]} (×{h.RefCount})");
    }

    // 2. 其他已加载 Bundle → 检查其依赖列表是否包含此 Bundle
    var allBundles = bundleLoader.GetLoadedBundlesDebugInfo();
    foreach (var b in allBundles)
    {
        if (b.BundleName == bundleName) continue;
        if (b.DependencyBundleNames?.Contains(bundleName) == true)
            refs.Add($"Bundle ← {b.BundleName} (RefCount={b.RefCount})");
    }

    // 3. AssetCache 中的条目
    var cache = packageBackend.GetAssetCacheDebugInfo();
    foreach (var c in cache)
    {
        if (c.BundleName == bundleName)
            refs.Add($"AssetCache → EntryId={c.EntryId[..8]}");
    }

    return refs;
}
```

O(bundles × deps + handles × bundles), 仅在选中时计算。对于 <100 Bundle 的项目可忽略。

---

## 七、与缺口的交叉依赖

| 缺口 | 交叉点 |
|------|--------|
| 缺口 1 (PlayMode) | PlayMode 标识整合到调试面板状态摘要行 |
| 缺口 4 (并发控制) | 列表显示 In-flight Task 数量 (ABBundleLoader 暴露) |
| 缺口 5 (超时) | Handle 列表过滤有 ErrorCode 的项 |
| 缺口 3 (自动绑定) | Handle 列表 → 检查是否有"游离 Handle" (RefCount>0 但无关联 GameObject) |

---

## 八、增量统计

| 文件 | 操作 | 程序集 | 估计行数 |
|------|------|:---:|:---:|
| `RuntimeDebuggerWindow.cs` | **新增** | Editor | ~350 |
| `ABBundleLoader.cs` | 修改 (+访问器) | Runtime | +15 |
| `HandleRegistry.cs` | 修改 (+访问器) | Runtime | +15 |
| `ABPackageBackend.cs` | 修改 (+访问器) | Runtime | +10 |
| `AssetPackageManager.cs` | 修改 (+桥接) | Runtime | +5 |
| `BuildPipelineWindow.cs` | 修改 (+侧栏入口) | Editor | +10 |

**总计: ~405 行, 1 新文件, 5 文件修改。Runtime 净增 ~45 行 (全部 `#if UNITY_EDITOR`)。**

---

## 已收敛决策

| # | 决策 | 结论 |
|---|------|------|
| 1 | 面板位置 | BuildPipelineWindow 侧栏新增 DEBUG 组 |
| 2 | 数据访问 | `#if UNITY_EDITOR` 类型安全访问器, 零反射 |
| 3 | 布局 | 对标 Frame Debugger: Toolbar → 状态行 → 左右分栏 → 异常提示 |
| 4 | 列定义 | Name/RefCount/Status/Size/Assets (Bundle); EntryId/BundleName/RefCount/Address/Error (Handle) |
| 5 | 颜色编码 | 浅绿/浅黄/浅红/浅蓝 — 对标 Profiler 配色体系 |
| 6 | 刷新策略 | Auto-Refresh (OnInspectorUpdate) + Pause 按钮 |
| 7 | 导出格式 | TXT + JSON 双格式, 下拉切换 |
| 8 | Handle 列表 | 独立 Tab, 与 Bundle 列表并列 |
| 9 | PayMode 指示 | 整合到状态摘要行 |
| 10 | 异常检测 | 零引用未卸载 + 有引用无追踪者 + 加载失败 |

---

## 变更记录

| Date | Change |
|------|--------|
| 2026-05-12 | Initial draft — 10 项决策收敛, 对标 Frame Debugger/Profiler 布局, 零反射访问器 |
