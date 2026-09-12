/// <summary>
/// 一次启动实际使用的内容归属。
/// </summary>
/// <remarks>
/// BuiltIn、Local 和 RemoteTarget 分别表示内置包、本地已激活包和正在准备的远端目标包；
/// Blocked 表示没有可用完整包。实际读取根由激活流程写入 RuntimePathManager.ActivePackageRoot。
/// </remarks>
public enum HotfixContentState
{
    /// <summary>内置完整包：安装包内 StreamingAssets（Standalone 时为隔离子目录）的完整包。</summary>
    BuiltIn = 0,

    /// <summary>本地热更包：持久化目录内已激活的完整包。</summary>
    Local = 1,

    /// <summary>远端目标包：正在隔离目录中准备、尚未激活的完整包。</summary>
    RemoteTarget = 2,

    /// <summary>阻断：没有可继续使用的完整包。</summary>
    Blocked = 3
}
