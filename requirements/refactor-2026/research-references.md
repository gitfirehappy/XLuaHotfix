# Phase 5-6 构建管线重构 — 调研参考

> 用于收集信息和灵感的参考列表
> 最后更新：2026-04-08

***

## 一、E1: Collector 框架

### 1.1 ECollectorType 使用场景

**核心问题**：如何处理动态加载的资源（构建时无法通过静态引用分析发现）

**调研方向**：

| 方案   | 描述                                                  | 参考来源     |
| ---- | --------------------------------------------------- | -------- |
| 加法原则 | DependAssetCollector 手动添加可能被动态加载的资源                 | YooAsset |
| 减法原则 | StaticAssetCollector 强制打包整个目录，保证完整性                 | 作者观点     |
| 折中方案 | Main + 自动依赖分析 + Static 补漏 + IsImplicitDependency 标记 | 建议方案     |

**调研链接**：

- YooAsset Collector 文档：<https://www.yooasset.com/docs/guide/AssetCollector>
- Unity Addressables 动态加载最佳实践：<https://docs.unity3d.com/Packages/com.unity.addressables@latest>

**思考问题**：

1. 当前项目中是否有动态加载资源的需求？
2. 如何平衡包体大小与资源完整性？
3. IsImplicitDependency 字段在运行时如何使用？

### 1.2 标签继承机制

**核心问题**：Group/Collector 级标签如何继承给资源

**调研方向**：

| 方案         | 描述                          |
| ---------- | --------------------------- |
| 两级继承       | Group 和 Collector 级标签都继承给资源 |
| 只 Group 继承 | 只有 Group 级标签继承              |
| 不继承        | 每个资源单独配置标签                  |

**调研链接**：

- YooAsset Tags 传播机制：<https://www.yooasset.com/docs/guide/AssetCollector#标签传播>

**思考问题**：

1. 标签继承是否会导致标签爆炸？
2. 如何处理标签冲突？

### 1.3 CollectorSettingEditor 可视化设计

**调研链接**：

- YooAsset Collector Window 截图：<https://www.yooasset.com/docs/guide/AssetCollector>
- Unity Addressables Groups Window

**思考问题**：

1. 如何展示三级结构（Package/Group/Collector）？
2. 如何可视化规则配置？
3. 如何预览收集结果？

***

## 二、E2: Packing 规则

### 2.1 内置 PackRule 实现

**调研方向**：

| PackRule       | 描述                       | 适用场景     |
| -------------- | ------------------------ | -------- |
| PackSeparately | 每个资源单独打包                 | 大资源、独立更新 |
| PackDirectory  | 按目录打包                    | 同类资源     |
| PackByLabel    | 按标签组合打包                  | 当前默认     |
| PackCollector  | 整个 Collector 打成一个 Bundle | 小资源合集    |

**调研链接**：

- YooAsset PackRule：<https://www.yooasset.com/docs/guide/AssetCollector#打包规则>
- Unity AssetBundle 最佳实践：<https://docs.unity3d.com/Manual/AssetBundles-BestPractices.html>

**思考问题**：

1. 不同 PackRule 对热更的影响？
2. Bundle 大小与加载性能的平衡？

### 2.2 RawFile Bundle

**调研方向**：

- 原始文件打包（非 AssetBundle）
- 适用于：Lua 脚本、配置文件、视频等

**调研链接**：

- YooAsset RawFile：<https://www.yooasset.com/docs/guide/AssetCollector#原生文件>

**思考问题**：

1. 当前项目是否有 RawFile 需求？
2. RawFile 与 AssetBundle 的统一接口设计？

***

## 三、E3: 忽略规则

### 3.1 忽略规则语义设计

**调研方向**：

| 方案           | 描述        | 示例                                       |
| ------------ | --------- | ---------------------------------------- |
| gitignore 风格 | glob 模式匹配 | `*.cs`, `Editor/`, `!Editor/MyEditor.cs` |
| 配置列表         | 后缀名/目录名列表 | `[".cs", ".meta", "Editor"]`             |
| 自定义语义        | 简化的规则语言   | 类似 gitignore 但更简单                        |

**调研链接**：

- .gitignore 规范：<https://git-scm.com/docs/gitignore>
- YooAsset IgnoreRule：<https://www.yooasset.com/docs/guide/AssetCollector#忽略规则>

**思考问题**：

1. 需要支持哪些忽略模式？
2. 是否需要支持否定模式（!）？
3. 规则的可视化编辑？

***

## 四、E4: 依赖分析

### 4.1 共享资源处理策略

**核心问题**：被多个 Bundle 引用的资源如何处理

**调研方向**：

| 方案      | 描述                    | 优点     | 缺点           |
| ------- | --------------------- | ------ | ------------ |
| 避免跨包共享  | 将共享依赖的资源放在同一个 Bundle  | 简单     | Bundle 过大    |
| 按加载时机分段 | 不会同时加载的 Bundle 可以共享依赖 | 减少内存   | 增加包体         |
| 依赖独立打包  | 共享资源单独打包成 Bundle      | 彻底消除重复 | 需管理运行时依赖     |
| 自动提取    | 被 N+ Bundle 引用的资源自动提取 | 自动化    | 可能产生小 Bundle |

