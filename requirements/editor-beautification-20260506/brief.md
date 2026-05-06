# Editor Beautification - 简要说明

目标：为 CollectorSetting.asset 添加一个自定义 Inspector（CollectorSettingInspector），在 Inspector 中不直接显示序列化字段，而是以一个醒目的大按钮 "Open Build Pipeline Window" 替代，点击后打开项目内已有的 BuildPipelineWindow 窗口。UI 风格参考 Addressables / YooAsset 等资源包的配置面板，提升可发现性与可用性。

背景与动机：
- 当前 CollectorSetting.asset 在 Inspector 中以原始序列化数据呈现，使用体验差且容易误操作。
- 团队已有 BuildPipelineWindow 作为构建/打包流程的集中管理入口，CollectorSetting 的主要操作场景是通过构建流程来配置与执行。将 CollectorSetting 的 Inspector 与 BuildPipelineWindow 做导航联通，能减少重复配置并引导用户进入统一的构建视图。
- 参考 Addressables / YooAsset 的做法，使用按钮/快捷入口替代复杂字段展示，可以显著降低出错率并提升新手上手速度。

目标产出：
1. 新增 CollectorSettingInspector 编辑器脚本（位于 Assets/Editor/... 或 Packages/...），在 Inspector 顶部展示大按钮，按钮文本为："Open Build Pipeline Window"（或中文本地化），点击后聚焦并打开 BuildPipelineWindow。
2. 在 BuildPipelineWindow 的侧边栏扩展：
   - 在 "Pipeline" 面板中加入对 BuildPipelineConfig 的入口或展开视图，方便在同一窗口查看/编辑管道配置。
   - 在 "Builder" 面板中加入 Hotfix / Snapshot 相关配置入口（占位 UI 或可实际编辑小组件），为后续热更功能集成做准备。

可验收条件：
- 打开 CollectorSetting.asset 时，Inspector 显示大按钮而非原始序列化字段（至少在默认折叠状态下）。
- 点击按钮可以可靠打开或切换到 BuildPipelineWindow，并在侧边栏默认选中 Pipeline 或 Builder 中的对应子项。
- BuildPipelineWindow 能显示新增的 Pipeline / Builder 配置入口（功能占位或基础编辑能力），且不会破坏现有窗口行为。

兼容性与注意事项：
- 避免触碰运行时逻辑，仅限编辑器功能改造。
- 新增 UI 要兼容现有的 EditorSkin（暗黑/浅色）与缩放设置。
- 如果项目使用代码生成（XLua 相关），不应将运行时代码移动到 Editor 目录之外。
