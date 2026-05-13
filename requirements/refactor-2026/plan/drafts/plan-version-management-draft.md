# Draft: 版本管理优化（VersionDataBase + 构建历史 + 一键化）

> **Status**: Draft — 2026-05-06, updated 2026-05-08
> **Scope**: 优化现有 VersionDataBase 系统，新旧管线共用
> **原则**: 不破坏现有功能，渐进增强
> **部分吸收**: VersionNumber 扩充 → 已移入 `plan-E9-version.md`（精确子计划）
> **后续方案**: 智能版本建议 → 已移入 `drafts/plan-smart-versioning-draft.md`
> **本草稿剩余范围**: BuildMetadata 丰富化 + BuildHistory 记录 + VersionPanel 重做 + 一键构建流程

---

## 现状

- `VersionDataBase.asset`：Major.Minor.Patch + LastBuildTime + DailyBuildCount
- `BuildSnapshots.asset`：Head/Staged/Current 三态快照
- `VersionPanel`：raw SerializedProperty inspector，信息不直观
- 构建流程：BuildProjectManager 手动触发，步骤分散

**Staged 区设计理念**（保留）：
- 开发期多次构建 → Staged 区覆盖式更新，版本号不变
- 确认发布 → Staged 推到 Head，版本号递增
- 变更线单向线性，不分叉

---

## 优化方向

### 1. 构建元数据丰富化

**修改** `VersionDataBase.cs`，新增字段：

```csharp
[Serializable]
public class BuildMetadata
{
    public string GitCommit;        // git rev-parse --short HEAD
    public string GitBranch;        // git branch --show-current
    public int BuildNumber;         // 全局递增，永不重置（区别于 DailyBuildCount）
    public string UnityVersion;     // Application.unityVersion
    public string Platform;         // EditorUserBuildSettings.activeBuildTarget
    public DateTime BuildTime;      // 精确构建时间
}
```

VersionDataBase 新增：
```csharp
public BuildMetadata LastBuildMetadata;
public int GlobalBuildNumber;  // 全局递增
```

**自动采集**：构建时自动执行 `git rev-parse` 和 `git branch`，无需手动填写。

### 2. 构建历史记录

**修改** `BuildSnapshots.cs`，每次构建记录一条历史：

```csharp
[Serializable]
public class BuildHistoryEntry
{
    public VersionNumber Version;
    public BuildMetadata Metadata;
    public bool IsReleased;         // 是否已发布到 Head
    public int AssetCount;          // 本次构建包含的资产数
    public long TotalSize;          // 总大小
    public string Notes;            // 可选备注
}

// BuildSnapshots 新增
public List<BuildHistoryEntry> History = new();  // 最近 N 条
```

### 3. VersionPanel 重做

```
┌─ Version Panel ──────────────────────────────────────────────────┐
│ ┌─ 当前状态 ───────────────────────────────────────────────────┐ │
│ │ 版本: 1.2.3   构建号: #47   平台: Android                   │ │
│ │ 分支: main    commit: a1b2c3d   时间: 2026-05-06 14:30      │ │
│ │ Staged: 有未发布的构建 (3 次修改)                            │ │
│ └──────────────────────────────────────────────────────────────┘ │
│                                                                  │
│ ┌─ 操作 ──────────────────────────────────────────────────────┐ │
│ │ [🔨 构建热更包]  [📦 构建完整包]  [🚀 确认发布 (Staged→Head)] │ │
│ └──────────────────────────────────────────────────────────────┘ │
│                                                                  │
│ ┌─ 构建历史 ──────────────────────────────────────────────────┐ │
│ │ #  │ 版本  │ 时间       │ 分支   │ commit  │ 状态          │ │
│ │ 47 │ 1.2.3 │ 05-06 14:30│ main   │ a1b2c3d │ 🟡 Staged    │ │
│ │ 46 │ 1.2.3 │ 05-05 16:00│ main   │ f4e5d6c │ 🟡 Staged    │ │
│ │ 45 │ 1.2.2 │ 05-04 10:00│ main   │ b7c8d9e │ 🟢 Released  │ │
│ │ 44 │ 1.2.1 │ 05-03 09:00│ hotfix │ c9d0e1f │ 🟢 Released  │ │
│ └──────────────────────────────────────────────────────────────┘ │
│                                                                  │
│ ┌─ 版本策略 ──────────────────────────────────────────────────┐ │
│ │ 自动递增: [Patch ▼]   手动设置: [Major: 1] [Minor: 2]       │ │
│ └──────────────────────────────────────────────────────────────┘ │
└──────────────────────────────────────────────────────────────────┘
```

### 4. 一键构建流程

当前步骤分散，整合为单按钮触发：

```
点击 "构建热更包" →
  1. 自动采集 git commit/branch
  2. GlobalBuildNumber++
  3. 运行构建管线 (新管线: E5 IBuildTask 链 / 旧管线: BuildProjectManager)
  4. 生成 version_state.json (旧管线) 或 ABManifest (新管线)
  5. 记录 BuildHistoryEntry
  6. 更新 Staged 快照
  7. 显示构建结果摘要

点击 "确认发布" →
  1. Staged → Head
  2. 版本号递增 (Patch++)
  3. 标记历史条目为 Released
  4. (可选) 触发 CDN 上传脚本
```

---

## 新旧管线共用策略

```
VersionDataBase (共用)
  ├─ 旧管线: BuildProjectManager 调用 IncrementVersion + 生成 version_state
  └─ 新管线: E5 TaskPrepareContext 读取版本信息 → E6 写入 ABManifest

BuildSnapshots (共用)
  ├─ 旧管线: DifferentialProcessor 操作 Head/Staged
  └─ 新管线: 构建完成后同样记录 BuildHistoryEntry

VersionPanel (共用)
  └─ 不区分新旧管线，统一展示版本状态和构建历史
```

---

## 文件变更预估

| 文件 | 操作 |
|------|------|
| `Build/BuildManage/VersionDataBase.cs` | 修改（加 BuildMetadata, GlobalBuildNumber） |
| `Build/BuildManage/BuildSnapshots.cs` | 修改（加 BuildHistoryEntry 列表） |
| `Build/Editor/VersionPanel.cs` | 重写（信息面板 + 操作按钮 + 历史列表） |
| `Build/BuildManage/Editor/BuildProjectManager.cs` | 修改（自动采集 git 信息） |
| `Build/BuildManage/Editor/GitInfoCollector.cs` | 新建（封装 git 命令调用） |

---

## 待确认

1. 构建历史保留多少条？（建议 50 条，超出自动清理最旧的）
2. "确认发布" 是否需要二次确认弹窗？（防误操作）
3. CDN 上传是否纳入一键流程？还是保持手动？
4. 新管线的 TaskPrepareContext 是否需要从 VersionDataBase 读取版本号写入 BuildContext？
