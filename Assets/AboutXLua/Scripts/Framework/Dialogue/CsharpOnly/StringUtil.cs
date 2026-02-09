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

    /// <summary>
    /// 推导解析参数列表
    /// </summary>
    public static List<object> ParseParamList(List<string> paramStrList)
    {
        var list = new List<object>();
        if (paramStrList == null) return list;

        foreach (var str in paramStrList)
        {
            list.Add(ParseValue(str));
        }
        return list;
    }

    /// <summary>
    /// 解析单个值
    /// </summary>
    public static object ParseValue(string s)
    {
        if (s == null) return null;
        s = s.Trim();
        if (s == "") return "";

        string lower = s.ToLower();
        if (lower == "nil" || lower == "null") return null;
        if (lower == "true") return true;
        if (lower == "false") return false;

        // 字符串转义处理
        if (s.Length >= 2)
        {
            char first = s[0];
            char last = s[s.Length - 1];
            if ((first == '"' && last == '"') || (first == '\'' && last == '\''))
            {
                // 去除引号并反转义
                return System.Text.RegularExpressions.Regex.Unescape(s.Substring(1, s.Length - 2));
            }
        }

        // 解析表类型 {...}
        if (s.StartsWith("{") && s.EndsWith("}"))
        {
            string inner = s.Substring(1, s.Length - 2);
            return ParseTable(inner);
        }

        // 数字尝试
        if (double.TryParse(s, out double d))
        {
            return d;
        }

        return s;
    }

    private static object ParseTable(string inner)
    {
        var items = new List<string>();
        var buf = new System.Text.StringBuilder();
        int depth = 0;
        bool inQuote = false;
        char quoteChar = '\0';

        // 1. 分割顶层逗号
        for (int i = 0; i < inner.Length; i++)
        {
            char ch = inner[i];
            char prev = i > 0 ? inner[i - 1] : '\0';

            if (!inQuote && (ch == '"' || ch == '\''))
            {
                inQuote = true;
                quoteChar = ch;
                buf.Append(ch);
            }
            else if (inQuote && ch == quoteChar && prev != '\\')
            {
                inQuote = false;
                quoteChar = '\0';
                buf.Append(ch);
            }
            else if (!inQuote)
            {
                if (ch == '{')
                {
                    depth++;
                    buf.Append(ch);
                }
                else if (ch == '}')
                {
                    depth--;
                    buf.Append(ch);
                }
                else if (ch == ',' && depth == 0)
                {
                    items.Add(buf.ToString().Trim());
                    buf.Clear();
                }
                else
                {
                    buf.Append(ch);
                }
            }
            else
            {
                buf.Append(ch);
            }
        }

        if (buf.Length > 0)
        {
            items.Add(buf.ToString().Trim());
        }

        // 2. 解析每一项
        bool hasKey = false;
        var listData = new List<object>();
        var dictData = new Dictionary<object, object>();
        int arrayIndex = 1; // 模拟Lua，虽然List是从0开始，但如果混合，我们尽量保持一致。List<object>自然从0开始。
        // 若要保持语义一致，user input {1,2,3} -> C# [1,2,3] is fine.
        // If {k=1}, C# dict["k"]=1.

        foreach (var token in items)
        {
            if (string.IsNullOrEmpty(token)) continue;

            // 寻找顶层等号
            int eqPos = -1;
            bool inQ = false;
            char qch = '\0';
            int d = 0;

            for (int i = 0; i < token.Length; i++)
            {
                char c = token[i];
                char prev = i > 0 ? token[i - 1] : '\0';
                if (!inQ && (c == '"' || c == '\''))
                {
                    inQ = true; qch = c;
                }
                else if (inQ && c == qch && prev != '\\')
                {
                    inQ = false; qch = '\0';
                }
                else if (!inQ)
                {
                    if (c == '{') d++;
                    else if (c == '}') d--;
                    else if (c == '=' && d == 0)
                    {
                        eqPos = i;
                        break;
                    }
                }
            }

            if (eqPos >= 0)
            {
                hasKey = true;
                string keyStr = token.Substring(0, eqPos).Trim();
                string valStr = token.Substring(eqPos + 1).Trim();

                object key = null;
                // 解析Key
                if (keyStr.Length >= 2)
                {
                     char f = keyStr[0];
                     char l = keyStr[keyStr.Length - 1];
                     if ((f == '"' && l == '"') || (f == '\'' && l == '\''))
                     {
                         key = System.Text.RegularExpressions.Regex.Unescape(keyStr.Substring(1, keyStr.Length - 2));
                     }
                }
                if (key == null)
                {
                    if (double.TryParse(keyStr, out double n)) key = n;
                    else key = keyStr;
                }

                dictData[key] = ParseValue(valStr);
            }
            else
            {
                object val = ParseValue(token);
                listData.Add(val);
                // 如果已经有Key模式，则也往Dict加? Lua是混用的。
                // 简单起见，如果不含Key，就认定为List。如果含Key，就转为Dict。
                // 如果是混合，比如 {1, a=2}, Lua是 t[1]=1, t["a"]=2. 
                // C#这里返回 List 还是 Dictionary? 
                // 返回Dictionary更通用。
                dictData[arrayIndex] = val; // arrayIndex for dict
                arrayIndex++;
            }
        }

        if (hasKey)
        {
            // 如果只有部分有key，那些没有key的已经被加入dictData了 (index keys)
            // 所以如果有任何key形式，返回Dictionary
            return dictData; 
        }
        else
        {
            // 纯数组
            return listData;
        }
    }
}