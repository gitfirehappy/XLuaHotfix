using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 采集器全局配置资产（单例 ScriptableObject）。
/// 存放路径：Assets/Build/CollectorSetting.asset。
/// 包含所有 Package 的层级配置：Setting → Package → Group → Collector。
/// </summary>
[CreateAssetMenu(fileName = "CollectorSetting", menuName = "XLua/CollectorSetting")]
public class CollectorSetting : ScriptableObject
{
    #region 字段

    /// <summary>所有资产包配置列表</summary>
    public List<CollectorPackage> Packages = new();

    #endregion
}

/// <summary>
/// Package 级别配置，对应一个独立的资产包（如主包、DLC 包等）。
/// </summary>
[Serializable]
public class CollectorPackage
{
    #region 字段

    /// <summary>包名，用于构建 Bundle 逻辑名的第一段前缀</summary>
    public string PackageName;
    
    /// <summary>该包下的所有 Group 配置</summary>
    public List<CollectorGroup> Groups = new();
    
    // SharePolicy 占位字段，E4 实现后启用
    // public SharePolicyConfig SharePolicy;

    #endregion
}

/// <summary>
/// Group 级别配置，对应一组具有相同标签和打包策略的采集器。
/// </summary>
[Serializable]
public class CollectorGroup
{
    #region 字段

    /// <summary>组名，用于构建 Bundle 逻辑名的第二段</summary>
    public string GroupName;

    /// <summary>组级别标签，与 Collector.Tags 取并集后写入 CollectedAssetInfo.Labels</summary>
    public List<string> Tags = new();

    /// <summary>该组下的所有采集器配置</summary>
    public List<Collector> Collectors = new();

    #endregion
}

/// <summary>
/// 最底层的采集规则绑定单元，指定一个目录路径及其对应的规则组合。
/// </summary>
[Serializable]
public class Collector
{
    #region 字段

    /// <summary>采集根目录路径（相对于 Assets/）</summary>
    public string CollectPath;

    /// <summary>采集器类型，决定资产的语义角色</summary>
    public ECollectorType CollectorType;

    /// <summary>强制指定载荷类型；Auto 表示由 Classifier 自动推断</summary>
    public EForcePayloadKind ForcePayloadKind;

    /// <summary>地址规则类名，由 RuleResolver 反射解析为 IAddressRule 实例</summary>
    public string AddressRuleName;

    /// <summary>打包规则类名，由 RuleResolver 反射解析为 IPackRule 实例</summary>
    public string PackRuleName;

    /// <summary>过滤规则类名，由 RuleResolver 反射解析为 IFilterRule 实例</summary>
    public string FilterRuleName;

    /// <summary>采集器级别标签，与 Group.Tags 取并集后写入 CollectedAssetInfo.Labels</summary>
    public List<string> Tags = new();

    /// <summary>忽略规则模式列表（类 gitignore 子集：*.ext / dirname/ / *keyword*）</summary>
    public List<string> IgnorePatterns = new();

    #endregion
}
