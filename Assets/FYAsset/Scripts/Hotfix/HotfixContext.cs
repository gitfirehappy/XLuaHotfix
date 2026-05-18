/// <summary>
/// 热更流程所需的上下文参数。
/// 由 HotfixManager 构建并传递给后端方法。
/// </summary>
public class HotfixContext
{
    /// <summary>构建索引数据，包含包名到路径的映射</summary>
    public BuildIndexData BuildIndex;

    /// <summary>目标包名（如 "hotfix"）</summary>
    public string TargetPackageName;

    /// <summary>远端 URL 根路径</summary>
    public string RemoteUrlRoot;

    /// <summary>下载目标目录（GUID 命名）</summary>
    public string TargetGUIDRoot;
}
