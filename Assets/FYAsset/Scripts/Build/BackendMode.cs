/// <summary>
/// 构建管线后端模式 —— 决定 AssetBundle 构建的数据源和 manifest 格式。
/// 实际来源为 FYAssetSettings.Instance.UseABBackend，CLI --backend 可局部覆盖。
/// Backend 选择由 BuildProjectManager 在创建 BuildPackageRequest 前确定。
/// </summary>
public enum BackendMode
{
    /// <summary>基于 AAManifest 的 AA 后端</summary>
    AA = 0,

    /// <summary>基于 ABManifest 的新版后端，后续 Task 默认使用此模式</summary>
    ABManifest = 1
}

public static class BackendModeNames
{
    public const string AA = "AA";
    public const string AB = "AB";

    public static string FromBackendMode(BackendMode mode)
    {
        return mode == BackendMode.ABManifest ? AB : AA;
    }

    public static string FromSettings()
    {
        return FYAssetSettings.Instance.UseABBackend ? AB : AA;
    }

    public static bool IsValid(string value)
    {
        return string.Equals(value, AA, System.StringComparison.OrdinalIgnoreCase)
               || string.Equals(value, AB, System.StringComparison.OrdinalIgnoreCase);
    }

    public static bool MatchesCurrentSettings(string value)
    {
        return string.Equals(value, FromSettings(), System.StringComparison.OrdinalIgnoreCase);
    }
}
