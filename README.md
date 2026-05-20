# Unity XLua热更新框架技术总结

> 文档分层说明：
> - `docs/` 面向人类开发者，使用中文，允许包含设计说明和重构方向。
> - `context/` 面向 AI 协作，使用英文，只记录已验证的当前事实。
> - 当前 AI 架构入口见 `context/architecture/INDEX.md`。

> 目前正在重构，详情见代码

> SO 创建入口：各类 `ScriptableObject` 的推荐创建方式见 `docs/FYAsset/so-创建入口说明.md`。

## 一、热更资源管理体系（核心模块）

> **最重要模块**，负责运行时资源加载与热更更新

### 1.1 构建期数据导出

| 数据类型 | 文件 | 用途 |
|----------|------|------|
| **BuildIndexData** | Bootstrap | 整包构建唯一标识(guid)、版本号、时间，大版本检测依赖 |
| **AAManifest** | AAManifest.json / AAManifest.bin | AA 版本号 + Bundle哈希/CRC/size 映射表，并嵌入 AA 资源索引数据 |
| **LuaScriptsIndex** | Build/LuaScriptsIndex.asset | AddressableKey → 内部脚本名映射，运行期加载Lua；按普通 Addressable 资产参与索引；类型定义归属 XLuaFramework |
| **PackageIndex** | manifest.json | 远程构建定位，指向最新导出包路径 |

### 1.2 差异快照系统

- **BuildSnapshots**: 管理 Head（已发布）/Staged（待发布）快照
- **DifferentialProcessor**: 
  - 扫描项目资源与Head快照比对，找出修改的资源
  - 自动将修改资源移入 Hotfix 组
  - 支持快照轮转：Staged → Head（确认发布）
  - 支持还原分组：热更组 → 原始组（整包发布前）

### 1.3 构建流程

- **BuildProjectManager**:
  - `BuildFullPackage`: 大版本更新，Major版本号自增
  - `BuildHotfix`: 小版本更新，Patch版本号自增
  - `ConfirmRelease`: 快照转正
  - `ResetGroupsToOriginal`: 还原资源分组
  - 每次构建先创建 `BuildPackageRequest`，统一持有版本、后端模式、包名、最终输出目录和 `PackageIndex` 写入路径
  - AB 后端的最终 package layout 已由 DAG Task 直接写入 `BuildPackageRequest.OutputDir`；backend post 方法只保留兼容校验
- **BuildPipelineWindow / PipelinePanel**:
  - AB Pipeline 的 Pipeline 页负责 BuildGraph DAG、Reload、Validate、构建选项、Build Mode 与 Build 入口
  - 构建入口复用 `BuildProjectManager` 的 Full/Hotfix 语义；DAGScheduler 执行事件会回显到节点状态
  - Builder 页不承载 DAG；当前为 UI Toolkit 占位壳，构建报告查询待 E7 差异快照与 digest 输出稳定后单独规划
  - Build Pipeline 编辑器窗口已迁移为 UI Toolkit `CreateGUI()` 壳层，侧栏保持 SETTINGS / AA PIPELINE / AB PIPELINE / MANAGE；AA 与 AB 组互斥灰显，AA Config 面板仅做 Addressables 摘要与 Groups 窗口入口
  - Settings、AA Config、AA Build、AA Report、Collect Config、Collector、Pipeline、Builder、Version 等 active 面板通过 UI Toolkit `CreateContent()` 承载；Pipeline 页继续复用现有 GraphView DAG
  - Collector 资产 Inspector 头部勾选入口仍使用 Unity 的 `Editor.finishedDefaultHeaderGUI` 回调，这是 Unity 默认 Inspector header 的 IMGUI 边界

### 1.4 运行时资源管理

- **AssetPackageManager**:
  - 资源索引：AA 路径从 `AAManifest.bin/json` 构建 Type/Label 查询缓存；AB 路径使用 ABAssetIndex + ABManifest
  - 资源池：引用计数管理，支持按标签/类型加载/卸载
  - B5-2 新增 Resolve/Load API：`LoadByAddress<T>` / `LoadByTypeKey<T>` 返回 `AssetHandle<T>`
- **HotfixManager**: 已重构为 orchestrator，仅负责公共步骤编排、进度回调、错误上报；后端差异由 `IHotfixPipeline` 实现
- **AAHotfixBackend / ABHotfixBackend**: AA 路径使用 Addressables `AAManifest.bin/json + catalog` 流程，runtime 从 `AAManifest` 读取 AA 索引；AB 路径使用 `ABManifest.bin/json` + bundles 流程；两条路径统一通过 `BundleDownloadItem` 传递 `FileHash` 与 `FileCRC`，下载/复用后由 `HotfixManager` 做 CRC 校验
- **ABAssetIndex**: 基于 ABManifest 的完整 IAssetIndex 实现，预缓存 RuntimeAssetEntry，零分配查询热路径
- **ManifestLoader**: 异步清单加载器（热更目录优先，StreamingAssets 回退）
- **ABBundleLoader**: 运行时从 `CurrentGUIDRoot/bundles/` 与 `StreamingAssets/bundles/` 查找 Bundle，依赖环按错误处理而不是静默跳过
- **ABPackageBackend**: 内部以 `EntryId` 作为缓存与释放的唯一身份，`Address` 只作为查询入口，兼容 duplicate Address 设计
- **RuntimePathManager**: 位于 `Runtime/` 根，统一管理运行时热更路径，包体GUID隔离
- **AB runtime models**: `AssetHandle` / `HandleRegistry` / `ResolveResult` / `RuntimeAssetEntry` 归入 `Runtime/Backends/AB/Models/`；`RuntimeMessage` 保留在 `Runtime/Models/` 作为共用诊断类型
- **NetworkDownloader**: 位于共享 `Helpers/`，供 AA/AB 双后端共用下载能力

