# Draft: BuildResults Management Panel with Deletion

**Date**: 2026-07-07  
**Status**: Promoted / Archived  
**Category**: Build Tools Enhancement

Promoted into `requirements/plan/plan-build-state-cleanup-tools-20260707.md`.

## Problem Statement

当前构建系统缺少对历史构建产物的管理能力：

1. **无历史构建列表视图**：
   - 构建包存储在 `BuildPathManager.PackagesDir`
   - 包名包含版本和时间戳（如 `Build_20260707064645_6.0.0`）
   - 但没有 UI 展示历史构建，需手动打开文件夹查看

2. **无删除/清理功能**：
   - 测试构建和废弃包体会积累占用磁盘空间
   - 只能手动删除文件夹，操作繁琐且易误删
   - 无法批量清理符合条件的构建

3. **缺少元数据管理**：
   - 无法区分测试构建、生产构建、已废弃构建
   - 无法快速定位特定版本的构建日志

**影响：**
- 磁盘空间浪费（测试构建积累）
- 开发效率低（查找历史构建困难）
- 误删风险（手动操作缺少保护）

## Proposed Solution

创建新的 Build Pipeline Panel：**BuildResultsPanel**

### Core Features

#### 1. Build List Display

显示所有历史构建，包含以下信息：

| Column | Type | Description | Source |
|--------|------|-------------|--------|
| Package Name | String | 完整包名 | 文件夹名称 |
| Version | VersionNumber | 版本号 | 从包名解析 |
| Build Time | DateTime | 构建时间 | 从包名解析 |
| Type | BuildType | Full/Hotfix | 从 PackageIndex 读取 |
| Size | long | 包体大小 (MB) | 计算文件夹大小 |
| Status | BuildStatus | Test/Production/Obsolete | 新增元数据文件 |
| Path | String | 完整路径 | BuildPathManager |

**数据结构：**

```csharp
public class BuildResultEntry
{
    public string PackageName { get; set; }
    public VersionNumber Version { get; set; }
    public DateTime BuildTime { get; set; }
    public BuildType BuildType { get; set; }
    public long SizeInBytes { get; set; }
    public BuildStatus Status { get; set; }
    public string FullPath { get; set; }
    
    public string SizeDisplay => FormatBytes(SizeInBytes);
}

public enum BuildStatus
{
    Unknown,      // 未标记
    Test,         // 测试构建
    Production,   // 生产构建
    Obsolete      // 已废弃
}
```

#### 2. Metadata Persistence

在每个构建包目录下创建 `.buildmeta.json` 文件：

```json
{
  "packageName": "Build_20260707064645_6.0.0",
  "version": "6.0.0",
  "buildTime": "2026-07-07T06:46:45Z",
  "buildType": "Hotfix",
  "status": "Test",
  "tags": ["hotfix", "test-feature-x"],
  "notes": "测试新的AB Diff算法"
}
```

**持久化时机：**
- 构建成功后由 `BuildProjectManager` 自动创建
- 用户在 BuildResultsPanel 中修改 Status/Tags/Notes 时更新

#### 3. Deletion Functionality

提供三种删除模式：

**a) 单个删除：**
- 选中某个构建，点击 "Delete" 按钮
- 弹出确认对话框，显示将删除的内容：
  ```
  确定删除以下构建吗？
  
  Package: Build_20260707064645_6.0.0
  Version: 6.0.0
  Size: 245.6 MB
  
  将删除：
  - 包体目录: E:\...\Packages\Build_20260707064645_6.0.0
  - 构建日志: E:\...\Logs\Build_20260707064645.log (如果存在)
  
  此操作不可撤销！
  ```

**b) 批量删除（按状态）：**
- 筛选器：Status = Test / Obsolete
- 批量操作按钮："Delete All Test Builds" / "Delete All Obsolete Builds"
- 确认对话框显示匹配的构建列表和总大小

