# Draft: 单机离线包方案

> Status: Draft — 2026-05-13, **Updated 2026-05-15**
> Scope: 构建期产出 + 运行时跳过热更流程，AB 新管线 + Legacy Addressables 双后端支持
> 不涉及: Editor PlayMode（已有 plan-playmode-draft.md）
>
> ## 2026-05-15 方案修正
>
> **核心简化**: 不做产物复制（TaskCopyToStreamingAssets），而是直接将构建输出路径重定向到 StreamingAssets。
>
> - **构建期**: `ABBuildBackend` 根据 BuildMode=Standalone 将 OutputRoot 设为 `StreamingAssets/`，`TaskOrganizeOutput` 直接把 bundles/ + ABManifest 写到 StreamingAssets，无需额外 Task
> - **运行时**: `StandaloneBuild=true` 时 HotfixManager 短路 + PathManager 不初始化热更路径 → ABBundleLoader/ManifestLoader 的回退路径天然命中 StreamingAssets
> - **关键安全约束**: 离线模式**绝不能读取热更区文件**。PathManager 在 Standalone 模式下不设置 CurrentGUIDRoot，确保 ABBundleLoader 只走 StreamingAssets 路径
> - **BuildMode 扩展**: 作为第三种并列选项 `BuildMode.StandalonePackage`（与 FullPackage/HotfixPackage 并列）
> - **不需要追加 Task**: 输出路径由 PathManager/BuildContext 灵活管理，TaskOrganizeOutput 已有的整理逻辑直接写到正确目标
>
> 以下原始分析保留作为参考，部分内容已被上述修正取代。

---

## 一、现状分析

### 已有能力（可复用）

| 组件 | 现状 | 离线包关联 |
|------|------|-----------|
| `ManifestLoader` | 热更目录优先 → StreamingAssets 回退 | ✅ 已支持从 StreamingAssets 加载 ABManifest |
| `ABBundleLoader` | `CurrentGUIDRoot/bundles/` 或 `StreamingAssets/bundles/` 双路径查找 | ✅ |
| `FileHelper` | 跨平台 I/O，Android 走 UnityWebRequest | ✅ |
| `HotfixManager` fallback | CDN 不可用时跳过下载，走本地 | ⚠️ 降级行为（打 Error log），不是设计目标 |

### 缺失

| 缺失项 | 说明 |
|--------|------|
| 构建期离线产出 | FullPackage 产物在 HotfixOutput/，未复制到 StreamingAssets/ |
| 运行时离线模式 | HotfixManager 11 步无差别执行，离线场景 8 步空转 + Error log |
| 显式开关 | 没有"不需要联网"的标记 |

---

## 二、方案

### 2.1 开关

`FYAssetSettings` 新增：

```csharp
[Header("Build Mode")]
public bool StandaloneBuild = false;  // 构建期 + 运行时双重生效
```

- 不新增菜单项，和 `BuildFullPackage` 共用入口
- SO bool 持久化，适合固定发布模式；CI 可后续加 CLI 覆盖

### 2.2 运行时改动

`HotfixManager.InitializeAsync()` — `StepLoadBuildIndexAsync` 之后插入短路：

```csharp
// StepLoadBuildIndexAsync 完成后:
PathManager.Initialize(buildIndex);
PathManager.EnsureDirectories();

if (FYAssetSettings.Instance.StandaloneBuild)
{
    await AssetPackageManager.Instance.Initialize();
    OnFinished?.Invoke();
    return;
}

// 在线模式继续原有流程 ...
```

- 不创建 Pipeline（`CreatePipeline()` 不走）
- 不联网、不下载、不比对版本
- `AssetPackageManager.Initialize()` 根据 `UseABBackend` 自行选择后端初始化

### 2.3 构建期 — AB 管线

DAG Task 逐个分析：

