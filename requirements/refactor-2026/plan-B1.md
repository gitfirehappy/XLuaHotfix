# Sub-Plan B1: 资源索引层抽象（IAssetIndex）

> **风险**: 低
> **依赖**: 无
> **预计改动文件**: 3 个新文件 + 1 个现有文件
> **状态**: 已完成并签收

---

## 修改思路（为什么要做这一步）

AAPackageManager.Initialize() 直接把 AddressableLabelsConfig 存为私有字段 _config，
所有 GetKeysByLabel / GetKeysByType 都通过它查询。这使得「索引数据从哪来」被硬编码到了
AAPackageManager 内部，换成自研 AB 时无法替换数据来源。

**方案**: 提取 IAssetIndex 接口，让 AAPackageManager 只依赖接口，
无论底层是 AddressableLabelsConfig 还是自研 ABManifest，上层代码不变。

---

## 改动范围

| 文件 | 改动类型 | 说明 |
|------|---------|------|
| 新建 IAssetIndex.cs | 新增 | 查询接口定义 |
| AddressableLabelsConfig.cs | 修改 | 实现 IAssetIndex 接口（添加接口，不改现有结构） |
| 新建 ABAssetIndex.cs | 新增 | 从自研 ABManifest 读取索引（B4 阶段使用，B1 先建好框架）。**B1 阶段只创建空壳类，方法抛出 NotImplementedException，ABManifest 格式和解析逻辑在 B4 阶段设计。** |
| AAPackageManager.cs | 修改 | _config 类型改为 IAssetIndex，Initialize 改为接受 IAssetIndex |

---

## IAssetIndex 接口设计

```csharp
/// <summary>
/// 资源索引接口
/// 抽象「Label/Type -> Key」的查询能力，解耦具体数据来源（Addressables 或自研 AB）
/// </summary>
public interface IAssetIndex
{
    /// <summary> 获取某标签下的所有资源 Key </summary>
    List<string> GetKeysByLabel(string label);

    /// <summary> 获取某类型的所有资源 Key </summary>
    List<string> GetKeysByType(string type);

    /// <summary> 获取所有已注册的标签 </summary>
    IEnumerable<string> GetLabels();

    /// <summary> 检查某 Key 是否已注册 </summary>
    bool ContainsKey(string key);
}
```

---

## ABAssetIndex.cs 说明

ABAssetIndex 在 B1 阶段只创建空壳类（方法抛出 NotImplementedException），真正的 ABManifest 格式和解析逻辑在 B4 阶段设计。

---

## AAPackageManager 修改说明

**修改前**（硬编码）:
```csharp
private AddressableLabelsConfig _config;  // 具体类型

public async Task Initialize()
{
    var handle = Addressables.LoadAssetAsync<AddressableLabelsConfig>(...);
    _config = await handle.Task;
    ...
}
```

**修改后**（依赖接口）:
```csharp
private IAssetIndex _index;  // 只依赖接口

// 保留原有方式初始化（向后兼容）
public async Task Initialize()
{
    var config = await LoadAddressableConfig();
    _index = config;  // AddressableLabelsConfig 实现了 IAssetIndex
    ...
}

// 新增：支持注入自定义索引（供 ABPackageBackend 使用）
public void SetIndex(IAssetIndex index) { _index = index; }
```

所有原 `_config.GetKeysByLabel(...)` 调用改为 `_index.GetKeysByLabel(...)`，
**AAPackageManager 对外 API 完全不变**。

---

## 保留项（必须通过）

- [ ] AddressableLabelsConfig 现有序列化格式不变（Unity .asset 文件兼容）
- [ ] AAPackageManager.GetKeysByLabel / GetKeysByType 等公开方法不变
- [ ] 不传 SetIndex 时，默认走原有 AddressableLabelsConfig 初始化逻辑

---

## 验收标准

- [ ] 编译通过，无 CS 错误
- [ ] AAPackageManager.Initialize() 后，GetKeysByLabel 返回与重构前相同的结果
- [ ] 真机运行资源加载正常（选一个包含多种标签资源的场景测试）

---

## 审批问题

- [x] IAssetIndex 接口是否还需要其他方法（如 GetAllEntries）？
  **决定**：当前四个方法足够（GetKeysByLabel、GetKeysByType、GetLabels、ContainsKey），不需要 GetAllEntries。
- [x] ABAssetIndex 的 ABManifest 格式是否有参考，还是 B4 时再设计？
  **决定**：B4 时再设计。B1 的 ABAssetIndex 只建空壳框架类，不填入具体 Manifest 解析逻辑。
