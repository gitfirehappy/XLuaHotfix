using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "LuaDatabase", menuName = "XLua/Lua Database", order = 1)]
public class LuaDataBase : ScriptableObject
{
    [Tooltip("所有 LuaScriptContainer 资源的引用")]
    public List<LuaScriptContainer> groups = new();

    public IEnumerable<LuaScriptContainer> AllGroups => groups;

    public LuaScriptContainer FindGroup(string name)
    {
        return groups.Find(g => g != null && g.groupName == name);
    }
}