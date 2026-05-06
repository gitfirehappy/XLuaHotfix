# 验证与手工测试步骤

以下步骤用于在本地 Unity 编辑器中验证已实现的功能（CollectorSettingInspector、PipelinePanel、BuilderPanel）：

1. 打开 Unity 项目并进入 Editor 模式（非 Play 模式）。
2. 在 Project 视图中找到或创建一个 CollectorSetting.asset（类型名为 CollectorSetting 的 ScriptableObject）。
   - 如果项目中尚无该类型，请使用已有示例或临时创建一个同名 ScriptableObject 实例用于验证。\
3. 选中 CollectorSetting.asset，观察 Inspector：
   - Inspector 顶部应显示一个显著的大按钮，文本为 "Open Build Pipeline Window"（或英文）。
   - Inspector 下方有一个折叠项 "Show Raw Serialized Fields"，展开后显示原始序列化字段。
4. 点击大按钮：
   - 应打开或聚焦 Build Pipeline 窗口（菜单：XLua/Build Pipeline）。
   - 窗口左侧侧边栏应自动切换至 "Pipeline" 子项（或至少打开窗口）。
5. 在 BuildPipelineWindow 中验证侧边栏扩展：
   - 点击 "Pipeline"：右侧内容区应显示 Build Pipeline Configuration 部分，若项目中存在 BuildPipelineConfig 类型的 asset，会显示名称并提供打开按钮；若不存在，会显示提示并可创建占位资源（Create Placeholder Config）。
   - 点击 "Builder"：右侧应显示 Hotfix / Snapshot 的占位信息，并提供 "Open Hotfix Config" 按钮用于打开对应的配置 asset（如果存在）。
6. 验证不会引发 Editor 报错：
   - 在 Console 中检查是否有异常或错误输出（RuntimeException / NullReferenceException 等）。
7. 可选：测试 SelectSidebar / SelectPanel 方法回调
   - 如果 BuildPipelineWindow 实现了 SelectSidebar 或 SelectPanel 方法，点击 Inspector 中按钮应触发该方法并选择相应面板。

问题回退与调试建议：
- 如果按钮没有弹出窗口，请在 Editor 的菜单中手动打开 XLua/Build Pipeline，确认窗口存在。
- 如果 PipelinePanel 无法创建占位配置，说明项目中尚未定义 BuildPipelineConfig 类型，需要先在代码中添加该类型的 ScriptableObject 定义或手工创建一个能用于占位的 SO。
