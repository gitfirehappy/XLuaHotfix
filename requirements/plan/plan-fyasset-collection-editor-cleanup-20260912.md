# AB Collection 编辑器与规则模型收敛计划

## 目的

收敛 AB Collection 的 Address、树视图、规则配置和编辑器交互，删除全局 AddressStyle、独立 Asset Overrides 面板、批量 Apply 按钮、手动 Validate 按钮、ExcludedAssets 及 RawFileRules 的三类字段拆分。

## 已确认约束

- Address 样式不再由 Setting 全局控制；未显式编辑的 Address 默认生成完整 `Assets/.../file.ext` 长路径。
- 每个资源只保留可选的短名/长路径样式和显式 Address/Labels 数据；按 GUID 的持久化类型命名为 `AssetAddressEntry` / `AssetAddressEntries`。
- Group 右键应用样式时覆盖该 Group 全部资产。
- `NameType` 删除；`LongAssetPathWithoutExtension` 改名为 `LongAssetPath`，行为保留扩展名。
- Ignore 与 RawFileRules 共用 gitignore 基础匹配器，支持 `*`、`?`、`**`、路径边界、目录规则、注释、`!` 反向规则；两者由上层解释不同。
- ExcludedAssets 删除，单项排除由 Ignore 反向规则表达。
- Details 明显放大，左侧树小幅放大；左侧显示 Address，路径放到 Details。
- Collection、Scan Preview、Curate Preview 初始全部折叠；当前面板生命周期内保留手动展开状态，重新进入时重置。
- 删除手动 Validate 入口，但 Save 前校验继续阻止错误配置落盘。
- 不做旧字段兼容，不保留废弃类型、字段、入口或空残留；当前有效文档和测试同步，历史 review/mistake 记录不改。

## 实施顺序

1. 运行 Collection 相关场景基线并记录结果。
2. 更新数据模型、Address 生成、通用 gitignore 匹配器、扫描/依赖分析/校验调用链。
3. 更新 Collection 编辑器和配置资产。
4. 更新契约测试、当前有效文档与配置语义。
5. 运行 solution build、相关场景、静态残留扫描和 `git diff --check`。
6. 将验证证据追加到 `requirements/progress.txt`。

## 验收标准

- 活动代码、测试和当前有效文档不再引用已删除的 `AddressStyle` 全局字段、`NameType`、`AssetOverride`、`AssetOverrides`、`AssetExclusion`、`ExcludedAssets`、RawFileRules 三字段和手动 Validate 入口。
- Collection UI 满足已确认的 Details、右键菜单、左侧 Address、默认折叠和字号要求。
- Ignore/RawFileRules 的共享 matcher 行为由测试覆盖，显式采集与隐式依赖使用同一规则结果。
- solution 编译成功，受影响场景和 diff 检查通过。
