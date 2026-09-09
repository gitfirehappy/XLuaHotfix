/// <summary>
/// 一次热更流程的身份、检查结果和目标路径，由 HotfixFlowBase 填充并传给后端。
/// </summary>
public class HotfixContext
{
    /// <summary>安装包内置基线的版本、身份、平台和后端数据。</summary>
    public BuildIndexData BuildIndex;

    /// <summary>应用本地热更指针前，由 BuildIndex 指定的内置包名。</summary>
    public string BaselinePackageName;

    /// <summary>由 BuildIndex 构造的内置整包身份。</summary>
    public PackageIndex BaselinePackageIndex;

    /// <summary>内置整包的严格检查结果。</summary>
    public HotfixPackageInspection BaselinePackageInspection;

    /// <summary>有效本地身份；本地指针不可用时为内置整包身份。</summary>
    public PackageIndex LocalPackageIndex;

    /// <summary>针对 LocalPackageIndex 的精确本地包检查结果。</summary>
    public HotfixPackageInspection LocalPackageInspection;

    /// <summary>本地内容根；整包使用 StreamingAssets，热更包使用 HotfixRoot 子目录。</summary>
    public string LocalPackageRoot;

    /// <summary>当前有效本地身份是否为内置整包。</summary>
    public bool LocalIsBaseline;

    /// <summary>已下载的远端包指针，仅在激活和 runtime manager 初始化成功后持久化。</summary>
    public PackageIndex RemotePackageIndex;

    /// <summary>目标包目录名（Build_*）。</summary>
    public string TargetPackageName;

    /// <summary>远端 URL 根路径</summary>
    public string RemoteUrlRoot;

    /// <summary>下载目标目录（GUID 命名）</summary>
    public string TargetGUIDRoot;
}
