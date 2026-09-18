using System;

/// <summary>构建面板所需的构建动作。</summary>
public sealed class BuildPanelActions
{
    public Func<BuildExecutionOptions, BuildResult> BuildFull;
    public Func<BuildExecutionOptions, BuildResult> BuildHotfix;
    public Func<BuildExecutionOptions, BuildResult> BuildStandalone;
}
