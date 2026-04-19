using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

[Serializable]
public class TypeReference
{
    public string assemblyName;//程序集名称
    public string typeName;//限定类名
    //此处小心循环引用！！！
    [NonSerialized] 
    public List<TypeReference> genericArguments = new List<TypeReference>();//泛型参数列表
    
    // 用于手动序列化存储的字段（Unity会序列化这个扁平结构）
    [SerializeField] 
    private List<string> _serializedGenerics = new List<string>();
    
    [NonSerialized] private Type _cachedType;       // 类型缓存
    [NonSerialized] private bool _isTypeCached;     // 缓存状态标记
    // 自身循环引用检测
    [NonSerialized] private HashSet<TypeReference> _resolutionPath = new HashSet<TypeReference>();


    // 序列化时将泛型参数转为字符串列表（避免递归）
    public void OnBeforeSerialize()
    {
        _serializedGenerics.Clear();
        foreach (var arg in genericArguments)
        {
            // 用特殊格式存储类型标识（程序集|类型名）
            _serializedGenerics.Add($"{arg.assemblyName}|{arg.typeName}");
        }
    }

    // 反序列化时从字符串列表恢复泛型参数
    public void OnAfterDeserialize()
    {
        genericArguments.Clear();
        foreach (var str in _serializedGenerics)
        {
            var parts = str.Split('|');
            if (parts.Length == 2)
            {
                genericArguments.Add(new TypeReference
                {
                    assemblyName = parts[0],
                    typeName = parts[1]
                });
            }
        }
    }
    
    /// <summary>
    /// 获取解析后的类型
    /// </summary>
    /// <returns></returns>
    public Type GetTypeCache()
    {
        if (_isTypeCached) return _cachedType;
        
        try
        {
            // 检查循环引用
            if (_resolutionPath.Contains(this))
            {
                Debug.LogWarning($"循环引用检测: 类型 {typeName} 的泛型参数包含自身引用");
                return CacheAndReturn(null);
            }
            
            _resolutionPath.Add(this);
            
            if (string.IsNullOrEmpty(assemblyName) || string.IsNullOrEmpty(typeName))
                return CacheAndReturn(null);
            
            //加载程序集
            Assembly assembly = LoadAssembly(assemblyName);
            if (assembly == null)
                return CacheAndReturn(null);
            
            //获取基础类型
            Type type = assembly.GetType(typeName);
            if (type == null)
            {
                Debug.LogWarning($"Type not found: {typeName} in {assemblyName}");
                return CacheAndReturn(null);
            }
            
            // 处理泛型类型
            if (genericArguments.Count > 0 && type.IsGenericTypeDefinition)
            {
                Type[] typeArgs = ResolveGenericArguments();    // 递归解析泛型参数
                if (typeArgs != null && typeArgs.Length == type.GetGenericArguments().Length)
                {
                    type = type.MakeGenericType(typeArgs);  // 创建具体泛型类型
                }
            }
            
            return CacheAndReturn(type);
        }
        catch (Exception ex)
        {
            Debug.LogError($"构造泛型类型失败: {typeName}: {ex.Message}");
            return CacheAndReturn(null);
        }
    }

    private Type CacheAndReturn(Type type)
    {
        _cachedType = type;
        _isTypeCached = true;
        return type;
    }

    private Assembly LoadAssembly(string assemblyName)
    {
        try
        {
            return Assembly.Load(assemblyName);
        }
        catch (Exception ex)
        {
            Debug.LogError($"Assembly load failed: {assemblyName}. Error: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 递归解析泛型参数
    /// </summary>
    /// <returns></returns>
    private Type[] ResolveGenericArguments()
    {
        const int MAX_DEPTH = 10;
        return ResolveGenericArguments(0,MAX_DEPTH, new HashSet<TypeReference>());
    }
    
    private Type[] ResolveGenericArguments(int currentDepth,int MAX_DEPTH, HashSet<TypeReference> visited)
    {
        // 深度保护
        if (currentDepth >= MAX_DEPTH)
        {
            Debug.LogError($"泛型参数解析超过最大深度限制({MAX_DEPTH})！可能存在循环引用");
            return null;
        }
        
        // 循环引用检测
        if (visited.Contains(this))
        {
            Debug.LogError($"循环引用检测: 类型 {typeName} 在泛型参数链中被重复引用");
            return null;
        }
        visited.Add(this);
        
        var resolvedArgs = new List<Type>();
        foreach (var argRef in genericArguments)
        {
            Type argType = argRef.GetTypeCache();
            if (argType == null)
            {
                Debug.LogError($"无法解析泛型参数: {argRef}");
                return null;
            }
            
            resolvedArgs.Add(argType);
        }
        visited.Remove(this);
        return resolvedArgs.ToArray();
    }

    public override string ToString()
    {
        if (string.IsNullOrEmpty(typeName)) 
            return "None";
        
        string baseName = $"{typeName}, {assemblyName}";
        
        // 添加泛型参数信息
        if (genericArguments.Count > 0)
        {
            string args = string.Join(", ", genericArguments);
            return $"{baseName}<{args}>";
        }
        return baseName;
    }
}