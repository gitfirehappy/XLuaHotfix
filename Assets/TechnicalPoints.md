# Unity XLua热更新框架技术总结

## 一、热更资源管理体系（核心模块）

> **最重要模块**，负责运行时资源加载与热更更新

### 1.1 构建期数据导出

| 数据类型 | 文件 | 用途 |
|----------|------|------|
| **BuildIndexData** | LocalStaticData | 整包构建唯一标识(guid)、版本号、时间，大版本检测依赖 |
| **VersionState** | HelperBuildData | 版本号 + Bundle哈希映射表，记录每个bundle的hash/size |
| **AddressableLabelsConfig** | HelperBuildData | Type/Label → Keys 多向映射索引，构建期导出 |
| **LuaScriptsIndex** | HelperBuildData | AddressableKey → 内部脚本名映射，运行期加载Lua |
| **Manifest** | HelperBuildData | 远程构建定位，指向最新导出包路径 |

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

### 1.4 运行时资源管理

- **AAPackageManager**:
  - 资源索引：加载 AddressableLabelsConfig 构建 Type/Label → Keys 映射
  - 资源池：引用计数管理，支持按标签/类型加载/卸载
- **PathManager**: 热更路径统一管理，包体GUID隔离
- **NetworkDownloader**: Catalog 重定向、增量下载优化（保留hash一致的bundle）

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
