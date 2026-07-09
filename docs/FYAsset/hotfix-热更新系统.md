# 热更新系统

> 返回总览：[资源管理架构文档](./资源管理架构文档.md)

> **关联代码**
>
> `Assets/FYAsset/Scripts/AA/Hotfix/AAHotfixManager.cs` · `Assets/FYAsset/Scripts/AB/Hotfix/ABHotfixManager.cs` · `Assets/FYAsset/Scripts/Shared/Hotfix/HotfixFlowBase.cs` · `Assets/FYAsset/Scripts/Shared/Compatibility/HotfixManager.cs` · `Assets/FYAsset/Scripts/Shared/Hotfix/IHotfixPipeline.cs` · `Assets/FYAsset/Scripts/AA/Hotfix/Backends/Addressables/` · `Assets/FYAsset/Scripts/AB/Hotfix/Backends/AB/`

---

## 概述

热更新系统负责在 App 启动后检查远端资源更新、下载新 Bundle、切换到最新版本。当前有 `AAHotfixManager` 与 `ABHotfixManager` 两个 concrete 入口，共用 `HotfixFlowBase` 的 11 步流程；旧 `HotfixManager` 作为兼容门面保留给现有启动调用方。

设计目标：
- 热更流程本身**与后端解耦** — 编排逻辑写在 `HotfixFlowBase` 里，AA/AB concrete flow 只提供后端、URL/重试设置和最终 runtime manager 初始化
- AA 路径仍依赖 Addressables catalog 进行资源定位；自定义 AAManifest 用于版本、Bundle 校验和查询索引。AB 路径用自研 ABManifest 替代 Addressables catalog。

---

## 核心组件

| 组件 | 职责 |
|------|------|
| `AAHotfixManager` | AA concrete 热更入口，使用 AA 设置、`AAHotfixBackend` 和 `AAPackageManager` |
| `ABHotfixManager` | AB concrete 热更入口，使用 AB 设置、`ABHotfixBackend` 和 `ABPackageManager` |
| `HotfixManager` | 兼容门面，根据 `UseABBackend` 路由旧调用方 |
| `HotfixFlowBase` | 11 步热更流程编排，进度/错误回调，BuildIndex 初始化 |
| `IHotfixPipeline` | 后端抽象接口，定义 5 个后端差异方法 |
| `ABHotfixBackend` | AB 后端实现 — 基于 ABManifest，无需 Addressables 依赖 |
| `AAHotfixBackend` | AA 后端实现 — 基于 Addressables + catalog，同时使用 AAManifest 作为版本和查询索引数据 |
| `HotfixContext` | 热更流程上下文，携带 BuildIndex / 目标包名 / URL 等 |
| `HotfixVersionInfo` | 统一版本视图，屏蔽 AA/AB 的数据模型差异 |
| `BundleDownloadItem` | 下载项最小信息集（BundleName / FileHash / FileCRC / FileSize） |
| `HotfixStepResult` | 结构化步骤结果，替代裸 bool 返回值 |
| `PackageCleaner` | 热更目录清理（大版本清空 / 旧包体轮转删除） |
| `NetworkDownloader` | 网络下载器，提供文本/字节/文件下载原语；Bundle 重试策略由 `HotfixFlowBase` 统一控制 |

---

## IHotfixPipeline — 后端抽象接口

```csharp
public interface IHotfixPipeline
{
    // 1. 后端初始化（AA: Addressables.InitializeAsync；AB: 无操作）
    Task<HotfixStepResult> InitializeBackendAsync();

    // 2. 读取本地版本信息（无本地版本时返回 null，首次安装场景）
    Task<HotfixVersionInfo> LoadLocalVersionAsync(string currentGUIDRoot);

    // 3. 下载并解析远端版本信息（后端缓存原始数据供 PostDownload 使用）
    Task<HotfixVersionInfo> FetchRemoteVersionAsync(string remoteUrlRoot);

    // 4. 从统一版本视图提取下载列表
    IReadOnlyList<BundleDownloadItem> GetBundleDownloadList(HotfixVersionInfo remoteInfo);

    // 5. 下载完成后处理（写入 manifest / 加载外部 catalog）
    Task<HotfixStepResult> PostDownloadAsync(HotfixContext ctx);
}
```

