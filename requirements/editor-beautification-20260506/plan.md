# 实施计划（Checklist）

目的：将 CollectorSetting.asset 的 Inspector 美化为一个入口按钮，并扩展 BuildPipelineWindow 的侧边栏以容纳 Pipeline 与 Builder 的额外配置入口。

任务清单：
1. 需求确认（本文档） - 完成
2. 代码位置确定 - 确认 CollectorSettingInspector 放置路径（建议：Assets/Editor/XLuaHotfix/），BuildPipelineWindow 修改路径（建议：Assets/Editor/BuildPipeline/）
3. CollectorSettingInspector 实现 - C# 编辑器脚本
   - 在 OnInspectorGUI 显示一个大按钮（可使用自定义 GUIStyle，使之在 Inspector 中占据显著位置）
   - 点击按钮后，打开或聚焦 BuildPipelineWindow，并通知窗口侧栏切换到对应的 Pipeline/Builder 子项
   - Inspector 默认隐藏原始序列化字段（可提供一个折叠开关以便高级用户查看）
4. BuildPipelineWindow 扩展 - 侧边栏与面板
   - 在侧边栏的 "Pipeline" 区域添加 BuildPipelineConfig 的入口或展开预览（显示配置名、简要描述、编辑按钮）
   - 在 "Builder" 区域新增 Hotfix / Snapshot 配置入口（至少为占位 UI，显示当前配置文件与编辑入口）
   - 保持现有功能兼容，新增项以只读或占位方式先行；必要时提供到配置窗口的跳转
5. 本地化与样式调整
   - 按需提供中文/英文文案，UI 风格兼容暗黑/浅色 Editor 主题
6. 验证与测试
   - 手动验证：打开 CollectorSetting.asset 显示按钮；点击可打开 BuildPipelineWindow 并选中相应侧栏项
   - 验证新增面板不影响已有构建流程调用或窗口行为
7. 文档与提交
   - 在 requirements/editor-beautification-20260506/ 中更新 progress.txt，写明完成步骤与变更摘要
   - 提交 PR 并请求代码审查

验收标准：
- CollectorSettingInspector 在打开 CollectorSetting.asset 时显示醒目按钮，且按钮可正常打开 BuildPipelineWindow
- BuildPipelineWindow 的侧边栏可见新增的 Pipeline 与 Builder 条目（至少为只读/占位），并可通过按钮导航到该条目

风险与缓解：
- 风险：直接修改 BuildPipelineWindow 可能影响现有构建逻辑或导致回归；
  缓解：初期仅在 UI 层做只读/占位，不更改底层逻辑；分小步提交 PR 并通知团队。
- 风险：不同开发者对 Editor 资源位置有不同约定，可能引发合并冲突；
  缓解：在 PR 描述中标注修改路径，协商一致后合并。

时间估算：
- CollectorSettingInspector：2-4 小时
- BuildPipelineWindow 侧边栏扩展：4-8 小时（视窗口复杂度）
- 测试与修正：1-2 小时

负责人：工具链工程师 / 编辑器工程师（默认由本任务申请者承担）

审批清单（提交 PR 前需决策）：
- CollectorSettingInspector 是否仅做导航（推荐：仅导航）或允许在 Inspector 中直接编辑配置？
- 新增 Pipeline / Builder 面板是否需要立即支持保存到配置文件（推荐：先只做显示/导航）
