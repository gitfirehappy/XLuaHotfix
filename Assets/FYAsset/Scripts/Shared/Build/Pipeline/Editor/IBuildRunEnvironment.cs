using System;

/// <summary>
/// Runner 依赖的编辑器运行环境。环境只负责把请求投影到 Context；输出交付由 BuildProjectRunner 负责。
/// </summary>
public interface IBuildRunEnvironment
{
    void PrepareContext(BuildRunContext context, BuildRequest request);
}
