using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ScriptableObject，用于承载一个 GameObject 上需要加载的 Lua 脚本列表
/// </summary>
[CreateAssetMenu(fileName = "LuaBehaviourConfig", menuName = "XLua/Bridge/Behaviour Config SO")]
public class LuaBehaviourConfigSO : ScriptableObject
{
    [Tooltip("此配置将加载的所有 Lua 脚本列表")]
    public List<LuaScriptConfig> scriptsToLoad = new();
}