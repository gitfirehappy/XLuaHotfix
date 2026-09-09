/// <summary>
/// 简易 Glob 匹配工具 —— 仅支持 * 通配符（匹配任意字符序列）。
/// 用于 IgnorePatterns 的 *.ext 和 *keyword* 模式匹配。
/// </summary>
public static class GlobMatcher
{
    /// <summary>
    /// 判断输入字符串是否匹配给定的 Glob 模式（不区分大小写）。
    /// </summary>
    public static bool IsMatch(string input, string pattern)
    {
        if (string.IsNullOrEmpty(pattern))
            return true;

        string[] segments = pattern.Split('*');

        if (segments.Length == 2 && segments[0].Length == 0 && segments[1].Length == 0)
            return true;

        int pos = 0;
        for (int i = 0; i < segments.Length; i++)
        {
            string seg = segments[i];
            if (seg.Length == 0)
                continue;

            int found = input.IndexOf(seg, pos, System.StringComparison.OrdinalIgnoreCase);
            if (found < 0)
                return false;

            pos = found + seg.Length;

            // 首个非空片段若不是模式开头（pattern 不以 * 开头），必须从头匹配
            if (i == 0 && !pattern.StartsWith("*") && found != 0)
                return false;
        }

        // 最后一段匹配后，若 pattern 不以 * 结尾，input 必须刚好匹配到末尾
        int lastNonEmpty = segments.Length - 1;
        while (lastNonEmpty >= 0 && segments[lastNonEmpty].Length == 0)
            lastNonEmpty--;

        if (lastNonEmpty >= 0 && !pattern.EndsWith("*"))
        {
            // 末尾锚定：最后片段必须刚好匹配到 input 末尾
            if (pos != input.Length)
                return false;
        }

        return true;
    }
}
