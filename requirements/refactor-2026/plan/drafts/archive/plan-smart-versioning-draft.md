# Draft: 智能版本管理 — 基于 Git 的自动化版本建议

> **Status**: ~~Draft (Deferred)~~ → **Archived 2026-05-18** — Superseded by `draft-build-repository-20260518.md`（版本建议逻辑作为 SourceDiffSummary 纳入统一 Build Repository）
> **Depends on**: E9 (VersionNumber 扩充) 完成后可实施
> **Insertion Point**: E7 (Diff snapshot) 或 VersionPanel 重做时考虑纳入
> **2026-05-15 结论**: 锦上添花功能，非紧迫。版本管理概念分离后确认：Version 只管发布语义，git 信息属于 Build Log 归属产物。Smart Versioning 作为未来 VersionPanel 重做的子功能延后。

---

## 现状

当前版本递增完全手动：
- 点 "Build Full Package" → Major++
- 点 "Build Hotfix" → Patch++
- 无 Minor 自动触发场景
- Build 号由 E9 设计为自动递增（DailyBuildCount）

---

## 智能化方案：基于 Git Diff 的版本建议

### 核心思路

构建前自动分析 git 变更，建议版本号递增类型：

```
git diff HEAD..{last-build-commit} --stat
  ↓ 分析变更文件类型
  ↓ 映射到版本建议
  ↓ 弹窗确认或自动应用
```

### 变更分类规则

| 变更类型 | 文件模式 | 建议 |
|---------|---------|------|
| 仅资源变更 | `Assets/**/*.{png,mat,prefab,asset,fbx,anim}` | Patch |
| 代码/配置变更 | `Assets/**/*.cs`, `ProjectSettings/**` | Minor |
| 破坏性变更 | 手动标记 / commit message 含 `BREAKING` | Major |
| 无变更 | git diff 为空 | 阻止构建（已有） |

### Conventional Commits 解析（可选增强）

解析 `last-build-commit..HEAD` 之间的 commit messages：

```
feat: xxx       → Minor
fix: xxx        → Patch
feat!: xxx      → Major (breaking)
chore/docs/ci   → 不影响版本（仅 Build++ ）
```

优先级：commit message > 文件模式推断

### 实现要点

1. **GitInfoCollector** (新建工具类)
   - `GetLastBuildCommit()` — 从 VersionDataBase 或 BuildSnapshots 读取上次构建的 git commit hash
   - `GetChangedFilesSince(string commitHash)` — 执行 `git diff --name-only`
   - `ParseConventionalCommits(string fromCommit)` — 解析 commit messages
   - `SuggestVersionBump(List<string> changedFiles, List<string> commitMessages)` → enum { Patch, Minor, Major }

2. **集成点**
   - `BuildProjectManager.ExecuteBuildFlow` 开头调用
   - 弹窗显示建议："检测到 12 个资源变更 + 3 个代码变更，建议 Minor 更新。[接受] [改为 Patch] [改为 Major]"
   - 或静默模式（CI）：直接应用建议

3. **记录 last-build-commit**
   - 每次构建成功后，将 `git rev-parse HEAD` 写入 VersionDataBase（或 BuildSnapshots）
   - E9-T2 可预留字段：`VersionDataBase.LastBuildCommit`

### SO vs JSON 迁移（可选，独立决策）

如果未来需要 CI 纯命令行读写版本信息：
- 将 `VersionDataBase` 从 SO 迁移为 `version.json`（项目根目录）
- 人工字段（Major/Minor/Channel）进 git
- 自动字段（Build/DailyBuildCount/LastBuildTime/LastBuildCommit）写入 `.local/build-state.json`（gitignore）
- 当前阶段 SO 够用，不急

---

## 渐进实施路径

```
E9 (VersionNumber 扩充)
  → E7 (Diff snapshot, 可顺带记录 last-build-commit)
    → 本方案 (GitInfoCollector + 构建前建议弹窗)
      → VersionPanel 重做 (显示 git 信息 + 建议历史)
```

---

## 待确认（实施时再决策）

1. 无 git 环境时（如 CI 未配置 git）的 fallback 策略
2. 是否支持 monorepo 子目录过滤（当前项目不需要）
3. 建议弹窗是否可被 CI 参数覆盖（`--version-bump=minor`）

---

## Change Log

| Date | Change |
|------|--------|
| 2026-05-08 | Initial draft from version management discussion |
