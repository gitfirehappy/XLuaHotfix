# Sub-Plan C: Lua 脚本目录自动管理

> **状态**: C1+C2 完成并签收，C3 待 Plan-B 后执行
> **依赖**: 无（纯 Editor 工具，独立性最强）
> **建议**: 优先执行，风险最低
> **子任务**: C0 人工验证映射（前置，已完成） | C1 LuaAutoSyncConfig SO | C2 LuaDirectoryScanner | C3 自动标签集成（Plan-B 后执行）

---

## 前置步骤 C0: 验证当前目录-容器对应关系（人工）

### 目标

在开始编码前，由开发者人工确认当前项目中「Lua 脚本目录 ↔ LuaScriptContainer SO」的对应关系是否正确。
AI 不方便直接查看 SO asset 内部的引用，需要开发者在 Unity Editor 中核实。

### 需要确认的事项

1. 打开 Unity Editor，在 Project 窗口找到所有 `LuaScriptContainer` 类型的 `.asset` 文件
2. 逐个检查每个 Container 的 `luaAssets` 列表，确认引用的 Lua 文件确实来自预期目录
3. 记录实际的目录-容器映射关系（将用于填写 LuaAutoSyncConfig 的初始配置）

### 参考：预期映射表（待开发者确认/修正）

| 目录 | 容器 SO | 状态 |
|------|---------|------|
| AboutXLua/LuaScripts/Core | Core.asset | 待确认 |
| AboutXLua/LuaScripts/Framework | Framework.asset | 待确认 |
| AboutXLua/LuaScripts/Game/Player | Player.asset | 待确认 |
| （开发者补充其他映射） | | |

**此步骤完成后，通过 ask_user 告知确认结果，再开始 C1。**

---

## 任务 C1: LuaAutoSyncConfig SO

### 目标

创建配置 SO，存储「目录路径 -> LuaScriptContainer」的映射规则，
供扫描工具读取，不硬编码路径。

### 改动范围

| 文件 | 改动 |
|------|------|
| 新建 LuaAutoSyncConfig.cs | ScriptableObject，含映射列表 |

### 数据结构

```csharp
[CreateAssetMenu(menuName = "XLua/Lua Auto Sync Config")]
public class LuaAutoSyncConfig : ScriptableObject
{
    [System.Serializable]
    public class DirectoryMapping
    {
        public string directoryPath;           // 扫描目录（相对 Assets/）
        public LuaScriptContainer container;   // 对应的 Container SO（已有时直接引用）
        public string outputDirectory;         // Container SO 生成目录（新建时使用，相对 Assets/）
        public bool recursive = false;         // 是否递归扫描子目录
    }
    public List<DirectoryMapping> mappings = new();
}
```

---

## 任务 C2: LuaDirectoryScanner Editor 工具

### 目标

扫描 LuaAutoSyncConfig 定义的目录，将找到的 .lua / .lua.txt 文件
填充到对应 Container.luaAssets（取并集，不删除手动项）。

### 改动范围

| 文件 | 改动 |
|------|------|
| 新建 LuaDirectoryScanner.cs（Editor 目录） | 扫描 + 填充逻辑 |
| LuaAddressableTagger.cs | 添加「扫描目录」按钮（调用 Scanner），集成到现有窗口 |

### 同步策略

- **取并集**：扫描结果 + 手动添加的文件合并，不删除手动项
- **手动触发**：通过 Editor 按钮触发，不监听文件系统变化
- **可选纯自动模式**：勾选后扫描结果替代手动项，默认关闭，需二次确认

### 当前目录到容器的默认映射

基于项目现状的推荐初始配置（写入 LuaAutoSyncConfig.asset 时预填）。
此映射完全可配置，不硬编码，应将所有已有容器的目录列入：

| 目录 | 容器 | 递归 |
|------|------|------|
| AboutXLua/LuaScripts/Core | Core.asset | 是 |
| AboutXLua/LuaScripts/Framework | Framework.asset | 是 |
| AboutXLua/LuaScripts/Game/Player | Player.asset | 是 |

注：Game/ 下扫描所有有容器的子目录，每个子目录对应一个容器（一对一）。
新增子目录时，在 LuaAutoSyncConfig 中手动添加对应映射条目即可。

---

## 保留项（必须通过）

- [ ] 现有所有 Container SO 的 luaAssets 内容不被自动清空
- [ ] LuaDataBase 结构不变
- [ ] LuaScriptContainer 不改动
- [ ] LuaAddressableTagger 现有标签管理功能不变（只新增按钮）
- [ ] XLuaLoader 不改动

