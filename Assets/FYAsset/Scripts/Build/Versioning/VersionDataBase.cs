using System;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 版本号存储，仅编辑器和构建时使用
/// </summary>
[CreateAssetMenu(fileName = "VersionDataBase", menuName = "Build/VersionDataBase", order = 1)]
public class VersionDataBase : ScriptableObject
{
    [Header("当前版本号")]
    public VersionNumber CurrentVersion = new() { Major = 1, Minor = 0, Patch = 0 };
    
    [Header("上次构建时间")]
    public string LastBuildTime;
    
    [Header("当日构建次数")]
    public int DailyBuildCount;
    
    public void IncrementVersion(bool isMajor = false, bool isMinor = false, string channel = "")
    {
        // 日期处理
        string today = DateTime.Now.ToString("yyyy-MM-dd");
        if (!string.IsNullOrEmpty(LastBuildTime) && LastBuildTime.StartsWith(today))
        {
            DailyBuildCount++;
        }
        else
        {
            DailyBuildCount = 1;
        }
        LastBuildTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

        // 版本号处理
        if (isMajor)
        {
            CurrentVersion.Major++;
            CurrentVersion.Minor = 0;
            CurrentVersion.Patch = 0;
        }
        else if (isMinor)
        {
            CurrentVersion.Minor++;
            CurrentVersion.Patch = 0;
        }
        else
        {
            CurrentVersion.Patch++;
        }

        CurrentVersion.Build = DailyBuildCount;
        if (!string.IsNullOrEmpty(channel) &&
            channel != "alpha" && channel != "beta" && channel != "rc")
        {
            Debug.LogError($"[VersionDataBase] Invalid channel '{channel}'. Fallback to \"\".");
            channel = "";
        }
        CurrentVersion.Channel = channel ?? "";

#if UNITY_EDITOR
        EditorUtility.SetDirty(this);
#endif
        Debug.Log($"[VersionDataBase] 版本更新至: {CurrentVersion.GetFullVersionString()}");
    }
}

/// <summary>
/// 版本号数据类型，整个项目统一使用。
/// SemVer 2.0 格式: Major.Minor.Patch-Channel+Build
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

    public string GetFullVersionString()
    {
        var core = $"{Major}.{Minor}.{Patch}";
        if (!string.IsNullOrEmpty(Channel))
            core += $"-{Channel}";
        if (Build > 0)
            core += $"+{Build}";
        return core;
    }

    public override string ToString() => GetFullVersionString();

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
    /// 解析 SemVer 2.0 格式: X.Y.Z[-channel][+build]
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

        // Parse +Build
        int build = 0;
        int plusIdx = remaining.IndexOf('+');
        if (plusIdx >= 0)
        {
            if (!int.TryParse(remaining.Substring(plusIdx + 1), out build))
                return false;
            remaining = remaining.Substring(0, plusIdx);
        }

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

        if (major < 0 || minor < 0 || patch < 0 || build < 0)
            return false;

        if (!string.IsNullOrEmpty(channel) &&
            channel != "alpha" && channel != "beta" && channel != "rc")
            return false;

        result = new VersionNumber
        {
            Major = major,
            Minor = minor,
            Patch = patch,
            Build = build,
            Channel = channel
        };
        return true;
    }

    #endregion
}