# 文档目录

本文档目录面向项目开发者。当前实现以源码为依据，审批、进度和验证结果保留在 `requirements/`，不把历史方案当作架构现状。

## 入口

- [FYAsset 核心流程与文件索引](FYAsset/fyasset-modeling.html)：离线 HTML，覆盖 AA、AB、Shared、Compat、Tests 与序列化。
- [FYAsset 总览](FYAsset/资源管理架构文档.md)：职责边界与专题导航。
- [序列化工具](Tools/序列化工具.md)：通用编码、注册和生成机制；业务注册由消费方持有。
- [FileHelper](Tools/FileHelper文件工具.md)：文件和 StreamingAssets 读写。
- [配置转换工具](Tools/配置数据转换工具.md)：配置格式转换。
- [对话系统](Dialogue/对话系统.md)：对话 DSL 与使用说明。

## 维护规则

- 类型、路径、字段与命令必须与实际代码一致；不能根据名称推断行为。
- 用核心流程、职责与失败边界说明系统；不在多篇 Markdown 中重复维护完整类清单。
- 编辑代码时同步修订关联说明。生成文件记录生成来源，不把手工改生成结果作为维护方式。
- `context/` 保存稳定可复用知识；`requirements/` 保存计划、批准、进度与审查；本目录保存面向人的现状说明。
