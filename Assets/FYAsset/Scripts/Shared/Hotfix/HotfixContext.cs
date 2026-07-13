/// <summary>
/// 热更流程所需的上下文参数。
/// 由 HotfixManager 构建并传递给后端方法。
/// </summary>
public class HotfixContext
{
    /// <summary>构建索引数据，包含包名到路径的映射</summary>
    public BuildIndexData BuildIndex;

    /// <summary>应用本地热更指针前，由 BuildIndex 指定的内置包名。</summary>
    public string BaselinePackageName;

    /// <summary>最近一次成功激活的本地指针；不存在时为空。</summary>
    public PackageIndex LocalPackageIndex;

    /// <summary>针对 LocalPackageIndex 的精确本地包检查结果。</summary>
    public HotfixPackageInspection LocalPackageInspection;

    /// <summary>已下载的远端包指针，仅在激活和 runtime manager 初始化成功后持久化。</summary>
    public PackageIndex RemotePackageIndex;

    /// <summary>目标包名（如 "hotfix"）</summary>
    public string TargetPackageName;

    /// <summary>远端 URL 根路径</summary>
    public string RemoteUrlRoot;

    /// <summary>下载目标目录（GUID 命名）</summary>
    public string TargetGUIDRoot;
}
