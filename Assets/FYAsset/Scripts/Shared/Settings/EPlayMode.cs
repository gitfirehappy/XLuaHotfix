/// <summary>
/// AB 在 Editor 下的资源加载模式。
/// 真机 / 非 Editor 永远走 Runtime 路径，忽略此枚举。
/// </summary>
public enum EPlayMode
{
    /// <summary>AssetDatabase 直读，日常开发最快迭代。</summary>
    Editor = 0,

    /// <summary>预留：虚拟 Manifest + AssetDatabase。本期未实现，等同 Runtime。</summary>
    Simulate = 1,

    /// <summary>真实 ABManifest + AssetBundle 加载。</summary>
    Runtime = 2
}
