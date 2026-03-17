using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Lua 目录自动同步配置
/// 存储「扫描目录 → LuaScriptContainer」的映射规则，供 LuaDirectoryScanner 读取
/// </summary>
[CreateAssetMenu(menuName = "XLua/Lua Auto Sync Config")]
public class LuaAutoSyncConfig : ScriptableObject
{
    [System.Serializable]
    public class DirectoryMapping
    {
        [Tooltip("扫描目录（相对 Assets/）")]
        public string directoryPath;

        [Tooltip("对应的 Container SO（已有时直接引用）")]
        public LuaScriptContainer container;

        [Tooltip("Container SO 生成目录（container 为空时，在此目录下自动创建）")]
        public string outputDirectory;

        [Tooltip("是否递归扫描子目录")]
        public bool recursive = false;
    }

    public List<DirectoryMapping> mappings = new();
}
