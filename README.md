# Unity XLua热更新框架技术总结

> 文档分层说明：
> - `docs/` 面向人类开发者，使用中文，允许包含设计说明和重构方向。
> - `context/` 面向 AI 协作，使用英文，只记录已验证的当前事实。
> - 知识入口见 `context/INDEX.md`，稳定工程约定见 `context/conventions/INDEX.md`。
> - 错误经验入口见 `context/mistakes/INDEX.md`。

> 目前正在重构，详情见代码

> SO 创建入口：各类 `ScriptableObject` 的推荐创建方式见 `docs/FYAsset/so-创建入口说明.md`。

## 一、热更资源管理体系（核心模块）

> **最重要模块**，负责运行时资源加载与热更更新

### 1.1 四段管线

```text
构建与完整包导出 → 无状态差异比较 → 本地交付与发布 → 运行时热更
```

| 数据类型 | 文件 | 用途 |
|----------|------|------|
| **BuildIndexData** | Shared/Runtime，写出到包内与 StreamingAssets | 内置包身份：BuildGUID、版本、平台、后端与 `RuntimeMode`（Online/Standalone 的唯一运行时事实） |
| **AAManifest** | AAManifest.json / AAManifest.bin | AA 版本号 + 内容哈希/CRC/大小映射表，并嵌入 AA 查询索引；catalog 仍负责实际定位 |
| **ABManifest** | ABManifest.json / ABManifest.bin | AB 完整 Asset/Content 映射与内容级依赖下标；公共 Address 只对 `IsPublic=true` 条目有意义 |
| **LuaScriptsIndex** | Build/LuaScriptsIndex.asset | AddressableKey → 内部脚本名映射，运行期加载 Lua；按普通 Addressable 资产参与索引 |
| **PackageIndex** | PackageIndex.json | 指向最新包：`LatestPackage` / `LatestVersion` / `BackendMode`；**构建不写，发布事务最后写** |
| **构建摘要（Summary）** | `BuildData/Summaries/{AA\|AB}/{BuildId}.json` + `index.json` | 一次构建的不可变事实（`CompleteBuildSummary`），发布身份的唯一天然来源；Index 记录作用域与版本，可重建 |
| **Hotfix 交付包** | `{OutputRoot}/{Packages}/Build_*` | 完整目标 Manifest + 相对作用域最近成功 Full 的变化内容；每次 Hotfix 独立成包，不覆盖历史 |

### 1.2 差异、缓存与发布

- **FileDigest / FileDiff**: 唯一无状态比较能力。`FileDigest` 是 `Name/Hash/CRC/Size` 纯数据，`FileDiff` 按名称（大小写敏感）分出 Added / Modified / Unchanged / Removed；不读 Git、PackageIndex、缓存或任何历史状态
- **存储职责分离**: 构建事实（Summary/Index）与历史 `Build_*` 包是制品复用的唯一来源；发布缓存位于 `BuildData/PublishCache/{AA|AB}/{TargetId}.json`（只提示漂移，不改变发布决定）；包目录只含发布内容，不含摘要、报告、日志或失败标记
- **构建缓存隔离**: 后端 + 目标平台 + 压缩模式 + Unity/构建格式任一变化都落到不同目录；条目缺少依赖事实时不得复用
- **BuildPublisher / PackagePublishTransaction**: 读服务器 `PackageIndex` 与其指向的 Manifest → 与本地包做 `FileDiff` → 在服务器隔离目录复用已有 Hash 内容 → 校验新目录 → 就位 → **最后**写 `PackageIndex`；服务器事实不可用时退化为完整上传
- **PublishMaintenance**: 旧包清理是独立维护入口，只删不被当前 `PackageIndex` 指向的 `Build_*` 目录，索引不可读时拒绝清理；发布本身永不删旧包
- **PushTarget / CloudflarePagesPushTarget**: `LocalDirectoryPushTarget` 在 Shared，Cloudflare 接入在 `Compat/Editor/Publish/`；`PublishTargetPanel` 的 `Apply URL` 是独立显式动作