---

## 验收标准

1. 在 Assets 中创建 LuaAutoSyncConfig.asset，配置映射规则
2. 点击「扫描目录」按钮
3. Core.asset 的 luaAssets 中出现 Core/ 目录下所有 .lua 文件
4. Core.asset 中手动添加的额外文件仍然保留
5. 再次点击「扫描目录」，不产生重复条目

---

## SO 容器生成方案

采用方案2：脚本和 SO 容器分离。LuaAutoSyncConfig 存储「扫描目录 -> 容器 SO 生成位置」的映射。

LuaAutoSyncConfig 的 DirectoryMapping 结构调整：

```csharp
[System.Serializable]
public class DirectoryMapping
{
    public string directoryPath;           // 扫描目录（相对 Assets/）
    public LuaScriptContainer container;   // 对应的 Container SO（已有时直接引用）
    public string outputDirectory;         // Container SO 生成目录（新建时使用，相对 Assets/）
    public bool recursive = false;         // 是否递归扫描子目录
}
```

当 `container` 字段为空时，工具会在 `outputDirectory` 下自动创建以目录名命名的 Container SO。

---

## 任务 C3: 自动标签集成（扩展功能，可开关）

> **注意**：此任务依赖 AB 包管理重构（Plan-B）完成后执行。
> 当前 LuaAddressableTagger 和 SO 批量打标签工具都依赖 AA 包分组管理，
> Plan-B 完成后这些工具将替换为 AB 包自主管理方式。
> **C3 在 Plan-B 完成后再实施，不阻塞 C1/C2。**

### 目标

在扫描填充 Container 后，自动为新增的 Lua 脚本打上 Addressable 标签，
省去手动调用 SO 打标签工具的步骤。此功能可通过配置开关控制。

### 修改思路

当前流程：
```
1. 手动/工具 → 把 Lua 文件添加到 Container SO
2. 手动 → 打开 LuaAddressableTagger 窗口，点击打标签按钮
```

优化后流程：
```
1. 点击「扫描目录」按钮 → 自动填充 Container
2. （如果开关开启）→ 自动调用标签逻辑，为新增文件打标签
```

### 改动范围

| 文件 | 改动 |
|------|------|
| LuaAutoSyncConfig.cs | 新增 `bool autoTagAfterSync = false` 开关字段 |
| LuaDirectoryScanner.cs | 扫描完成后，如果开关开启，调用 LuaAddressableTagger 的标签逻辑 |
| LuaAddressableTagger.cs | 提取打标签核心逻辑为可被外部调用的静态方法（如 `TagContainerAssets(LuaScriptContainer)`） |

### 配置说明

```csharp
// LuaAutoSyncConfig 新增字段
[Header("扫描后自动打标签")]
[Tooltip("开启后，扫描填充 Container 完成时自动为新增文件调用 Addressable 打标签")]
public bool autoTagAfterSync = false;
```

- **默认关闭**：不影响现有工作流，只有手动开启才生效
- **工作原理**：LuaDirectoryScanner 完成扫描后，检查此开关，如果为 true 则遍历本次新增的文件，调用 `LuaAddressableTagger.TagContainerAssets()` 打标签
- **Console 输出**：打标签完成后在 Console 输出摘要（N 个文件已打标签）

### 保留项

- [ ] autoTagAfterSync 默认为 false，不影响不开启此功能的用户
- [ ] LuaAddressableTagger 窗口的手动打标签功能不变
- [ ] 标签规则与现有 LuaAddressableTagger 完全一致

### 验收标准

1. autoTagAfterSync = false 时，扫描后不触发任何标签操作
2. autoTagAfterSync = true 时，扫描后新增的 Lua 文件自动获得正确的 Addressable 标签
3. 已有标签的文件不会被重复打标签或标签被修改
4. Console 输出打标签摘要

---

## 审批清单

- [x] 目录到容器映射，是否需要支持「一个目录对应多个容器」的场景？
  **决定：一对一，不支持一对多。**
- [x] Game/ 目录处理：只扫描 Game/Player/ 子目录，还是 Game/ 下所有子目录各建一个容器？
  **决定：不限于 Player/，扫描所有有容器的子目录。映射配置在 LuaAutoSyncConfig 中维护。**
- [x] 是否需要在 Unity 保存 Assets 时自动触发扫描（AssetPostprocessor），还是只手动？
  **决定：仅手动按钮触发。**
