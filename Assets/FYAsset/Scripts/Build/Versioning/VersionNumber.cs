using System;

/// <summary>
/// 版本号数据类型，整个项目统一使用。
/// Release version string format: Major.Minor.Patch[-Channel].
/// Build is stored as a separate field and must not be appended to version strings.
/// </summary>
[Serializable]
[BinarySerializable]
public class VersionNumber : IComparable<VersionNumber>
{
    [BinaryField(0)] public int Major;
    [BinaryField(1)] public int Minor;
    [BinaryField(2)] public int Patch;
    [BinaryField(3)] public int Build;
    [BinaryField(4)] public string Channel;

    public string GetVersionString() => $"{Major}.{Minor}.{Patch}";

    public string GetReleaseVersionString()
    {
        var core = $"{Major}.{Minor}.{Patch}";
        if (!string.IsNullOrEmpty(Channel))
            core += $"-{Channel}";
        return core;
    }

    public override string ToString() => GetReleaseVersionString();

    public bool RequiresForceUpdate(VersionNumber baseline)
    {
        if (baseline == null) return true;
        return Major != baseline.Major;
    }

    #region ChannelRank

    private static int GetChannelRank(string channel)
    {
        if (string.IsNullOrEmpty(channel)) return 3;
        return channel.ToLowerInvariant() switch
        {
            "alpha" => 0,
            "beta"  => 1,
            "rc"    => 2,
            _       => throw new ArgumentException($"未知 Channel: '{channel}'。有效值: alpha, beta, rc, \"\"。")
        };
    }

    #endregion

    #region IComparable

    public int CompareTo(VersionNumber other)
    {
        if (other == null) return 1;
        int cmp = Major.CompareTo(other.Major);
        if (cmp != 0) return cmp;
        cmp = Minor.CompareTo(other.Minor);
        if (cmp != 0) return cmp;
        cmp = Patch.CompareTo(other.Patch);
        if (cmp != 0) return cmp;
        return GetChannelRank(Channel).CompareTo(GetChannelRank(other.Channel));
    }

    public static bool operator >(VersionNumber a, VersionNumber b) =>
        a != null && b != null && a.CompareTo(b) > 0;
    public static bool operator <(VersionNumber a, VersionNumber b) =>
        a != null && b != null && a.CompareTo(b) < 0;
    public static bool operator >=(VersionNumber a, VersionNumber b) =>
        a != null && b != null && a.CompareTo(b) >= 0;
    public static bool operator <=(VersionNumber a, VersionNumber b) =>
        a != null && b != null && a.CompareTo(b) <= 0;

    #endregion

    #region Equality

    public override bool Equals(object obj)
    {
        if (obj is VersionNumber other)
        {
            return Major == other.Major &&
                   Minor == other.Minor &&
                   Patch == other.Patch &&
                   string.Equals(Channel, other.Channel, StringComparison.OrdinalIgnoreCase);
        }
        return false;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Major, Minor, Patch, Channel?.ToLowerInvariant() ?? "");
    }

    public static bool operator ==(VersionNumber a, VersionNumber b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (ReferenceEquals(a, null) || ReferenceEquals(b, null)) return false;
        return a.Equals(b);
    }

    public static bool operator !=(VersionNumber a, VersionNumber b) => !(a == b);

    #endregion

    #region Parse

    /// <summary>
    /// 解析发布版本格式: X.Y.Z[-channel]
    /// </summary>
    public static VersionNumber Parse(string input)
    {
        if (TryParse(input, out var result))
            return result;
        throw new FormatException($"无效版本号字符串: '{input}'");
    }

    public static bool TryParse(string input, out VersionNumber result)
    {
        result = null;
        if (string.IsNullOrEmpty(input))
            return false;

        string remaining = input.Trim();

        if (remaining.IndexOf('+') >= 0)
            return false;

        // Parse -Channel
        string channel = "";
        int dashIdx = remaining.IndexOf('-');
        if (dashIdx >= 0)
        {
            channel = remaining.Substring(dashIdx + 1);
            remaining = remaining.Substring(0, dashIdx);
        }

        // Parse X.Y.Z
        string[] parts = remaining.Split('.');
        if (parts.Length != 3)
            return false;
        if (!int.TryParse(parts[0], out int major) ||
            !int.TryParse(parts[1], out int minor) ||
            !int.TryParse(parts[2], out int patch))
            return false;

        if (major < 0 || minor < 0 || patch < 0)
            return false;

        if (!string.IsNullOrEmpty(channel) &&
            channel != "alpha" && channel != "beta" && channel != "rc")
            return false;

        result = new VersionNumber
        {
            Major = major,
            Minor = minor,
            Patch = patch,
            Build = 0,
            Channel = channel
        };
        return true;
    }

    #endregion
}
