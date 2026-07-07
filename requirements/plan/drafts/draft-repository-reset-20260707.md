# Draft: Repository Reset (Test Environment)

**Date**: 2026-07-07  
**Status**: Promoted / Archived  
**Category**: Build Tools Enhancement

Promoted into `requirements/plan/plan-build-state-cleanup-tools-20260707.md`.

## Problem Statement

当前缺少完整的仓库重置能力，导致测试环境下版本状态与仓库内容容易产生偏移：

**具体场景：**
1. 开发者执行多次测试构建后，需要回到干净状态重新验证
2. 版本号通过 "Reset to 1.0.0" 重置后，Repository HEAD 仍指向已有记录，产生状态不一致
3. 手动删除包体目录后，VersionDataBase 仍保留旧的构建时间和次数
4. 无法一键将系统重置到初始状态（版本+仓库均归零）

**状态偏移的风险：**
- Repository 有提交记录 → VersionDataBase 显示 1.0.0 → 构建出错或版本号混乱
- 包体目录已删除 → BuildIndex.json 仍引用该包体 → CI 构建拉取包体失败
- DailyBuildCount 显示 3 → LastBuildTime 为空 → 构建次数统计逻辑分叉

## Scope of "Repository Reset"

仓库重置 = 以下所有组件同步清零/归位：

| Component | Reset Action |
|-----------|--------------|
| VersionDataBase | Major=1, Minor=0, Patch=0, Build=0, LastBuildTime="", DailyBuildCount=0 |
| Repository HEAD | 清除所有 Commit 记录，或回滚到指定 commit |
| BuildIndex.json | 清空包体引用列表 |
| Packages 目录 | 可选：删除本地测试构建包体 |
| StreamingAssets/BuildIndex | 同步更新（如适用） |

## Proposed Solution

### Component Analysis

需先确认各组件的重置接口：

1. **VersionDataBase** - 已在 `draft-version-system-test-features` 中设计，提供 `ResetVersionToTest()`
2. **Repository HEAD** - 需确认 `BuildRepositoryFacade` 是否有清除 HEAD 的 API
3. **BuildIndex.json** - 需确认写入逻辑，明确清空方式
4. **Packages 目录** - 文件系统操作，`Directory.Delete` + 重建空目录

### Target API

```csharp
public static class BuildRepositoryFacade
{
    // 🆕 新增：清除所有提交记录（测试环境）
    public static void ResetHeadForTest()
    {
        // Implementation TBD after reviewing FileBuildRepository internals
    }
}
```

