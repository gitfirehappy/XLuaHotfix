# Plan-D: 模块化程序集拆分

> **状态**: 草稿，待开发者审批后方可执行
> **创建**: 2026-03-22
> **更新**: 2026-03-22 - 调整为独立模块 + 静态胶水层架构

---

## 背景与目标

将项目按功能模块拆分为**独立程序集**，通过**静态胶水层**（静态中间层调用方法）通信，实现：
- 支持扩展包独立发布
- 模块间无直接依赖，可独立演进
- 轻量级解耦，避免运行时事件系统的复杂性

---

## 拆分方案

### 程序集划分

| 程序集名称 | 包含模块 | 文件数 | 依赖情况 |
|-----------|---------|--------|---------|
| **Hotfix.Build** | 热更构建模块 | ~25 | 无运行时依赖 |
| **Framework.UI** | UI管理模块 | ~5 | 无直接依赖（通过胶水层调用） |
| **Framework.Config** | 数据文本转换模块 | ~17 | Editor-only，无运行时依赖 |
| **Framework.Dialogue** | 对话系统模块 | ~12 | 无直接依赖（通过胶水层调用） |

### 独立模块 + 静态胶水层架构

```
┌─────────────────────────────────────────────────────────────┐
│                      Assembly-CSharp                        │
│                        (主程序集)                            │
│  ┌──────────┐  ┌──────────┐  ┌──────────┐  ┌──────────┐  │
│  │  Bridge  │  │  胶水层   │  │   Core   │  │   Game   │  │
│  │  (桥接)  │  │ (Facade) │  │ (工具类) │  │ (逻辑)   │  │
│  └──────────┘  └────┬─────┘  └──────────┘  └──────────┘  │
│                     │                                      │
└─────────────────────┼──────────────────────────────────────┘
                      │
                      ▼
┌─────────────────────────────────────────────────────────────┐
│                    独立模块程序集                             │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐      │
│  │Hotfix.Build │  │Framework.UI  │  │Framework.    │      │
│  │  (构建)      │  │  (UI管理)     │  │Dialogue      │      │
│  └──────────────┘  └──────────────┘  │  (对话系统)  │      │
│                                        └──────────────┘      │
│  ┌──────────────┐                                        │
│  │Framework.   │                                        │
│  │Config        │                                        │
│  │(数据转换)    │                                        │
│  └──────────────┘                                        │
└─────────────────────────────────────────────────────────────┘
```

**核心原则**: 程序集之间**无直接引用**，通过静态胶水层（Facade）间接调用

---

## 静态胶水层设计

### 胶水层结构

```
Assets/AboutXLua/Scripts/Core/Glue/
├── BuildFacade.cs       // 热更构建模块门面
├── UIFacade.cs          // UI管理模块门面
└── DialogueFacade.cs    // 对话系统模块门面
```

### Facade 接口设计

```csharp
// BuildFacade.cs - 热更构建模块门面
public static class BuildFacade {
    public static void StartBuild(BuildType type) {
        BuildProjectManager.Instance.StartBuild(type);
    }

    public static string GetLastBuildOutput() {
        return BuildProjectManager.Instance.LastOutputPath;
    }
}

// UIFacade.cs - UI管理模块门面
public static class UIFacade {
    public static void ShowPanel(string panelName, object data = null) {
        UIManager.Instance.Show(panelName, data);
    }

    public static void HidePanel(string panelName) {
        UIManager.Instance.Hide(panelName);
    }
}

// DialogueFacade.cs - 对话系统模块门面
public static class DialogueFacade {
    public static void StartDialogue(string dialogueId) {
        DialogueController.Instance.StartDialogue(dialogueId);
    }

    public static void ShowDialogueUI(object data) {
        UIFacade.ShowPanel("DialoguePanel", data);
    }
}
```

### 调用示例

```csharp
// DialogueController 调用 UI（通过胶水层）
public void StartDialogue(string dialogueId) {
    var data = LoadDialogueData(dialogueId);
    DialogueFacade.ShowDialogueUI(data);  // 通过胶水层调用 UI
}

// Lua 脚本调用 C# 模块
-- Lua 中调用
Glue.BuildFacade.StartBuild("Hotfix")
Glue.UIFacade.ShowPanel("SettingsPanel")
```

---

## 各模块详细设计

### D1: Hotfix.Build（热更构建模块）

**路径**: `Assets/AboutXLua/Scripts/Core/Hotfix_AAPackageManage/`

**包含文件** (~25个):
- AAPackageManager.cs
- ABAssetIndex.cs
- ABBundleLoader.cs
- ABPackageBackend.cs
- AddressablesBackend.cs
- CatalogUpdater.cs
- HotfixManager.cs
- IAssetIndex.cs
- IPackageBackend.cs
- NetworkDownloader.cs
- PackageCleaner.cs
- BuildManage/ 全部子目录 (~13个文件)

**asmdef 配置**:
```json
{
    "name": "Hotfix.Build",
    "rootNamespace": "Hotfix.Build",
    "references": [
        "Unity.Addressables",
        "Unity.ResourceManager"
    ],
    "includePlatforms": [],
    "excludePlatforms": []
}
```

---

### D2: Framework.UI（UI管理模块）

**路径**: `Assets/AboutXLua/Scripts/Framework/UI/`

**包含文件** (5个):
- UIAnimation.cs
- UIFormBase.cs
- UIFormConfigSO.cs
- UIManager.cs
- UIResourceConfigSO.cs

