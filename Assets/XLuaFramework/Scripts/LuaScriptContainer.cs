using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "LuaContainer", menuName = "XLua/Lua Script Container", order = 2)]
public class LuaScriptContainer : ScriptableObject
{
    [Tooltip("LuaScriptContainer 所包含的Lua文件")]
    public List<TextAsset> luaAssets = new();

    [ContextMenu("清空列表")]
    public void ClearList()
    {
        luaAssets.Clear();
#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(this);
#endif
    }
}