---

## 二、XLua框架核心

### 2.1 自定义Loader

- **XLuaLoader**: 
  - 支持三种模式：`EditorOnly` / `AddressablesOnly` / `Hybrid`
  - **内容缓存**：模块名 → 二进制数据
  - **索引缓存**：LuaScriptsIndex 懒加载，写入内容缓存
  - **按容器释放**：支持按 LuaScriptContainer 卸载指定缓存

- **LuaEnvManager**: LuaEnv 生命周期管理（创建/销毁/全局访问）

### 2.2 Lua-C# 桥接

- **LuaBehaviourBridge**: 
  - 挂载到GameObject，绑定Lua脚本生命周期与Unity同步
  - 支持 Class/Module 两种脚本模式
  - 缓存 Update/LateUpdate/FixedUpdate 等函数指针
  - 初始化顺序：SO → Input → Physics2D → Collision2D → Anim → UIEvent → Gizmos

### 2.3 系统桥接组件

| 组件 | 用途 |
|------|------|
| **ScriptObjectBridge** | SO数据加载 |
| **InputBridge** | 输入系统 |
| **Physics2DBridge** | 2D物理 |
| **Collision2DBridge** | 碰撞回调 |
| **AnimBridge** | 动画系统 |
| **UIEventBridge** | UI事件 |
| **GizmosBridge** | 调试绘制 |

### 2.4 特性配置系统

- **三类特性**：
  - `[LuaCallCSharp]`: Lua调用C#
  - `[CSharpCallLua]`: C#调用Lua
  - `[Hotfix]`: 运行时替换方法

- **配置实现**：
  - **TypeMemberListSO**: 类型级/成员级配置
  - **TypeReference / MemberReference**: 泛型约束解决Unity序列化与反射兼容
  - **XluaTypeConfigLoader**: 异步加载所有标签配置，构建白名单

### 2.5 Lua脚本示例

```lua
-- PlayerController.lua
local PlayerController = {}
function PlayerController.New(go)
    local obj = {gameObject = go, transform = go.transform}
    setmetatable(obj, { __index = PlayerController })
    return obj
end

function PlayerController:Awake()
    -- 获取Bridge组件
    self.so = self.gameObject:GetComponent("ScriptObjectBridge")
    self.physics = self.gameObject:GetComponent("Physics2DBridge")
    -- 从SO加载数据
    self.playerData = self.so:GetSO("PlayerControllerSO")
end

function PlayerController:Update()
    -- 游戏逻辑
end

return PlayerController
```

---

## 三、事件系统（四向交互）

- **EventCentre**: 
  - 四向端口：C#↔C#、Lua↔Lua、C#↔Lua、Lua↔C#
  - 多参数触发（0-3参数自动匹配，3以上DynamicInvoke）
  - Lua委托实例映射解决跨语言注册/注销匹配
- **EventViewerWindow**: 编辑器可视化调试

---

## 四、协程调度系统

- **CoroutineBridge**: Lua↔C#协程双向等待
  - 维护等待关系映射表
  - 自动清理过期协程关联
- **CSharpCoroutineScheduler / LuaCoroutineScheduler**: 各自调度器

---

## 五、日志系统

- **LogUtility**: 
  - 分层（Core/Framework/Game）
  - 分类（Info/Warning/Error）
  - 运行时级别开关控制
- **LogViewerWindow**: 按语言/层级筛选，关键字搜索
- **运行时诊断约定**:
  - 开发诊断 `Debug.Log*` 可使用 `[Component]` 前缀，便于 Unity Console 检索
  - 面向 UI / OnError 的 `RuntimeMessage` 使用 `[Code] Message`，描述文本不重复携带组件前缀
  - 构建侧 `BuildMessage` / `BuildTaskResult` 也遵循同一规则：结构化结果保留错误码，描述文本不重复携带组件前缀

---

## 六、配置格式转换工具

- **读写抽象层**: IConfigReader / IConfigWriter
- **格式支持**: CSV / JSON / XML / Lua
- **SimpleParser**: 内置轻量级JSON/XML解析器
- **ConfigConverterWindow**: 批量/单文件转换编辑器工具

---

## 七、UI框架

- **UIManager**: 界面管理
- **UIFormBase**: 界面基类
- **UIFormConfigSO / UIResourceConfigSO**: 配置SO
- **UIAnimation**: 动画管理

---

## 八、对话系统

- **DialoguePanel**:
  - 打字机效果（逐字符显示）
  - 角色立绘淡入淡出
  - 选项动态生成
  - 差分图配置

---

## 九、Lua文件管理

- **ScriptedImporter**: `.lua` → `TextAsset` 识别
- **LuaScriptContainer**: SO管理Lua文件+Addressables标签
- **LuaDataBase**: 容器数据库
- **LuaBatchConverterWindow**: `.lua` ↔ `.lua.txt` 批量转换

---

---