```csharp
public static class BuildResetTool
{
    /// <summary>
    /// 一键重置测试环境（版本号 + 仓库 HEAD + BuildIndex）
    /// </summary>
    public static void ResetTestEnvironment(bool deletePackages = false)
    {
        Debug.Log("[BuildResetTool] 开始重置测试环境...");
        
        // 1. 重置版本号
        ResetVersionDataBase();
        
        // 2. 清除 Repository HEAD
        ResetRepositoryHead();
        
        // 3. 清空 BuildIndex
        ClearBuildIndex();
        
        // 4. 可选：删除本地包体
        if (deletePackages)
            ClearLocalPackages();
        
        AssetDatabase.Refresh();
        Debug.Log("[BuildResetTool] 测试环境重置完成");
    }
    
    private static void ResetVersionDataBase()
    {
        var versionPath = FYAssetSettings.Instance.VersionDataBasePath;
        var versionDB = AssetDatabase.LoadAssetAtPath<VersionDataBase>(versionPath);
        if (versionDB == null)
        {
            Debug.LogWarning("[BuildResetTool] 未找到 VersionDataBase，跳过版本重置");
            return;
        }
        
        versionDB.CurrentVersion = new VersionNumber { Major = 1, Minor = 0, Patch = 0, Build = 0 };
        versionDB.LastBuildTime = "";
        versionDB.DailyBuildCount = 0;
        EditorUtility.SetDirty(versionDB);
        AssetDatabase.SaveAssets();
        
        Debug.Log("[BuildResetTool] VersionDataBase 已重置为 1.0.0");
    }
    
    private static void ResetRepositoryHead()
    {
        // FileBuildRepository 存储结构（代码审查结果）：
        //   BuildData/Snapshots/{channelKey}/
        //     HEAD.json           - { "HeadVersion": "1.0.1" }
        //     objects/
        //       1.0.0.json        - RepositoryCommit 快照
        //       1.0.1.json
        //     PushHistory.json    - 推送历史
        //
        // 测试重置 = 删除当前 channelKey 下的 HEAD + objects + PushHistory
        // 注意：channelKey 包含 buildTarget + channel + backend，
        //       需对所有可能的 channelKey 执行清除，或仅清除当前活跃的。
        //
        // BuildRepositoryFacade 尚无直接的 ClearChannelForTest API，
        // 需要新增（见下方 "Required API Changes" 章节）。
        
        var backendMode = FYAssetSettings.Instance.UseABBackend
            ? BackendMode.ABManifest
            : BackendMode.AA;
        string channelKey = BuildRepositoryFacade.GetChannelKey(
            version: new VersionNumber { Channel = "" },
            backendMode: backendMode);
        
        // 调用待新增的 API
        BuildRepositoryFacade.ClearChannelForTest(channelKey);
        Debug.Log($"[BuildResetTool] Repository channel 已清除: {channelKey}");
    }
    
    private static void ClearBuildIndex()
    {
        // PackageIndexPath = BuildPathManager.OutputRoot + FYAssetSettings.PACKAGE_INDEX_FILE_NAME
        // 测试重置 = 写入空的 PackageIndex（或删除文件）
        //
        // 空 PackageIndex 更安全（避免文件不存在导致运行时异常）：
        string indexPath = BuildPathManager.PackageIndexPath;
        if (!FileHelper.Exists(indexPath))
        {
            Debug.Log("[BuildResetTool] BuildIndex 不存在，跳过清除");
            return;
        }
        
        var empty = new PackageIndex
        {
            LatestPackage = "",
            LatestVersion = null,
            BackendMode = ""
        };
        FileHelper.WriteAllTextAtomic(indexPath, SerializationUtility.SerializeToJson(empty, true));
        Debug.Log($"[BuildResetTool] BuildIndex 已清空: {indexPath}");
    }
    
    private static void ClearLocalPackages()
    {
        string packagesDir = BuildPathManager.PackagesDir;
        if (!Directory.Exists(packagesDir)) return;
        
        Directory.Delete(packagesDir, recursive: true);
        Directory.CreateDirectory(packagesDir);
        
        Debug.Log($"[BuildResetTool] 已清空包体目录: {packagesDir}");
    }
}
```

### UI Integration

在 BuildResultsPanel（或新建 ResetPanel）中添加重置入口：

```
┌─────────────────────────────────────────────────────────────┐
│ Repository Panel - Reset Section                            │
├─────────────────────────────────────────────────────────────┤
│ ⚠️ 危险操作 / 仅用于测试环境                               │
│                                                             │
│ [Reset Version Only]  [Reset Repository Only]              │
│                                                             │
│ [Reset All (Version + Repository + BuildIndex)]            │
│ ☐ 同时删除本地包体目录 (Packages/)                        │
│                                                             │
│ 当前状态：                                                  │
│   版本号:     1.0.0                                        │
│   Repository: 3 commits                                     │
│   BuildIndex: 3 entries                                     │
│   包体目录:   5 packages (1.2 GB)                          │
└─────────────────────────────────────────────────────────────┘
```

### Safety Guards

1. **确认对话框** - 所有重置操作均需二次确认，显示将影响的内容
2. **生产保护** - 可通过 `FYAssetSettings.IsProductionBuild` 标志禁用重置按钮
3. **备份建议** - 重置前提示开发者是否需要先备份当前仓库状态
4. **日志记录** - 所有重置操作写入构建日志，便于追溯

## Code Review Findings (已完成)

**FileBuildRepository 存储结构（已确认）：**
```
BuildData/Snapshots/
└── {channelKey}/            例: Android/AB
    ├── HEAD.json            { "HeadVersion": "1.0.1" }
    ├── objects/
    │   ├── 1.0.0.json      RepositoryCommit 快照（JSON）
    │   └── 1.0.1.json
    └── PushHistory.json     推送历史记录
```

**channelKey 格式（BuildRepositoryFacade.GetChannelKey）：**
```
{buildTarget}[-channel] / {backend}
例: Android/AB, Android-beta/AA
```

**现有 API 分析：**
- `TryRollbackHead(channelKey, expectedVersion, parentVersion, out reason)`
  - 当 `parentVersion` 为空时**删除** HEAD.json，并删除对应 commit object
  - 但需要提供精确的 `expectedHeadVersion`，不适合直接用于测试重置
