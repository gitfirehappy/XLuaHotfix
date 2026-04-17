# Phase 5-6 构建管线重构 — 草稿 Plan

> 状态：草稿收敛完成，待精确化
> 最后更新：2026-04-17

***

## 已确认的设计决策

### 1. 配置层级：三级结构

```csharp
CollectorSetting (SO)
├── List<CollectorPackage>      // 包（热更包/内置包）
│   └── List<CollectorGroup>    // 分组（UI/Scene/Audio）
│       └── List<Collector>     // 收集器（扫描路径 + 规则）
```

### 2. 规则接口：三规则分离

| 规则 | 职责 | 输入 | 输出 |
| --- | --- | --- | --- |
| CollectRule | 决定收集哪些资源 | collectPath | List\<AssetInfo\> |
| GroupRule | 决定资源分组 | AssetInfo | GroupKey |
| PackRule | 决定 Bundle 命名 | GroupKey, List\<AssetInfo\> | BundleName |

保留接口：IAddressRule（生成 Address）

### 3. Bundle 命名组成

```
{packageName}_{groupName}_{type}_{labels}_{hash}.bundle

示例：
hotfix_ui_prefab_panel_abc123.bundle
hotfix_audio_clip_music_def456.bundle
```

- Labels 全部入名，多标签用下划线连接
- Hash 长度可配置（默认 8 位，可选 16/32 位）
- type 字段取 PrimaryType 简名（如 Prefab、Texture2D）

### 4. Pipeline Pattern + BuildContext

- 每个 Task 只做一件事（单一职责）
- 新增 Task 不影响现有 Task（开闭原则）
- 通过 BuildContext 共享数据（依赖注入）
- BuildContext 类型安全存取（泛型约束），生命周期随构建过程自动管理

### 5. 文件系统设计

- 接口分离的依赖注入框架
- 文件系统只做文件流操作，不提供网络下载模块
- 网络下载由下载中心控制（参考现有架构）
- 热更流程维持现有流程

职责划分：

| 职责 | 说明 |
| --- | --- |
| 统一路径管理 | 所有路径通过文件系统获取，自动处理平台差异 |
| 文件读写 | 同步/异步读取、写入、存在性检查 |
| 文件校验 | Hash 计算、CRC 校验 |
| 缓存管理 | 缓存目录管理、清理策略 |
| 文件解压 | 如有压缩需求 |

不包含：网络下载（NetworkDownloader）、热更流程、资源加载（ABBundleLoader）

### 6. Analyze Rules：延后到 Phase 8

构建管线中用日志输出检查项，可视化工具延后。

### 7. Group 作用：逻辑分组 + 输出目录

- 逻辑分组：便于管理和查看
- 输出目录：`{outputRoot}/{outputSubDir}/`
- 不包含：打包控制（由 Collector 的 PackRule 决定）

### 8. 标签类型：Labels + Tags

| 类型 | 作用 | 层级 |
| --- | --- | --- |
| Labels | 资源分类、查询过滤 | 资源级 |
| Tags | 下载策略、分包控制 | Bundle 级 |

### 9. IgnoreRule：Collector 级配置

每个 Collector 可配置不同的忽略规则。格式：自定义语义规则（类似 gitignore）。

### 10. 循环依赖处理：报错中断构建

### 11. 构建管线接口后端分离

- 架构统一为"接口层 → Task 层 → 具体后端层"，流程语义与实现职责解耦。
- 旧构建方案与新构建方案通过后端切换进行安全替换。
- 开关粒度为"后端级切换"（Addressable 后端 / AB 后端）。
- 新后端以 ABManifest 作为主数据源，不依赖 VersionState。
- VersionState 为旧后端产物：旧设计保持原样；新后端不依赖、不生成。

***

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
    public SharePolicy sharePolicy;      // 共享资源策略
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

// ===== 资产分类 =====
public struct AssetClassification
{
    public AssetRole AssetRole;       // Main / Static / ImplicitDependency
    public PayloadKind PayloadKind;   // Serialized / RawFile / Scene
}

// ===== 共享策略 =====
public class SharePolicy
{
    public int minReferenceCount = 2;        // 被几个 Bundle 引用才提取到共享 Bundle
    public long minAssetSizeBytes = 0;       // 小于此大小的不提取
    public List<string> noSharePatterns;     // 路径模式：匹配的资产不提取，允许重复
}

