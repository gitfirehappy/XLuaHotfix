# Sub-Plan B3: DialogueDataManager 独立双模式

> **风险**: 低（独立模块，不影响核心热更流程）
> **依赖**: B1 完成后可执行（不强制依赖 B2）
> **状态**: 已完成 (2026-03-18)

---

## 背景说明（开发者必读）

DialogueDataManager 是对话系统的独立模块，设计上希望「可拆即用」：
其他项目可以只复制对话系统，不依赖庞大的 AAPackageManager。

因此，不建议把 DialogueDataManager 强制改为通过 AAPackageManager 加载，
而是保留直接调用 Addressables 的能力，同时提供「接入 AAPackageManager」的可选模式。

---

## 修改思路

通过编译开关（#if）或运行时配置（DialogueLoaderMode），让 DialogueDataManager
在两种模式下工作：

**模式 A（默认，保持现状）**: 直接调用 Addressables.LoadAssetAsync
- 适合：独立使用对话系统，不需要 AAPackageManager

**模式 B（可选，集成模式）**: 通过 AAPackageManager 加载
- 适合：项目已有完整 AB 管理体系，希望统一资源入口

---

## 改动范围

| 文件 | 改动 |
|------|------|
| DialogueDataManager.cs | 添加 LoaderMode 枚举 + 可切换的加载逻辑 |

---

## 实现方案

```csharp
public static class DialogueDataManager
{
    /// <summary>
    /// 资源加载模式
    /// Standalone: 直接使用 Addressables（模块独立可用）
    /// Integrated: 通过 AAPackageManager（项目统一入口）
    /// </summary>
    public enum LoaderMode { Standalone, Integrated }

    /// <summary> 当前加载模式，默认 Standalone，项目初始化时可切换 </summary>
    public static LoaderMode Mode = LoaderMode.Standalone;

    public static List<DialogueData> LoadDialogueData(string csvFileName)
    {
        ...
        // 根据 Mode 选择加载路径
        if (Mode == LoaderMode.Standalone)
        {
            // 原有逻辑：直接 Addressables.LoadAssetAsync
        }
        else
        {
            // 集成逻辑：AAPackageManager.Instance.LoadAssetSync<TextAsset>
        }
    }
}
```

---

## 保留项（必须通过）

- [ ] 不设置 Mode 时，行为与现在完全一致（Standalone 为默认）
- [ ] DialogueDataManager 依然可以从项目中独立复制使用
- [ ] LoadDialogueData(TextAsset) 重载不变（直接传资源，无需任何加载器）

---

## 验收标准

- [ ] Standalone 模式：行为与重构前完全一致
- [ ] Integrated 模式：通过 AAPackageManager 加载，资源正确返回
- [ ] 切换 Mode 不影响已缓存的对话数据

---

## 无需审批问题

本阶段方案已明确，开发者确认方向后可直接执行：
- 保留 Standalone 模式 + 新增 Integrated 可选模式
- 如有其他想法可提问
