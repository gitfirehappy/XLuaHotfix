# Plan: 单机离线包 (Standalone Offline Package)

> **Status**: Completed / Verified / Archived — 2026-07-24
> **Created**: 2026-07-24
> **Source Draft**: `requirements/plan/drafts/archive/draft-offline-standalone-package-20260513.md`
> **Scope**: 构建期产出离线包 + 运行时跳过热更流程，AB 后端 PC/Windows 优先
> **Known Limitation**: Android 平台离线包暂不支持（StreamingAssets bundle 加载限制），作为后续独立任务
>
> ## 执行备注（相对原 Plan 的实现修正）
>
> 1. **2026-07-24 纠正**：Standalone 的 `BuildPackageRequest.OutputDir` 直接指向 `StreamingAssets/Standalone/`，Bundle 和 Manifest 均在最终目录生成；删除 `PublishStandaloneToStreamingAssets` 和 `HotfixOutput/Packages` 冗余副本。
> 2. `TaskExportLocalBuildData` 对 Standalone 只写 `BuildIndex.json`，不覆盖在线基线 bundles。
> 3. Standalone E2E 复用 `BuildPlayerSession` + `FYASSET_E2E_COORDINATOR`，验证 Player exit 0。

---

## 收敛决策汇总

| # | 决策 | 结论 |
|---|------|------|
| D1 | 开关形式 | `FYAssetSettings.StandaloneBuild` SO bool；CLI 覆盖作 Residual |
| D2 | 保留 HotfixOutput | 否；离线包只保留最终 `StreamingAssets/Standalone/` 产物 |
| D3 | AB + Legacy 双后端 | 正交支持；本 Plan 仅实现 AB 管线，Legacy Addressables 作 Residual |
| D4 | Android 支持 | Known Limitation，不在本 Plan 范围内 |
| D5 | 人工构建入口 | 唯一入口为 AB 管线面板内部 `Mode=Standalone` + `Build`；顶部菜单只打开窗口 |
| BuildType | 区分方式 | 新增 `BuildType.Standalone` 枚举值 |
| Repository commit | standalone 行为 | 跳过 `CommitBuildRepository` 和 `TaskWritePackageIndex`；`TaskExportLocalBuildData` 仍执行（写 BuildIndex.json） |
| TaskOrganizeOutput | 路径安全 | Standalone 的 request 直接使用精确路径 `StreamingAssets/Standalone/`；失败清理只允许该精确目录 |
| HotfixFlowBase 短路 | 钩子方式 | `protected virtual bool IsStandaloneMode()` 返回 false，AB/AA Flow 子类 override |

---

## 方案架构

```
构建期:
  ABBuildProjectManager.BuildStandalonePackage()
    -> BuildProjectRunner.BuildStandalone()
       -> BuildPackageRequest(BuildType.Standalone)
       -> ABBuildBackend.BuildAsync()
          -> 标准 DAG（全量 Task 不跳）
          -> TaskOrganizeOutput: bundles 直接写入 StreamingAssets/Standalone/
          -> TaskWriteABPackageManifest: manifest 直接写入同一目录
       -> TaskExportLocalBuildData.Publish()  (写 BuildIndex.json)
       // CommitBuildRepository 跳过
       // TaskWritePackageIndex 跳过

运行时:
  HotfixManager.InitializeAsync(BackendMode.ABManifest)
    -> ABHotfixFlow.RunAsync()
       -> LoadStartupStateAsync()  (加载 BuildIndex, RuntimePathManager.Initialize)
       -> IsStandaloneMode() == true
          -> AssetPackageManager.Initialize()
          -> OnFinished?.Invoke()
          <- 短路返回，跳过所有联网步骤

  ABBundleLoader 加载路径（不变）:
    primary: CurrentGUIDRoot/bundles/[name]  (热更区，standalone 下为空)
    fallback: StreamingAssets/bundles/[name] (命中离线包内容)
```

---

## 任务拆分

### T1: `BuildType.Standalone` 枚举扩展

**文件**: `Assets/FYAsset/Scripts/Shared/Build/Release/Editor/IBuildBackend.cs`

在 `BuildType` 枚举新增 `Standalone` 值：

```csharp
public enum BuildType
{
    Full,
    Hotfix,
    Standalone
}
```

审计所有使用 `BuildType` 的 switch/if 分支，确保 `Standalone` 不被遗漏或误处理。重点：
- `TaskOrganizeOutput.Execute()` — 现有 `buildType == BuildType.Hotfix` 判断，`Standalone` 与 `Full` 行为相同（全量 bundle 复制）

