using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using XLua;

// C#版本对话系统函数注册与调用器
public static class DialogueFuncRegistry
{
    private static Dictionary<string, MethodInfo> _funcMap = new();

    /// <summary>
    /// 扫描实现了IDialogueFuncProvider接口的类
    /// </summary>
    public static void ScanAndRegister()
    {
        _funcMap.Clear();
        var assembly = Assembly.GetExecutingAssembly();
        
        // 只获取实现了IDialogueFuncProvider接口的类型
        var targetTypes = assembly.GetTypes()
            .Where(t => typeof(IDialogueFuncProvider).IsAssignableFrom(t) && !t.IsInterface);

        foreach (var type in targetTypes)
        {
            // 只处理静态方法
            var methods = type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            foreach (var method in methods)
            {
                var attr = method.GetCustomAttribute<DialogueFuncAttribute>();
                if (attr != null)
                {
                    string funcName = !string.IsNullOrEmpty(attr.DisplayName) ? 
                        attr.DisplayName : method.Name;
                    if (string.IsNullOrEmpty(funcName))
                    {
                        Debug.LogWarning($"[DialogueFuncRegistry] 对话函数名不能为空：{type.Name}.{method.Name}");
                        continue;
                    }

                    if (_funcMap.ContainsKey(funcName))
                    {
                        Debug.LogWarning($"[DialogueFuncRegistry] 对话函数名重复：{funcName}（{type.Name}.{method.Name}）");
                        continue;
                    }

                    _funcMap.Add(funcName, method);
                    Debug.Log($"[DialogueFuncRegistry] 注册对话函数：{funcName} -> {type.Name}.{method.Name}");
                }
            }
        }
    }

    /// <summary>
    /// 调用对话函数
    /// </summary>
    public static object InvokeFunction(string funcName, params object[] parameters)
    {
        _funcMap.TryGetValue(funcName,out var method);
        if (method == null)
        {
            Debug.LogError($"[DialogueFuncRegistry] 未找到对话函数：{funcName}");
            return null;
        }

        try
        {
            // 参数适配：根据目标方法的参数类型修正推导的参数类型
            var adaptedParams = AdaptParameters(method, parameters);
            return method.Invoke(null, adaptedParams);
        }
        catch (Exception e)
        {
            Debug.LogError($"[DialogueFuncRegistry] 执行对话函数出错 {funcName}：{e.Message}");
            return null;
        }
    }

    /// <summary>
    /// 参数适配：根据目标方法的参数类型签名修正推导的参数类型
    /// 解决自动类型推导导致的类型不匹配问题（如期望string的"123"被推导成double）
    /// </summary>
    private static object[] AdaptParameters(MethodInfo method, object[] parameters)
    {
        var paramInfos = method.GetParameters();
        if (parameters == null || parameters.Length == 0)
            return parameters;

        var adapted = new object[parameters.Length];
        for (int i = 0; i < parameters.Length; i++)
        {
            var value = parameters[i];
            // 如果有对应的参数信息，则尝试适配类型
            if (i < paramInfos.Length)
            {
                var expectedType = paramInfos[i].ParameterType;
                adapted[i] = AdaptValue(value, expectedType);
            }
            else
            {
                // 超出定义的参数，保持原样（可能是params参数）
                adapted[i] = value;
            }
        }
        return adapted;
    }

    /// <summary>
    /// 将值适配为期望类型
    /// </summary>
    private static object AdaptValue(object value, Type expectedType)
    {
        if (value == null)
            return null;

        var valueType = value.GetType();
        
        // 如果类型已经匹配，直接返回
        if (expectedType.IsAssignableFrom(valueType))
            return value;

        try
        {
            // 期望string类型，将任意类型转换为字符串
            if (expectedType == typeof(string))
            {
                return value.ToString();
            }
            
            // 期望数值类型，尝试转换
            if (expectedType == typeof(int) || expectedType == typeof(Int32))
            {
                return Convert.ToInt32(value);
            }
            if (expectedType == typeof(long) || expectedType == typeof(Int64))
            {
                return Convert.ToInt64(value);
            }
            if (expectedType == typeof(float) || expectedType == typeof(Single))
            {
                return Convert.ToSingle(value);
            }
            if (expectedType == typeof(double) || expectedType == typeof(Double))
            {
                return Convert.ToDouble(value);
            }
            if (expectedType == typeof(bool) || expectedType == typeof(Boolean))
            {
                // 字符串转bool
                if (value is string strVal)
                {
                    var lower = strVal.ToLower();
                    if (lower == "true" || lower == "1") return true;
                    if (lower == "false" || lower == "0") return false;
                }
                return Convert.ToBoolean(value);
            }

            // 处理List<object>类型
            if (expectedType == typeof(List<object>) && value is Dictionary<object, object> dict)
            {
                // 如果是连续整数键的字典，转为列表
                return DictToList(dict);
            }
            
            // 特殊处理：List<object> 转 List<string> (解决泛型转换失败 InvalidCastException)
            if (expectedType == typeof(List<string>) && value is List<object> objList)
            {
                return objList.Select(o => o?.ToString()).ToList();
            }

            // 特殊处理：String 转 List (防止解析失败导致单个字符串传给List参数)
            if (typeof(System.Collections.IList).IsAssignableFrom(expectedType) && value is string sVal)
            {
                if (expectedType == typeof(List<string>))
                    return new List<string> { sVal };
                if (expectedType == typeof(List<object>))
                    return new List<object> { sVal };
            }

            // 使用ChangeType作为兜底
            return Convert.ChangeType(value, expectedType);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[DialogueFuncRegistry] 参数类型适配失败：{value}({valueType.Name}) -> {expectedType.Name}，使用原值。错误：{e.Message}");
            return value;
        }
    }

    /// <summary>
    /// 将连续整数键的字典转换为列表
    /// </summary>
    private static List<object> DictToList(Dictionary<object, object> dict)
    {
        var list = new List<object>();
        int index = 1;
        while (dict.TryGetValue((double)index, out var val) || dict.TryGetValue(index, out val))
        {
            list.Add(val);
            index++;
        }
        return list.Count > 0 ? list : dict.Values.ToList();
    }
}
