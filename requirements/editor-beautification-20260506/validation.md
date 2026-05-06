# 手动验证步骤

## 原有功能验证

1. 打开 Unity 编辑器。
2. 找到 `Assets/Build/CollectorSetting.asset`，在 Inspector 确认大按钮 **"Open Build Pipeline Window"** 存在。
3. 点击按钮打开 `Build Pipeline` 窗口。
4. Inspector 中 **"Show Raw Serialized Fields"** Foldout 展开后可看到原始字段。

## 分组侧栏验证（新）

5. 侧栏应显示三个分组标题：**COLLECT**、**BUILD**、**MANAGE**，每个标题下有分割线。
6. **COLLECT** 分组包含两个面板按钮：`Collect Config`、`Collector`。
7. **BUILD** 分组包含两个面板按钮：`Pipeline`、`Builder`。
8. **MANAGE** 分组包含一个面板按钮：`Version`。

## 各面板内容验证（新）

9. 点击 `Collect Config`：展示 `CollectorSetting.asset` 的 Inspector 视图（含 Packages 列表），顶部有 Reload 按钮。
10. 点击 `Collector`：原有树状收集规则编辑器 + 扫描功能正常。
11. 点击 `Pipeline`：`BuildPipelineConfig.asset` 的 Inspector 视图，无配置时提示创建。
12. 点击 `Builder`：占位符内容（Builder Settings 卡片）。
13. 点击 `Version`：展示 `VersionDataBase.asset` 的 Inspector 视图（版本号、构建时间），无配置时提示创建。
15. Scan Preview 底栏拖拽时，TextArea 高度随之变化，文本始终从左上角开始显示，无大段空行。
16. Scan Preview 文本可鼠标选中并 Ctrl+C 复制。
