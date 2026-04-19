using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using XLua;

public static class XLuaConfig
{
    [LuaCallCSharp]
    public static List<Type> LuaCallCSharp = new()
    {
        // 常用基础静态类型
        typeof(UnityEngine.Object),
        typeof(UnityEngine.GameObject),
        typeof(UnityEngine.Transform),
        typeof(UnityEngine.MonoBehaviour),
        typeof(UnityEngine.Time),
        typeof(UnityEngine.Vector3),
        typeof(UnityEngine.Vector2),
        typeof(UnityEngine.Collision2D),  
        typeof(UnityEngine.Collider2D),   
        typeof(UnityEngine.Rigidbody2D),  
        typeof(UnityEngine.Quaternion),
        typeof(UnityEngine.Debug),
        typeof(UnityEngine.Mathf),
        typeof(UnityEngine.LayerMask),
        typeof(UnityEngine.Color),
        typeof(System.Collections.Generic.List<int>),
        typeof(System.Action),
        typeof(System.Action<int>),
        typeof(System.Func<bool>),

        // 常用框架方法
        typeof(IBridge), // 所有Bridge继承的接口，继承此接口的类都可以通过Lua调用
        
    };
    
    [CSharpCallLua]
    public static List<Type> CSharpCallLua = new List<Type>()
    {
        // 常用基础静态类型
        typeof(System.Action),
        typeof(System.Action<int>),
        typeof(System.Action<string>),
        typeof(System.Func<int, string>),
        typeof(System.Collections.IEnumerator), 
        typeof(System.Func<System.Collections.IEnumerator>),

    };

    // 热修复静态基础类型(几乎不用)
    [Hotfix]
    public static List<Type> Hotfix = new List<Type>()
    {
        
    };
}