### 1.3 构建流程

- **两条固定主干**（`AAPipelineBackbone` / `ABPipelineBackbone`）：
  - AA 5 段：`PrepareAAInput → BuildAAContent → GenerateAAManifest → VerifyAAContent → ExportAAOutput`
  - AB 6 段：`CollectABAssets → AnalyzeABDependencies → BuildABContent → GenerateABManifest → VerifyABContent → ExportABOutput`
- **BuildPipelineRunner**: 只顺序执行 Composer 组装好的 Task 列表，管理 Context 与 attempt，成功后提升正式输出；不认识后端，不做差异、发布、PackageIndex 或版本回滚
- **BuildPipelineConfig**: 只保存 `BundleCompression` 与自定义 Task 的 `CustomTaskEntry(Slot, TaskName)`；每个槽位允许 `0..N` 条，不能增删主干；物理名由 `BundleNameBuilder` 按固定规则生成
- **BuildProjectManager（Compat 门面）**: `BuildFullPackage`（Major+1）、`BuildHotfix`（Patch+1）、`BuildStandalonePackage`（固定 AB 管线）；后端由 `Assets/Build/FYAssetBackendSettings.asset` 的 `Backend` 决定
- **交付事务（BuildProjectRunner）**: Runner 提升 → 写 StreamingAssets 启动数据（Full/Standalone）→ 写 Summary → 最后写 Index；任一步失败按逆序补偿（Summary 删除 + Index 字节恢复），失败构建不推进版本
- **输出路径**: 构建输出根由 `FYAssetSettings.BuildOutputRoot` 配置，正式包目录为 `{OutputRoot}/{BuildPackagesFolderName}/Build_yyyyMMddHHmmss_x.y.z`，attempt 为 `{OutputRoot}/_attempt`；不再有固定累计目录
- **PackageIndex 不由构建写入**: 它是发布事务的产物
- **AA Group 修改**必须由 AA 入口 `try/finally` 恢复，不由尾部 Task 恢复
- **编辑器窗口**: `FYAsset/Build/AA Build Pipeline` 与 `FYAsset/Build/AB Build Pipeline` 两个独立窗口（UI Toolkit）；AB 的 Collection 面板负责 Project Scan / Curate / Save，Pipeline 面板负责槽位编辑、构建入口与结果摘要区
- **结果摘要**: `CompleteBuildSummary` 是结果摘要区唯一数据源；AB 窗口另有只读 `BuildData/Reports/AB` JSON 报告的 `ABReportPanel`
- **Collection 校验**: 构建与面板共用 `AssetCollectionSettingValidator`，以结构化 `BuildMessage` 阻断非法 Group/Collector、无效覆盖 GUID、空配置项与同路径冲突；隐式依赖只由依赖分析生成，配置层不声明 Role/Payload

### 1.4 运行时资源管理

- **AssetPackageManager（Compat 门面）**: 启动时绑定一次 AA/AB，自身不持有资源缓存
- **ABPackageManager（AB concrete 入口）**:
  - 索引：`ABAssetIndex` 只索引 `IsPublic=true` 的条目，建立 Address（大小写不敏感且唯一）/ Type / Label 查询；隐式依赖条目只保留 EntryId
  - 查询只返回 Address：`GetAddressesByType<T>` / `GetAddressesByLabel` / `GetAddressesByTypeAndLabel<T>`
  - 加载统一返回 Handle：`LoadByAddress` / `LoadByAddressSync` / `LoadByType` / `LoadByLabels` / `LoadByTypeAndLabels`；批量加载逐项独立成败
  - RawFile 直接读内容文件（`LoadRawBytesAsync` / `LoadRawTextAsync`）；Scene 通过 `LoadSceneAsync(address, mode, activateOnLoad)` 返回 `SceneHandle`
