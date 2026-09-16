# 版本号系统

> **关联代码** | [VersionNumber](../../Assets/FYAsset/Scripts/Shared/Build/Versioning/VersionNumber.cs) · [BuildVersionPlanner](../../Assets/FYAsset/Scripts/Shared/Build/Editor/Summary/BuildVersionPlanner.cs) · [BuildSummaryIndex](../../Assets/FYAsset/Scripts/Shared/Build/Editor/Summary/BuildSummaryIndex.cs) · [BuildSummaryStore](../../Assets/FYAsset/Scripts/Shared/Build/Editor/Summary/BuildSummaryStore.cs)

`VersionNumber` 是含解析与比较逻辑的 struct。发布字符串是 `Major.Minor.Patch[-Channel]`；Build 独立存储，不拼入字符串，也不参与排序和相等性。

## 值语义

赋值、参数传递和返回会复制版本值。修改副本不会更新原版本所有者（`BuildSummaryIndex.ProjectVersion` 或 Manifest）；需要修改所有者字段或把修改后的值赋回。Channel 是 string，但 string 自身不可变。

`default(VersionNumber)` 的数值字段均为零、Channel 为 null，格式化得到 `0.0.0`。`TryParse` 失败返回 false 并输出 default；调用方必须检查返回值，不能仅看输出值是否为零。`Parse` 失败抛 FormatException。

字段没有内置范围保护；手工写负数或非法 Channel 不会在赋值时自动失败。入口校验与 Parse 负责相应边界。

## 字符串与比较

- 格式示例：`1.2.3`、`2.0.0-alpha`、`1.5.0-rc`。
- Parse 接受三个非负整数段及可选小写 alpha/beta/rc，拒绝 `+Build`，不是完整 SemVer 2.0 实现。
- 排序顺序：Major、Minor、Patch，然后 alpha < beta < rc < release。
- Build=1 和 Build=99 的两个 `1.0.0` 值比较相等；`1.0.0+1` 本身不是合法输入格式。
- `GetReleaseVersionString()` 与 `ToString()` 输出相同，没有 `GetVersionString()` API。
- 保留现有 Channel 语义：排序将 null/empty 都视为 release，但 Equals 使用字符串比较，null 与 empty 不相等。不要用排序结果为零代替所有相等性判断。

## 版本计划与提交

没有单独的版本资产：项目版本事实保存在 `BuildData/Summaries/index.json` 的 `BuildSummaryProjectVersion`（`CurrentSuccessfulVersion`、``、``），作用域（后端、平台、通道）记录存在同一个索引的 `Scopes` 里。

候选版本由纯服务 [BuildVersionPlanner](../../Assets/FYAsset/Scripts/Shared/Build/Editor/Summary/BuildVersionPlanner.cs) 计算，它不读写任何存储：

- 项目版本全局唯一：Full / Standalone 推进 `Major`（Minor/Patch 清零），Hotfix 推进 `Patch`；无成功基准时 Full/Standalone 从 `1.0.0` 开始，Hotfix 因缺少同作用域基准而直接拒绝。
- Channel 默认继承当前成功版本，只能在构建确认时显式选择 `alpha` / `beta` / `rc` / `release`（release 映射为无后缀正式版），且禁止通道降级。
- `Build` 写入当日构建序号：按索引的 `` / `` 计算，同日递增。
- 目标版本必须严格高于当前全局成功版本，否则返回可展示的失败原因而不抛异常。

版本提交属于构建事实事务：`BuildProjectRunner` 先提升产物、应用本地启动数据，再写正式 Summary（`BuildData/Summaries/{AA|AB}/{BuildId}.json`，不可变），最后写 Summary Index；任一步失败按逆序回滚，构建失败不推进项目版本。实际事务差异见 [本地交付与发布](./publish-本地交付与发布.md)。

Hotfix 的基准 Full 也从同一份索引定位：按后端、平台、通道取 `LatestFullSummaryId` 后再按摘要的制品路径找到 Full 包目录（[HotfixBaselineResolver](../../Assets/FYAsset/Scripts/Shared/Build/Editor/Summary/HotfixBaselineResolver.cs)）。

## 热更与数据格式

BuildIndex.Version 表示安装包内置包版本，PackageIndex.LatestVersion 表示目标或已激活包版本。Major 是客户端兼容判断的输入，具体决策仍需结合本地完整性、包名和前向版本规则；BuildGUID 不参与版本排序，也不参与兼容判断（它是内置包的目录身份）。

BinaryReflectionSerializer 对 struct 不写引用存在标记。class 改为 struct 后，即使字段 Order 不变，旧二进制也不兼容；FYAsset 根 Manifest schema 已更新，旧数据需要重新构建。JSON 读取边界要求版本字段存在且为对象：缺失或 null 拒绝；显式 `0.0.0` 仍合法。PackageIndex 检查 LatestVersion，BuildIndex 与 AAManifest 检查 Version，ABManifest 检查 PackageVersion。

参见 [序列化工具](../Tools/序列化工具.md)、[热更系统](./hotfix-热更新系统.md) 与 [HTML 流程图](./fyasset-modeling.html)。