**asmdef 配置**:
```json
{
    "name": "Framework.UI",
    "rootNamespace": "Framework.UI",
    "references": [],
    "includePlatforms": [],
    "excludePlatforms": []
}
```

---

### D3: Framework.Config（数据文本转换模块）

**路径**: `Assets/AboutXLua/Scripts/Framework/ConfigConvertTool/`

**包含文件** (~17个):
- Core/ 全部 (6个)
- Editor/ 全部 (2个)
- Reader/ 全部 (5个)
- Writer/ 全部 (4个)
- SimpleParser/ 全部 (2个)

**asmdef 配置**:
```json
{
    "name": "Framework.Config",
    "rootNamespace": "Framework.Config",
    "references": [],
    "includePlatforms": ["Editor"],
    "excludePlatforms": []
}
```

---

### D4: Framework.Dialogue（对话系统模块）

**路径**: `Assets/AboutXLua/Scripts/Framework/Dialogue/`

**包含文件** (~12个):
- DialoguePanel.cs
- CharacterConfig.cs
- CsharpOnly/ 全部 (10个)

**asmdef 配置**:
```json
{
    "name": "Framework.Dialogue",
    "rootNamespace": "Framework.Dialogue",
    "references": [],
    "includePlatforms": [],
    "excludePlatforms": []
}
```

**⚠️ 解耦任务（执行 D4 前必须完成）**:
1. `DialogueFacade` 作为胶水层，对话模块不直接引用 UI
2. `DialogueController` 调用 `DialogueFacade.ShowDialogueUI(data)` 显示面板
3. Lua 脚本通过 `DialogueFacade.StartDialogue(id)` 启动对话

---

## 执行顺序

**同步进行策略**：每解耦一个模块，同步更新胶水层，确保系统全程可运行

```
模块拆分与胶水层同步
├── D0: 创建胶水层目录 + 基础 Facade
├── D1: 拆分 Hotfix.Build + BuildFacade
├── D2: 拆分 Framework.UI + UIFacade
├── D3: 拆分 Framework.Config（纯Editor，无运行时依赖）
└── D4: 解耦 Dialogue + DialogueFacade + 拆分 Framework.Dialogue
```

**每个模块拆分步骤**:
1. 创建对应 Facade
2. 模块代码移入新程序集
3. 验证胶水层调用正常
4. 继续下一个模块

---

## 执行任务清单

### D0: 胶水层创建
- [ ] 创建 `Assets/AboutXLua/Scripts/Core/Glue/` 目录
- [ ] 创建 `BuildFacade.cs`
- [ ] 创建 `UIFacade.cs`
- [ ] 创建 `DialogueFacade.cs`

### D1: Hotfix.Build 程序集创建
- [ ] 创建 `Hotfix.Build.asmdef`
- [ ] 将 Hotfix_AAPackageManage/ 下所有 .cs 移入新程序集
- [ ] Editor 脚本放入 `Hotfix.Build.Editor/` 子目录

### D2: Framework.UI 程序集创建
- [ ] 创建 `Framework.UI.asmdef`
- [ ] 将 Framework/UI/ 下所有 .cs 移入新程序集
- [ ] 确保 UIManager 不再直接引用其他模块

### D3: Framework.Config 程序集创建
- [ ] 创建 `Framework.Config.asmdef`
- [ ] 将 ConfigConvertTool/ 下所有 .cs 移入新程序集
- [ ] Editor 脚本放入 `Framework.Config.Editor/` 子目录

### D4: Dialogue 解耦 + Framework.Dialogue 程序集创建
- [ ] **解耦**: 确保 Dialogue 模块通过 DialogueFacade 调用 UI
- [ ] 创建 `Framework.Dialogue.asmdef`
- [ ] 将 Dialogue/ 下所有 .cs 移入新程序集

---

## 保留项（不变动）

1. **Core/Utility/** — 基础工具类保留在主程序集
2. **Bridge/** — XLua 桥接代码保留在主程序集
3. **Glue/** — 胶水层在主程序集，作为模块间调用的唯一入口
4. **Game/** — 游戏逻辑保留在主程序集
5. **Global/** — 启动逻辑保留在主程序集

---

## 优势说明

| 对比项 | 事件通信 | 静态胶水层 |
|--------|---------|-----------|
| 耦合度 | 松耦合 | 中等耦合（通过 Facade） |
| 运行时开销 | 有（事件系统） | 无（直接调用） |
| 调试难度 | 较难（异步事件流） | 容易（同步调用） |
| 类型安全 | 弱（字符串事件名） | 强（编译期检查） |
| 学习成本 | 需了解事件系统 | 类似工具类调用 |

---

## 风险与注意事项

1. **程序集 GUID 变更**: 拆分后程序集 GUID 会变化
   - Unity 会自动修复大部分引用
2. **Facade 膨胀**: 随着模块交互增多，Facade 可能变得臃肿
   - 解决：Facade 只做简单转发，不含业务逻辑
3. **循环依赖风险**: 需确保 Facade 不引入循环依赖

---

## 后续扩展

拆分完成后，各程序集可独立作为 Unity Package 发布：
- `com.hotfix.build@1.0.0.unitypackage`
- `com.framework.ui@1.0.0.unitypackage`
- `com.framework.config@1.0.0.unitypackage`
- `com.framework.dialogue@1.0.0.unitypackage`