**调研链接**：

- Unity AssetBundle 重复资源：<https://docs.unity3d.com/Manual/AssetBundles-Troubleshooting.html>
- YooAsset EnableSharePackRule：<https://www.yooasset.com/docs/guide/BuildPipeline#共享资源打包>

**思考问题**：

1. 共享资源的阈值如何确定？
2. 单引用共享资源是否需要单独打包？
3. 共享 Bundle 的命名规则？

***

## 五、E5: 构建管线

### 5.1 Task 序列设计

**调研方向**：

- YooAsset BuildPipeline Task 序列
- Unity Scriptable Build Pipeline

**调研链接**：

- YooAsset BuildPipeline：<https://www.yooasset.com/docs/guide/BuildPipeline>
- Unity Scriptable Build Pipeline：<https://docs.unity3d.com/Packages/com.unity.scriptablebuildpipeline@latest>

**思考问题**：

1. 是否需要支持 Task 插入/移除？
2. Task 之间的依赖关系如何管理？
3. 增量构建如何实现？

### 5.2 BuildContext 数据结构

**调研方向**：

- YooAsset BuildContext
- ASP.NET Core HttpContext

**思考问题**：

1. Context 对象的生命周期？
2. 如何避免 Context 污染？
3. Context 的序列化需求？

### 5.3 错误处理策略

**调研方向**：

- 构建失败时的回滚策略
- 错误日志的收集和展示

**思考问题**：

1. 构建失败后如何清理中间产物？
2. 如何定位错误发生的 Task？

***

## 六、E6: ABManifest 导出

### 6.1 与运行时 ABManifest 对齐

**调研方向**：

- 当前 ABManifest 字段定义（plan-B6-manifest.md）
- 运行时 ABAssetIndex 的查询需求

**思考问题**：

1. 构建时生成的字段是否足够？
2. 是否需要额外的构建时元数据？

### 6.2 版本号来源

**调研方向**：

- VersionNumber 类的使用
- 是否需要 Package 级独立版本号

**思考问题**：

1. 多包场景下的版本管理？
2. 版本号与热更的关系？

***

## 七、E7: DifferentialProcessor 适配

### 7.1 快照数据结构扩展

**调研方向**：

- 当前 AssetSnapshot 字段
- 新增字段（BundleName、PrimaryType、BundleIndex）

**思考问题**：

1. 快照格式是否需要向后兼容？
2. 快照迁移脚本的需求？

### 7.2 差异检测逻辑迁移

**调研方向**：

- 当前 DifferentialProcessor 的差异检测逻辑
- 如何适配新的构建管线

**思考问题**：

1. 差异检测的触发时机？
2. 如何保持与现有热更流程的兼容？

***

## 八、E8: 文件系统

### 8.1 接口设计

**调研方向**：

- YooAsset IFileSystem：<https://www.yooasset.com/docs/guide/FileSystem>
- Unity StreamingAssets 访问方式

**思考问题**：

1. 接口粒度如何确定？
2. 是否需要支持虚拟文件系统？

### 8.2 平台适配

**调研方向**：

- Android StreamingAssets 的特殊处理
- iOS/PC 的路径差异

**调研链接**：

- Unity StreamingAssets：<https://docs.unity3d.com/Manual/StreamingAssets.html>
- UnityWebRequest 读取 StreamingAssets

**思考问题**：

1. 如何统一处理不同平台的文件访问？
2. 是否需要支持异步文件操作？

***

## 九、综合参考

### 9.1 开源项目

| 项目           | 链接                                                          | 参考价值                                       |
| ------------ | ----------------------------------------------------------- | ------------------------------------------ |
| YooAsset     | <https://github.com/tuyoogame/YooAsset>                     | Collector/Packing/BuildPipeline/FileSystem |
| Addressables | <https://github.com/Unity-Technologies/Addressables-Sample> | Analyze Rules/Group Schema                 |
| xasset       | <https://github.com/xasset/xasset>                          | 构建管线设计                                     |

### 9.2 文章

| 文章                       | 链接                                                            | 参考价值   |
| ------------------------ | ------------------------------------------------------------- | ------ |
| 游戏开发：资源管理与YooAsset分析     | <https://zhuanlan.zhihu.com/p/1983829640641524143>            | 架构分析   |
| Unity AssetBundle 常见问题排查 | <https://blog.csdn.net/qq_40882017/article/details/154342119> | 重复资源处理 |

### 9.3 已有知识库

| 文件                            | 路径                    | 内容                   |
| ----------------------------- | --------------------- | -------------------- |
| yooasset-collector-packing.md | context/dependencies/ | Collector/Packing 规则 |
| yooasset-build-pipeline.md    | context/dependencies/ | 构建管线                 |
| yooasset-manifest-model.md    | context/dependencies/ | Manifest 数据模型        |
| yooasset-runtime-loading.md   | context/dependencies/ | 运行时加载                |
| yooasset-filesystem.md        | context/dependencies/ | 文件系统                 |

