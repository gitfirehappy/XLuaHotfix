using System;

/// <summary>
/// 一次构建运行的请求：Runner 只把它透传给运行环境，不解释后端包结构，也不认识管线配置。
/// </summary>
/// <remarks>
/// Package 承载后端的 BuildPipelineRequest（含输出目录、attempt 布局与交付出口），
/// 由 <see cref="IBuildRunEnvironment"/> 实现方读取并写入 Context；Runner 自身不读它，
/// 这样 Runner 才能留在不依赖 Unity 的纯逻辑里。
/// Options 只用于向编辑器 UI 回传单个 Task 的状态事件，可以为 null。
/// </remarks>
public sealed class BuildPipelineRequest
{
    /// <summary>后端包请求（Editor 侧为 BuildRequest）</summary>
    public readonly BuildRequest Package;

    /// <summary>执行过程状态回传；为 null 表示不需要 UI 状态</summary>
    public readonly BuildExecutionOptions Options;

    /// <summary>本次运行的编辑器环境；为 null 表示无 Context 准备、无 attempt 提升</summary>
    public readonly IBuildRunEnvironment Environment;

    public BuildPipelineRequest(BuildRequest package, BuildExecutionOptions options = null, IBuildRunEnvironment environment = null)
    {
        Package = package ?? throw new ArgumentNullException(nameof(package));
        Options = options;
        Environment = environment;
    }
}
