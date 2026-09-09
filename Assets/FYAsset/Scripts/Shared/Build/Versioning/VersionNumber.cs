using System;
using System.Text;

/// <summary>
/// 发布版本值，格式为 Major.Minor.Patch[-Channel]。
/// Build 单独存储，不参与排序和相等性比较。
/// </summary>
[Serializable]
[BinarySerializable]
public struct VersionNumber : IComparable<VersionNumber>, IEquatable<VersionNumber>
{
    [BinaryField(0)] public int Major;
    [BinaryField(1)] public int Minor;
    [BinaryField(2)] public int Patch;
    [BinaryField(3)] public int Build;
    [BinaryField(4)] public string Channel;

    public string GetReleaseVersionString()
    {
        var core = $"{Major}.{Minor}.{Patch}";
        if (!string.IsNullOrEmpty(Channel))
            core += $"-{Channel}";
        return core;
    }

    public override string ToString() => GetReleaseVersionString();

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
        int cmp = Major.CompareTo(other.Major);
        if (cmp != 0) return cmp;
        cmp = Minor.CompareTo(other.Minor);
        if (cmp != 0) return cmp;
        cmp = Patch.CompareTo(other.Patch);
        if (cmp != 0) return cmp;
        return GetChannelRank(Channel).CompareTo(GetChannelRank(other.Channel));
    }

    public static bool operator >(VersionNumber a, VersionNumber b) =>
        a.CompareTo(b) > 0;
    public static bool operator <(VersionNumber a, VersionNumber b) =>
        a.CompareTo(b) < 0;
    public static bool operator >=(VersionNumber a, VersionNumber b) =>
        a.CompareTo(b) >= 0;
    public static bool operator <=(VersionNumber a, VersionNumber b) =>
        a.CompareTo(b) <= 0;

    #endregion

    #region Equality

    public override bool Equals(object obj) => obj is VersionNumber other && Equals(other);

    public bool Equals(VersionNumber other)
    {
        return Major == other.Major &&
               Minor == other.Minor &&
               Patch == other.Patch &&
               string.Equals(Channel, other.Channel, StringComparison.OrdinalIgnoreCase);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Major, Minor, Patch, Channel?.ToLowerInvariant() ?? "");
    }

    public static bool operator ==(VersionNumber a, VersionNumber b) => a.Equals(b);

    public static bool operator !=(VersionNumber a, VersionNumber b) => !(a == b);

    #endregion

    #region Parse

    /// <summary>
    /// 解析 X.Y.Z[-channel]；输入无效时抛出 FormatException。
    /// </summary>
    public static VersionNumber Parse(string input)
    {
        if (TryParse(input, out var result))
            return result;
        throw new FormatException($"无效版本号字符串: '{input}'");
    }

    /// <summary>输入无效时返回 false，并将 <paramref name="result"/> 设为 default。</summary>
    public static bool TryParse(string input, out VersionNumber result)
    {
        result = default;
        if (string.IsNullOrEmpty(input))
            return false;

        string remaining = input.Trim();

        if (remaining.IndexOf('+') >= 0)
            return false;

        string channel = "";
        int dashIdx = remaining.IndexOf('-');
        if (dashIdx >= 0)
        {
            channel = remaining.Substring(dashIdx + 1);
            remaining = remaining.Substring(0, dashIdx);
        }

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

    #region JSON presence

    /// <summary>
    /// 判断 JSON 对象是否包含指定字段且值为对象。
    /// struct 反序列化后无法用字段值区分缺失与显式 0.0.0，读取边界必须先检查字段存在。
    /// </summary>
    public static bool JsonHasObjectField(string json, string fieldName)
    {
        return TryFindObjectValue(json, 0, json == null ? 0 : json.Length, fieldName, out _);
    }

    /// <summary>
    /// 在 UTF-8 JSON 字节中检查对象字段；会去掉 BOM。二进制数据应先按 Magic 分流，不要调用本方法。
    /// </summary>
    public static bool JsonHasObjectField(byte[] data, string fieldName)
    {
        if (data == null || data.Length == 0)
            return false;

        string json = Encoding.UTF8.GetString(data);
        if (json.Length > 0 && json[0] == '\uFEFF')
            json = json.Substring(1);
        return JsonHasObjectField(json, fieldName);
    }

    /// <summary>
    /// 当父字段存在且为对象时，检查其子对象是否包含指定对象字段。父字段缺失时返回 false。
    /// </summary>
    public static bool JsonHasNestedObjectField(string json, string parentField, string fieldName)
    {
        if (!TryFindObjectValue(json, 0, json == null ? 0 : json.Length, parentField, out int objectStart))
            return false;

        int objectEnd = MatchObjectEnd(json, objectStart);
        if (objectEnd < 0)
            return false;
        return TryFindObjectValue(json, objectStart + 1, objectEnd, fieldName, out _);
    }

    private static bool TryFindObjectValue(string json, int start, int end, string fieldName, out int objectStart)
    {
        objectStart = -1;
        if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(fieldName) || start < 0 || end > json.Length || start >= end)
            return false;

        string key = "\"" + fieldName + "\"";
        int i = SkipJsonWhitespace(json, start, end);
        if (i < end && json[i] == '{')
            i++;

        int relativeDepth = 0;
        bool inString = false;
        bool escape = false;
        for (; i < end; i++)
        {
            char c = json[i];
            if (inString)
            {
                if (escape)
                    escape = false;
                else if (c == '\\')
                    escape = true;
                else if (c == '"')
                    inString = false;
                continue;
            }

            if (c == '"')
            {
                if (relativeDepth == 0
                    && i + key.Length <= end
                    && string.CompareOrdinal(json, i, key, 0, key.Length) == 0)
                {
                    int colon = SkipJsonWhitespace(json, i + key.Length, end);
                    if (colon < end && json[colon] == ':')
                    {
                        int value = SkipJsonWhitespace(json, colon + 1, end);
                        if (value < end && json[value] == '{')
                        {
                            objectStart = value;
                            return true;
                        }

                        return false;
                    }
                }

                inString = true;
                continue;
            }

            if (c == '{')
                relativeDepth++;
            else if (c == '}')
                relativeDepth--;
        }

        return false;
    }

    private static int MatchObjectEnd(string json, int objectStart)
    {
        int depth = 0;
        bool inString = false;
        bool escape = false;
        for (int i = objectStart; i < json.Length; i++)
        {
            char c = json[i];
            if (inString)
            {
                if (escape)
                    escape = false;
                else if (c == '\\')
                    escape = true;
                else if (c == '"')
                    inString = false;
                continue;
            }

            if (c == '"')
                inString = true;
            else if (c == '{')
                depth++;
            else if (c == '}')
            {
                depth--;
                if (depth == 0)
                    return i;
            }
        }

        return -1;
    }

    private static int SkipJsonWhitespace(string json, int index, int end)
    {
        while (index < end && char.IsWhiteSpace(json[index]))
            index++;
        return index;
    }

    #endregion
}
