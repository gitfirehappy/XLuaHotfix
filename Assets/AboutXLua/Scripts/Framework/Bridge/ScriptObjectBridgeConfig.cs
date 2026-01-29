using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "ScriptObjectBridgeConfig", menuName = "XLua/Bridge/SOBridgeConfig")]
public class ScriptObjectBridgeConfig : ScriptableObject
{
    [Serializable]
    public class SOEntry
    {
        public string key;
        public ScriptableObject so;
    }

    public SOEntry[] entries;

    public ScriptableObject GetSO(string key)
    {
        foreach (var e in entries)
        {
            if (e.key == key)
                return e.so;
        }
        Debug.LogWarning($"[ScriptObjectBridgeConfig] SO with key '{key}' not found.");
        return null;
    }
}
