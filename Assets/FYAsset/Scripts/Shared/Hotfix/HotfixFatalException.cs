using System;

/// <summary>
/// 表示热更流程报告 OnError 后必须终止启动。
/// </summary>
public sealed class HotfixFatalException : Exception
{
    public HotfixFatalException(string message) : base(message)
    {
    }

    public HotfixFatalException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
