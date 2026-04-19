using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

[Serializable]
public class MemberReference
{
    public TypeReference ownerType;
    public string memberName;
    
    public enum MemberType { Method, Field, Property }
    public MemberType memberType;
    
    // 函数参数类型（用于区分重载）
    [NonSerialized] public List<TypeReference> parameterTypes = new List<TypeReference>();
    
    // 序列化参数类型（避免递归）
    [SerializeField] private List<string> _serializedParameters = new List<string>();
    
    [NonSerialized] private MemberInfo _cachedMember;
    [NonSerialized] private bool _isCached;
    
    public void OnBeforeSerialize()
    {
        _serializedParameters.Clear();
        foreach (var param in parameterTypes)
        {
            _serializedParameters.Add($"{param.assemblyName}|{param.typeName}");
        }
    }
    
    public void OnAfterDeserialize()
    {
        parameterTypes.Clear();
        foreach (var str in _serializedParameters)
        {
            var parts = str.Split('|');
            if (parts.Length == 2)
            {
                parameterTypes.Add(new TypeReference
                {
                    assemblyName = parts[0],
                    typeName = parts[1]
                });
            }
        }
    }
    
    public MemberInfo GetMemberCache()
    {
        if (_isCached) return _cachedMember;
        
        var ownerTypeObj = ownerType.GetTypeCache();
        if (ownerTypeObj == null)
            return CacheAndReturn(null);
        
        // 解析参数类型
        var paramTypes = parameterTypes.Select(t => t.GetTypeCache()).ToArray();
        
        // 根据类型查找成员
        switch (memberType)
        {
            case MemberType.Method:
                _cachedMember = FindMethod(ownerTypeObj, paramTypes);
                break;
            case MemberType.Field:
                _cachedMember = ownerTypeObj.GetField(memberName);
                break;
            case MemberType.Property:
                _cachedMember = ownerTypeObj.GetProperty(memberName);
                break;
        }
        
        return CacheAndReturn(_cachedMember);
    }
    
    private MethodInfo FindMethod(Type ownerType, Type[] paramTypes)
    {
        return ownerType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
            .FirstOrDefault(m => 
                m.Name == memberName && 
                ParametersMatch(m.GetParameters(), paramTypes)
            );
    }
    
    private bool ParametersMatch(ParameterInfo[] parameters, Type[] expectedTypes)
    {
        if (parameters.Length != expectedTypes.Length) 
            return false;
        
        for (int i = 0; i < parameters.Length; i++)
        {
            if (expectedTypes[i] == null) continue;
            
            if (parameters[i].ParameterType != expectedTypes[i] &&
                !(parameters[i].ParameterType.IsGenericType && 
                  expectedTypes[i].IsGenericType &&
                  parameters[i].ParameterType.GetGenericTypeDefinition() == expectedTypes[i].GetGenericTypeDefinition()))
            {
                return false;
            }
        }
        return true;
    }
    
    private MemberInfo CacheAndReturn(MemberInfo member)
    {
        _cachedMember = member;
        _isCached = true;
        return member;
    }
    
    public override string ToString()
    {
        if (ownerType == null || string.IsNullOrEmpty(memberName))
            return "None";
        
        return $"{ownerType}.{memberName} ({memberType})";
    }
}