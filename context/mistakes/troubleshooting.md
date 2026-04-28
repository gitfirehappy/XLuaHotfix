# 问题排查经验

> 记录开发过程中遇到的坑和解决方案，只记录已验证的信息。

---

## XLua 相关

### Lua 注销 C# 事件失败 / 重复触发
- **现象**: Lua 端 `EventCentre.Off` 后事件仍然被触发，或内存中持有多份监听
- **原因**: Lua 闭包每次创建都是新对象，直接传闭包注销时 C# 端找不到匹配的 Delegate
- **解决**: 必须通过 `EventCentre` 的封装接口注册/注销，底层会以 `Tuple(端口, 事件名, Lua函数指针)` 为键缓存对应 Delegate，注销时复用同一个引用
- **预防**: 禁止绕过 EventCentre 直接用 C# delegate 跨 Lua 订阅

### XLua Generate Code 未更新导致运行报错
- **现象**: 修改了 C# 类后，Lua 调用报 `attempt to index a nil value` 或类型找不到
- **原因**: XLua 生成代码（xlua_gen 目录）未重新生成，仍是旧类型的 Wrapper
- **解决**: 菜单 XLua → Generate Code 重新生成，或运行 `XluaTypeConfigLoader` 确认白名单包含目标类型
- **预防**: Git 提交前必须执行 Generate Code，将生成文件纳入版本控制

### Lua require 找不到模块
- **现象**: `module 'xxx' not found`
- **原因**: LuaScriptsIndex 未包含该脚本，或 XLuaLoader 模式不匹配当前环境
- **解决**: 确认脚本已被 Addressables 打包并标记正确的 Label；检查 `XLuaLoader` 当前模式（EditorOnly/AddressablesOnly/Hybrid）

---

## Addressables 相关

### 热更包下载后资源未更新
- **现象**: 下载完成但运行时仍加载旧资源
- **原因**: Catalog 未重定向，或本地缓存 bundle 的 hash 与远端一致未触发替换（实为预期行为）
- **解决**: 确认 `NetworkDownloader` 的 Catalog 重定向逻辑已执行；检查 VersionState 哈希映射是否正确更新
- **预防**: BuildHotfix 后必须调用 ConfirmRelease 完成快照转正

### BuildHotfix 后资源分组未还原
- **现象**: 下次 BuildFullPackage 时热更资源仍在 Hotfix 分组，导致大版本包缺失资源
- **原因**: 忘记在大版本构建前执行 `ResetGroupsToOriginal`
- **解决**: 执行 `BuildProjectManager.ResetGroupsToOriginal()`，将 Hotfix 分组资源迁回原始分组
- **预防**: 大版本构建 SOP：ResetGroupsToOriginal → BuildFullPackage → ConfirmRelease

---

## Unity 编辑器相关

### [待确认] Lua 文件在 Editor 下修改后热重载不生效
- **现象**: 修改 .lua.txt 文件后运行游戏仍执行旧逻辑
- **原因**: XLuaLoader 内容缓存未清除
- **解决**: 手动触发 `LuaEnvManager` 重启，或在编辑器模式下调用缓存清除接口

### version_state 自引用哈希问题
- **现象**: 如果 `version_state.json` 里记录的是它自身文件内容的哈希，就会出现“先写内容才能算哈希，但哈希写回去后内容又变了”的自引用问题
- **原因**: 摘要字段本身参与了被摘要内容，导致输入和输出相互影响
- **解决**: 业务层先写一个不含最终哈希的临时文件，对临时文件算哈希，再把结果回写到正式 `version_state.json`
- **预防**: 通用哈希工具保持纯粹；凡是涉及版本描述文件、自描述清单等场景，摘要边界由业务调用方明确控制

### AB 运行时路径与身份模型不一致导致加载异常
- **现象**: 启用 AB backend 后，Bundle 文件明明已下载到热更目录，但运行时仍报 BundleNotFound；或 duplicate Address 场景下加载到错误资源 / 释放错资源
- **原因**: 运行时加载器如果直接在 `CurrentGUIDRoot/` 查 bundle，而现有热更链路实际将 bundle 落在 `CurrentGUIDRoot/bundles/`；同时如果 backend 继续以 Address 作为缓存唯一键，会违背 B5 允许 duplicate Address 的运行时契约
- **解决**: Bundle 查找必须与当前落盘结构保持一致，优先走 `CurrentGUIDRoot/bundles/` 与 `StreamingAssets/bundles/`；AB backend 内部缓存与释放统一使用 `EntryId` 作为唯一身份，Address 仅保留为查询输入
- **预防**: 运行时路径策略必须和 HotfixManager/BuildProjectManager 的真实输出目录一起审查；涉及 duplicate Address 的设计一旦落地，后续缓存/句柄/释放链路都要检查是否仍在偷偷依赖 Address 唯一性

### Hotfix pipeline 抽象后出现步序/共享状态错位
- **现象**: 将 HotfixManager 从单体流程拆成 orchestrator + backend 后，如果沿用旧的步骤编号或继续依赖静态共享字段，进度条会跳步，后端也容易读到错误上下文
- **原因**: 旧实现把“流程编排”“版本数据源”“下载后处理”混在一个静态类里；拆分后如果不同时引入统一上下文对象和连续步骤编号，公共逻辑与后端逻辑的责任边界会再次混淆
- **解决**: 用 `HotfixContext` 承载共享状态（BuildIndex/TargetPackageName/RemoteUrlRoot/TargetGUIDRoot），HotfixManager 只控制公共步骤与事件回调；Legacy/AB 后端各自负责 `LoadLocalVersion / FetchRemoteVersion / GetBundleDownloadList / PostDownload`
- **预防**: 后续再扩热更链路时，先判断逻辑属于“公共编排”还是“后端差异”，不要重新把网络请求、版本文件格式判断、后处理写回 HotfixManager

### E1-3 PATH_NOT_FOUND 严重级别与计划规格不一致
- **现象**: `CollectionScanner.cs` 对 `PATH_NOT_FOUND` 使用了 `Error` (中止扫描)，但 `plan-E1-3.md` 错误条件表明确指定为 `Warning` (继续扫描)
- **原因**: 实现时凭记忆写代码，未逐行对照计划中的错误条件表格（7 条件 × 4 列）。"path not found" 听起来像 Error，但计划理由是该 Collector 异常不影响同 Package 其他 Collector
- **解决**: 修正为 `Warning` 级别 + 不 return false。将随 R1-B3（统一错误架构改造）一并修复，届时所有消息构造迁移到 `BuildMessage.Warning()` 工厂方法
- **预防**: 实现枚举式条件表（错误码表/配置表/状态机）时，必须逐行对齐源码与规格，不能凭记忆

### 新增 Unity 脚本后 `dotnet build` 没有覆盖到实际改动
- **现象**: 新脚本已经落盘，但外部执行 `dotnet build XLuaHotfix.sln` 时看不到对应编译结果，容易误判为“代码没问题”或“验证已通过”
- **原因**: 当前项目没有为 FYAsset 新构建链路单独建立 asmdef，外部 `dotnet build` 依赖 Unity 生成的 `Assembly-CSharp.csproj` / `Assembly-CSharp-Editor.csproj` 编译项；新增文件如果尚未被项目文件包含，就不会进入这条验证链路
- **解决**: 新增 Runtime/Editor 脚本后，同时检查并同步对应 `.csproj` 的 `Compile Include` 项，再运行 `dotnet build` 做外部验证
- **预防**: 在没有 asmdef 的目录下推进新模块时，把“脚本落盘”和“项目文件纳入编译”视为同一个闭环；否则验证结果不可信