**c) 批量删除（按时间）：**
- 筛选器：Build Time < 某个日期
- 批量操作按钮："Delete Builds Older Than..."
- 弹出日期选择器

**删除逻辑：**

```csharp
public static class BuildResultsManager
{
    public static bool DeleteBuild(BuildResultEntry entry, out string error)
    {
        error = null;
        
        if (!Directory.Exists(entry.FullPath))
        {
            error = "构建目录不存在";
            return false;
        }
        
        try
        {
            // Delete package directory
            Directory.Delete(entry.FullPath, recursive: true);
            
            // Delete associated log file (if exists)
            string logPath = GetLogPath(entry.PackageName);
            if (File.Exists(logPath))
                File.Delete(logPath);
            
            Debug.Log($"[BuildResultsManager] Deleted build: {entry.PackageName}");
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }
    
    public static List<BuildResultEntry> GetAllBuilds()
    {
        string packagesDir = BuildPathManager.PackagesDir;
        if (!Directory.Exists(packagesDir))
            return new List<BuildResultEntry>();
        
        var results = new List<BuildResultEntry>();
        var directories = Directory.GetDirectories(packagesDir);
        
        foreach (var dir in directories)
        {
            if (TryParseBuildDirectory(dir, out var entry))
            {
                results.Add(entry);
            }
        }
        
        return results.OrderByDescending(e => e.BuildTime).ToList();
    }
    
    private static bool TryParseBuildDirectory(string path, out BuildResultEntry entry)
    {
        entry = null;
        string folderName = Path.GetFileName(path);
        
        // Parse pattern: Build_YYYYMMDDHHMMSS_X.Y.Z
        var match = Regex.Match(folderName, @"Build_(\d{14})_([\d\.]+(?:-\w+)?)");
        if (!match.Success)
            return false;
        
        string timeStr = match.Groups[1].Value;
        string versionStr = match.Groups[2].Value;
        
        if (!DateTime.TryParseExact(timeStr, "yyyyMMddHHmmss", 
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var buildTime))
            return false;
        
        if (!VersionNumber.TryParse(versionStr, out var version))
            return false;
        
        long size = CalculateDirectorySize(path);
        var metadata = LoadMetadata(path);
        
        entry = new BuildResultEntry
        {
            PackageName = folderName,
            Version = version,
            BuildTime = buildTime,
            BuildType = metadata?.BuildType ?? BuildType.Full,
            SizeInBytes = size,
            Status = metadata?.Status ?? BuildStatus.Unknown,
            FullPath = path
        };
        
        return true;
    }
}
```

### UI Layout (UI Toolkit)

```
┌─────────────────────────────────────────────────────────────┐
│ BuildResults Panel                                       [x]│
├─────────────────────────────────────────────────────────────┤
│ [Refresh] [Delete Selected] [Delete Test] [Delete Obsolete]│
│                                                             │
│ Filter: [Status: All ▼] [Type: All ▼] [Older than: None ▼]│
├─────────────────────────────────────────────────────────────┤
│ ┌─────────────────────────────────────────────────────────┐│
│ │☑ Package Name            │ Version │ Time │ Type │ Size ││
│ ├─────────────────────────────────────────────────────────┤│
│ │☑ Build_20260707064645... │ 6.0.0   │07/07 │ HF   │245MB││
│ │☐ Build_20260706153020... │ 5.3.1   │07/06 │ Full │1.2GB││
│ │☐ Build_20260705120030... │ 5.3.0   │07/05 │ Full │1.2GB││
│ │☑ Build_20260704090000... │ 5.2.5   │07/04 │ HF   │180MB││ <- Test
│ │☑ Build_20260703080000... │ 5.2.4   │07/03 │ HF   │150MB││ <- Obsolete
│ └─────────────────────────────────────────────────────────┘│
│                                                             │
│ Selected: 3 builds, Total Size: 575 MB                     │
└─────────────────────────────────────────────────────────────┘
```
