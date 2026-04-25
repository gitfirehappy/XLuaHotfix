# 架构和技术栈

## 技术栈
- **引擎**: Unity（具体版本见 ProjectSettings/ProjectVersion.txt）
- **热更语言**: Lua 5.3 / LuaJIT（通过 XLua 框架集成）
- **主要语言**: C# + Lua
- **资源管理**: Unity Addressables
- **构建产出**: Android / iOS / PC（见 build/ 目录的构建脚本）

---

## 核心模块

### 1. 热更资源管理体系（最重要模块）

#### 构建期数据导出
| 数据类型 | 文件 | 用途 |
|----------|------|------|
| BuildIndexData | LocalStaticData | 整包构建唯一标识(guid)、版本号、时间，大版本检测依赖 |
| VersionState | HelperBuildData | 版本号 + Bundle哈希映射表 |
| AddressableLabelsConfig | HelperBuildData | Type/Label → Keys 多向映射索引 |
| LuaScriptsIndex | HelperBuildData | AddressableKey → 内部脚本名映射 |
| Manifest | HelperBuildData | 远程构建定位，指向最新导出包路径 |

#### 差异快照系统
- **BuildSnapshots**: 管理 Head（已发布）/Staged（待发布）快照
- **DifferentialProcessor**: 扫描资源哈希 vs Head 快照 → 自动将变更资源移入 Hotfix 组 → 支持快照轮转（Staged → Head）和还原分组

#### 构建流程（BuildProjectManager）
```
BuildFullPackage  →  Major 版本号+1，需先 ResetGroupsToOriginal
BuildHotfix       →  Patch 版本号+1，DifferentialProcessor 自动识别变更
ConfirmRelease    →  快照转正 (Staged → Head)，正式发布后调用
ResetGroupsToOriginal → 还原热更组资源到原始组（整包发布前必须执行）
```

#### 新构建管线基础（Collector Framework）
- **CollectorSetting / CollectorPackage / CollectorGroup / Collector**：已落地新构建管线的基础数据模型，用于表达独立于 Addressables 的资源收集配置
- **CollectorEnums / AssetClassification**：定义收集意图、载荷类型、资源角色和强制分类配置，为后续扫描、打包、依赖分析提供统一契约
- **IAddressRule / IPackRule / IFilterRule**：Editor 侧规则接口已建立，`IPackRule` 当前契约为 `GetPackKey`，由框架统一组装最终逻辑 Bundle 名
- **CollectedAssetInfo / RuleResolver**：分别承担构建期扁平中间结果与规则类名到实例的反射解析职责

#### 运行时资源管理（AssetPackageManager）
- 资源索引：加载 AddressableLabelsConfig，构建 Type/Label → Keys 映射
- 引用计数池：安全管理异步加载 Handle，支持按标签/类型批量加载/卸载
- **PathManager**: 热更路径统一管理，包体 GUID 隔离
- **NetworkDownloader**: Catalog 重定向 + 增量下载优化（保留 hash 一致的 bundle）

---

### 2. XLua 框架核心

#### 自定义 Loader（XLuaLoader）
- 支持三种模式：`EditorOnly` / `AddressablesOnly` / `Hybrid`
- 懒加载索引（LuaScriptsIndex）+ 本地字节流缓存，提升 `require` 效率
- 支持按 `LuaScriptContainer` 整体释放内存缓存

#### Lua-C# 桥接（LuaBehaviourBridge）
- 挂载到 GameObject，绑定 Lua 生命周期与 Unity 同步
- 支持 **Class**（面向对象实例化）和 **Module**（静态）两种脚本模式
- 生命周期初始化顺序：SO → Input → Physics2D → Collision2D → Anim → UIEvent → Gizmos
- 缓存 `Update/LateUpdate/FixedUpdate` 等高频函数指针，避免每帧查表开销

#### 系统桥接组件
| 组件 | 用途 |
|------|------|
| ScriptObjectBridge | SO 数据加载 |
| InputBridge | 输入系统 |
| Physics2DBridge | 2D 物理 |
| Collision2DBridge | 碰撞回调 |
| AnimBridge | 动画系统 |
| UIEventBridge | UI 事件 |
| GizmosBridge | 调试绘制 |

#### XLua 特性配置
- `[LuaCallCSharp]`: Lua 调用 C#
- `[CSharpCallLua]`: C# 调用 Lua
- `[Hotfix]`: 运行时替换方法
- 配置入口：**TypeMemberListSO**（类型级/成员级），通过 `XluaTypeConfigLoader` 异步加载

---

### 3. EventCentre（跨语言事件中心）
- 支持四向通信：C#-C#、Lua-Lua、C#-Lua、Lua-C#
- Lua 注册时自动生成对应 C# Delegate，以 `Tuple(端口, 事件名, Lua函数指针)` 为键缓存到 `luaDelegateMap`
- 注销时复用相同 Delegate，彻底解决跨语言委托注销无法匹配的问题

---

### 4. 协程桥接（CoroutineBridge）
- Lua 中可等待 C# 异步结果，C# 中可阻塞等待 Lua 协程
- `CSharpCoroutineScheduler` / `LuaCoroutineScheduler` 维护等待关系链并清理过期关联

---

### 5. 编辑器工具链
- **LuaBatchConverterWindow**: Lua 文件批量转 TextAsset 后缀
- **EventViewerWindow**: 可视化跨语言事件调试器
- CSV/JSON/XML 配置格式一键转换工具

---

## 目录结构
```
XLuaHotfix/
├── Assets/
│   ├── XLua/           # XLua 框架及自定义扩展（Loader、桥接组件、特性配置）
│   ├── Plugins/        # 第三方插件
│   ├── StreamingAssets/# 初始包内资源
│   ├── AddressableAssetsData/ # Addressables 配置
│   └── Resources/      # 运行时直接加载的资源
├── HotfixOutput/       # 热更包输出目录（manifest.json + Packages/）
├── build/              # 多平台 XLua Native 构建脚本（lua53/lua54/luajit）
├── context/            # AI 协作知识库
└── requirements/       # 需求追踪目录
```

---

## 架构决策

### 决策：使用 DifferentialProcessor 自动管理热更分组
- **选择**: 基于深度哈希比对自动识别变更资源并移入 Hotfix 分组
- **理由**: 传统人工管理分组易漏配，风险高；自动化保证增量更新准确无误
- **权衡**: 需维护快照状态机（Head/Staged），构建流步骤增加

### 决策：XLuaLoader 三态加载模式
- **选择**: EditorOnly / AddressablesOnly / Hybrid 运行时切换
- **理由**: 编辑器下直接读文件避免频繁打包，生产环境走 Addressables 热更
- **权衡**: 需维护三套加载路径逻辑

### 决策：EventCentre 缓存 Lua Delegate
- **选择**: 以 Tuple(端口, 事件名, Lua函数指针) 为键缓存 C# Delegate
- **理由**: Lua 闭包不可预测导致反注册失败、内存泄漏
- **权衡**: 多一层字典查找开销，但彻底解决注销匹配问题