| Task | 离线行为 | 原因 |
|------|:---:|------|
| TaskPrepareContext | 不改 | 版本/平台/输出目录逻辑相同 |
| TaskAnalyzeDependencies | 不改 | 依赖分析和在线/离线无关 |
| TaskBuildBundles | 不改 | 同样 Bundle 构建，压缩不变 |
| TaskVerifyBuildResult | 不改 | 完整性校验离线包更需要 |
| TaskGenerateManifest | 不改 | ABManifest 运行时必需 |
| TaskOrganizeOutput | **末尾加一步** | `if (StandaloneBuild) CopyToStreamingAssets(outputDir)` |

**结论：一个 Task 都不跳，TaskOrganizeOutput 末尾加 if 分支。**

`CopyToStreamingAssets`:
- `outputDir/bundles/*` → `StreamingAssets/bundles/`
- `outputDir/ABManifest.json` → `StreamingAssets/ABManifest.json`
- `outputDir/ABManifest.bin` → `StreamingAssets/ABManifest.bin`（如存在）

### 2.4 构建期 — Legacy Addressables 管线

`LegacyAddressableBuildBackend` 改三处：

| 步骤 | 离线行为 | 原因 |
|------|:---:|------|
| `ConfigureBasicSettings` | **分支** — 离线时设 Local 路径，`BuildRemoteCatalog=false` | Remote 路径的 catalog 指向 CDN，离线不可用 |
| `BuildPlayerContent` | 不改 | 同样构建 |
| `OrganizeOutput` | **跳过** | Addressables Local 模式自动放 StreamingAssets |
| `GenerateVersionState` | **跳过** | 没有远端版本比对需求 |

**注意**：`ConfigureBasicSettings` 修改了 Addressables Group Schema（BuildPath/LoadPath），离线构建完成后需恢复原始 Remote 设置，或者设计为 save → modify → build → restore。

### 2.5 StreamingAssets 最终布局

```
StreamingAssets/
├── BuildIndex.json          # 已有，LocalStatusExporter 导出
├── ABManifest.json          # AB 后端离线清单
├── ABManifest.bin           # AB 后端二进制清单（可选）
├── catalog_xxx.json         # Legacy 后端离线 catalog
├── bundles/
│   ├── xxx.bundle
│   └── ...
└── [Addressables 自身文件]  # settings.json / xxx.hash 等
```

---

## 三、已收敛决策

| # | 问题 | 决策 |
|---|------|------|
| D3 | AB + Legacy 双后端都支持？ | **是**，`StandaloneBuild` 和 `UseABBackend` 正交，各自处理 |
| AB 管线是否跳 Task | **不跳**，TaskOrganizeOutput 末尾加 CopyToStreamingAssets |
| Legacy 是否跳步骤 | **跳** OrganizeOutput + GenerateVersionState；ConfigureBasicSettings 分支处理 |

---

## 四、待讨论决策

| # | 问题 | 选项 |
|---|------|------|
| D1 | 开关是 SO bool 还是 CLI 参数？ | **SO bool**：持久配置；**CLI 覆盖**：CI 灵活，两者可共存（SO 默认 + --standalone 覆盖） |
| D2 | 离线包要不要保留 HotfixOutput 目录？ | **保留**：方便调试，Build 后能看到产物结构 |
| D4 | ABBundleLoader 在 Android 上读 StreamingAssets/bundles/ 是否正常？ | 需验证：`FileHelper` Android 分支走 UnityWebRequest，StreamingAssets 路径下 bundle 文件需确认可被 `AssetBundle.LoadFromFileAsync` 加载（已知限制：Android StreamingAssets 在 APK 内，不能直接 File I/O，需先拷贝到可写目录或走 UnityWebRequest 读取字节后 `LoadFromMemory`） |
| D5 | 要不要加 `[MenuItem("Tools/Build/Build Standalone Offline Package")]` 快捷入口？ | 便捷 vs 菜单简洁 |

---

## 五、不改的范围

- **Editor PlayMode**：已有 `plan-playmode-draft.md`
- **DifferentialProcessor / 快照系统**：保留不动，切回在线模式仍需要
- **HotfixManager 进度回调**：离线包直接 `OnFinished?.Invoke()`
- **Lua 热更**：离线包 Lua 脚本在 Bundle 内，走正常 AssetPackageManager 路径
