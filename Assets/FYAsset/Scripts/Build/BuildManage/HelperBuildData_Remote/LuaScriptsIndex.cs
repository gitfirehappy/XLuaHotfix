using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Lua 脚本索引
/// 用于记录：某个 LuaScriptContainer (AddressableKey) 包含哪些 Lua 脚本 (NormalizedName)
/// </summary>
public class LuaScriptsIndex : ScriptableObject
{
    [Serializable]
    public class ContainerEntry
    {
        public string containerAddress; // AA Key
        public List<string> scriptNames; // 包含的脚本名
    }

    public List<ContainerEntry> data = new();
    
    // 运行时快速查找字典
    public Dictionary<string, List<string>> ContainerToScripts { get; private set;}
    public Dictionary<string, string> ScriptToContainer { get; private set;}

    /// <summary>
    /// 构建运行时快速查找字典
    /// </summary>
    public void BuildRuntimeDics()
    {
        ScriptToContainer = new Dictionary<string, string>();
        ContainerToScripts = new Dictionary<string, List<string>>();
        
        foreach (var entry in data)
        { 
            ContainerToScripts[entry.containerAddress] = entry.scriptNames;

            foreach (var scriptName in entry.scriptNames)
            {
                if (!ScriptToContainer.ContainsKey(scriptName))
                {
                    ScriptToContainer[scriptName] = entry.containerAddress;
                }
                else
                {
                    Debug.LogWarning($"[LuaScriptsIndex] 脚本名冲突: {scriptName} 同时存在于 {ScriptToContainer[scriptName]} 和 {entry.containerAddress}");
                }
            }
        }
    }
}