`HotfixFlowBase` 按固定顺序调用这 5 个方法，不关心后端是 AA 还是 AB。具体后端由 `AAHotfixManager` / `ABHotfixManager` 的 concrete flow 决定；`UseABBackend` 只用于旧 `HotfixManager` 兼容门面路由。

---

## HotfixVersionInfo — 统一版本视图

AA 和 AB 后端各自有内部数据模型（AAManifest / ABManifest），但对外统一输出 `HotfixVersionInfo`：

```csharp
public class HotfixVersionInfo
{
    public VersionNumber Version;                // 版本号
    public int BundleCount;                      // Bundle 总数
    public long TotalSize;                       // 总下载大小（字节）
    public IReadOnlyList<BundleDownloadItem> Bundles;  // 下载列表
}
```

AB 后端转换规则：

- 新 ABManifest 优先用 `DeliveryBundles` 生成下载列表；
- Full 包或无差异 Hotfix 的 `DeliveryBundles` 可以为空，表示本次远端包没有需要下载的 bundle；
- 旧 JSON ABManifest 如果没有 `DeliveryBundles` 字段，则按兼容路径回退使用完整 `BundleEntries`；
- 运行时资源查找仍使用完整 `BundleEntries`，不是 `DeliveryBundles`。

---

## 完整热更流程（11 步）

```
Start
  │
  ├─[0] Load BuildIndex ── 从 StreamingAssets 读取 BuildIndex.json
  │     ├─ 初始化 RuntimePathManager（确定目录结构）
  │     ├─ 检查是否需要清理旧包（BuildGUID 变更检测）
  │     └─ 尝试从本地 PackageIndex.json 恢复断点续传状态
  │
  ├─[1] Initialize Backend ── 后端初始化（AB 路径无操作，AA 路径调 InitializeAsync）
  │
  ├─[2] Download PackageIndex ── 下载远端 PackageIndex.json
  │     └─ 解析 LatestPackage → 确定目标包名和规范化 URL
  │
  ├─[3] Load Local Version ── 从当前 GUID 目录读取本地版本信息；AA 首包可回退 StreamingAssets 中的 AAManifest
  │
  ├─[4] Fetch Remote Version ── 下载远端 ABManifest/AAManifest
  │     ├─ AB: 优先 .bin 二进制，回退 .json
  │     └─ AA: 优先 .bin 二进制，回退 .json
  │
  ├─[5] Compare Version ── 比对本地与远端版本
  │     ├─ Major 不一致 + BuildIndex.Version 匹配远端 → 全量清理（整包更新）
  │     └─ Major 不一致 + BuildIndex.Version 也不匹配 → 报错，要求下载最新整包
  │
  ├─[6] Prepare Download List ── 从远端版本信息提取 Bundle 列表
  │
  ├─[7] Download Bundles ── 并发下载（最大 6 并发）
  │     ├─ 清理目标 bundles 目录中的 stale .tmp 文件
  │     ├─ 同名 Hash 优化：检查本地是否有相同 Hash 的旧 Bundle，复制到 .tmp 并校验后替换
  │     ├─ 网络下载写入 .tmp，CRC 通过后再替换目标 Bundle
  │     ├─ 下载失败与 CRC 失败走同一重试策略
  │     └─ 清理旧 Build_xxxx 目录（保留最近 1 个）
  │
  ├─[8] Post Download ── 后端特定后处理
  │     ├─ AB: 将缓存的 ABManifest 写入目标目录
  │     └─ AA: 下载 catalog.json + 加载外部 Catalog
  │
  ├─[9] Apply Update ── 更新本地 PackageIndex 指针 + RuntimePathManager 切换
  │
  └─[10] Finalize ── 初始化 AAPackageManager 或 ABPackageManager
```

### 进度回调

`HotfixManager` 兼容门面和 AA/AB concrete manager 都提供事件供 UI 层监听：

