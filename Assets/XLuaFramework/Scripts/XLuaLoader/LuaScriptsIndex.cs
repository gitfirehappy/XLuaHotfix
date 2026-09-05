using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Lua 脚本索引
/// 用于记录：某个 LuaScriptContainer Address 包含哪些 Lua 脚本 (NormalizedName)
/// </summary>
public class LuaScriptsIndex : ScriptableObject
{
    public const string AssetAddress = "LuaScriptsIndex";
    public const string EditorAssetPath = "Assets/Build/LuaScriptsIndex.asset";

    [Serializable]
    public class ContainerEntry
    {
        public string containerAddress;
        public List<string> scriptNames; // 包含的脚本名
    }

    public List<ContainerEntry> data = new();

    // 运行时快速查找字典
    public Dictionary<string, List<string>> ContainerToScripts { get; private set; }
    public Dictionary<string, string> ScriptToContainer { get; private set; }

    /// <summary>
    /// 构建运行时快速查找字典
    /// </summary>
    public void BuildRuntimeDics()
    {
        if (data == null || data.Count == 0)
            throw new InvalidOperationException("LuaScriptsIndex 不包含 ContainerEntry。");

        ScriptToContainer = new Dictionary<string, string>(StringComparer.Ordinal);
        ContainerToScripts = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        for (int i = 0; i < data.Count; i++)
        {
            ContainerEntry entry = data[i];
            if (entry == null)
                throw new InvalidOperationException($"LuaScriptsIndex ContainerEntry[{i}] 为空。");
            if (string.IsNullOrWhiteSpace(entry.containerAddress))
                throw new InvalidOperationException($"LuaScriptsIndex ContainerEntry[{i}] Address 为空。");
            if (entry.scriptNames == null)
                throw new InvalidOperationException(
                    $"LuaScriptsIndex ContainerEntry[{i}] scripts 为空: {entry.containerAddress}");
            if (ContainerToScripts.ContainsKey(entry.containerAddress))
                throw new InvalidOperationException(
                    $"LuaScriptsIndex Container Address 重复: {entry.containerAddress}");

            ContainerToScripts[entry.containerAddress] = entry.scriptNames;

            for (int s = 0; s < entry.scriptNames.Count; s++)
            {
                string scriptName = entry.scriptNames[s];
                if (string.IsNullOrWhiteSpace(scriptName))
                    throw new InvalidOperationException(
                        $"LuaScriptsIndex 脚本名为空: Container={entry.containerAddress}, Index={s}");
                if (ScriptToContainer.TryGetValue(scriptName, out string existingAddress))
                    throw new InvalidOperationException(
                        $"LuaScriptsIndex 脚本名重复: {scriptName}, Containers={existingAddress}/{entry.containerAddress}");

                ScriptToContainer[scriptName] = entry.containerAddress;
            }
        }

        if (ScriptToContainer.Count == 0)
            throw new InvalidOperationException("LuaScriptsIndex 不包含任何 Lua 模块。");
    }
}
