# FileHelper 文件工具

> **关联代码** | `Assets/FYAsset/Scripts/Helpers/FileHelper.cs`

## 概述

跨平台文件 I/O 工具类，提供异步读写、原子写入、跨平台存在性检查、安全删除等文件操作。定位为 FYAsset 基础设施层，与 `NetworkDownloader`（网络下载）、`PathManager`（路径管理）、`SerializationUtility`（序列化工具）同级。

所有异步读 API 对 Android StreamingAssets 路径自动走 `UnityWebRequest`，其他平台走 `System.IO`。所有写 API 保证不会产生半截文件。删除 API 绝不抛异常。

---

## API 参考

### 异步读取

| 方法 | 返回 | 说明 |
|------|------|------|
| `ReadAllBytesAsync(string path)` | `Task<byte[]>` | 异步读取文件全部字节 |
| `ReadAllTextAsync(string path)` | `Task<string>` | 异步读取文件全部文本（UTF-8） |

平台分支逻辑（两个方法一致）：

```
路径以 Application.streamingAssetsPath 开头？
  ├─ Android (非 Editor) → UnityWebRequest.Get() → downloadHandler.data/text
  └─ 其他平台           → Task.Run(File.ReadAllBytes/ReadAllText)
```

- **必须在主线程调用**（Android StreamingAssets 路径需要 `UnityWebRequest.SendWebRequest()`）
- 参数为 null/空时抛 `ArgumentNullException`
- 文件不存在时抛 `FileNotFoundException`
- StreamingAssets 读取失败时抛 `IOException`（含路径和错误信息）

### 原子写入

| 方法 | 说明 |
|------|------|
| `WriteAllBytesAtomic(string path, byte[] data)` | 原子写入字节数组 |
| `WriteAllTextAtomic(string path, string text)` | 原子写入字符串（UTF-8） |

原子写入流程：

```
1. EnsureDirectoryForFile(path)        — 确保父目录存在
2. 生成临时路径（path + ".tmp." + GUID前8位）
3. File.WriteAllBytes/Text(tempPath)   — 写入临时文件
4. File.Delete(path)                   — 删除旧文件（如存在）
5. File.Move(tempPath → path)          — rename 到目标路径
```

**保证**：目标文件要么是旧版本（完整），要么是新版本（完整），不会出现写入中断导致的半截文件。可用于热更新下载完成后替换本地文件。

- 参数为 null 时抛 `ArgumentNullException`

### 文件系统操作

| 方法 | 返回 | 说明 |
|------|------|------|
| `Exists(string path)` | `bool` | 跨平台文件存在性检查 |
| `TryDelete(string path)` | `bool` | 安全删除文件，失败返回 false + 警告日志 |
| `TryDeleteDirectory(string path, bool recursive)` | `bool` | 递归删除目录，失败返回 false + 警告日志 |
| `EnsureDirectoryForFile(string filePath)` | `void` | 确保文件路径的父目录存在，不存在则创建 |

**Exists 平台行为**：
- Android StreamingAssets 路径（jar: URI）无法用 `File.Exists` 检测 → 直接返回 `false`
- 其他平台/路径 → `File.Exists(path)`
- 参数为 null/空 → 返回 `false`

**TryDelete / TryDeleteDirectory 行为约定**：
- 文件/目录不存在 → 返回 `true`（视为"已删除"）
- 删除失败 → `Debug.LogWarning` 输出原因 + 返回 `false`
- **绝不抛异常**，适合在 `finally` 块或清理逻辑中使用

---

## 平台适配

| 场景 | Android (Player) | Editor / Standalone |
|------|:---:|:---:|
| StreamingAssets 读取 | `UnityWebRequest` | `System.IO.File` |
| 普通路径读取 | `System.IO.File` | `System.IO.File` |
| StreamingAssets Exists | 永远 `false` | `File.Exists` |
| 写入（原子/非原子） | `System.IO.File` | `System.IO.File` |

---

## 注意事项

1. **Android StreamingAssets 只能读不能写**：`Exists` 返回 false、写入不支持此路径
2. **异步方法需要主线程**：Android StreamingAssets 路径的 `UnityWebRequest` 依赖 Unity 主线程调度
3. **原子写入的临时文件**：暂未主动清理——正常流程会 rename 走，仅在 rename 前崩溃时残留 `.tmp.xxx` 文件。可在清理逻辑中按 `*.tmp.*` 模式统一清理
4. **`EnsureDirectoryForFile` 是写入前的安全网**：`WriteAllBytesAtomic` / `WriteAllTextAtomic` 内部已自动调用，外部直接使用 `File.WriteAllBytes` 时需手动调用
5. **`TryDeleteDirectory` 默认递归**：`recursive` 参数默认 `true`，非递归删除非空目录会失败
