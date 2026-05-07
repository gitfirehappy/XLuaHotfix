# 全链路字段语义参考表

> **版本**: 1.0.0
> **更新日期**: 2026-05-07
> **用途**: 所有数据模型字段的语义权威定义，避免命名歧义和跨层理解偏差
> **前置**: 取代并扩展 `terminology-2026-04-27.md`（已过时，Tags/Labels 定义与当前代码不一致）

---

## 一、配置层（ScriptableObject）

### CollectorSetting
| 字段 | 语义 | 约束 |
|------|------|------|
| `Packages` | 所有 Package 配置列表 | 可由用户增删改 |

### CollectorPackage
| 字段 | 语义 | 约束 |
|------|------|------|
| `PackageName` | 包标识名，用于 Bundle 逻辑名第一段 | 非空唯一，构建期硬校验 |
| `Groups` | 该包下的 Group 列表 | — |
| `SharePolicy` | Per-Package 的共享提取策略配置 | 由 E4 DependencyAnalyzer 消费 |

### CollectorGroup
| 字段 | 语义 | 约束 |
|------|------|------|
| `GroupName` | 组名，Bundle 逻辑名第二段 | 非空，同 Package 内不可重名 |
| `Enabled` | 是否启用；关闭后 CollectionScanner 跳过整组 | — |
| `Labels` | 组级别资产标签，与 Collector.Labels 取并集 | 大小写不敏感匹配 |
| `Collectors` | 该组下的 Collector 列表 | — |

### Collector
| 字段 | 语义 | 约束 |
|------|------|------|
| `CollectPath` | 采集根路径（相对 `Assets/`） | 可指向目录或单文件 |
| `CollectPathType` | 路径类型：Folder / SingleFile | 默认 Folder |
| `CollectorType` | 采集器类型：MainAsset / ByDependency / Implicit | 决定资产语义角色 |
| `ForcePayloadKind` | 强制载荷类型；Auto 由 Classifier 推断 | — |
| `AddressRuleName` | 地址规则类名，由 RuleResolver 反射解析为 IAddressRule | — |
| `PackRuleName` | 打包规则类名，解析为 IPackRule | — |
| `FilterRuleName` | 过滤规则类名，解析为 IFilterRule | — |
| `GroupRuleName` | 分组规则类名，解析为 IGroupRule | — |
| `Labels` | 采集器级别标签，与 Group.Labels 取并集 | 大小写不敏感匹配 |
| `IgnorePatterns` | 类 gitignore 模式列表（`*.ext` / `dirname/` / `*keyword*`） | — |

### SharePolicyConfig
| 字段 | 语义 | 约束 |
|------|------|------|
| `MinReferenceCount` | 最少引用次数阈值，低于此值不进入共享 | 默认 2 |
| `MinAssetSizeBytes` | 最小资产大小阈值；0 = 不按大小过滤 | 计划定义但执行层当前未消费 (P2) |
| `NoSharePatterns` | 禁止共享的文件模式（glob 子集） | 与 ForceSharePatterns 冲突时报 Error |
| `ForceSharePatterns` | 强制共享的文件模式 | 与 NoSharePatterns 冲突时报 Error |

---

## 二、采集中间层（Editor Only，不序列化）

### CollectedAssetInfo
| 字段 | 语义 | 来源 |
|------|------|------|
| `AssetPath` | 资产在项目中的相对路径 | `AssetDatabase.GUIDToAssetPath` |
| `AssetGUID` | Unity GUID，对应 Runtime 的 EntryId | `AssetDatabase.AssetPathToGUID` |
| `Address` | 运行时寻址键 | IAddressRule 生成 |
| `PrimaryType` | 资产主类型名称（如 Texture2D） | `AssetDatabase.GetMainAssetTypeAtPath` |
| `Labels` | 合并后的标签列表 = Group.Labels ∪ Collector.Labels | 扫描期去重 |
| `GroupName` | 所属 Group 名称 | 透传自 Collector 所属 Group |
| `PackageName` | 所属 Package 名称 | 透传自 Collector 所属 Package |
| `BundleName` | 逻辑 Bundle 名（三段式） | BundleNameBuilder 组装 |
| `Classification` | 分类结果：资产角色 + 载荷类型 | Classifier 推断 |
| `CollectorType` | 采集器类型 | 透传自 Collector.CollectorType |
| `IsInSharedBundle` | 是否打入共享 Bundle | 依赖分析决策（E4） |
| `IsDuplicated` | 隐式依赖是否被复制到多个引用 Bundle | 依赖分析决策（E4） |

### AssetClassification（嵌套结构体）
| 字段 | 语义 | 来源 |
|------|------|------|
| `Role` | 资产语义角色：MainAsset / ByDependency / ImplicitDependency | CollectionScanner + DependencyAnalyzer |
| `PayloadKind` | 载荷类型：Serialized / Scene / RawFile | Classifier 推断或 ForcePayloadKind 覆盖 |

