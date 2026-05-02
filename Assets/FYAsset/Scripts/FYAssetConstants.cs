/// <summary>
/// FYAsset 模块全局常量 —— 项目基础配置、管线路径、规则类名。
/// 跨模块引用的顶层常量集中管理于此；职责单一的子常量类（错误码、Context Key）独立文件。
/// </summary>
public static class FYAssetConstants
{
    #region 项目/全局开关

    /// <summary>项目名</summary>
    public const string PROJECTNAME = "ProjectName";

    /// <summary>服务器 URL</summary>
    public const string HOTFIX_URL = "https://firehappy-cfy.com/";

    /// <summary>
    /// 资源运行时全局后端开关。
    /// true  = ABManifest + AB backend 全链路；
    /// false = Legacy Addressables 全链路。
    /// </summary>
    public const bool USE_AB_BACKEND = false;

    #endregion

    #region 旧管线 — 文件路径

    /// <summary>AA 条目配置文件保存路径</summary>
    public const string AA_LABELS_CONFIG_ASSETPATH = "Assets/Build/HelperBuildData/AddressableLabelsConfig.asset";

    /// <summary>Lua 脚本索引文件保存路径</summary>
    public const string LUA_SCRIPTS_INDEX_ASSETPATH = "Assets/Build/HelperBuildData/LuaScriptsIndex.asset";

    /// <summary>快照信息保存路径</summary>
    public const string SNAPSHOT_ASSET_PATH = "Assets/Build/Snapshots.asset";

    /// <summary>BuildIndex Json 文件的编辑器存储路径（仅用于查看）</summary>
    public const string BUILD_INDEX_JSON_PROJECT_PATH = "Assets/Build/LocalStaticData/BuildIndex.json";

    #endregion

    #region 旧管线 — 标识符

    /// <summary>导出的 AA 配置条目的 Key</summary>
    public const string AA_LABELS_CONFIG = "AddressableLabelsConfig";

    /// <summary>远端辅助构建数据组名</summary>
    public const string HELPER_BUILD_DATA_GROUP_NAME = "HelperBuildData";

    /// <summary>Lua 脚本索引的 Key</summary>
    public const string LUA_SCRIPTS_INDEX = "LuaScriptsIndex";

    /// <summary>默认的 XLua 三属性配置的 AA 标签</summary>
    public const string DEAULT_XLUA_TYPE_CONFIG_LOAD_LABEL = "XLuaConfigs";

    /// <summary>热更 AA Group 名</summary>
    public const string HOTFIX_GROUP_NAME = "HotfixGroup";

    /// <summary>StreamingAssets 中 BuildIndex.json 的文件名</summary>
    public const string BUILD_INDEX_FILENAME = "BuildIndex.json";

    #endregion

    #region 新管线 — 文件路径

    /// <summary>BuildPipelineWindow 编辑器菜单路径</summary>
    public const string BUILD_PIPELINE_WINDOW_MENU_PATH = "XLua/Build Pipeline";

    /// <summary>二进制序列化器生成代码输出路径</summary>
    public const string BINARY_SERIALIZER_GENERATE_PATH = "Assets/Tools/Scripts/Serialization/Generated";

    /// <summary>CollectorSetting 配置文件保存路径</summary>
    public const string COLLECTOR_SETTING_ASSET_PATH = "Assets/Build/CollectorSetting.asset";

    /// <summary>BuildPipelineConfig SO 存储路径</summary>
    public const string PIPELINE_CONFIG_ASSET_PATH = "Assets/Build/BuildPipelineConfig.asset";

    #endregion

    #region 新管线 — 文件命名

    /// <summary>ABManifest JSON 格式文件名</summary>
    public const string MANIFEST_FILE_NAME = "ABManifest.json";

    /// <summary>ABManifest 二进制格式文件名</summary>
    public const string MANIFEST_FILE_NAME_BIN = "ABManifest.bin";

    #endregion

    #region Collector Rules

    /// <summary>地址规则：使用文件名（不含扩展名）作为 Address</summary>
    public const string RULE_ADDRESS_BY_FILE_NAME = "AddressByFileName";

    /// <summary>过滤规则：收集所有有效资源（排除 .meta / .cs / .dll 等）</summary>
    public const string RULE_COLLECT_ALL = "CollectAll";

    /// <summary>打包规则：同一 Collector 下的所有资源打入同一 Bundle</summary>
    public const string RULE_PACK_BY_COLLECT_PATH = "PackByCollectPath";

    /// <summary>打包规则：每个资源单独打入一个 Bundle</summary>
    public const string RULE_PACK_SEPARATELY = "PackSeparately";

    /// <summary>打包规则：同一目录下的资源打入同一 Bundle</summary>
    public const string RULE_PACK_BY_DIRECTORY = "PackByDirectory";

    /// <summary>打包规则：相同 Labels 的资源打入同一 Bundle（Labels = Group.Labels ∪ Collector.Labels）</summary>
    public const string RULE_PACK_BY_LABEL = "PackByLabel";

    /// <summary>分组规则：所有资源归属到 Collector 的父 Group（默认）</summary>
    public const string RULE_GROUP_ALL = "GroupAll";

    /// <summary>分组规则：按主类型名称分组</summary>
    public const string RULE_GROUP_BY_TYPE = "GroupByType";

    /// <summary>分组规则：按标签分组</summary>
    public const string RULE_GROUP_BY_LABEL = "GroupByLabel";

    /// <summary>分组规则：按子目录分组</summary>
    public const string RULE_GROUP_BY_DIRECTORY = "GroupByDirectory";

    #endregion
}