// ===== 构建任务接口 =====
public interface IBuildTask
{
    string TaskName { get; }
    string[] DependsOn { get; }
    string[] ReadKeys { get; }
    string[] WriteKeys { get; }
    BuildTaskResult Execute(BuildContext context);
}
```

***

## E1: Collector 框架（已定）

- [x] 配置层级：三级结构
- [x] Group 作用：逻辑分组 + 输出目录
- [x] 标签类型：Labels + Tags
- [x] IgnoreRule：Collector 级配置
- [x] ECollectorType 与 PayloadKind 正交分离
- [x] 标签继承：默认不继承，显式声明优先
- [ ] CollectorSettingEditor 可视化设计（归编辑器专项）

### E1 已定决策

1. ECollectorType 与 PayloadKind 正交分离：
   - ECollectorType（用户配置维度）= Main / Static / Depend — 表示"你想怎么对待这个资源"。
   - PayloadKind（Classifier 推断维度）= Serialized / RawFile / Scene — 表示"这个资源技术上是什么"。
   - 两者正交组合构成资产完整分类身份。

2. 核心契约精简为 2 字段：
   - AssetClassification = { AssetRole, PayloadKind }
   - 三规则职责边界：CollectRule（扫描+过滤+身份识别）→ GroupRule（逻辑分组，不改身份）→ PackRule（Bundle 归属+命名，可看 PayloadKind）。

3. 依赖分析阶段权限：
   - 可以：发现未收集的隐式依赖 → 创建 AssetRole=ImplicitDependency 新条目。
   - 可以：标记已收集资源的 IsImplicitDependency 标志位（不改变 AssetRole）。
   - 不可以：降级 Main → Depend / Static → ImplicitDependency / 修改显式 PayloadKind。
   - 归属模块：E4 TaskAnalyzeDependencies。

4. PackRule 关系：
   - 三种内置规则（PackSeparately / PackByDirectory / PackByLabel）并列可选。
   - PackRule 是接口，框架支持自定义实现。
   - 特殊场景优先通过子目录收集器（E3）+ 目录结构组合解决。

5. 未识别资源策略：
   - Dev：仅告警并跳过打包。
   - CI/Release：被引用且未识别的资源阻断构建；未被引用资源仅告警。

### E2: Packing 规则（已定）

- [x] 内置 PackRule（PackSeparately/PackDirectory/PackByLabel）— 并列可选，接口支持自定义扩展
- [x] Bundle 命名规则细节（见已确认决策 #3）
- [x] RawFile 是 PayloadKind，PackRule 内部差异化处理

### E3: 子目录收集器 + 忽略规则（方向已定）

- [x] 支持子目录收集器；不支持按资产对象类型进行收集
- [x] 子目录收集器与 IgnoreRule 执行顺序：先路径归属排除，再执行 IgnoreRule
- [ ] 子目录收集器的配置方式（细节待定）
- [ ] 忽略规则的语义设计（细节待定）

**子目录收集器核心规则**：

1. 目录优先：资源归属由目录路径决定。
2. 自动剔除：父收集器自动排除已被子收集器覆盖的路径。最深路径优先。
3. 冲突检测：同深度多收集器覆盖同一路径 → 配置冲突告警。
4. 唯一归属：每个资源在收集阶段仅归属一个收集器。

**待定**：冲突级别策略（Dev 告警 / CI 阻断的差异）。

### E4: 依赖分析（已定）

- [x] 循环依赖处理：报错中断构建
- [x] 共享资源处理策略 — 简化方案

**E3-E4 责任分界**：

- E3 管归属：每个资源唯一归属一个收集器，输出 AssetClassification。
- E4 管共享：基于归属结果构建依赖图，仅对 ImplicitDependency 执行共享提取。
- E4 不覆盖 E3 明确归属的 Main/Static 资源。

**SharePolicy（Package 层配置）**：

- AutoShare 仅对 ImplicitDependency 生效（Main/Static 不存在重复问题）。
- ForceShare 去掉（显式加入 Collector 即可）。
- NoShare 作为例外机制（路径模式匹配，允许重复不提取）。
- 95% 场景只配 minReferenceCount + minAssetSizeBytes 全自动。

### E5: 构建管线（已定）

- [x] Pipeline Pattern + BuildContext 大致框架
- [x] DAG 调度器
- [x] IBuildTask 4 字段契约
- [x] 骨干节点 + 扩展节点
- [ ] BuildContext 数据结构细节（实施时定）
- [ ] 错误处理策略细节（实施时定）

**后端级切换**：

- 构建入口选择 BackendMode（LegacyAddressable / ABManifest），启动后锁定。
- SO 提供默认 BackendMode，构建参数可覆盖。
- Task 调度层只依赖接口，不直接依赖具体后端类型。

**DAG 调度器**：

- 调度原语：仅 Sequence + Parallel。不引入 Selector/Fallback/Decorator。
- 节点粒度：Task 级（不拆子步骤）。
- 调度配置：SO 统一配置。
- 冲突处理：声明式读写集合 + 连线阶段拒绝。
- 兼容：提供顺序执行回退模式（Debug/排障）。

**IBuildTask 接口 4 字段**：

- TaskName：唯一标识。
- DependsOn：前置依赖列表。
- ReadKeys / WriteKeys：BuildContext 数据键声明。
- Execute(BuildContext)：返回 BuildTaskResult。

**骨干节点（6 个，不可跳过）**：

1. TaskPrepareContext（版本/路径/平台 + 后端锁定）
2. TaskCollectAssets（收集资源列表 + 分类）
3. TaskAnalyzeDependencies（依赖分析 + 共享提取）
4. TaskBuildBundles（核心打包）
5. TaskGenerateManifest（产出 ABManifest）
6. TaskOrganizeOutput（输出整理）

**可选扩展节点（声明依赖后插入 DAG）**：

- TaskGenerateSnapshot（仅 Full 构建）
- TaskPrepareDiff（仅 Hotfix 构建）
- TaskExportRuntimeIndex（旧后端兼容 version_state 导出）
- 自定义扩展：Lua 脚本数据导出、游戏配置导出、Shader 变体收集等

**错误处理**：Task 策略 + 统一返回壳层，不做全局一刀切重试规则。

### E6: ABManifest 导出（已定）

- [x] ABManifest 只承载运行时消费字段，不承担构建侧扩展信息容器职责
- [x] 版本号来源：BuildContext 统一版本字段（单一事实源）
- [x] E6/E7 采用"同批次、分 Task"协作，复用同一次扫描结果

### E7: DifferentialProcessor 适配（方向已定）

- [x] 差异逻辑迁移边界：沿接口 + 新旧后端分离路径
- [x] DiffResult 双轨输出（AssetDelta + BundleDelta）
- [x] 快照扩展必须包含 BundleDigestList（name/hash/size）
- [x] BundleDigestList 归属快照文件侧（非 ABManifest）
- [x] DeleteList 双轨过渡（资产级保留 + Bundle 级新增）
- [x] ConfirmRelease 固化"快照 + BundleDigest + 发布指针"
- [x] AB 后端直接以 ABManifest 作为运行时更新主索引
- [ ] 快照数据结构扩展（实施时定）
- [ ] 差异检测逻辑迁移（实施时定）

**候选接口拆分（不锁命名）**：

1. IBuildArtifactBackend — 隔离"如何导出构建主数据"
2. IRuntimeUpdateIndexAdapter — AB 后端下为直通适配层
3. ISnapshotDiffBackend — 隔离差异算法实现

### E8: 文件系统（方向已定）

- [x] 职责划分确认（5 类）
- [x] 平台适配边界：Android StreamingAssets 统一走文件系统适配层读取
- [ ] 接口设计（实施时定）
- [ ] 平台适配实现（待 Android 专项）

***

## 大方向收敛看板（2026-04-17 更新）

| 方向 | 状态 | 已定 | 待定 |
| --- | --- | --- | --- |
| G1（E1+E2） | 已收敛 | 主链路 Classifier→GroupRule→PackRule；ECollectorType 与 PayloadKind 正交分离；核心契约 2 字段；PackRule 并列可选+接口扩展；Bundle 命名：Labels 全部入名，Hash 可配置 | — |
| G2（E3+E4） | 已收敛 | E3 管归属、E4 管共享；依赖分析仅补写不覆盖；SharePolicy Package 层：AutoShare + NoShare，仅处理 ImplicitDependency | E3 子目录冲突级别 |
| G3（E5） | 已收敛 | DAG 调度器（Sequence+Parallel）；IBuildTask 4 字段契约；6 骨干节点+扩展节点；HelperBuildData 已取消 | — |
| G4（E6+E7） | 收敛 | BuildContext 统一版本源；Digest 在快照；ConfirmRelease 固化；version_state 仅旧后端 | 回滚机制文档化 |
| G5（E7） | 非优先 | 旧后端可停放，不强制退场 | — |
| G6（E8） | 延期 | 文件系统 5 类接口；异步统一 Task 语义 | Android StreamingAssets 治理 |

## 暂定口径（未定稿）

1. E2 命名倾向：Bundle 名尽量保留更多语义信息。
2. G6 倾向：统一访问路径更好，治理原则待安卓专项。

***

## F 系列：特殊资产处理（已定）

### 核心结论：统一管线 + 5 标准扩展点

所有资产走同一条构建管线，不存在"特殊资产管线"。差异化行为通过 5 个标准扩展点注入：

| 阶段 | 扩展点 | 职责 |
|------|--------|------|
| 导入期 | IAssetImportRule | 资产导入时自动设置 Importer 参数 |
| 分类期 | Classifier → PayloadKind | 识别资产技术类型（Serialized/RawFile/Scene） |
| 打包期 | PackRule | Bundle 归属与命名，实现层可读 PayloadKind + PrimaryType 差异化 |
| 构建期 | 自定义 IBuildTask | 构建过程中的额外处理步骤 |
| 运行期 | IPackageBackend | 资产加载方式（LoadAsset / LoadRawFile / LoadScene） |

### 已定决策

1. PayloadKind 是管线路由信号：Serialized / RawFile / Scene 三值覆盖所有资产。
2. BundleType 是描述性标签，供编辑器可视化和诊断用，不影响管线路由。
3. PackRule 接口不变，实现层自然读取 AssetInfo 中的类型信息做差异化处理。
4. RawFile 扩展：Build Backend 增加文件拷贝路径、IPackageBackend 增加 LoadRawFile API、Manifest 携带 PayloadKind。
5. SpriteAtlas 由标准依赖分析自然覆盖，额外需求用自定义 Task 节点。
6. 平台压缩策略由 AssetImportPipeline 处理，不需要构建管线核心变更。
7. 设计原则：遇到新的特殊资产需求时，先分析现有 5 个扩展点能否覆盖，不新增管线分支。

***

## 编辑器架构设计（已定方向）

### 主窗口：BuildPipelineWindow

单窗口 + 左侧侧栏按钮切换功能区，5 个功能区按顺序排布：

| 顺序 | 功能区 | 职责 | 对应架构层 |
|------|--------|------|-----------|
| 1 | **Collector** | Package → Group → Collector 三级树形编辑 | E1/E2/E3 |
| 2 | **Pipeline** | Task DAG 编排预览与扩展节点开关 | E5 |
| 3 | **Builder** | 构建触发（Full/Hotfix）、参数、进度、日志 | E5 |
| 4 | **Inspector** | Bundle 列表、大小、依赖、资产归属查询 | E6/E7 |
| 5 | **Settings** | 全局构建参数（输出路径、Hash、SharePolicy、BackendMode 默认） | E4/E8 |

### 各功能区最小能力集

**Collector 区**：
- 左半区：Package/Group/Collector 树形列表（可折叠、可拖拽排序）
- 右半区：选中项属性面板
- 底部：配置校验（路径冲突 + 规则合法性）
- 可选后加：选中 Collector → 预览收集到的资产列表

**Pipeline 区**：
- Task 列表：骨干 + 扩展节点，标注状态
- 依赖关系：DependsOn、ReadKeys/WriteKeys 展示
- 扩展节点启用/禁用开关
- 拓扑预览：执行顺序 + 并行批次
- 校验：循环依赖检测、读写冲突检测

**Builder 区**：
- 构建模式（Full/Hotfix）、BackendMode、目标平台
- 构建按钮 + 进度条 + 日志滚动
- 最近构建历史（时间、版本、状态、耗时）

**Inspector 区**：
- Bundle 表格：名称、大小、资产数、依赖数、BundleType
- 选中 Bundle → 资产列表 + 依赖关系
- 搜索框：按路径搜索资产归属 Bundle

**Settings 区**：
- CollectorSetting SO 全局参数编辑
- BackendMode 默认值、Hash 长度、SharePolicy 参数、输出路径

### 设计原则

1. 数据源统一：所有编辑器读写同一个 CollectorSetting SO
2. 只做展示和配置：构建逻辑在管线中，编辑器不包含构建逻辑
3. 诊断工具保持独立：T4/T5 等独立 EditorWindow 不合入主窗口
4. 渐进式开发：先 Collector 区 → Builder 区 → Inspector/Settings/Pipeline 按需推进

***

## 延期议题池

1. E3：子目录冲突级别策略（Dev 告警 / CI 阻断的差异）。
2. G6：Android StreamingAssets 治理原则。

***

## 专项讨论进度

1. ~~收集器和规则设计（E1/E2/E3）~~ → **已完成**
2. ~~资源共享逻辑（E4）~~ → **已完成**
3. ~~Task 节点设计（E5）~~ → **已完成**
4. ~~特殊资源处理（F1/F2/F3）~~ → **已完成（统一管线 + 5 扩展点）**
5. ~~编辑器架构设计~~ → **已完成（BuildPipelineWindow + 5 功能区）**
