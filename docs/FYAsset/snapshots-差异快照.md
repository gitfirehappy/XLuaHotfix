# Snapshots 差异快照

> 返回总览：[资源管理架构文档](./资源管理架构文档.md)

> **关联代码**
>
> `Assets/FYAsset/Scripts/Build/Snapshots/ArtifactDigest.cs` · `Assets/FYAsset/Scripts/Build/Snapshots/ArtifactDelta.cs` · `Assets/FYAsset/Scripts/Build/Snapshots/Editor/ArtifactDiffer.cs`

---

## 概述

Snapshots 子系统是构建仓库（Build Repository）的差异计算引擎。它定义了一次构建产物的最小内容指纹（`ArtifactDigest`），以及两次构建之间的三段式差异结果（`ArtifactDelta`），并通过纯函数 `ArtifactDiffer` 按名称配对比对 Hash。

设计原则：
- **命名域独立**：AA 使用 Asset GUID 作为 Name，AB 使用 BundleName 作为 Name。调用方保证同一次 Diff 两侧处于同一命名域
- **纯计算、零副作用**：`ArtifactDiffer` 不访问 Unity API，不产生文件 I/O
- **JSON 可序列化**：`ArtifactDigest` 和 `ArtifactDelta` 均可 JSON 序列化，作为 `RepositoryCommit` 的一部分持久化

---

## 数据模型

### ArtifactDigest（产物指纹）

构建产物的最小内容指纹。JSON 可序列化，不参与 Binary 序列化。

| 字段 | 类型 | JSON | 语义 |
|------|------|:----:|------|
| `Name` | string | ✓ | 产物身份标识。AA 使用 Asset GUID，AB 使用 BundleName |
| `Hash` | string | ✓ | 内容 Hash，当前使用 MD5 字符串 |
| `Size` | long | ✓ | 产物大小，单位为 byte |
| `CRC` | uint | ✓ | CRC32 快速校验值 |

```csharp
[Serializable]
public class ArtifactDigest
{
    public string Name;   // 产物身份
    public string Hash;   // MD5 内容哈希
    public long Size;     // 产物大小（byte）
    public uint CRC;      // CRC32 校验值
}
```

### ArtifactDelta（差异结果）

`ArtifactDiffer.Diff()` 的三段式输出。JSON 可序列化。

| 字段 | 类型 | JSON | 语义 |
|------|------|:----:|------|
| `Added` | List\<ArtifactDigest\> | ✓ | 目标侧存在、基准侧不存在的产物 |
| `Modified` | List\<ArtifactDigest\> | ✓ | 两侧 Name 相同但 Hash 不同的产物（取目标侧值） |
| `Removed` | List\<string\> | ✓ | 基准侧存在、目标侧不存在的产物，只需保留 Name |
| `IsEmpty` | bool (computed) | — | 没有任何新增、修改或删除（`Added.Count == 0 && Modified.Count == 0 && Removed.Count == 0`） |

```csharp
public class ArtifactDelta
{
    public List<ArtifactDigest> Added = new();
    public List<ArtifactDigest> Modified = new();
    public List<string> Removed = new();
    public bool IsEmpty => Added.Count == 0 && Modified.Count == 0 && Removed.Count == 0;
}
```

---

## ArtifactDiffer（差异计算器）

纯静态方法，Editor-only（`#if UNITY_EDITOR`），按 Name 配对并比较 Hash：

```csharp
public static ArtifactDelta Diff(
    IReadOnlyList<ArtifactDigest> from,   // 基准侧（通常是 Repository HEAD）
    IReadOnlyList<ArtifactDigest> to      // 目标侧（当前扫描结果）
)
```

### 算法

1. **构建基准索引**：遍历 `from`，按 `Name` → `ArtifactDigest` 建字典（跳过 null 和空 Name）
2. **分类目标产物**：遍历 `to`（跳过 null 和空 Name）：
   - `Name` 不在基准索引中 → **Added**
   - `Name` 在基准索引中但 `Hash` 不同（Ordinal 字符串比较）→ **Modified**
   - `Name` 在基准索引中且 `Hash` 相同 → 无变化（不记录）
3. **查找删除项**：基准索引中 `Name` 不在目标侧 → **Removed**（只保留 Name 字符串）

### 使用约束

- 调用方必须保证 `from` 和 `to` 处于同一命名域
- 跳过 null 条目和空 `Name`，不抛出异常
- Hash 比较使用 `StringComparison.Ordinal`（区分大小写、无文化感知）

---

## 在构建流程中的使用

### AA 热更差异扫描

`TaskScanAddressableHotfixDiff` 在 AA Hotfix 构建时执行：

1. 遍历 `AddressableAssetSettings` 的所有 Group 和 Entry（跳过 `Built In Data` 组）
2. 对每个 Entry，计算源文件 + `.meta` 文件的**复合内容指纹**：
   - 读取源文件和 `.meta` 文件的字节内容拼接
   - 计算 MD5 Hash → `ArtifactDigest.Hash`
   - 计算 CRC32 → `ArtifactDigest.CRC`
   - 文件大小 → `ArtifactDigest.Size`
   - 使用 Asset GUID → `ArtifactDigest.Name`
3. 从 `BuildRepositoryFacade` 获取 Repository HEAD 的 `Artifacts` 列表
4. 调用 `ArtifactDiffer.Diff(headArtifacts, currentArtifacts)` 计算差异
5. 将 `ArtifactDelta` 和 `RepositoryArtifacts` 写入 `BuildContext`

### AB 热更差异扫描

`TaskScanABHotfixDiff` 在 AB Hotfix 构建时执行：

1. 从 `ABManifest.BundleEntries` 获取当前构建的 Bundle 列表
2. 每个 Bundle 转换为 `ArtifactDigest`（Name=BundleName, Hash=FileHash, Size=FileSize, CRC=FileCRC）
3. 从 `BuildRepositoryFacade` 获取 Repository HEAD 的 `Artifacts` 列表
4. 调用 `ArtifactDiffer.Diff(headArtifacts, currentArtifacts)` 计算差异
5. 将 `ArtifactDelta` 和 `RepositoryArtifacts` 写入 `BuildContext`

### Diff Preview

`RepositoryPreviewRunner` 使用相同的 `ArtifactDiffer.Diff()` 逻辑，但在预览模式下执行（不写 HEAD）：

- **AA Preview**：运行到 `TaskScanAddressableHotfixDiff` 停，无临时文件
- **AB Preview**：先构建再运行到 `TaskScanABHotfixDiff` 停，临时目录在 finally 清理

### Repository Push

`FileBuildRepository.Push()` 使用 `ArtifactDiffer.Diff()` 计算 from → to 的差异，只推送 Added 和 Modified 的 Bundle 文件。

---

## 与其他系统的关系

```
CollectionScanner / BuildPipeline
         │
         ▼
  ArtifactDiffer.Diff(from, to)
         │
         ▼
    ArtifactDelta ──→ TaskMoveAddressableHotfixGroups（AA 移动 Group）
         │
         ▼
  RepositoryArtifacts ──→ BuildRepositoryFacade.Commit()（写入 HEAD）
         │
         ▼
  FileBuildRepository.Push() ──→ ArtifactDiffer.Diff(fromCommit, toCommit)
         │
         ▼
    PushPayload ──→ LocalDirectoryPushTarget.Push()
```
