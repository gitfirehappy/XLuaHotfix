# Sub-Plan B4: Catalog 重定向层替换（高风险，独立评估）

> **风险**: 高（热更核心链路）
> **依赖**: B1 + B2 完成并在真机稳定验证后
> **状态**: 概念设计阶段，不在本次审批范围

---

## 背景说明

CatalogUpdater 使用了 Addressables 的深度 API，
这套机制是热更生效的核心——替换它意味着重新实现整个热更分发链路。

**开发者必须理解当前机制才能评估替换风险**：

### 当前 Catalog 机制工作原理

```
启动 → Addressables.InitializeAsync()
         ↓
         加载 StreamingAssets 内置 Catalog（包体内的资源索引）
         ↓
热更检测 → 下载远程 Catalog 文件（HotfixManager → NetworkDownloader）
         ↓
CatalogUpdater.LoadExternalCatalog()
         ↓
         用 Addressables.LoadContentCatalogAsync 加载外部 Catalog
         ↓
         用 Addressables.RemoveResourceLocator 移除内置旧索引
         ↓
         Addressables.ResourceManager.InternalIdTransformFunc
         把远程 HTTP 路径 → 本地热更目录路径
         ↓
         此后所有资源加载自动走热更目录
```

**替换这套机制，等价于自己实现一套「资源索引管理 + 路径重定向」系统。**

---

## 自研等效设计（概念）

| Addressables 能力 | 自研等效 |
|-------------------|---------|
| LoadContentCatalogAsync | 加载本地 ABManifest JSON |
| ResourceLocators | ABResourceRegistry（Key -> BundlePath 映射表） |
| RemoveResourceLocator | ABResourceRegistry.SwitchToHotfixManifest() |
| InternalIdTransformFunc | ABBundleLoader 内部路径解析（热更目录优先） |
| Addressables.InitializeAsync | ABPackageBackend.InitializeAsync |

---

## 关键设计决策（需要评审）

1. **ABManifest 格式**：参考 Addressable Catalog JSON 还是自定义格式？
2. **构建侧同步**：构建 AB 时如何生成 ABManifest？（当前 HelperBuildDataExporter 生成 AddressableLabelsConfig，需要同时生成 ABManifest）
3. **增量下载**：NetworkDownloader 当前使用 VersionState 的 bundle hash 比对，自研后如何维护？
4. **回滚机制**：替换失败时如何回退到 Addressables？

---

## 建议

- B4 启动前需要召集专项设计评审
- B1 + B2 完成后，先在真机上跑一段时间确认稳定
- B4 可作为独立的长期迭代任务，不与 B1-B3 绑定

---

## 本阶段无审批清单

B4 处于概念设计阶段，评估是否执行在 B1-B3 完成后进行。
