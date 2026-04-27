using System;
using System.Collections.Generic;
using System.Reflection;

/// <summary>
/// 规则解析器 —— 将规则类名字符串通过反射解析为规则接口实例，并按类名缓存。
/// 规则实现类须满足：无参公共构造函数 + 实现对应接口。
/// 仅在 Editor 环境下使用。
/// </summary>
public static class RuleResolver
{
    #region 私有字段

    private static readonly Dictionary<string, IAddressRule> AddressRuleCache = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, IPackRule> PackRuleCache = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, IFilterRule> FilterRuleCache = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, IGroupRule> GroupRuleCache = new(StringComparer.Ordinal);

    #endregion

    #region 公共方法

    /// <summary>根据类名获取地址规则实例（缓存）</summary>
    public static IAddressRule GetAddressRule(string className)
    {
        return GetRule(className, AddressRuleCache);
    }

    /// <summary>根据类名获取打包规则实例（缓存）</summary>
    public static IPackRule GetPackRule(string className)
    {
        return GetRule(className, PackRuleCache);
    }

    /// <summary>根据类名获取过滤规则实例（缓存）</summary>
    public static IFilterRule GetFilterRule(string className)
    {
        return GetRule(className, FilterRuleCache);
    }

    /// <summary>根据类名获取分组规则实例（缓存）</summary>
    public static IGroupRule GetGroupRule(string className)
    {
        return GetRule(className, GroupRuleCache);
    }

    #endregion

    #region 私有方法

    private static T GetRule<T>(string className, Dictionary<string, T> cache) where T : class
    {
        if (string.IsNullOrWhiteSpace(className))
            throw new ArgumentException("规则类名不能为空。", nameof(className));

        if (cache.TryGetValue(className, out T cachedRule))
            return cachedRule;

        Type ruleType = ResolveType(className, typeof(T));
        if (ruleType == null)
            throw new InvalidOperationException(string.Concat("未找到规则类型：", className));

        if (ruleType.IsAbstract)
            throw new InvalidOperationException(string.Concat("规则类型是抽象类，无法实例化：", className));

        ConstructorInfo ctor = ruleType.GetConstructor(Type.EmptyTypes);
        if (ctor == null)
            throw new InvalidOperationException(string.Concat("规则类型缺少公共无参构造函数：", className));

        T rule = ctor.Invoke(null) as T;
        if (rule == null)
            throw new InvalidOperationException(string.Concat("规则实例化失败：", className));

        cache[className] = rule;
        return rule;
    }

    /// <summary>
    /// 在所有已加载程序集中查找指定类名且实现了 requiredInterface 的类型。
    /// 先按全名精确匹配，再按简单名匹配（仅限实现了目标接口的类型，避免同名误匹配）。
    /// </summary>
    private static Type ResolveType(string className, Type requiredInterface)
    {
        Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();

        // 第一轮：按全名精确匹配（带命名空间的类名直接命中）
        for (int i = 0; i < assemblies.Length; i++)
        {
            Type match = assemblies[i].GetType(className, false);
            if (match != null && requiredInterface.IsAssignableFrom(match))
                return match;
        }

        // 第二轮：按简单名匹配，仅检查实现了目标接口的类型，减少误匹配风险
        for (int i = 0; i < assemblies.Length; i++)
        {
            Type[] types;
            try
            {
                types = assemblies[i].GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                types = ex.Types;
            }

            if (types == null)
                continue;

            for (int j = 0; j < types.Length; j++)
            {
                Type candidate = types[j];
                if (candidate == null)
                    continue;

                // 仅匹配实现了目标接口的类型，避免同名但接口不符的类型干扰
                if (!requiredInterface.IsAssignableFrom(candidate))
                    continue;

                if (string.Equals(candidate.Name, className, StringComparison.Ordinal) ||
                    string.Equals(candidate.FullName, className, StringComparison.Ordinal))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    #endregion
}
