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
