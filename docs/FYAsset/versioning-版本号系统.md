# 版本号系统

> **关联代码** | [VersionNumber](../../Assets/FYAsset/Scripts/Shared/Build/Versioning/VersionNumber.cs) · [VersionRecord](../../Assets/FYAsset/Scripts/Shared/Build/Versioning/VersionRecord.cs)

`VersionNumber` 是含解析与比较逻辑的 struct。发布字符串是 `Major.Minor.Patch[-Channel]`；Build 独立存储，不拼入字符串，也不参与排序和相等性。

## 值语义

赋值、参数传递和返回会复制版本值。修改副本不会更新原 VersionRecord 或 Manifest；需要修改所有者字段或把修改后的值赋回。Channel 是 string，但 string 自身不可变。

`default(VersionNumber)` 的数值字段均为零、Channel 为 null，格式化得到 `0.0.0`。`TryParse` 失败返回 false 并输出 default；调用方必须检查返回值，不能仅看输出值是否为零。`Parse` 失败抛 FormatException。

字段没有内置范围保护；手工写负数或非法 Channel 不会在赋值时自动失败。入口校验与 Parse 负责相应边界。

## 字符串与比较

- 格式示例：`1.2.3`、`2.0.0-alpha`、`1.5.0-rc`。
- Parse 接受三个非负整数段及可选小写 alpha/beta/rc，拒绝 `+Build`，不是完整 SemVer 2.0 实现。
- 排序顺序：Major、Minor、Patch，然后 alpha < beta < rc < release。
- Build=1 和 Build=99 的两个 `1.0.0` 值比较相等；`1.0.0+1` 本身不是合法输入格式。
- `GetReleaseVersionString()` 与 `ToString()` 输出相同，没有 `GetVersionString()` API。
- 保留现有 Channel 语义：排序将 null/empty 都视为 release，但 Equals 使用字符串比较，null 与 empty 不相等。不要用排序结果为零代替所有相等性判断。

## VersionRecord

VersionRecord 是位于非 Editor 目录的 ScriptableObject；保存操作中的 UnityEditor 调用由条件编译隔离。CurrentVersion、LastBuildTime、DailyBuildCount 保存当前版本与每日计数。

`BuildNextVersion(isMajor, isMinor)` 计算候选，不直接消耗已保存版本。Major 模式清零 Minor/Patch；Minor 模式清零 Patch；默认模式增加 Patch。正式 Full/Standalone 入口选择 Major，Hotfix 选择默认 Patch；方法支持 Minor 参数不意味着存在独立“功能性热更”构建菜单。

路径由 `FYAssetSettings.VersionRecordPath` 配置。构建缺少资产时失败。AB attempt 交付中应用版本并登记补偿；AA 非 attempt 路径在构建交付返回成功后应用。实际事务差异见 [构建基线与发布](./repository-构建仓库.md)。

## 热更与数据格式

BuildIndex.Version 表示安装包基线，PackageIndex.LatestVersion 表示目标或已激活包版本。Major 是客户端兼容判断的输入，具体决策仍需结合本地完整性、包名和前向版本规则；BuildGUID 不参与版本排序。

BinaryReflectionSerializer 对 struct 不写引用存在标记。class 改为 struct 后，即使字段 Order 不变，旧二进制也不兼容；FYAsset 根 Manifest schema 已更新，旧数据需要重新构建。JSON 读取边界要求版本字段存在且为对象：缺失或 null 拒绝；显式 `0.0.0` 仍合法。PackageIndex 检查 LatestVersion，BuildIndex 与 AAManifest 检查 Version，ABManifest 检查 PackageVersion。

参见 [序列化工具](../Tools/序列化工具.md)、[热更系统](./hotfix-热更新系统.md) 与 [HTML 流程图](./fyasset-modeling.html)。
