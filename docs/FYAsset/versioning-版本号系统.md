# 版本号系统

> 返回总览：[资源管理架构文档](./资源管理架构文档.md)

> **关联代码**
>
> `Assets/FYAsset/Scripts/Shared/Build/Versioning/VersionDataBase.cs` · `Assets/FYAsset/Scripts/Shared/Build/Versioning/VersionNumber.cs`

---

## 概述

项目采用 **SemVer 2.0** 版本号规范（`Major.Minor.Patch-Channel+Build`），全项目统一使用 `VersionNumber` 类型。版本号在构建期由 `VersionDataBase` ScriptableObject 管理，运行期参与热更版本比对。

---

## VersionNumber — 版本号数据类型

### 格式

```
Major.Minor.Patch[-Channel][+Build]

示例：
  1.2.3              → release 版本，Build=0
  2.0.0-alpha+5      → alpha 渠道，第 5 次构建
  1.5.0-rc+12        → RC 渠道，第 12 次构建
```

### 字段

| 字段 | 类型 | 语义 |
|------|------|------|
| `Major` | int | 主版本号 — 不兼容的大版本更新，客户端必须强制更新 |
| `Minor` | int | 次版本号 — 功能性更新，热更可达 |
| `Patch` | int | 修订号 — Bug 修复和资源微调，热更可达 |
| `Build` | int | 构建号 — 当日自增计数，不参与版本比较，仅用于区分同版本的多次构建 |
| `Channel` | string | 发布渠道 — `""`(release)、`"alpha"`、`"beta"`、`"rc"`。参与版本比较 |

### 版本比较规则

比较优先级：**Major → Minor → Patch → ChannelRank**

Channel 排序：`alpha(0) < beta(1) < rc(2) < release("", 3)`

`Build` 不参与版本比较——例如 `1.0.0+1` 和 `1.0.0+99` 视为同一版本。

```csharp
public int CompareTo(VersionNumber other)
{
    // 依次比较 Major → Minor → Patch → ChannelRank
}

// 支持标准比较运算符
public static bool operator >(VersionNumber a, VersionNumber b);
public static bool operator <(VersionNumber a, VersionNumber b);
public static bool operator >=(VersionNumber a, VersionNumber b);
public static bool operator <=(VersionNumber a, VersionNumber b);
```

### 字符串解析

支持 `TryParse` / `Parse` 从 SemVer 字符串还原：

```csharp
VersionNumber.TryParse("2.1.0-beta+3", out var version);
// version.Major=2, Minor=1, Patch=0, Channel="beta", Build=3

VersionNumber.TryParse("invalid", out _);  // → false
```

解析会校验字段范围（不允许负数）和 Channel 合法性（仅允许 alpha/beta/rc/空）。

### 格式化

```csharp
version.GetVersionString();       // "1.2.3"（三字段，忽略 Channel 和 Build）
version.GetFullVersionString();   // "1.2.3-alpha+5"（完整 SemVer）
version.ToString();               // 同 GetFullVersionString()
```

### 强制更新判断

```csharp
public bool RequiresForceUpdate(VersionNumber baseline)
{
    // Major 不同 → 需要强制更新整包
    return Major != baseline.Major;
}
```

这个判断用在热更版本比对步骤——如果远端的 Major 版本和本地不同且 BuildIndex 也不匹配，就提示用户去应用商店下载最新整包。

---

## VersionDataBase — 版本管理 ScriptableObject

`VersionDataBase` 是 Editor 程序集的 ScriptableObject，负责在构建时管理和递增版本号。

### 字段

| 字段 | 类型 | 说明 |
|------|------|------|
| `CurrentVersion` | VersionNumber | 当前版本号 |
| `LastBuildTime` | string | 上次构建时间（`yyyy-MM-dd HH:mm:ss`） |
| `DailyBuildCount` | int | 当日构建次数 |

### 版本递增逻辑

```csharp
public void IncrementVersion(bool isMajor, bool isMinor, string channel)
{
    // 日期处理：跨天自动重置 DailyBuildCount
    // 版本递增：
    //   isMajor  → Major+1, Minor=0, Patch=0
    //   isMinor  → Minor+1, Patch=0
    //   else     → Patch+1
    // Build = DailyBuildCount
    // Channel 校验（仅允许 alpha/beta/rc/""）
}
```

三种递增模式对应三种构建类型：

| 构建类型 | isMajor | isMinor | 效果 |
|----------|---------|---------|------|
| 整包构建 | true | false | Major+1, Minor=0, Patch=0 |
| 功能性热更 | false | true | Minor+1, Patch=0 |
| 修复性热更 | false | false | Patch+1 |

### 创建方式

菜单：`Create → Build → VersionDataBase`

路径在 `FYAssetSettings.VersionDataBasePath` 中配置，默认为 `Assets/Build/VersionDataBase.asset`。`VersionDataBase` 是产品级共享版本源，不按 AA / AB 拆分。

---

## 与热更流程的关系

1. **构建时**：`VersionDataBase.IncrementVersion()` 递增版本号 → 写入 `ABManifest.PackageVersion`
2. **热更时**：`HotfixFlowBase` 的版本比对步骤会比对本地的 `BuildIndex.Version`、`localInfo.Version` 和 `remoteInfo.Version`
3. **判断规则**：
   - 远端 Major > 本地 Major + BuildIndex.Version 不匹配 → 要求强制更新整包
   - 远端 Major > 本地 Major + BuildIndex.Version 匹配远端 → 全量清理旧热更数据（整包已更新）
   - 其他情况 → 正常热更下载
