using System;

/// <summary>
/// PipelinePanel 的构建行为注入包 —— 由各后端窗口注入自己 manager 的方法。
/// BuildStandalone 可选：未注入时面板不提供 Standalone 构建选项。
/// </summary>
public sealed class BuildPanelActions
{
    public Action<BuildExecutionOptions> BuildFull;
    public Action<BuildExecutionOptions> BuildHotfix;
    public Action<BuildExecutionOptions> BuildStandalone;

    /// <summary>查询上一次构建是否成功（绑定到对应后端 manager）。</summary>
    public Func<bool> LastBuildSuccess;
}
