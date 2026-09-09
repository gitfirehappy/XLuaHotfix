/// <summary>
/// Editor 中 AB 资源的加载方式。真机与发布包始终按 Runtime 加载，不读取该枚举。
/// </summary>
public enum EPlayMode
{
    /// <summary>从 Collector 配置构建内存索引，经 AssetDatabase 直接加载，不构建 AssetBundle。</summary>
    Editor = 0,

    /// <summary>当前与 Runtime 行为一致：读 ABManifest 并经 AssetBundle 加载。</summary>
    Simulate = 1,

    /// <summary>读 ABManifest 并经 AssetBundle 加载，与真机一致。</summary>
    Runtime = 2
}
