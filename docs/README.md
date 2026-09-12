# 文档目录

本文档目录面向项目开发者。当前实现以源码为依据，审批、进度和验证结果保留在 `requirements/`，不把历史方案当作架构现状。

## 入口

- [FYAsset 核心流程与文件索引](FYAsset/fyasset-modeling.html)：离线 HTML，覆盖 AA、AB、Shared、Compat、Tests 与序列化。
- [FYAsset 总览](FYAsset/资源管理架构文档.md)：四段管线、职责边界与专题导航。
- [序列化工具](Tools/序列化工具.md)：通用编码、注册和生成机制；业务注册由消费方持有。
- [FileHelper](Tools/FileHelper文件工具.md)：文件和 StreamingAssets 读写。
- [配置转换工具](Tools/配置数据转换工具.md)：配置格式转换。
- [对话系统](Dialogue/对话系统.md)：对话 DSL 与使用说明。

## FYAsset 专题

| 主题 | 文档 |
|---|---|
| 采集与配置 | [collector-采集与配置.md](FYAsset/collector-采集与配置.md) |
| 依赖分析与共享内容 | [dependency-analysis-依赖分析.md](FYAsset/dependency-analysis-依赖分析.md) |
| Runner、Composer 与固定主干 | [build-pipeline-构建管线.md](FYAsset/build-pipeline-构建管线.md) |
| 文件摘要与无状态差异 | [diff-无状态差异.md](FYAsset/diff-无状态差异.md) |
| 本地交付与发布 | [publish-本地交付与发布.md](FYAsset/publish-本地交付与发布.md) |
| AB 运行时加载 | [ab-runtime-运行时加载.md](FYAsset/ab-runtime-运行时加载.md) |
| 热更新系统 | [hotfix-热更新系统.md](FYAsset/hotfix-热更新系统.md) |
| 版本号系统 | [versioning-版本号系统.md](FYAsset/versioning-版本号系统.md) |
| 字段语义 | [字段语义参考表.md](FYAsset/字段语义参考表.md) |
| 错误处理 | [错误处理.md](FYAsset/错误处理.md) |
| 命令行构建 | [使用命令行打包.md](FYAsset/使用命令行打包.md) |
| 自动化测试 | [自动化测试管线.md](FYAsset/自动化测试管线.md) |
| 导出边界 / SO 入口 | [export-sets-导出集.md](FYAsset/export-sets-导出集.md) · [so-创建入口说明.md](FYAsset/so-创建入口说明.md) |

## 维护规则

- 类型、路径、字段与命令必须与实际代码一致；不能根据名称推断行为。
- 用核心流程、职责与失败边界说明系统；不在多篇 Markdown 中重复维护完整类清单。
- 编辑代码时同步修订关联说明。生成文件记录生成来源，不把手工改生成结果作为维护方式。
- `context/` 保存稳定可复用知识；`requirements/` 保存计划、批准、进度与审查；本目录保存面向人的现状说明。
