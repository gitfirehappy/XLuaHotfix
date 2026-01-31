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
        [Tooltip("Lua中的标识")] public string luaKey;
        [Tooltip("SO的Addressable Key")] public string assetKey;
    }

    public SOEntry[] entries;
}
