# Draft: FYAsset 对标 Addressables / YooAsset 分析

> **Status**: Draft — 2026-05-08
> **Purpose**: 记录框架对标结论，指导后续优先级判断

---

## 已实现覆盖度

| 维度 | Addressables 对标 | YooAsset 对标 | FYAsset 当前状态 |
|------|------------------|--------------|-----------------|
| 运行期加载 | 完整替代 | 等价能力 | AB 路径已落地 |
| Handle 系统 | 泛型更强 | 比 YooAsset 更类型安全 | `AssetHandle<T>` struct + ref-counting |
| 热更流程 | 完整替代 | 等价 | IHotfixPipeline + 双 Backend |
| Collector 采集 | N/A | 结构对齐 | 层级/规则接口/分类器全对齐 |
| 构建管线 | 替代 BuildPlayerContent | 核心对齐 | IBuildTask + BuildContext + DAGScheduler |
| 依赖分析 | 替代 | 缺 shared bundle 策略 | E4 BFS 已实现，SharePolicy 留口 |
| 差异快照 | N/A | N/A (YooAsset 无此设计) | E7 设计完成待实现 |
| Manifest | 替代 Catalog | 结构等价 | ABManifest + binary/JSON 双格式 |
| 文件系统抽象 | PathManager 较简 | 缺 IFileSystem 层 | 仅 PathManager + FileHelper |
| 下载管理 | 基础 | 缺并发控制/断点续传 | NetworkDownloader 基础功能 |
| Editor 模拟模式 | 依赖 Addressables | 未实现 | 仍用 Addressables 模拟 |

---

## FYAsset 相对优势

1. **泛型 Handle** — `AssetHandle<T>` 编译期类型安全
2. **显式错误处理** — RuntimeMessage 值类型，不依赖异常
3. **自动差异热更** — 快照 diff 自动识别变更资源
4. **构建管线可控** — IBuildTask DAG 完全自定义
5. **二进制序列化** — 零依赖自研 binary codec

---

## DAGScheduler 并行性澄清

DAGScheduler 实现为**逻辑 DAG + 单线程串行执行**：
- 拓扑排序确保依赖顺序正确
- 同批次（入度=0）Task 按字母序顺序执行，非多线程并行
- `SequentialMode` 控制批大小（true=1, false=ready.Count），但执行仍是 for 循环
- Unity Editor 主线程限制下，真正的并行需要 Task 内部自行 offload 到子线程

**价值**: 正确性保证 + 冲突检测 + 确定性 + 未来扩展口（改 for 为 Task.WhenAll）

**对比 YooAsset**: YooAsset 也是顺序执行 IBuildTask（`foreach task in pipeline: task.Run(context)`），无并行。两者在这点上等价。

---

## 关键设计差异

| 决策点 | FYAsset | YooAsset | 评价 |
|--------|---------|----------|------|
| 运行期依赖模型 | Bundle 级 | Asset 级 DependBundleIDs | FYAsset 更简，YooAsset 更精确 |
| Handle 泛型 | `AssetHandle<T>` generic struct | `AssetHandle` runtime cast | FYAsset 更安全 |
| 引用计数 | Entry 级单层 | Provider + Bundle 双层 | YooAsset 更健壮 |
| 构建管线 | DAGScheduler 拓扑序 | Sequential pipeline | 等价（都是单线程串行） |
| Play Mode | 2 Backend | 5 模式 | YooAsset 覆盖更全 |
| 文件系统 | 高层 IPackageBackend | 低层 IFileSystem 组合 | YooAsset 可扩展性更强 |
| 时间切片 | 未实现 | MaxTimeSlice=30ms | YooAsset 防帧率卡顿 |
| 下载管理 | 基础 | 并发+续传+重试+CDN容灾 | YooAsset 生产级 |

---

## 可补充方向（按优先级）

| 优先级 | 功能 | 来源 | 阶段 |
|--------|------|------|------|
| 高 | DAGScheduler 接入 BuildCommandLine | 审查发现 | E7-T11 |
| 高 | TaskBuildBundles 增量重建 | 审查发现 | E7-T9 |
| 中 | Shared bundle 策略 | YooAsset | E4 SharePolicy 扩展 |
| 中 | Editor 模拟模式 | YooAsset EditorSimulate | Phase 8 |
| 低 | 下载并发控制 + 断点续传 | YooAsset | Phase 7 F2 |
| 低 | OperationSystem 时间切片 | YooAsset | Phase 9 H1 |
| 低 | IFileSystem 组合 | YooAsset | 仅 DLC 场景需要 |

---

## Change Log

| Date | Change |
|------|--------|
| 2026-05-08 | Initial draft. DAGScheduler 并行性澄清：逻辑 DAG + 单线程串行，与 YooAsset 等价 |
