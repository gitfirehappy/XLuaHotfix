using System;
using System.Collections.Generic;
#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;

/// <summary>
/// 项目级自动 Address 生成样式。
/// </summary>
public enum AssetAddressStyle
{
    ShortName = 0,
    LongAssetPathWithoutExtension = 1,
    NameType = 2
}

/// <summary>
/// 资产采集配置资产。
/// 层级结构：Setting -> Package -> Group -> Collector，资产元数据按 GUID 独立存储。
/// </summary>
public class AssetCollectionSetting : ScriptableObject
{
    #region 字段

    /// <summary>所有资产包配置列表</summary>
    public List<AssetCollectionPackage> Packages = new();

    /// <summary>自动 Address 的项目级默认生成样式。</summary>
    public AssetAddressStyle AddressStyle = AssetAddressStyle.ShortName;

    /// <summary>Project Scan 阶段的全局忽略规则，用于生成候选 Collector 前过滤项目资产。</summary>
    public List<string> IgnorePatterns = CreateDefaultIgnorePatterns();

    /// <summary>被 Folder Collector 覆盖但显式排除的资产列表，按 GUID 判断，路径只作为可读缓存。</summary>
    public List<AssetExclusion> ExcludedAssets = new();

    /// <summary>资产级元数据，按 Unity GUID 作为权威键</summary>
    public List<AssetEntry> AssetEntries = new();

    #endregion

    #region 公共方法

    public AssetEntry FindAssetEntry(string assetGuid)
    {
        if (string.IsNullOrEmpty(assetGuid) || AssetEntries == null)
            return null;

        for (int i = 0; i < AssetEntries.Count; i++)
        {
            AssetEntry entry = AssetEntries[i];
            if (entry != null && string.Equals(entry.AssetGUID, assetGuid, StringComparison.Ordinal))
                return entry;
        }

        return null;
    }

    public AssetExclusion FindExcludedAsset(string assetGuid)
    {
        if (string.IsNullOrEmpty(assetGuid) || ExcludedAssets == null)
            return null;

        for (int i = 0; i < ExcludedAssets.Count; i++)
        {
            AssetExclusion exclusion = ExcludedAssets[i];
            if (exclusion != null && string.Equals(exclusion.AssetGUID, assetGuid, StringComparison.Ordinal))
                return exclusion;
        }

        return null;
    }

    public bool IsExcludedAssetGuid(string assetGuid)
    {
        return FindExcludedAsset(assetGuid) != null;
    }

    public bool AddExcludedAsset(string assetGuid, string assetPath)
    {
        if (string.IsNullOrEmpty(assetGuid))
            return false;

        ExcludedAssets ??= new List<AssetExclusion>();
        AssetExclusion existing = FindExcludedAsset(assetGuid);
        if (existing != null)
        {
            string normalizedPath = NormalizeAssetPath(assetPath);
            if (string.Equals(existing.AssetPath, normalizedPath, StringComparison.Ordinal))
                return false;

            existing.AssetPath = normalizedPath;
            return true;
        }

        ExcludedAssets.Add(new AssetExclusion
        {
            AssetGUID = assetGuid,
            AssetPath = NormalizeAssetPath(assetPath)
        });
        return true;
    }

    public bool RemoveExcludedAsset(string assetGuid)
    {
        if (string.IsNullOrEmpty(assetGuid) || ExcludedAssets == null)
            return false;

        for (int i = ExcludedAssets.Count - 1; i >= 0; i--)
        {
            AssetExclusion exclusion = ExcludedAssets[i];
            if (exclusion == null || !string.Equals(exclusion.AssetGUID, assetGuid, StringComparison.Ordinal))
                continue;

            ExcludedAssets.RemoveAt(i);
            return true;
        }

        return false;
    }

#if UNITY_EDITOR
    public bool AddExcludedAssetByGuid(string assetGuid)
    {
        if (string.IsNullOrEmpty(assetGuid))
            return false;

        return AddExcludedAsset(assetGuid, AssetDatabase.GUIDToAssetPath(assetGuid));
    }

    public bool RefreshExcludedAssetPaths()
    {
        if (ExcludedAssets == null)
            return false;

        bool changed = false;
        for (int i = ExcludedAssets.Count - 1; i >= 0; i--)
        {
            AssetExclusion exclusion = ExcludedAssets[i];
            if (exclusion == null || string.IsNullOrEmpty(exclusion.AssetGUID))
            {
                ExcludedAssets.RemoveAt(i);
                changed = true;
                continue;
            }

            string assetPath = NormalizeAssetPath(AssetDatabase.GUIDToAssetPath(exclusion.AssetGUID));
            if (string.Equals(exclusion.AssetPath, assetPath, StringComparison.Ordinal))
                continue;

            exclusion.AssetPath = assetPath;
            changed = true;
        }

        return changed;
    }
#endif

