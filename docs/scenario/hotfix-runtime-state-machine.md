# 场景：Hotfix 运行时状态机

> **关联代码** | `Assets/FYAsset/Scripts/Shared/Hotfix/` · `Assets/FYAsset/Scripts/AA/Hotfix/` · `Assets/FYAsset/Scripts/AB/Hotfix/`

- Given：客户端具有包体 BuildIndex，可能存在已激活的本地包，远端 PackageIndex 可能可用、失效或指向不同包。
- When：GameLauncher 执行 Hotfix 初始化。
- Then：流程应确定性地选择本地激活、缺失修复、增量更新、可恢复降级或致命失败，并保证激活成功后只完成一次。

## 测试步骤

- Case 1：远端与本地指向相同且本地包完整，只请求 PackageIndex 并直接激活本地包。
- Case 2：远端与本地指向相同但本地包不完整，下载远端 Manifest 并只修复缺失文件。
- Case 3：远端指向不同包或同 Major 回滚包，按远端指针更新，并在运行时初始化成功后保存本地 PackageIndex。
- Case 4：远端元数据失败时，按策略继续使用完整本地包或抛出致命异常。
- Case 5：远端 Major 高于 BuildIndex 时发出客户端更新信号并启动当前 Major 本地内容；远端较低时只告警并本地启动。
- Case 6：AA 4.0.0 首装、同包二次启动、离线启动、缺失 Bundle 修复及 4.0.1 本地增量均通过真实 Unity 验证。
- Case 7：BuildIndex Major 高于本地活动包时清理旧 HotfixRoot 后继续远端流程；反向关系清理并终止启动。
- Case 8：同尺寸但 CRC 不匹配的 Bundle 被判定为不完整并进入同包修复。
- Case 9：FinishHotfix 或活动指针写入失败时不清理旧包、不触发 OnFinished。

## Status

- [x] 状态决策单元测试
- [x] AA 4.0.0 本地/离线/修复验收
- [x] AA 4.0.0 → 4.0.1 本地更新与同包重启验收
- [ ] AB Full/Hotfix 真实验收
- [x] Write solid test according to document
- [x] Run test and watch it failing
- [x] Implement to make test pass
- [x] Run test and confirm it passed
- [x] Refactor implementation without breaking test
- [x] Run test and confirm still passing after refactor

### Major baseline follow-up

- [x] Write scenario document
- [x] Write solid test according to document
- [x] Run test and watch it failing
- [x] Implement to make test pass
- [x] Run test and confirm it passed
- [x] Refactor implementation without breaking test
- [x] Run test and confirm still passing after refactor
