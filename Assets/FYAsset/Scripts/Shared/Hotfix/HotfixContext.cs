/// <summary>
/// 一次热更流程的身份、当前内容、检查结果与目标路径，由 HotfixFlowBase 填充并传给后端。
/// </summary>
/// <remarks>
/// 启动流程与运行中 Check / Prepare / Apply 共用同一份上下文；
/// 激活成功之前 CurrentPackageRoot / TargetGUIDRoot 都只是候选路径，不代表已切换包根；
/// TargetGUIDRoot 固定为隔离 staging 根，正式 Build_* 根只在 Apply 换入阶段由 HotfixFlowBase 写入。
/// </remarks>
public class HotfixContext
{
    /// <summary>安装包内置包的身份、版本、平台和后端数据。</summary>
    public BuildIndexData BuildIndex;

    /// <summary>本次启动的运行模式，取自 BuildIndex.RuntimeMode。</summary>
    public RuntimeMode RuntimeMode;

    /// <summary>内置完整包根；Standalone 时为 StreamingAssets 隔离子目录。</summary>
    public string BuiltInPackageRoot;

    /// <summary>由 BuildIndex 构造的内置包身份。</summary>
    public PackageIndex BuiltInPackageIndex;

    /// <summary>内置完整包的严格检查结果；不完整时启动阻断。</summary>
    public HotfixPackageInspection BuiltInPackageInspection;

    /// <summary>当前候选内容的包根。</summary>
    public string CurrentPackageRoot;

    /// <summary>当前候选内容的身份；本地指针不可信时为内置包身份。</summary>
    public PackageIndex CurrentPackageIndex;

    /// <summary>针对 CurrentPackageIndex 的精确检查结果。</summary>
    public HotfixPackageInspection CurrentPackageInspection;

    /// <summary>本地 PackageIndex 是否可信；不可信时下一次决策需要补写指针。</summary>
    public bool CurrentPointerTrusted;

    /// <summary>已下载的远端包指针，仅在激活和资源管理器初始化成功后持久化。</summary>
    public PackageIndex RemotePackageIndex;

    /// <summary>目标包目录名（Build_*）。</summary>
    public string TargetPackageName;

    /// <summary>远端 URL 根路径。</summary>
    public string RemoteUrlRoot;

    /// <summary>本次目标包的隔离写入根（staging）：准备阶段的所有复制、下载与元数据写入都只发生在这里。</summary>
    public string TargetGUIDRoot;

    /// <summary>目标包是否已在隔离目录内准备并通过精确校验。</summary>
    public bool TargetPrepared;

    /// <summary>换入阶段是否已把正式包目录 move 到 backup；失败补偿与成功清理都依赖该事实。</summary>
    public bool TargetRootBackedUp;

    /// <summary>换入阶段是否已把 staging move 到正式包根；失败补偿依赖该事实把未激活内容退回 staging。</summary>
    public bool TargetRootSwapped;

    /// <summary>本次失败目标的 staging 诊断物路径；为空表示本次没有失败目标，旧包回收不需要跳过隔离目录。</summary>
    public string TargetDiagnosticRoot;

    /// <summary>当前选定的高层动作，供运行中 Prepare / Apply 判断是否还有待执行目标。</summary>
    public HotfixStateAction PendingAction;
}
