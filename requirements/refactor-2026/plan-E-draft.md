# Phase 5-6 构建管线重构 — 草稿 Plan

> 状态：讨论中，非正式计划
> 最后更新：2026-04-08

---

## 已确认的设计决策

### 1. 配置层级：三级结构

```csharp
CollectorSetting (SO)
├── List<CollectorPackage>      // 包（热更包/内置包）
│   └── List<CollectorGroup>    // 分组（UI/Scene/Audio）
│       └── List<Collector>     // 收集器（扫描路径 + 规则）
```

**理由**：作为通用框架，需支持多包场景。

### 2. 规则接口：三规则分离

| 规则 | 职责 | 输入 | 输出 |
|------|------|------|------|
| CollectRule | 决定收集哪些资源 | collectPath | List<AssetInfo> |
| GroupByRule | 决定资源分组 | AssetInfo | GroupKey |
| PackRule | 决定 Bundle 命名 | GroupKey, List<AssetInfo> | BundleName |

**保留接口**：IAddressRule（生成 Address）

**理由**：职责更清晰，与 YooAsset 作者观点一致。

### 3. Bundle 命名组成

```
{packageName}_{groupName}_{type}_{labels}_{hash}.bundle

示例：
hotfix_ui_prefab_panel_abc123.bundle
hotfix_audio_clip_music_def456.bundle
```

**包含部分**：packageName、groupName、type、labels、contentHash

### 4. Pipeline Pattern + BuildContext

**Pipeline Pattern**：
- 每个 Task 只做一件事（单一职责）
- 新增 Task 不影响现有 Task（开闭原则）
- 通过 BuildContext 共享数据（依赖注入）

**BuildContext**：
- 类型安全的存取（泛型约束）
- 封装中间数据，避免全局变量污染
- 生命周期随构建过程自动管理

**Task 序列**：
```
TaskPrepare → TaskCollectAssets → TaskAnalyzeDependencies 
→ TaskBuildBundles → TaskGenerateManifest → TaskGenerateSnapshot 
→ TaskOrganizeOutput
```

### 5. 文件系统设计

**设计原则**：
- 接口分离的依赖注入框架
- 文件系统只做文件流操作，不提供网络下载模块
- 网络下载由下载中心控制（参考现有架构）
- 热更流程维持现有流程

**Bundle 加载流程**：
```
下载（下载中心）→ 解压（文件系统）→ 导入（文件系统）→ 加载（文件系统）
```

**目标**：较解耦的模块，不像 YooAsset 那么复杂

**职责划分**：
| 职责 | 说明 |
|------|------|
| 统一路径管理 | 所有路径通过文件系统获取，自动处理平台差异 |
| 文件读写 | 同步/异步读取、写入、存在性检查 |
| 文件校验 | Hash 计算、CRC 校验 |
| 缓存管理 | 缓存目录管理、清理策略 |
| 文件解压 | 如有压缩需求 |

**不包含**：网络下载（NetworkDownloader）、热更流程、资源加载（ABBundleLoader）

### 6. Analyze Rules：延后到 Phase 8

构建管线中用日志输出检查项，可视化工具延后。

### 7. Group 作用：逻辑分组 + 输出目录

| 作用 | 说明 |
|------|------|
| 逻辑分组 | 便于管理和查看 |
| 输出目录 | `{outputRoot}/{outputSubDir}/` |

**不包含**：打包控制（由 Collector 的 PackRule 决定）

### 8. 标签类型：Labels + Tags

| 类型 | 作用 | 层级 |
|------|------|------|
| Labels | 资源分类、查询过滤 | 资源级 |
| Tags | 下载策略、分包控制 | Bundle 级 |

### 9. IgnoreRule：Collector 级配置

每个 Collector 可配置不同的忽略规则。

**格式**：自定义语义规则（类似 gitignore）

### 10. 循环依赖处理：报错中断构建

---

## 数据结构草稿

```csharp
// ===== 顶层配置 SO =====
public class CollectorSetting : ScriptableObject
{
    public List<CollectorPackage> packages;
}

// ===== Package（包）=====
public class CollectorPackage
{
    public string packageName;           // 包名
    public string outputRoot;            // 输出根目录
    public List<CollectorGroup> groups;
}

// ===== Group（分组）=====
public class CollectorGroup
{
    public string groupName;             // 分组名
    public string outputSubDir;          // 输出子目录
    public List<Collector> collectors;
}

// ===== Collector（收集器）=====
public class Collector
{
    public string collectPath;           // 扫描路径
    public ECollectorType collectorType; // Main/Static/Depend
    public AddressRule addressRule;      // Address 生成规则
    public PackRule packRule;            // Bundle 打包规则
    public FilterRule filterRule;        // 过滤规则
    public IgnoreRule ignoreRule;        // 忽略规则
}
```

---

## 待讨论的问题

### E1: Collector 框架

- [x] 配置层级：三级结构
- [x] Group 作用：逻辑分组 + 输出目录
- [x] 标签类型：Labels + Tags
- [x] IgnoreRule：Collector 级配置
- [ ] ECollectorType 的使用场景（Main/Static/Depend）— 需深入设计
  - 加法原则（DependAsset）vs 减法原则（StaticAsset）
  - 是否需要折中方案
  - IsImplicitDependency 字段设计
- [ ] 标签继承机制
- [ ] CollectorSettingEditor 可视化设计

### E2: Packing 规则

- [ ] 内置 PackRule 实现（PackSeparately/PackDirectory/PackByLabel）
- [ ] Bundle 命名规则细节
- [ ] 是否支持 RawFile Bundle

### E3: 子目录收集器 + 忽略规则

- [ ] 子目录收集器的配置方式
- [ ] 忽略规则的语义设计（类似 gitignore）

### E4: 依赖分析

- [ ] 共享资源处理策略 — 需深入设计
  - Unity 方案：避免跨包共享 / 按加载时机分段 / 依赖独立打包
  - YooAsset 方案：EnableSharePackRule + SingleReferencedPackAlone
  - 建议方案：自动提取 + 可配置阈值（minReferenceCount）
- [x] 循环依赖处理：报错中断构建

### E5: 构建管线

- [x] Pipeline Pattern + BuildContext 大致框架
- [ ] Task 序列细节确认
- [ ] BuildContext 数据结构细节
- [ ] 错误处理策略

### E6: ABManifest 导出

- [ ] 与运行时 ABManifest 的字段对齐确认
- [ ] 版本号来源（VersionNumber vs 配置）

### E7: DifferentialProcessor 适配

- [ ] 快照数据结构扩展
- [ ] 差异检测逻辑迁移

### E8: 文件系统

- [x] 职责划分确认
- [ ] 接口设计
- [ ] 平台适配（Android StreamingAssets）

---

## 下一步讨论

1. E1 剩余细节：ECollectorType 使用场景
2. E2 Packing 规则细节
3. E4 共享资源处理策略
4. E5 构建管线细节
5. E8 文件系统接口设计