- **Handle token**: 一次 Load 或一次 Retain 分配一个独立 token；普通 struct 复制不增加所有权；重复 Release 幂等；同一 EntryId 最后一个 token 释放才回调后端卸载
- **Shutdown 门禁**: `ABPackageManager.Shutdown()` 与热更 Apply 在仍有活跃 Asset/Scene Handle 时返回 `ACTIVE_HANDLES_REMAIN`，不强制释放
- **AAA/AB HotfixManager**: 共用 `HotfixFlowBase` 的 12 步状态机；内容来源只有 `BuiltIn` / `Local` / `RemoteTarget` / `Blocked`，`BuildIndex.RuntimeMode` 决定 Online/Standalone；运行中提供 `CheckAsync` / `PrepareAsync` / `ApplyAsync`
- **单一激活包根**: `RuntimePathManager.ActivePackageRoot` 是 Manifest、Bundle、RawFile 的唯一读取根；加载器不得逐文件回退 StreamingAssets
- **ABBundleLoader / ABSceneLoader**: Bundle 引用计数归零才 `Unload(true)`；Scene 按 Unity 官方顺序加载，`UnloadSceneAsync` 后才释放内容引用
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

## 目录结构

```text
Assets/FYAsset/Scripts/          资源管理框架
├─ AA/                           Addressables 构建、catalog、AAManifest、加载与热更后端
├─ AB/                           Collection、依赖分析、内容构建与缓存、ABManifest、加载与热更后端
├─ Shared/                       线性 Task 编排、版本、文件摘要与差异、发布事务、热更状态机
├─ Compat/                       宿主后端选择门面（BackendMode / FYAssetBackendSettings）、
│                                Lua 索引构建接入、Compat/Editor/Publish 的 Cloudflare 接入、序列化注册
└─ Tests/                        Build/E2E 测试入口与自检

Assets/Tools/Scripts/            FileHelper、HashGenerator、Serialization、配置转换等通用工具
Assets/Build/                    BuildPipelineConfig.asset、AABuildPipelineConfig.asset、
                                 FYAssetBackendSettings.asset、Bootstrap/BuildIndex.json（构建导出，reset 工具会清空）
Assets/FYAsset/CollectorData/    CollectorSetting.asset（AB 采集配置）
Assets/Resources/                FYAssetSettings / FYAssetAASettings / FYAssetABSettings
CommandLine/                     build.bat、build.config、fyasset_test.py、fyasset_reset.py、hotfix_server.py
docs/FYAsset/                    架构、专题说明与 fyasset-modeling.html 建模页
context/                         稳定工程约定、已验证错误经验、外部资料笔记
requirements/                    计划、批准、进度与审查记录
tests/scenario/                  纯 .NET 隔离场景工程（不在 XLuaHotfix.sln 内）
```

## 构建与测试命令

```bash
# 正式构建（Unity batchmode）
Unity.exe -batchmode -quit -projectPath <path> \
  -executeMethod BuildCommandLine.Build -buildType full|hotfix|standalone

# Unity 构建 / Player E2E 测试（获准的隔离环境）
python CommandLine/fyasset_test.py aa|ab build|e2e full|hotfix|chain|standalone --target <id>

# 破坏性本地状态重置
python CommandLine/fyasset_reset.py aa|ab|all [--dry-run]

# 隔离场景（各自独立工程，退出码 0=通过，1=失败）
dotnet run --project tests/scenario/pipeline_realignment/PipelineRealignmentTests.csproj
dotnet run --project tests/scenario/build_cache/BuildCacheTests.csproj
dotnet run --project tests/scenario/pipeline_compose/PipelineComposeTests.csproj
dotnet run --project tests/scenario/publish_diff/PublishDiffTests.csproj
node tests/scenario/serialization/run.mjs

# Unity 生成解决方案编译检查
dotnet build XLuaHotfix.sln --no-restore
```

详见 `docs/FYAsset/使用命令行打包.md` 与 `docs/FYAsset/自动化测试管线.md`。
