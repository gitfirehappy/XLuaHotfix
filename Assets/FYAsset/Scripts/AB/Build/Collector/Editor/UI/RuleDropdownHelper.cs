using System;
using System.Collections.Generic;
using System.Reflection;

/// <summary>
/// 规则下拉菜单辅助类 —— 通过反射扫描所有 IFilterRule/IGroupRule 实现，
/// 缓存类名列表供 UI Toolkit 下拉控件使用。
/// </summary>
public static class RuleDropdownHelper
{

    private static List<string> _filterRuleNames;
    private static List<string> _groupRuleNames;

    private static string[] _filterRuleArray;
    private static string[] _groupRuleArray;

    public static string[] GetFilterRuleNames()
    {
        EnsureCache();
        return _filterRuleArray;
    }

    public static string[] GetGroupRuleNames()
    {
        EnsureCache();
        return _groupRuleArray;
    }

    private static void EnsureCache()
    {
        if (_filterRuleNames == null)
        {
            _filterRuleNames = ScanImplementations(typeof(IFilterRule));
            _groupRuleNames = ScanImplementations(typeof(IGroupRule));
            _filterRuleArray = _filterRuleNames.ToArray();
            _groupRuleArray = _groupRuleNames.ToArray();
        }
    }

    private static List<string> ScanImplementations(Type interfaceType)
    {
        var names = new List<string>();
        Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();

        for (int a = 0; a < assemblies.Length; a++)
        {
            Type[] types;
            try
            {
                types = assemblies[a].GetTypes();
            }
            catch (ReflectionTypeLoadException)
            {
                continue;
            }

            for (int t = 0; t < types.Length; t++)
            {
                Type type = types[t];
                if (type == null || type.IsAbstract || type.IsInterface)
                    continue;
                if (!interfaceType.IsAssignableFrom(type))
                    continue;
                if (type.GetConstructor(Type.EmptyTypes) == null)
                    continue;

                names.Add(type.Name);
            }
        }

        names.Sort(StringComparer.Ordinal);
        return names;
    }
}
