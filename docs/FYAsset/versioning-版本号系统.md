# 版本号系统

> 返回总览：[资源管理架构文档](./资源管理架构文档.md)

> **关联代码**
>
> `Assets/FYAsset/Scripts/Shared/Build/Versioning/VersionDataBase.cs` · `Assets/FYAsset/Scripts/Shared/Build/Versioning/VersionNumber.cs`

---

## 概述

项目统一使用 `VersionNumber`。发布版本字符串采用 `Major.Minor.Patch[-Channel]`；`Build` 是单独存储的本地构建计数，不拼入版本字符串，也不参与版本比较。版本号在构建期由 `VersionDataBase` 管理，运行期参与热更 Major 策略判断。

---

## VersionNumber — 版本号数据类型

### 格式

```
Major.Minor.Patch[-Channel]

示例：
  1.2.3              → release 版本，Build=0
  2.0.0-alpha        → alpha 渠道
  1.5.0-rc           → RC 渠道
```

### 字段

| 字段 | 类型 | 语义 |
|------|------|------|
| `Major` | int | 主版本号 — 不兼容的大版本更新，客户端必须强制更新 |
| `Minor` | int | 次版本号 — 功能性更新，热更可达 |
| `Patch` | int | 修订号 — Bug 修复和资源微调，热更可达 |
| `Build` | int | 当日构建计数，单独存储，不参与版本比较或发布字符串 |
| `Channel` | string | 发布渠道 — `""`(release)、`"alpha"`、`"beta"`、`"rc"`。参与版本比较 |

### 版本比较规则

比较优先级：**Major → Minor → Patch → ChannelRank**

Channel 排序：`alpha(0) < beta(1) < rc(2) < release("", 3)`

`Build` 不参与版本比较——例如 `1.0.0+1` 和 `1.0.0+99` 视为同一版本。

调用方直接使用标准比较运算符；具体顺序以 `VersionNumber.CompareTo` 为准。

### 字符串解析

使用 `TryParse` / `Parse` 解析 SemVer 字符串。解析会拒绝负数、非法 Channel 和包含 `+Build` 的旧格式。

### 格式化

`GetVersionString()` 只返回三段版本；`GetReleaseVersionString()` 与 `ToString()` 返回包含可选 Channel 的发布身份。

### 强制更新判断

Major 是客户端兼容边界，判断必须区分方向：

- `BuildIndex.Major > 本地 PackageIndex.Major`：已经安装新整包，删除旧 Major 热更目录后继续当前 Major 的远端流程。
- `BuildIndex.Major < 本地 PackageIndex.Major`：旧客户端或错误安装，删除不兼容目录、触发 `OnClientUpdateRequired` 并停止启动。
- `Remote PackageIndex.Major > BuildIndex.Major`：提示存在新客户端，跳过远端包内容并启动当前 Major 本地内容。
- `Remote PackageIndex.Major < BuildIndex.Major`：视为发布或 Channel 异常，告警后启动当前 Major 本地内容。

`BuildGUID` 是 Full baseline 的唯一包身份和路径名称，不参与兼容判断。

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

跨天时先重置每日构建计数，再按构建类型递增 Major、Minor 或 Patch；Channel 只接受 alpha、beta、rc 或空值。

三种递增模式对应三种构建类型：

| 构建类型 | isMajor | isMinor | 效果 |
|----------|---------|---------|------|
| 整包构建 | true | false | Major+1, Minor=0, Patch=0 |
| 功能性热更 | false | true | Minor+1, Patch=0 |
| 修复性热更 | false | false | Patch+1 |

### 资产位置与提交时机

路径在 `FYAssetSettings.VersionDataBasePath` 中配置，默认为 `Assets/Build/VersionDataBase.asset`。当前没有独立 `VersionPanel` 或 `CreateAssetMenu` 创建入口，项目使用已提交的共享资产；缺失时构建会报错。

`BuildProjectRunner` 先用 `BuildNextVersion()` 计算候选版本，只有构建与 Repository commit 都成功后才调用 `ApplyVersion()` 写回，避免失败构建提前消耗版本号。Repository 面板只提供测试用的 `Reset Version`。

---

## 与热更流程的关系

1. **构建时**：预计算候选版本，写入当前后端 manifest 与 `PackageIndex`；成功 commit 后再更新 `VersionDataBase`
2. **整包启动**：`BuildIndex.Version` 表示客户端基线 Major
3. **热更时**：远端 `PackageIndex` 指向目标 Hotfix 包，本地 `PackageIndex` 只记录完成激活、runtime manager 初始化和指针持久化的本地 Hotfix 包
