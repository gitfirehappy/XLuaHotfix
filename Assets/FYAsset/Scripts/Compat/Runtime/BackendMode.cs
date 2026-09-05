/// <summary>
/// 宿主选择的运行时资源后端。
/// </summary>
public enum BackendMode
{
    Unspecified = 0,
    AA = 1,
    ABManifest = 2
}

public static class BackendModeNames
{
    public const string AA = "AA";
    public const string AB = "AB";

    public static string FromBackendMode(BackendMode mode)
    {
        if (mode == BackendMode.AA)
            return AA;
        if (mode == BackendMode.ABManifest)
            return AB;
        throw new System.ArgumentOutOfRangeException(nameof(mode), mode, "Unsupported backend mode.");
    }

    public static bool IsValid(BackendMode mode)
    {
        return mode == BackendMode.AA || mode == BackendMode.ABManifest;
    }

    public static bool IsValid(string value)
    {
        return string.Equals(value, AA, System.StringComparison.OrdinalIgnoreCase)
               || string.Equals(value, AB, System.StringComparison.OrdinalIgnoreCase);
    }
}
