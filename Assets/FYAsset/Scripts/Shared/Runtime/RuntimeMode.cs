/// <summary>
/// Player 运行模式：本次启动是否联网检查热更。
/// </summary>
/// <remarks>
/// 唯一的运行时事实来源是 BuildIndex.RuntimeMode；它由构建导出按构建类型写入，
/// 运行时不得回读设置资产或从包目录推测 Online 与 Standalone。
/// </remarks>
public enum RuntimeMode
{
    /// <summary>联网包：启动后读取远端 PackageIndex，并按同 Major 前向规则准备目标。</summary>
    Online = 0,

    /// <summary>单机离线包：只使用内置完整包，不发起任何远端检查。</summary>
    Standalone = 1
}
