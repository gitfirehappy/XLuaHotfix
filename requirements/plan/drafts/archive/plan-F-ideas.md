# Forward-Looking Ideas (Draft Notes)

> **Status**: ~~Draft notes~~ → **Archived 2026-05-18** — 大部分内容已被后续 plan 吸收，仅 A/B 测试未纳入
> **Principle**: 尽量依赖现有架构能力完成，不过度扩充。每个 idea 标注最小增量改动。

---

## Idea 1: 单机离线包

**目标**：所有资源打入安装包，首次启动无需网络下载。

**依赖现有能力**：
- `CollectorPackage` 层级已有 Package 概念
- `BackendMode` 枚举已有后端切换机制
- `ABPackageBackend` 已有 StreamingAssets 加载路径（B7）

**最小增量**：

```
CollectorPackage 加一个字段:
  public EPackageDeliveryMode DeliveryMode = Streamed;

enum EPackageDeliveryMode:
  Streamed  — CDN 下载（默认，当前行为）
  Builtin   — 打入 StreamingAssets
  Hybrid    — 核心内置 + 可选下载

构建侧 (TaskBuildBundles 或 TaskOrganizeOutput):
  - Builtin → 输出到 StreamingAssets/{Platform}/
  - Streamed → 输出到 Build/{Version}/

运行时:
  ABAssetIndex 初始化: 先读 StreamingAssets manifest → 再检查 CDN 更新 → 合并
```

**增量文件**：`EPackageDeliveryMode.cs` (Runtime, ~10行) + 构建目标路径分支 (~20行) + 运行时双源合并 (~40行)

---

## Idea 2: 后台下载

**目标**：游戏运行中后台拉取资源，不阻塞启动。

**依赖现有能力**：
- `ManifestBundleEntry.Tags` 已预留 Bundle 级下载策略标签
- `ABPackageBackend` + `ABBundleLoader` 已有加载流程
- Labels 系统完整

**最小增量**：

```
Bundle Tags 语义定义:
  "startup"    — 启动阻塞下载（默认，空 Tags = startup）
  "background" — 后台静默下载
  "on-demand"  — 用到时懒加载

构建侧:
  Group.Tags / Collector.Tags → 聚合到 Bundle Tags → 写入 ManifestBundleEntry.Tags
  (Tags 字段已存在，E6 已留空。只需约定 Tag 命名规范)

运行时:
  BackgroundDownloadManager:
    - UnityWebRequest 异步下载
    - 优先级队列（startup > on-demand > background）
    - 下载完成 → 更新 ABAssetIndex → Bundle 立即可用
```

**增量文件**：`BackgroundDownloadManager.cs` (Runtime, ~150行) + Tags 命名约定文档。不修改任何现有结构。

---

## Idea 3: A/B 测试适配

**目标**：不同用户组获取不同资源变体，按需下载。

**依赖现有能力**：
- Labels（Asset 级）+ Tags（Bundle 级）
- GroupRule 可按标签路由资源
- ABManifest 已有 Label 索引查询
- 上述"后台下载"的优先级队列

**最小增量**：

```
配置侧:
  变体资源用不同 Collector，打不同 Labels:
    Collector "UI_vA": Tags=["ab:ui_variant_a"]
    Collector "UI_vB": Tags=["ab:ui_variant_b"]
  公共资源无特殊 Tags
  GroupRule 路由到对应 Group → 产出不同 Bundle

构建侧:
  构建时产出 "VariantIndex"（附加小文件，非 ABManifest 的一部分）:
    Bundle "hotfix_ui_variant_a" → tags ["ab:ui_variant_a"]
    Bundle "hotfix_ui_variant_b" → tags ["ab:ui_variant_b"]
    公共 Bundle 不在 VariantIndex 中

运行时:
  ABTestManager:
    1. 服务端下发: user → "ab:ui_variant_a"
    2. 读 VariantIndex → 过滤下载列表
    3. 只在 Tags 匹配 user group 或 无 AB tag 的 Bundle → 加入下载队列
    4. 复用 BackgroundDownloadManager 下载
```

**增量文件**：`VariantIndex.cs` (Runtime, ~30行数据结构) + `ABTestManager.cs` (Runtime, ~80行) + 构建侧生成 VariantIndex (~40行)。

---

## 三者交叉影响

| | 单机离线包 | 后台下载 | A/B 测试 |
|------|------|------|------|
| 构建管线增量 | DeliveryMode 枚举 + 目标路径分支 | Tags 约定（零代码） | VariantIndex 生成 |
| 运行时增量 | 双源 manifest 合并 | BackgroundDownloadManager | ABTestManager + VariantIndex |
| 依赖现有能力 | Package + BackendMode | Bundle Tags | Labels + GroupRule + BackgroundDownloadManager |
| 是否互阻 | 否 | 否 | 否（依赖后台下载基础设施） |
| 建议 Phase | Phase 7 | Phase 7 | Phase 8 |

---

## 变更记录

| Date | Change |
|------|--------|
| 2026-04-26 | Initial draft — 3 ideas from developer discussion |
