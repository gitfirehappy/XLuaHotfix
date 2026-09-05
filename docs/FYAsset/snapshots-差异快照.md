# Snapshots 差异快照

> 返回总览：[资源管理架构文档](./资源管理架构文档.md)

> **关联代码**
>
> `Assets/FYAsset/Scripts/Shared/Build/Snapshots/ArtifactDigest.cs` · `Assets/FYAsset/Scripts/Shared/Build/Snapshots/ArtifactDelta.cs` · `Assets/FYAsset/Scripts/Shared/Build/Snapshots/Editor/ArtifactDiffer.cs` · `Assets/FYAsset/Scripts/Shared/Build/Repository/`

---

## 文档已合并

差异指纹与 diff 预览已经并入 [构建基线与发布](./repository-构建仓库.md) 统一说明。

请到该文档查看：

- `ArtifactDigest` / `ArtifactDelta` 数据模型（仍位于 `Shared/Build/Snapshots/`）
- Changes 与 AB Hotfix Delivery 两类差异
- 基线（`BuildData/Baselines/`）与 diff 预览的边界
- Push、正式打包与基线写入的先后约束

原基于 Commit 链与 `BuildData/Snapshots/` 的差异快照机制已于 2026-07-24 的基线化重构中删除。
保留本文件是为了兼容已有链接。