---

### T2: `FYAssetSettings.StandaloneBuild` 字段

**文件**: `Assets/FYAsset/Scripts/Shared/Settings/FYAssetSettings.cs`

在 `[Header("Build")]` 区块新增：

```csharp
[Header("Build")]
public string BuildOutputRoot = "HotfixOutput";
public string BuildPackagesFolderName = "Packages";
public bool StandaloneBuild = false;  // 构建期 + 运行时双重生效
```

---

### T3: Standalone 直接输出到 `StreamingAssets/Standalone/`

**文件**: `BuildPathManager.cs`、`BuildPackageRequest.cs`、`BuildProjectRunner.cs`

- `BuildPackageRequest.Create()` 在 `BuildType.Standalone` 时将 `OutputDir` 设为 `BuildPathManager.StandalonePackageDir`。
- 现有 `TaskOrganizeOutput` 和 `TaskWriteABPackageManifest` 直接消费 request 路径，无额外复制。
- `BuildProjectRunner` 删除二次复制方法；失败清理仅允许精确的 Standalone 最终目录。

---

### T4: `BuildContextKeys` 新增键（取消）

直接复用 `BuildPackageRequest.OutputDir`，不增加重复状态键。

---

### T5: `BuildProjectRunner.BuildStandalone`

**文件**: `Assets/FYAsset/Scripts/Shared/Build/Editor/BuildProjectRunner.cs`

新增公开方法（与 `BuildFullPackage` / `BuildHotfix` 并列）：

```csharp
public static bool BuildStandalone(
    BackendMode backendMode,
    Func<IBuildBackend> backendFactory,
    BuildExecutionOptions options = null)
{
    VersionDataBase versionData = LoadVersionDataBase();
    if (versionData == null)
        return false;

    VersionNumber nextVersion = versionData.BuildNextVersion(true);

    bool success = RunBuild(nextVersion, BuildType.Standalone, backendMode, backendFactory, options);
    if (success)
        success = ApplyBuiltVersion(nextVersion);

    return success;
}
```

同时修改 `RunBuild`：当 `buildType == BuildType.Standalone` 时，
- 跳过 `CommitBuildRepository`
- 跳过 `TaskWritePackageIndex.Publish`（在 `PublishBuildArtifacts` 内新增判断）
- 仍执行 `TaskExportLocalBuildData.Publish`

`PublishBuildArtifacts` 拆分：

```csharp
private static void PublishBuildArtifacts(BuildPackageRequest request)
{
    TaskExportLocalBuildData.Publish(request);  // 始终执行
    if (request.BuildType != BuildType.Standalone)
        TaskWritePackageIndex.Publish(request); // standalone 跳过
}
```

---

### T6: `ABBuildProjectManager.BuildStandalonePackage`

**文件**: `Assets/FYAsset/Scripts/AB/Build/Editor/ABBuildProjectManager.cs`

新增方法：

```csharp
public static void BuildStandalonePackage(BuildExecutionOptions options = null)
{
    LastBuildSuccess = BuildProjectRunner.BuildStandalone(
        BackendMode.ABManifest,
        () => new ABBuildBackend(),
        options);
}
```

---

### T7: `HotfixFlowBase` standalone 短路钩子

**文件**: `Assets/FYAsset/Scripts/Shared/Hotfix/HotfixFlowBase.cs`

1. 新增 protected virtual 方法（默认 false，不影响现有流程）：

```csharp
protected virtual bool IsStandaloneMode() => false;
```

2. 在 `RunAsync()` 中 `LoadStartupStateAsync` 之后插入短路：

```csharp
private async Task RunAsync()
{
    var ctx = new HotfixContext();
    await LoadStartupStateAsync(ctx);

    if (IsStandaloneMode())
    {
        await FinishHotfix();
        RaiseFinished();
        return;
    }

    // 在线流程继续...
    IHotfixPipeline pipeline = CreatePipeline();
    ...
}
```

---

### T8: `ABHotfixFlow.IsStandaloneMode` override

**文件**: `Assets/FYAsset/Scripts/AB/Hotfix/ABHotfixManager.cs`

在 `ABHotfixFlow` 内部类新增：

```csharp
protected override bool IsStandaloneMode() =>
    FYAssetSettings.Instance.StandaloneBuild;
```

---

### T9: `AAHotfixFlow.IsStandaloneMode` override

