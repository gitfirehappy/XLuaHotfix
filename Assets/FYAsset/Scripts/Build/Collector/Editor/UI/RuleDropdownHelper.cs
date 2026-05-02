using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 规则下拉菜单辅助类 —— 通过反射扫描所有 IAddressRule/IPackRule/IFilterRule/IGroupRule 实现，
/// 缓存类名列表并渲染 EditorGUI.Popup 下拉菜单。
/// </summary>
public static class RuleDropdownHelper
{
    #region Cache

    private static List<string> _addressRuleNames;
    private static List<string> _packRuleNames;
    private static List<string> _filterRuleNames;
    private static List<string> _groupRuleNames;

    #endregion

    #region Public Popup Methods

    public static string AddressRulePopup(Rect rect, string currentValue)
    {
        EnsureCache();
        return DrawPopup(rect, currentValue, _addressRuleNames);
    }

    public static string PackRulePopup(Rect rect, string currentValue)
    {
        EnsureCache();
        return DrawPopup(rect, currentValue, _packRuleNames);
    }

    public static string FilterRulePopup(Rect rect, string currentValue)
    {
        EnsureCache();
        return DrawPopup(rect, currentValue, _filterRuleNames);
    }

    public static string GroupRulePopup(Rect rect, string currentValue)
    {
        EnsureCache();
        return DrawPopup(rect, currentValue, _groupRuleNames);
    }

    /// <summary>强制重新扫描规则实现（新规则类加入后调用）</summary>
    public static void ClearCache()
    {
        _addressRuleNames = null;
        _packRuleNames = null;
        _filterRuleNames = null;
        _groupRuleNames = null;
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
                // Must have parameterless constructor
                if (type.GetConstructor(Type.EmptyTypes) == null)
                    continue;

                names.Add(type.Name);
            }
        }

        names.Sort(StringComparer.Ordinal);
        return names;
    }

    private static string DrawPopup(Rect rect, string currentValue, List<string> names)
    {
        if (names == null || names.Count == 0)
            return currentValue;

        int currentIndex = names.IndexOf(currentValue);
        if (currentIndex < 0)
            currentIndex = 0;

        int newIndex = EditorGUI.Popup(rect, currentIndex, names.ToArray());
        return newIndex >= 0 && newIndex < names.Count ? names[newIndex] : currentValue;
    }

    #endregion
}
