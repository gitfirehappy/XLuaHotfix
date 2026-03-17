# XLuaHotfix - AI 协作手册

## 项目概述
基于 Unity Addressables + XLua 的热更新框架，实现从资源构建、版本差异管理到运行时 Lua 与 C# 深度互调的完整业务流，包含自动化工具链及组件化开发架构。

## 技术栈
- **引擎**: Unity（Addressables 资源管理）
- **热更语言**: Lua（XLua 框架）
- **主要语言**: C# + Lua
- **资源管理**: Unity Addressables
- **构建工具**: 自研差异快照构建管线（DifferentialProcessor）

## 协作原则
- 先理解需求再动手，不确定时提问而不是猜测
- 重要决策必须说明理由
- 优先使用中文沟通，技术术语保持英文
- 每次修改代码考虑可维护性
- 修改 Lua-C# 桥接代码前，先确认 XLua 特性配置是否需要同步更新

## 知识管理
- 工作前先查看 `context/INDEX.md` 了解已有知识
- 新发现的经验写入 `context/{主题}.md`
- 只记录已验证的知识，未确认的标注 [待确认]

## 需求工作流
1. 创建 `requirements/{需求ID}/brief.md` 描述需求目标和背景
2. 在 `requirements/{需求ID}/progress.txt` 记录关键进展（开始/完成/决策/阻塞/下一步）
3. 遇到的问题和解决方案写入 `context/troubleshooting.md`
4. 需求完成后将有价值的经验迁移到 `context/`

### 执行协议（强制）
```
1. 开发者批准子计划（回答审批清单）
   |
2. 执行子计划（按任务逐步实现）
   |
3. 执行完毕 -> 讲解修改思路 -> 请求开发者确认收工
   |
4. 开发者可随时提问，执行方负责解释
   |
5. 收工确认后 -> 询问是否开启下一个子计划
   |
6. 不满意 -> 调优当前子计划（回到步骤 2）
```
**没有开发者明确批准，不执行任何代码修改。**

### Post-Plan Checklist（每个子计划完成后强制执行）
子计划完成后，按以下顺序执行：
1. **追加 progress.txt**：记录完成事项、关键决策、验证事实
2. **更新 plan.md 状态表**：将完成的子计划状态从 TODO 改为 DONE
3. **同步 README.md**：反映新能力或结构变更
4. **更新 context/ 知识库**：添加新验证的事实、模式或踩坑经验
5. **请求开发者签收**：展示变更摘要，问「以上更新是否确认？可以进入下一个子计划吗？」
   **未签收前不得开启下一个子计划。**
   签收记录写入 progress.txt：`[done] YYYY-MM-DD plan-XX SIGNED OFF`

### 审批清单格式
每个子计划必须以具体的审批问题结尾（不是泛化的「你确定吗？」）：

好的审批清单（问具体设计决策）：
```
## 审批清单
- [ ] 目录到容器映射，是否需要支持「一个目录对应多个容器」的场景？
- [ ] Game/ 目录处理：只扫描 Game/Player/ 子目录，还是 Game/ 下所有子目录各建一个容器？
- [ ] 是否需要在 Unity 保存 Assets 时自动触发扫描，还是只手动？
```
坏的审批清单：
```
- [ ] 你批准这个计划吗？
- [ ] 你确定吗？
```
通过 ask_user / question 工具呈现审批问题，等待回答。回答成为实现的约束条件。

### progress.txt 格式
```
# {需求ID} 进度记录
# 格式：[类型] YYYY-MM-DD 描述
# 类型: start(开始) / done(完成) / decision(决策) / blocked(阻塞) / next(下一步)
# 签收格式: [done] YYYY-MM-DD plan-XX SIGNED OFF - {摘要}
```

## 会话恢复
当你说"继续 {需求ID}"时，我会：
1. 读取 `requirements/{需求ID}/progress.txt` 了解上次进度
2. 用 2-3 句话总结当前状态和建议的下一步
3. 等待你确认后继续工作

## 项目特定规则

### 代码规范
- C# 类命名使用 PascalCase，Lua 模块使用 PascalCase，局部变量使用 camelCase
- XLua 桥接组件统一以 `Bridge` 结尾（如 `InputBridge`、`AnimBridge`）
- 新增 Lua 可调用的 C# 类型，必须同步更新 `TypeMemberListSO` 配置

### 架构约束
- Lua 脚本支持 Class（面向对象实例化）和 Module（静态）两种模式，新脚本需明确选择
- 资源加载必须走 `AAPackageManager`，**不推荐**直接使用 Addressables 原生接口
  - 正在推进 `IPackageBackend` 接口化重构（见 requirements/refactor-2026/plan-B.md）
  - 新代码优先通过 `AAPackageManager`；已有直接调用 Addressables 的代码，重构时逐步迁移
  - 底层热更流程（HotfixManager/NetworkDownloader）仍依赖 Addressables，待 B3 阶段处理
- 热更资源分组由 `DifferentialProcessor` 自动管理，**禁止手动修改 Hotfix 分组**
- 跨语言事件注册/注销必须通过 `EventCentre`，禁止直接使用 C# delegate 跨 Lua 订阅

### 构建流程
- `BuildFullPackage` → 大版本（Major+1），需还原所有资源分组后执行
- `BuildHotfix` → 小版本（Patch+1），DifferentialProcessor 自动识别变更资源
- `ConfirmRelease` → 快照转正（Staged → Head），正式发布后调用

### Git 规范
- feat: 新功能 / fix: 修复 / refactor: 重构 / docs: 文档 / chore: 工程配置
- 提交前确保 XLua 代码生成（Generate Code）已执行

## 关键文件索引
| 文件/目录 | 说明 |
|-----------|------|
| `Assets/XLua/` | XLua 框架及自定义扩展 |
| `Assets/Plugins/` | 第三方插件 |
| `Assets/StreamingAssets/` | 初始包内资源 |
| `HotfixOutput/` | 热更包输出目录 |
| `context/` | AI 协作知识库 |
| `requirements/` | 需求追踪目录 |
