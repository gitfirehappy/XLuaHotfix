/// <summary>
/// AA 构建管线专属的 BuildContext 键名。
/// 中性键名统一在 Shared 的 BuildContextKeys；本类只放 AA 私有契约键。
/// </summary>
public static class AABuildContextKeys
{
    public const string AAManifest = "AAManifest";

    /// <summary>本次构建的 Addressables 源快照（List&lt;FileHelper.FileDigest&gt;，名称为 Asset GUID）；由 PrepareAAInputTask 写入</summary>
    public const string AASourceScan = "AASourceScan";

}