**文件**: `Assets/FYAsset/Scripts/AA/Hotfix/AAHotfixManager.cs`

同 T8，供 Legacy AA 后端在未来支持时使用（本 Plan 内 AA backend 离线包作 Residual，但钩子需同步加入）：

```csharp
protected override bool IsStandaloneMode() =>
    FYAssetSettings.Instance.StandaloneBuild;
```

---

### T10: 管线面板唯一构建入口

**文件**: `ABBuildPipelineWindow.cs`、`PipelinePanel.cs`

- 删除顶部 `FYAsset/Build/Build Standalone Package (AB)` 直构建菜单。
- 保留 AB Pipeline 面板内部唯一的 `Build` 按钮和模式选择。
- AB 模式为 Full / Hotfix / Standalone；AA 模式为 Full / Hotfix。
- `Mode=Standalone` 时调用 `ABBuildProjectManager.BuildStandalonePackage(options)`。

---

### T11: 路径隔离 — `StreamingAssets/Standalone/` 独立目录

**动机**: T3 写入 `StreamingAssets/Standalone/`（已在上方代码体现），在线包基线仍在 `StreamingAssets/bundles/`，两套包物理隔离，不需要清理即可切换测试。

**文件 1**: `Assets/FYAsset/Scripts/Shared/Settings/FYAssetSettings.cs`

新增常量：

```csharp
public const string STANDALONE_DIRECTORY_NAME = "Standalone";
```

**文件 2**: `Assets/FYAsset/Scripts/AB/Runtime/Backends/AB/ABBundleLoader.cs`

standalone 模式 fallback 路径加 `Standalone/` 子层：

```csharp
// 现有:
string fallbackPath = FYAssetPathUtility.JoinFilePath(
    Application.streamingAssetsPath,
    FYAssetSettings.BUNDLES_DIRECTORY_NAME, bundleName);

// 改为:
string standaloneSubDir = FYAssetSettings.Instance.StandaloneBuild
    ? FYAssetSettings.STANDALONE_DIRECTORY_NAME
    : string.Empty;
string fallbackPath = string.IsNullOrEmpty(standaloneSubDir)
    ? FYAssetPathUtility.JoinFilePath(
        Application.streamingAssetsPath,
        FYAssetSettings.BUNDLES_DIRECTORY_NAME, bundleName)
    : FYAssetPathUtility.JoinFilePath(
        Application.streamingAssetsPath,
        standaloneSubDir,
        FYAssetSettings.BUNDLES_DIRECTORY_NAME, bundleName);
```

**文件 3**: `Assets/FYAsset/Scripts/AB/Runtime/Backends/AB/ABManifestLoader.cs`

standalone 模式读 `StreamingAssets/Standalone/ABManifest.*`：

```csharp
string standaloneDir = FYAssetSettings.Instance.StandaloneBuild
    ? FYAssetPathUtility.JoinFilePath(
        Application.streamingAssetsPath,
        FYAssetSettings.STANDALONE_DIRECTORY_NAME)
    : Application.streamingAssetsPath;
// standaloneDir 替换原 Application.streamingAssetsPath 的直接引用
```

**最终 StreamingAssets 布局：**

```
StreamingAssets/
├── BuildIndex.json          ← 两种模式共用
├── bundles/                 ← 在线模式基线（不变）
├── ABManifest.json          ← 在线模式基线 Manifest
└── Standalone/              ← 离线包专属，在线模式不读此目录
    ├── bundles/
    │   └── *.bundle
    └── ABManifest.json
```

---

### T12: Editor 快切按钮

**文件**: `Assets/FYAsset/Scripts/Shared/Build/Editor/Settings/SettingsPanel.cs`（或 `BuildPipelineWindow.cs`，以实际布局为准）

在 Settings 面板 Build 区域新增 PlayMode 快切行：

```csharp
// 当前模式状态显示
bool isStandalone = FYAssetSettings.Instance.StandaloneBuild;
string modeLabel = isStandalone ? "● Standalone" : "● Online";
EditorGUILayout.LabelField("Current Play Mode:", modeLabel);

// 快切按钮
using (new EditorGUILayout.HorizontalScope())
{
    using (new EditorGUI.DisabledScope(isStandalone))
    {
        if (GUILayout.Button("▶ Run as Standalone"))
        {
            FYAssetSettings.Instance.StandaloneBuild = true;
            EditorUtility.SetDirty(FYAssetSettings.Instance);
            AssetDatabase.SaveAssets();
            EditorApplication.isPlaying = true;
        }
    }
    using (new EditorGUI.DisabledScope(!isStandalone))
    {
        if (GUILayout.Button("▶ Run Online"))
        {
            FYAssetSettings.Instance.StandaloneBuild = false;
            EditorUtility.SetDirty(FYAssetSettings.Instance);
            AssetDatabase.SaveAssets();
            EditorApplication.isPlaying = true;
        }
    }
}
```