---

## 三、构建中间层（Editor Only，不序列化）

### BundleBuildInfo
| 字段 | 语义 | 来源 |
|------|------|------|
| `BundleName` | 逻辑 Bundle 名（如 `hotfix_ui_all`） | 来自 CollectedAssetInfo.BundleName 分组 |
| `OutputFileName` | 实际输出文件名（如 `hotfix_ui_all_abc123.bundle`） | Unity BuildPipeline 产出 |
| `Hash` | 文件内容哈希（MD5），用于增量更新比较 | HashGenerator.GenerateFileHash |
| `Size` | 文件大小（字节），用于下载进度估算 | FileInfo.Length |
| `AssetPaths` | Bundle 内所有资产路径 | 分组聚合 |
| `PayloadKind` | 主导载荷类型 | Serialized / Scene / RawFile |

### BundleDependencyGraph
| 字段 | 语义 |
|------|------|
| `Edges` | Bundle 间依赖边列表（From, To, ViaAssets） |
| `_dependencyMap` | 懒构建的依赖映射缓存（GetDependencyMap 用） |

---

## 四、清单/序列化层（Runtime，JSON 序列化）

### ABManifest
| 字段 | 语义 | 约束 |
|------|------|------|
| `PackageName` | 包裹标识（如 "MainPackage"） | — |
| `PackageVersion` | 包裹版本号（Major.Minor.Patch） | 当前来自 VersionDataBase.asset 直读 |
| `BuildTimestamp` | 构建时间戳（ISO 8601） | 调试用 |
| `AssetEntries` | 所有资产条目 | List\<ManifestAssetEntry\> |
| `BundleEntries` | 所有 Bundle 条目 | List\<ManifestBundleEntry\> |

### ManifestAssetEntry
| 字段 | 语义 | 对应 Runtime 字段 |
|------|------|------------------|
| `EntryId` | 内部唯一身份 = Unity GUID | RuntimeAssetEntry.EntryId |
| `Address` | 运行时寻址键，允许重复 | RuntimeAssetEntry.Address |
| `PrimaryType` | 资产主类型名 | RuntimeAssetEntry.PrimaryType |
| `Labels` | 资产级分类标签（Group.Labels ∪ Collector.Labels） | RuntimeAssetEntry.Labels |
| `SourcePath` | 资产在项目中的原始路径（仅诊断） | RuntimeAssetEntry.SourcePath |
| `Group` | 构建分组名（仅诊断） | RuntimeAssetEntry.Group |
| `AutoAddress` | Address 是否自动生成 | RuntimeAssetEntry.AutoAddress |
| `BundleIndex` | 所属 Bundle 在 BundleEntries 中的下标（Manifest 特有） | — |

### ManifestBundleEntry
| 字段 | 语义 | 约束 |
|------|------|------|
| `BundleName` | Bundle 文件名（如 `hotfix_ui_all_abc123.bundle`） | 唯一标识 |
| `FileHash` | 文件内容哈希（MD5） | 增量更新比较基准 |
| `FileCRC` | 文件 CRC32 校验码 | 快速校验 |
| `FileSize` | 文件大小（字节） | 下载进度估算 |
| `Encrypted` | 是否加密 | 布尔标记 |
| `BundleType` | 内容类型字符串（>80% 阈值推断，否则 "Mixed"） | V1 不用枚举，字符串适配扩展 |
| `Tags` | **Bundle 级下载策略标签**（如 "必装"/"DLC-1"/"语音包"）。语义与 Labels 完全不同。不从 Labels 自动聚合 — 由独立的 Bundle 级配置填入 | 用于增量下载时按标签过滤 |
| `DependBundleIndices` | 依赖 Bundle 在 BundleEntries 中的下标数组 | 递归展开由运行时 ABBundleLoader 负责 |
| `IncludeAssets` | 反向查找：该 Bundle 包含的资产列表 | 运行时字段，不序列化 |
| `ReferencedByBundleIndices` | 反向依赖：依赖本 Bundle 的其他 Bundle 下标 | 运行时字段，不序列化 |

---

## 五、运行时层

### RuntimeAssetEntry
| 字段 | 语义 | 用途 |
|------|------|------|
| `EntryId` | 内部唯一身份 = Unity GUID | 缓存键、诊断、句柄归属 |
| `Address` | 运行时寻址键，允许重复 | LoadByAddress 查询入口 |
| `PrimaryType` | 资产主类型名 | LoadByTypeKey 查询入口，类型兼容校验 |
| `Labels` | 资产级分类标签，无序唯一集合 | HasLabel / HasAllLabels 查询 |
| `SourcePath` | 项目原始路径 | 仅编辑器诊断，不作为运行时查询入口 |
| `Group` | 构建分组名 | 仅编辑器报表与构建语义 |
| `AutoAddress` | 是否自动生成 Address | 标记可否重建 |

