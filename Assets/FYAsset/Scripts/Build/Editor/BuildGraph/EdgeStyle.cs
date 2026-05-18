/// <summary>BuildGraph 边类型——区分代码依赖、SO 配置依赖和数据流。</summary>
public enum EdgeStyle
{
    CodeDependency,
    SODependency,
    DataFlow,
}

/// <summary>EdgeStyle 便捷别名，供初始化器使用。</summary>
public static class EdgeStyles
{
    public static readonly EdgeStyle CodeDependency = EdgeStyle.CodeDependency;
    public static readonly EdgeStyle SODependency = EdgeStyle.SODependency;
    public static readonly EdgeStyle DataFlow = EdgeStyle.DataFlow;
}