    public AssetEntry GetOrCreateAssetEntry(string assetGuid, string generatedAddress, AssetClassification generatedClassification)
    {
        AssetEntries ??= new List<AssetEntry>();

        AssetEntry existing = FindAssetEntry(assetGuid);
        if (existing != null)
            return existing;

        AssetEntry entry = new AssetEntry
        {
            AssetGUID = assetGuid,
            AutoAddress = true,
            Address = generatedAddress,
            AutoRole = true,
            Role = generatedClassification.Role,
            AutoPayload = true,
            PayloadKind = generatedClassification.PayloadKind,
            Labels = new List<string>()
        };
        AssetEntries.Add(entry);
        return entry;
    }

    public static List<string> CreateDefaultIgnorePatterns()
    {
        return new List<string>
        {
            "Assets/FYAsset/**",
            "Assets/Build/**",
            "Assets/StreamingAssets/**"
        };
    }

    private static string NormalizeAssetPath(string assetPath)
    {
        return string.IsNullOrEmpty(assetPath) ? string.Empty : assetPath.Replace('\\', '/').TrimEnd('/');
    }

    #endregion
}

/// <summary>
/// 资产级排除条目。GUID 是权威键，AssetPath 是面向编辑器显示和迁移审计的缓存。
/// </summary>
[Serializable]
public class AssetExclusion
{
    public string AssetGUID;
    public string AssetPath;
}

/// <summary>
/// Package 级别配置，对应一个独立的资产包（如主包、DLC 包等）。
/// </summary>
[Serializable]
public class AssetCollectionPackage
{
    #region 字段

    /// <summary>包名，用于构建 Bundle 逻辑名的第一段前缀</summary>
    public string PackageName;
    
    /// <summary>该包下的所有 Group 配置</summary>
    public List<AssetCollectionGroup> Groups = new();
    
    /// <summary>Package 级共享提取策略，由依赖分析 Task 读取</summary>
    public SharePolicyConfig SharePolicy = new();

    #endregion
}

/// <summary>
/// Group 级别配置，对应一组具有相同标签和打包策略的采集器。
/// </summary>
[Serializable]
public class AssetCollectionGroup
{
    #region 字段

    /// <summary>组名，用于构建 Bundle 逻辑名的第二段</summary>
    public string GroupName;

    /// <summary>是否启用该 Group。为 false 时 CollectionScanner 跳过整个 Group</summary>
    public bool Enabled = true;

    /// <summary>组级别标签，会强制继承到该 Group 下所有资产</summary>
    public List<string> Labels = new();

    /// <summary>Addressables 风格的 Group 打包模式</summary>
    public BundlePackingMode BundlePackingMode = BundlePackingMode.PackTogetherByLabel;

    /// <summary>该组下的所有采集器配置</summary>
    public List<Collector> Collectors = new();

    #endregion
}

/// <summary>
/// 最底层的采集规则绑定单元，指定一个目录或文件路径及其对应的规则组合。
/// </summary>
[Serializable]
public class Collector
{
    #region 字段

    /// <summary>采集根路径（相对于 Assets/，可指向目录或单个文件）</summary>
    public string CollectPath;

    /// <summary>采集路径类型；默认 Folder 表示目录采集器</summary>
    public ECollectPathType CollectPathType = ECollectPathType.Folder;

    /// <summary>采集器类型，决定资产的语义角色</summary>
    public ECollectorType CollectorType;

    /// <summary>强制指定载荷类型；Auto 表示由 Classifier 自动推断</summary>
    public EForcePayloadKind ForcePayloadKind;

    /// <summary>过滤规则类名，由 RuleResolver 反射解析为 IFilterRule 实例</summary>
    public string FilterRuleName;

    /// <summary>分组规则类名，由 RuleResolver 反射解析为 IGroupRule 实例</summary>
    public string GroupRuleName;

    #endregion
}

/// <summary>
/// 资产级权威元数据，以 Unity GUID 为键。
/// </summary>
[Serializable]
public class AssetEntry
{
    public string AssetGUID;
    public bool AutoAddress = true;
    public string Address;
    public List<string> Labels = new();
    public bool AutoRole = true;
    public EAssetRole Role = EAssetRole.Main;
    public bool AutoPayload = true;
    public EPayloadKind PayloadKind = EPayloadKind.Serialized;
}