### VersionNumber
| 字段 | 语义 |
|------|------|
| `Major` | 主版本号（大版本更新递增） |
| `Minor` | 次版本号（功能更新递增） |
| `Patch` | 补丁号（热修复更新递增） |

### VersionDataBase（SO，仅编辑器）
| 字段 | 语义 |
|------|------|
| `CurrentVersion` | VersionNumber 当前版本号 |
| `LastBuildTime` | 上次构建时间（yyyy-MM-dd HH:mm:ss） |
| `DailyBuildCount` | 当日构建次数 |

---

## 六、Rule 上下文（构建期传入）

### AddressRuleContext
| 字段 | 语义 |
|------|------|
| `AssetPath` | 资产路径 |
| `PrimaryType` | 资产主类型（E1-2 增强添加） |
| `GroupName` | 所属 Group 名 |
| `CollectPath` | 采集根路径 |

### PackRuleContext
| 字段 | 语义 |
|------|------|
| `AssetPath` | 资产路径 |
| `Classification` | 资产分类结果 |
| `Labels` | 资产标签（合并后） |
| `GroupName` | 所属 Group 名 |
| `PackageName` | 所属 Package 名 |
| `CollectPath` | 采集根路径 |

### FilterRuleContext
| 字段 | 语义 |
|------|------|
| `AssetPath` | 资产路径 |
| `PrimaryType` | 资产主类型 |
| `FileExtension` | 文件扩展名 |

### GroupRuleContext
| 字段 | 语义 |
|------|------|
| `AssetPath` | 资产路径 |
| `Classification` | 资产分类结果 |
| `CollectPath` | 采集根路径 |
| `PackageName` | 所属 Package 名 |
| `ParentGroupName` | Collector 直属父 Group 名（E1-2 增强添加） |

---

## 七、BuildContext 关键 Key

| Key 常量 | 值类型 | 写入方 | 消费方 |
|----------|--------|--------|--------|
| `CollectedAssets` | `List<CollectedAssetInfo>` | TaskCollectAssets / TaskCollectBuiltins | TaskAnalyzeDependencies, TaskBuildBundles, TaskGenerateManifest |
| `BundleDependencyGraph` | `BundleDependencyGraph` | TaskAnalyzeDependencies | TaskGenerateManifest, TaskBuildBundles |
| `BundleBuildResults` | `List<BundleBuildInfo>` | TaskBuildBundles | TaskGenerateManifest, TaskVerifyBuildResult, TaskOrganizeOutput |
| `ABManifest` | `ABManifest` | TaskGenerateManifest | TaskVerifyBuildResult, TaskOrganizeOutput |
| `BuildVerificationResult` | `BuildVerificationResult` | TaskVerifyBuildResult | TaskOrganizeOutput |
| `OutputRoot` | `string` | TaskPrepareContext | TaskBuildBundles, TaskVerifyBuildResult, TaskOrganizeOutput, TaskGenerateManifest |
| `BuildVersion` | `string` | TaskPrepareContext | TaskOrganizeOutput (summary) |
| `TargetPlatform` | `BuildTarget` | TaskPrepareContext | TaskBuildBundles, TaskOrganizeOutput |
| `BackendMode` | `BackendMode` | TaskPrepareContext | TaskOrganizeOutput |
| `BuildMode` | `BuildMode` (ForceRebuild / Incremental) | TaskPrepareContext | TaskBuildBundles |

---

## 八、术语关键区分

| 术语对 | 区别 |
|--------|------|
| **Labels vs Tags** | Labels = 资产级分类标签（"UI", "Battle", "Texture2D"）。Tags = Bundle 级下载策略标签（"必装"/"DLC-1"/"语音包"），语义完全不同，不从 Labels 自动聚合 |
| **EntryId vs Address** | EntryId = 内部唯一身份（Unity GUID），用于缓存/句柄/诊断。Address = 运行时寻址键，允许重复，对用户可见 |
| **BundleName（清单）vs BundleName（CollectedAssetInfo）** | ManifestBundleEntry.BundleName = 含 hash 和扩展名的完整文件名。CollectedAssetInfo.BundleName = 不含 hash/后缀的三段式逻辑名 |
| **GroupName vs Group** | GroupName = 当前代码中的标准字段名。ManifestAssetEntry/RuntimeAssetEntry 中的 `Group` 字段待改名为 `GroupName`（terminology doc 已列但未执行） |
| **PayloadKind vs CollectorType** | PayloadKind = 描述资产文件格式（Serialized/Scene/RawFile）。CollectorType = 描述资产在构建管线中的语义角色（MainAsset/ByDependency/Implicit） |

---

## 变更日志

| 日期 | 变更 |
|------|------|
| 2026-05-07 | 初始版本：全链路 7 层数据模型字段语义覆盖 |
