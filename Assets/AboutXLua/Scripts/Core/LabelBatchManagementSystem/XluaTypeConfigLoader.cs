using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

/// <summary>
/// XLua配置标签加载器（Addressables实现）
/// </summary>
public static class XluaTypeConfigLoader
{
    // 地址标签，用于在Addressables中识别TypeListSO资产
    private const string _defaultConfigLabel = Constants.DEAULT_XLUA_TYPE_CONFIG_LOAD_LABEL;
    
    // 类型级配置
    public static List<Type> HotfixTypes { get; private set; }
    public static List<Type> LuaCallCSharpTypes { get; private set; }
    public static List<Type> CSharpCallLuaTypes { get; private set; }
    
    // 成员级配置
    public static List<MemberInfo> HotfixMembers { get; private set; }
    public static List<MemberInfo> LuaCallCSharpMembers { get; private set; }
    public static List<MemberInfo> CSharpCallLuaMembers { get; private set; }
    

    /// <summary>
    /// 初始化加载配置（核心层启动时调用）
    /// </summary>
    /// /// <param name="configLabel">Addressables标签名，默认为"XLuaConfigs"</param>
    public static async Task InitAsync(string configLabel = _defaultConfigLabel)
    {
        Debug.Log("[XluaTypeConfigLoader] 初始化配置中...");

        // 1. 初始化列表
        HotfixTypes = new List<Type>();
        LuaCallCSharpTypes = new List<Type>();
        CSharpCallLuaTypes = new List<Type>();
        HotfixMembers = new List<MemberInfo>();
        LuaCallCSharpMembers = new List<MemberInfo>();
        CSharpCallLuaMembers = new List<MemberInfo>();

        // 2. 使用Addressables加载所有 TypeListSO
        IList<TypeMemberListSO> allConfigs = await AssetPackageManager.Instance.LoadAssetByLabelAsync<TypeMemberListSO>(configLabel);

        if (allConfigs == null || allConfigs.Count == 0)
        {
            Debug.LogWarning($"没有找到带有 '{configLabel} 标签的 TypeListSO 资源'. 请确保它们是可寻址的，并且标记正确");
            return;
        }

        // 3. 遍历所有配置，并根据Tag分类
        foreach (var config in allConfigs)
        {
            if (config == null)
            {
                Debug.LogWarning("加载过程中发现 TypeListSO 资源为空。");
                continue;
            }

            // 解析配置项
            List<Type> resolvedTypes = new List<Type>();
            List<MemberInfo> resolvedMembers = new List<MemberInfo>();
    
            foreach (var configItem in config.configurations)
            {
                var type = configItem.typeRef?.GetTypeCache();
                if (type == null) continue;

                if (configItem.isEntireType)
                {
                    // 整个类型
                    resolvedTypes.Add(type);
                }
                else
                {
                    // 特定成员
                    var member = configItem.memberRef?.GetMemberCache();
                    if (member != null)
                    {
                        resolvedMembers.Add(member);
                    }
                }
            }

            // 按标签分类处理
            switch (config.tag)
            {
                case TypeMemberListSO.ConfigTag.Hotfix:
                    HotfixTypes.AddRange(resolvedTypes);
                    HotfixMembers.AddRange(resolvedMembers);
                    Debug.Log($"Loaded {resolvedTypes.Count} Hotfix types and {resolvedMembers.Count} members from '{config.name}'.");
                    break;
                case TypeMemberListSO.ConfigTag.LuaCallCSharp:
                    LuaCallCSharpTypes.AddRange(resolvedTypes);
                    LuaCallCSharpMembers.AddRange(resolvedMembers);
                    Debug.Log($"Loaded {resolvedTypes.Count} LuaCallCSharp types and {resolvedMembers.Count} members from '{config.name}'.");
                    break;
                case TypeMemberListSO.ConfigTag.CSharpCallLua:
                    CSharpCallLuaTypes.AddRange(resolvedTypes);
                    CSharpCallLuaMembers.AddRange(resolvedMembers);
                    Debug.Log($"Loaded {resolvedTypes.Count} CSharpCallLua types and {resolvedMembers.Count} members from '{config.name}'.");
                    break;
                default:
                    Debug.LogWarning($"未知的 ConfigTag '{config.tag}' in '{config.name}'.");
                    break;
            }
        }

        Debug.Log(
            $"[XluaTypeConfigLoader] 初始化配置完成. " +
            $"Hotfix: {HotfixTypes.Count} types, {HotfixMembers.Count} members; " +
            $"LuaCallCSharp: {LuaCallCSharpTypes.Count} types, {LuaCallCSharpMembers.Count} members; " +
            $"CSharpCallLua: {CSharpCallLuaTypes.Count} types, {CSharpCallLuaMembers.Count} members.");
    }
    
    /// <summary>
    /// 清理缓存列表
    /// </summary>
    public static void ClearCache()
    {
        HotfixTypes?.Clear();
        LuaCallCSharpTypes?.Clear();
        CSharpCallLuaTypes?.Clear();
        HotfixMembers?.Clear();
        LuaCallCSharpMembers?.Clear();
        CSharpCallLuaMembers?.Clear();
    }

    // 后续在XLua初始化代码中，可以这样使用:
    // private static List<Type> LuaCallCSharp = XluaTypeConfigLoader.LuaCallCSharpTypes;
    // private static List<Type> CSharpCallLua = XluaTypeConfigLoader.CSharpCallLuaTypes;
    // private static List<Type> Hotfix = XluaTypeConfigLoader.HotfixTypes;
}