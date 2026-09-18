using System;

/// <summary>Unity 系统来源记录；只用于诊断，不属于 AB 可构建资源。</summary>
[Serializable]
public sealed class CollectionSystemSource
{
    public static readonly CollectionSystemSource Resources = new CollectionSystemSource(
        "Resources",
        "Assets/Resources",
        "Unity Player Resources 目录，由 Player/Resources 运行时负责，不进入 AB。");

    public string Name { get; }
    public string RootPath { get; }
    public string Reason { get; }
    public bool ReadOnly => true;

    private CollectionSystemSource(string name, string rootPath, string reason)
    {
        Name = name;
        RootPath = rootPath;
        Reason = reason;
    }
}
