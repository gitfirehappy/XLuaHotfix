using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 所有 string 常量
/// TODO: 当前字段较为分散，后续需要重构整理，或采取导表等更成熟的方案管理常量
/// </summary>
public static class Constants
{
    /// <summary> 项目名 </summary>
    public const string PROJECTNAME = "ProjectName";
    
    /// <summary> 服务器url </summary>
    public const string HOTFIX_URL = "https://firehappy-cfy.com/";
    
    // ------------------AA相关字段-----------------------//
    
    /// <summary> 导出的AA配置条目的 Key </summary>
    public const string AA_LABELS_CONFIG = "AddressableLabelsConfig";

    /// <summary> 远端辅助构建数据组名 </summary>
    public const string HELPER_BUILD_DATA_GROUP_NAME = "HelperBuildData";

    /// <summary> AA条目配置文件保存路径 </summary>
    public const string AA_LABELS_CONFIG_ASSETPATH = "Assets/Build/HelperBuildData/AddressableLabelsConfig.asset";

    /// <summary> Lua 脚本索引的 Key </summary>
    public const string LUA_SCRIPTS_INDEX = "LuaScriptsIndex";
    
    /// <summary> Lua 脚本索引文件保存路径 </summary>
    public const string LUA_SCRIPTS_INDEX_ASSETPATH = "Assets/Build/HelperBuildData/LuaScriptsIndex.asset";

    /// <summary> 默认的XLua三属性配置的AA标签 </summary>
    public const string DEAULT_XLUA_TYPE_CONFIG_LOAD_LABEL = "XLuaConfigs";

    /// <summary> 快照信息保存路径 </summary>
    public const string SNAPSHOT_ASSET_PATH = "Assets/Build/Snapshots.asset";

    /// <summary> 热更AAGroup名 </summary>
    public const string HOTFIX_GROUP_NAME = "HotfixGroup";

    /// <summary> StreamingAssets 中 BuildIndex.json 的文件名 </summary>
    public const string BUILD_INDEX_FILENAME = "BuildIndex.json";
    
    /// <summary> BuildIndex Json 文件的编辑器存储路径（仅用于查看） </summary>
    public const string BUILD_INDEX_JSON_PROJECT_PATH = "Assets/Build/LocalStaticData/BuildIndex.json";
    
    
    // ------------------重构计划涉及字段-----------------------//

    /// <summary>
    /// 资源运行时全局后端开关。
    /// true = ABManifest + AB backend 全链路；
    /// false = Legacy Addressables 全链路。
    /// </summary>
    public const bool USE_AB_BACKEND = false;

    public const string MANIFEST_FILE_NAME = "ABManifest.json";
    public const string MANIFEST_FILE_NAME_BIN = "ABManifest.bin";
    
    public const string BINARY_SERIALIZER_GENERATE_PATH = "Assets/AboutXLua/Scripts/Utility/Serialization/Generated";
}
