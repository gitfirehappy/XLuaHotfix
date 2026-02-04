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
            return method.Invoke(null, parameters);
        }
        catch (Exception e)
        {
            Debug.LogError($"[DialogueFuncRegistry] 执行对话函数出错 {funcName}：{e.Message}");
            return null;
        }
    }
}
