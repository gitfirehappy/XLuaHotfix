using System.Collections.Generic;
using System.Linq;

/// <summary>
/// 字符串工具类
/// </summary>
public static class StringUtil
{
    /// <summary>
    /// ";"分隔字符串解析（去除首尾空格）
    /// </summary>
    public static List<string> SplitSemicolon(string str)
    {
        var result = new List<string>();
        if (string.IsNullOrEmpty(str)) return result;

        var parts = str.Split(';');
        foreach (var part in parts)
        {
            var trimed = part.Trim();
            if (!string.IsNullOrEmpty(trimed))
                result.Add(trimed);
        }
        return result;
    }

    /// <summary>
    /// "&"分隔字符串解析（去除首尾空格）
    /// </summary>
    public static List<string> SplitAmpersand(string str)
    {
        var result = new List<string>();
        if (string.IsNullOrEmpty(str)) return result;

        var parts = str.Split('&');
        foreach (var part in parts)
        {
            var trimed = part.Trim();
            if (!string.IsNullOrEmpty(trimed))
                result.Add(trimed);
        }
        return result;
    }
}