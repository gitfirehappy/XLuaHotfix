/// <summary>
/// 一次启动实际使用的内容归属。
/// </summary>
/// <remarks>
/// 取值对应计划 T8 的内容来源：BuiltIn 是 StreamingAssets 内置完整包，
/// Local 是 Persistent/Hotfix/Build_xxx 已激活完整包，RemoteTarget 是远端 PackageIndex 指向的待准备包；
/// Blocked 表示没有可用完整包、启动被阻断。
/// 一次运行时上下文只读取其中一个包根，由激活流程显式写入 RuntimePathManager.ActivePackageRoot。
/// </remarks>
public enum HotfixContentState
{
    /// <summary>内置完整包：安装包内 StreamingAssets（Standalone 时为隔离子目录）的完整包。</summary>
    BuiltIn = 0,

    /// <summary>本地热更包：持久化目录内已激活的完整包。</summary>
    Local = 1,

    /// <summary>远端目标包：正在隔离目录中准备、尚未激活的包。</summary>
    RemoteTarget = 2,

    /// <summary>阻断：没有可继续使用的完整包。</summary>
    Blocked = 3
}
