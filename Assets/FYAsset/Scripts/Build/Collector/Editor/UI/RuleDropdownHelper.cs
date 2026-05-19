using System;
using System.Collections.Generic;
using System.Reflection;

/// <summary>
/// 规则下拉菜单辅助类 —— 通过反射扫描所有 IAddressRule/IPackRule/IFilterRule/IGroupRule 实现，
/// 缓存类名列表供 UI Toolkit 下拉控件使用。
/// </summary>
public static class RuleDropdownHelper
{
    #region Cache

    private static List<string> _addressRuleNames;
    private static List<string> _packRuleNames;
    private static List<string> _filterRuleNames;
    private static List<string> _groupRuleNames;

    private static string[] _addressRuleArray;
    private static string[] _packRuleArray;
    private static string[] _filterRuleArray;
    private static string[] _groupRuleArray;

    #endregion

    #region Public Popup Methods

    public static string[] GetAddressRuleNames()
    {
        EnsureCache();
        return _addressRuleArray;
    }

    public static string[] GetPackRuleNames()
    {
        EnsureCache();
        return _packRuleArray;
    }

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

    /// <summary>强制重新扫描规则实现（新规则类加入后调用）</summary>
    public static void ClearCache()
    {
        _addressRuleNames = null;
        _packRuleNames = null;
        _filterRuleNames = null;
        _groupRuleNames = null;
        _addressRuleArray = null;
        _packRuleArray = null;
        _filterRuleArray = null;
        _groupRuleArray = null;
    }

    #endregion

    #region Private — Cache & Scan

    private static void EnsureCache()
    {
        if (_addressRuleNames == null)
        {
            _addressRuleNames = ScanImplementations(typeof(IAddressRule));
            _packRuleNames = ScanImplementations(typeof(IPackRule));
            _filterRuleNames = ScanImplementations(typeof(IFilterRule));
            _groupRuleNames = ScanImplementations(typeof(IGroupRule));
            _addressRuleArray = _addressRuleNames.ToArray();
            _packRuleArray = _packRuleNames.ToArray();
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
                // 必须有无参构造函数
                if (type.GetConstructor(Type.EmptyTypes) == null)
                    continue;

                names.Add(type.Name);
            }
        }

        names.Sort(StringComparer.Ordinal);
        return names;
    }

    #endregion
}