- `OnStepChanged(string stepName)` — 步骤切换时触发
- `OnProgress(float progress, string stepName)` — 进度更新（0~1 全局进度 + 当前步骤名）

总步骤表由 `StepNames` 数组驱动，计算公式：`overallProgress = (stepIndex + stepProgress) / StepNames.Length`。

### 错误处理

- `OnError(string message)` — 非致命错误通过此事件通知 UI
- 致命错误（如无法加载 BuildIndex）直接 return，整个热更流程中止
- PackageIndex 下载失败会降级使用本地已有资源；Bundle 下载失败会按配置重试，重试耗尽后中止本次热更并保留旧资源

### Bundle 下载安全策略

Bundle 下载阶段由 `HotfixFlowBase` 统一管理重试与校验：

- `HotfixUrl`、最大重试次数和基础退避时间来自当前后端设置：AA 读取 `FYAssetAASettings`，AB 读取 `FYAssetABSettings`。
- 默认最大重试次数为 3，基础退避时间为 1 秒，即失败后按 1s / 2s / 4s 等待重试。
- 网络下载先写入 `{bundleName}.tmp`，只有下载完成且 CRC 校验通过后才替换目标 Bundle。
- 本地同 Hash 复用也先复制到 `.tmp`，CRC 通过后再替换目标文件。
- 启动下载前会清理目标 bundles 目录中的 stale `.tmp` 文件。
- `FileCRC == 0` 表示 CRC 元数据不可用，会输出 Warning 并跳过 CRC 校验。

---

## URL 与本地路径规范

- 远端路径统一通过 `FYAssetPathUtility.JoinUrl(...)` 生成，包括 `PackageIndex.json`、包体根、manifest、`catalog.json` 和 bundle 下载 URL；当前后端设置中的 `HotfixUrl` 带不带尾斜杠都应得到相同的单斜杠 URL。
- 本地热更目录、目标包体目录、bundle 保存路径、manifest 写入路径和本地 `PackageIndex.json` 使用本地文件系统路径规则拼接。
- Unity `StreamingAssets` 读取路径通过共享路径工具拼接，但 Android `jar:` URI-like 路径保持 `/` 分隔符，不会被规范化成 Windows 本地路径。
- `bundles`、`catalog.json`、`BuildIndex.json` 等跨模块目录/文件名来自 `FYAssetSettings` 常量，不在热更主链路中重复写字符串字面量。

---

## 双后端差异

| | AB 后端 | AA 后端 |
|---|---|---|
| **元数据文件** | 1 个（ABManifest.bin/.json） | 2 个（AAManifest + catalog.json） |
| **初始化** | 无操作（直接返回 Ok） | Addressables.InitializeAsync |
| **版本信息源** | ABManifest.BundleEntries；下载列表优先 ABManifest.DeliveryBundles | AAManifest.BundleEntries |
| **下载后处理** | 写 ABManifest 到磁盘 | 下载 catalog + 加载外部 Catalog |
| **格式优先级** | .bin → .json | .bin → .json |
| **Addressables 依赖** | 无 | 需要 |

---

## PackageCleaner — 目录清理

两种清理策略：

**大版本清理 (`ClearAllHotfix`)**：检测到整包覆盖安装时，清空整个 Hotfix 目录 + Unity AssetBundle 缓存 + Addressables 缓存。

**旧包体轮转 (`CleanOldBuildPackages`)**：每次热更下载完成后执行。保留最新的 N 个 Build_xxxx 目录（默认 1 个），删除更早的。防止长期使用后磁盘占用膨胀。

---

## 智能 Bundle 复用

下载阶段有一个优化：如果本地已有 Bundle 的文件 Hash 与远端完全一致，直接复制旧文件而不是从网络下载。流程：

1. 从本地版本信息建立 `Hash → BundleName` 索引
2. 遍历远端下载列表，检查本地是否有同名 Hash
3. 有匹配 → 复制到 `.tmp` + CRC 校验 + 替换目标文件 → 跳过下载
4. CRC 不匹配 → 删除 `.tmp` → 回退到网络下载

这个优化在"少量资源变更"的热更场景下效果显著——大部分 Bundle 根本没变。