- `Repair(channelKey, dryRun)` 
  - 将损坏的 HEAD 移到 quarantine 子目录，不是归零
- **结论：无现成的"清除所有历史"接口，需要新增**

**BuildIndex 路径（已确认）：**
- `BuildPathManager.PackageIndexPath` = `BuildPathManager.OutputRoot + PACKAGE_INDEX_FILE_NAME`
- 结合 git status 的文件名，OutputRoot 对应 `Assets/StreamingAssets/`（或项目 Build 目录）
- 清空方式：写入空 `PackageIndex` 对象（而非删除文件，避免运行时空引用）

## Required API Changes

需要在 `BuildRepositoryFacade` 和 `FileBuildRepository` 中新增测试重置 API：

```csharp
// BuildRepositoryFacade.cs 新增
public static class BuildRepositoryFacade
{
    // ... existing methods ...

    /// <summary>
    /// 测试专用：清除指定 channel 的全部 Repository 数据
    /// （HEAD + objects + PushHistory）
    /// 警告：不可逆，仅限测试环境
    /// </summary>
    public static void ClearChannelForTest(string channelKey)
    {
        if (FileRepository == null)
            throw new InvalidOperationException("Repository does not support channel clear.");
        FileRepository.ClearChannelForTest(channelKey);
    }
}

// FileBuildRepository.cs 新增
public sealed class FileBuildRepository : IBuildRepository
{
    // ... existing methods ...

    /// <summary>
    /// 删除整个 channel 目录下的所有数据（HEAD + objects + PushHistory）
    /// </summary>
    public void ClearChannelForTest(string channelKey)
    {
        string channelRoot = GetChannelRoot(channelKey);
        if (!FileHelper.DirectoryExists(channelRoot))
        {
            Debug.Log($"[FileBuildRepository] ClearChannelForTest: channel 目录不存在，跳过: {channelRoot}");
            return;
        }

        // 删除 HEAD.json
        string headPath = FYAssetPathUtility.JoinFilePath(channelRoot, "HEAD.json");
        FileHelper.TryDelete(headPath);

        // 删除全部 commit objects
        string objectsDir = GetObjectsDir(channelKey);
        if (FileHelper.DirectoryExists(objectsDir))
        {
            string[] files = FileHelper.GetFiles(objectsDir, "*.json", SearchOption.TopDirectoryOnly);
            foreach (var file in files)
                FileHelper.TryDelete(file);
        }

        // 删除 PushHistory
        string pushHistoryPath = GetPushHistoryPath(channelKey);
        FileHelper.TryDelete(pushHistoryPath);

        // 清除内存中的错误缓存
        ClearLastHeadError(channelKey);

        Debug.Log($"[FileBuildRepository] Channel 已清除 (TestReset): {channelKey}");
    }
}
```

## Dependencies

- 依赖 `draft-version-system-test-features-20260707.md` 中的 `ResetVersionToTest()`
- 依赖 `draft-buildresults-management-panel-20260707.md` 中的 `BuildResultsManager.DeleteBuild()`

## Open Questions

1. ~~Repository 重置是否物理删除提交记录？~~ **已明确：测试重置 = 物理删除 channel 下所有文件**（ClearChannelForTest）
2. **`Assets/Build/Bootstrap/BuildIndex.json` 与 `Assets/StreamingAssets/BuildIndex.json` 是否同步清空？** 需确认两者关系，目前只处理 `BuildPathManager.PackageIndexPath`
3. 重置后是否需要调用 `AssetDatabase.Refresh()` 同步 Unity 资产数据库？**建议是（已在 ResetTestEnvironment 中加入）**
4. 是否需要支持"回滚到某个历史 commit"而非完全归零？**当前 draft 仅覆盖完全归零；回滚到历史版本是独立需求**

## Recommendation

**优先级：P1 (High)**  
**预估工作量：1 人日（代码审查已完成，实现路径明确）**

实现顺序：
1. 在 `FileBuildRepository.cs` 中新增 `ClearChannelForTest(channelKey)` 方法
2. 在 `BuildRepositoryFacade.cs` 中新增门面方法
3. 实现 `BuildResetTool` 整合（VersionDataBase + Repository + BuildIndex）
4. 集成到 RepositoryStatusPanel 的 Reset Section（或 BuildResultsPanel）
5. 完整测试：重置后执行一次构建，验证无状态偏移（版本号/提交记录/BuildIndex 均正确）
