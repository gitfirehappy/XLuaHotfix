# FileHelper 文件工具

> **关联代码** | `Assets/Tools/Scripts/FileHelper.cs`

## 概述

跨平台文件 I/O 工具类，提供同步/异步读取、原子写入、复制/替换、目录枚举、跨平台存在性检查和安全删除。定位为 FYAsset 基础设施层，与 `NetworkDownloader`、`FYAssetPathUtility`、`SerializationUtility` 同级。

异步读 API 对 Android StreamingAssets 路径自动走 `UnityWebRequest`，其他平台走 `System.IO`。`WriteAll*Atomic` 与 `ReplaceFile` 用临时/替换语义避免半截目标文件；普通复制 API 不承诺事务性。`TryDelete*` 不抛异常。

---

## API 参考

### 异步读取

| 方法 | 返回 | 说明 |
|------|------|------|
| `ReadAllBytesAsync(string path)` | `Task<byte[]>` | 异步读取文件全部字节 |
| `ReadAllTextAsync(string path)` | `Task<string>` | 异步读取文件全部文本（UTF-8） |

两个异步读取方法使用同一平台策略：Android Player 的 StreamingAssets 通过 `UnityWebRequest` 读取，其余路径使用 `System.IO`。

- **必须在主线程调用**（Android StreamingAssets 路径需要 `UnityWebRequest.SendWebRequest()`）
- 参数为 null/空时抛 `ArgumentNullException`
- 文件不存在时抛 `FileNotFoundException`
- StreamingAssets 读取失败时抛 `IOException`（含路径和错误信息）

### 原子写入

| 方法 | 说明 |
|------|------|
| `WriteAllBytesAtomic(string path, byte[] data)` | 原子写入字节数组 |
| `WriteAllTextAtomic(string path, string text)` | 原子写入字符串（UTF-8） |

原子写入先确保父目录存在，再在目标旁写入唯一临时文件；目标存在时通过 `File.Replace` 同卷替换，不存在时通过 `File.Move` 就位。调用方只会看到完整旧文件或完整新文件。

**保证**：目标文件要么是旧版本（完整），要么是新版本（完整），不会出现写入中断导致的半截文件。可用于热更新下载完成后替换本地文件。

- 参数为 null 时抛 `ArgumentNullException`

### 文件系统操作

| 方法 | 返回 | 说明 |
|------|------|------|
| `Exists(string path)` | `bool` | 跨平台文件存在性检查 |
| `TryDelete(string path)` | `bool` | 安全删除文件，失败返回 false + 警告日志 |
| `TryDeleteDirectory(string path, bool recursive)` | `bool` | 递归删除目录，失败返回 false + 警告日志 |
| `EnsureDirectoryForFile(string filePath)` | `void` | 确保文件路径的父目录存在，不存在则创建 |
| `EnsureDirectory(string dirPath)` / `DirectoryExists(string path)` | `void` / `bool` | 创建目录与检查目录 |
| `CopyFile(...)` / `TryCopyFile(...)` | `void` / `bool` | 复制文件；Try 版本失败时返回 false |
| `ReplaceFile(sourcePath, targetPath)` | `void` | 同文件系统原子替换；目标不存在时移动 source |
| `ReadAllText/ReadAllBytes` | `string` / `byte[]` | 同步读取 |
| `GetFiles/GetDirectories` | `string[]` | 目录枚举 |
| `GetDirectorySize(string path)` | `long` | 递归统计目录大小；失败文件跳过并记录警告 |
| `FormatBytes(long bytes)` | `string` | 按 1024 进制输出 B/KB/MB/GB/TB，最多两位小数 |

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
3. **原子替换必须同卷**：临时文件与目标位于同一目录；写入或替换失败时会立即尝试删除临时文件，进程崩溃仍可能留下 `.tmp.xxx`
4. **`EnsureDirectoryForFile` 是写入前的安全网**：`WriteAllBytesAtomic` / `WriteAllTextAtomic` 内部已自动调用，外部直接使用 `File.WriteAllBytes` 时需手动调用
5. **`TryDeleteDirectory` 默认递归**：`recursive` 参数默认 `true`，非递归删除非空目录会失败
