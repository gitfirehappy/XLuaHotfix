using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 无需继承MonoBehaviour的单例类
/// </summary>
public class Singleton<T> where T : Singleton<T>, new()
{
    private readonly static object _locked = new();
    
    private static  T _instance ;

    public static T Instance
    {
        get
        {
            lock (_locked)
            {
                if (_instance == null)
                    _instance = new T();
                return _instance;
            }
        }
    }
}

