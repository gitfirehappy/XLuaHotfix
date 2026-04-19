using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using UnityEngine;

/// <summary>
/// XLua标签批量配置的ScriptableObject容器
/// </summary>
[CreateAssetMenu(fileName = "TypeList", menuName = "XLua/Type List", order = 0)]
public class TypeMemberListSO : ScriptableObject,ISerializationCallbackReceiver
{
    [ReadOnly] public string remind = "系统类型在XLuaConfig统一静态注册,SO管理自定义上层类型";
    
    /// <summary>
    /// 需要管理的标签类型
    /// </summary>
    public enum ConfigTag
    {
        Hotfix = 0,
        LuaCallCSharp = 1,
        CSharpCallLua = 2
    }
    [Tooltip("配置分类")]
    public ConfigTag tag;
    
    public List<TypeMemberConfiguration> configurations = new List<TypeMemberConfiguration>();
    
    public void OnBeforeSerialize()
    {
        foreach (var config in configurations)
        {
            config.OnBeforeSerialize();
        }
    }

    public void OnAfterDeserialize()
    {
        foreach (var config in configurations)
        {
            config.OnAfterDeserialize();
        }
    }
}
[Serializable]
public class TypeMemberConfiguration
{
    [SerializeReference]
    public TypeReference typeRef; // 选择的类型
    
    [SerializeReference]
    public MemberReference memberRef;       // 选择的成员（可为空）\
    
    [SerializeReference]
    public bool isEntireType;               // 是否选择整个类型
    
    public string DisplayText {
        get {
            if (typeRef == null) return "未选择类型";
        
            if (isEntireType)
            {
                return $"类型: {typeRef.typeName.Split('.').Last()}";
            }
            else
            {
                if (memberRef == null) return "已选择类型，请选择成员";
                if (string.IsNullOrEmpty(memberRef.memberName)) return "已选择类型，未选择成员";
                return $"成员: {typeRef.typeName.Split('.').Last()}.{memberRef.memberName}";
            }
        }
    }
    
    public void OnBeforeSerialize()
    {
        typeRef?.OnBeforeSerialize();
        memberRef?.OnBeforeSerialize();
    }
    
    public void OnAfterDeserialize()
    {
        typeRef?.OnAfterDeserialize();
        memberRef?.OnAfterDeserialize();
    }
}
