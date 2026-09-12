using System;

/// <summary>
/// 管线前置失败：Composer 组装失败，或运行环境初始化（平台/输出等解析）失败。
/// Code 取 <see cref="BuildErrorCodes"/> 常量，Message 直接面向使用者说明哪一条输入不合法。
/// </summary>
/// <remarks>
/// 一律致命：不存在“跳过非法条目继续构建”的降级路径，
/// 否则配置或环境错误会被静默吞掉，构建结果与声明不一致。
/// 调用方（AA/AB 后端与编辑器面板）按约定把本异常显示为红色文本。
/// </remarks>
public sealed class BuildPipelineException : Exception
{
    /// <summary>错误码，取自 BuildErrorCodes</summary>
    public string Code { get; }

    public BuildPipelineException(string code, string message)
        : base($"[{code}] {message}")
    {
        Code = code ?? string.Empty;
    }
}