当前模式对应的按钮置灰（避免重复点击）。

---

### T13: Standalone E2E 校验

**验证深度**: 一级 — Player exit 0 即通过，不校验网络请求计数。

**文件 1**: `Assets/FYAsset/Scripts/Shared/Build/Tests/Editor/E2ETestEngine.cs`

新增 standalone 测试入口（与现有 `RunFullE2ETest` / `RunHotfixE2ETest` 并列）：

```csharp
public static bool RunStandaloneE2ETest(BuildExecutionOptions options = null)
{
    // 1. Build standalone package
    ABBuildProjectManager.BuildStandalonePackage(options);
    if (!ABBuildProjectManager.LastBuildSuccess)
    {
        Debug.LogError("[E2ETest] Standalone build failed.");
        return false;
    }

    // 2. Launch Player with StandaloneBuild=true baked
    //    Player launched via existing batch-mode Player launch mechanism
    //    Pass --standalone flag so Player reads StandaloneBuild=true at runtime
    return LaunchAndWaitForPlayer(standaloneMode: true);
}
```

**文件 2**: `tests/fyasset_test.py`（或 `run_fyasset_test_batch.py`）

新增 standalone 测试用例：

```python
# Standalone E2E case
{
    "name": "standalone_e2e",
    "build_type": "standalone",
    "verify": "exit_0",
    "description": "Build standalone package -> launch Player -> verify exit 0"
}
```

**验证标准**: Player 以 `StandaloneBuild=true` 启动，完成初始化后正常退出（exit code 0）；`HotfixOutput/Packages/<BuildGUID>` 不存在。测试结果写入 `HotfixOutput/TestRuns/` 与现有用例一致。

---

## 涉及文件汇总

| 文件 | 操作 | 估计行数 |
|------|------|:---:|
| `IBuildBackend.cs` | 修改 — 新增 `BuildType.Standalone` | +2 |
| `FYAssetSettings.cs` | 修改 — 新增 `StandaloneBuild` 字段 + `STANDALONE_DIRECTORY_NAME` 常量 | +3 |
| `BuildPathManager.cs` / `BuildPackageRequest.cs` | 修改 — Standalone 直接选择最终输出目录 | +6 |
| `TaskOrganizeOutput.cs` | 修改 — 沿用 request 最终路径，无复制分支 | +0 |
| `BuildProjectRunner.cs` | 修改 — 新增 `BuildStandalone`，拆分 `PublishBuildArtifacts`，删除冗余复制 | 净减少 |
| `ABBuildProjectManager.cs` | 修改 — 新增 `BuildStandalonePackage` | +7 |
| `HotfixFlowBase.cs` | 修改 — 新增 `IsStandaloneMode()` + 短路逻辑 | +10 |
| `ABHotfixManager.cs` | 修改 — override `IsStandaloneMode()` | +3 |
| `AAHotfixManager.cs` | 修改 — override `IsStandaloneMode()` | +3 |
| `ABBuildPipelineWindow.cs` / `PipelinePanel.cs` | 修改 — 删除顶部直构建菜单，Standalone 接入面板 Mode + Build | 净减少 |
| `ABBundleLoader.cs` | 修改 — standalone fallback 路径加 `Standalone/` 子层 | +10 |
| `ABManifestLoader.cs` | 修改 — standalone 模式读 `Standalone/` 子目录 | +8 |
| `SettingsPanel.cs`（或 `BuildPipelineWindow.cs`）| 修改 — 新增模式状态 + 快切按钮 | +25 |
| `E2ETestEngine.cs` | 修改 — 新增 `RunStandaloneE2ETest` | +20 |
| `fyasset_test.py` / `run_fyasset_test_batch.py` | 修改 — 新增 standalone 测试用例 | +15 |

**总计: ~162 行，0 新文件，15 文件修改。**

---

## Residual（本 Plan 不做）

- Legacy Addressables 离线构建（AA backend D3）
- Android 平台 StreamingAssets bundle 加载（D4）
- CLI `--standalone` 参数覆盖（D1 CLI 覆盖）
