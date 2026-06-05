using System;
using System.Collections.Generic;

/// <summary>
/// 清单资源条目 — ABManifest 的序列化单元。
/// 
/// 设计说明：
/// - 字段集与 RuntimeAssetEntry 完全一致，额外增加 BundleIndex 字段
/// - 作为 ABManifest 的核心数据结构，直接参与 JSON 序列化；运行时通过 ABAssetIndex 转换为 RuntimeAssetEntry 使用
/// - 反序列化后由 ABAssetIndex 转换为 RuntimeAssetEntry + Bundle 映射
/// - BundleIndex 表示所属 Bundle 在 ABManifest.BundleEntries 数组中的索引
/// - 当前使用 JsonUtility 序列化；后续可升级为 Protobuf 等二进制格式
/// </summary>
[Serializable]
[BinarySerializable]
public class ManifestAssetEntry
{
    #region 运行时必须字段（与 RuntimeAssetEntry 对齐）

    /// <summary>
    /// 内部唯一身份（复用 Unity GUID）。
    /// 用途：缓存键、诊断标识、句柄归属。
    /// </summary>
    [BinaryField(0)]
    public string EntryId;

    /// <summary>
    /// 逻辑名（允许重复）。
    /// 自动生成样式由构建期 AssetCollectionSetting.AddressStyle 决定；不会因短名冲突自动重写。
    /// </summary>
    [BinaryField(1)]
    public string Address;

    /// <summary>
    /// 资源类型名（如 "Texture2D"、"GameObject"）。
    /// ScriptableObject 使用具体类名。
    /// </summary>
    [BinaryField(2)]
    public string PrimaryType;

    /// <summary>
    /// 分类标签集合。
    /// 匹配时大小写不敏感（归一化在查询侧处理）。
    /// </summary>
    [BinaryField(3)]
    public List<string> Labels = new();

    #endregion

    #region 编辑器诊断字段（可配置裁剪 StripEditorFields）

    /// <summary>
    /// 资源在项目中的路径（如 "Assets/Prefabs/Player.prefab"）。
    /// 仅用于编辑器定位与线上问题排查，不作为运行时查询入口。
    /// </summary>
    [BinaryField(4)]
    public string SourcePath;

    /// <summary>
    /// 构建分组名称（如 "Characters"）。
    /// 仅参与编辑器报表与构建语义。
    /// </summary>
    [BinaryField(5)]
    public string Group;

    /// <summary>
    /// 标记 Address 是自动生成还是手动覆写。
    /// true = 自动生成（可重建）；false = 手动覆写（锁定）。
    /// </summary>
    [BinaryField(6)]
    public bool AutoAddress = true;

    #endregion

    #region Bundle 绑定（ABManifest 特有）

    /// <summary>
    /// 所属 Bundle 在 ABManifest.BundleEntries 中的索引。
    /// 运行时通过此索引快速定位 Bundle 元数据。
    /// </summary>
    [BinaryField(7)]
    public int BundleIndex;

    #endregion

    /// <summary>
    /// 转换为 RuntimeAssetEntry（不含 BundleIndex，由 ABAssetIndex 另行维护映射）。
    /// </summary>
    public RuntimeAssetEntry ToRuntimeEntry()
    {
        var entry = new RuntimeAssetEntry
        {
            EntryId = EntryId,
            Address = Address,
            PrimaryType = PrimaryType,
            SourcePath = SourcePath,
            Group = Group,
            AutoAddress = AutoAddress
        };
        entry.SetLabels(Labels);
        return entry;
    }
}
