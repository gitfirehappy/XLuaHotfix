using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

/// <summary>
/// Collection 规则共用的 gitignore 基础匹配器。
/// </summary>
public static class GitIgnoreMatcher
{
    public static bool Evaluate(string assetPath, IList<string> patterns)
    {
        string normalizedPath = NormalizePath(assetPath);
        if (string.IsNullOrEmpty(normalizedPath) || patterns == null)
            return false;

        bool matched = false;
        for (int i = 0; i < patterns.Count; i++)
        {
            if (TryParsePattern(patterns[i], out string pattern, out bool negated, out bool directory))
            {
                if (!Matches(normalizedPath, pattern, directory))
                    continue;

                matched = !negated;
            }
        }

        return matched;
    }

    private static bool Matches(string assetPath, string pattern, bool directory)
    {
        bool anchored = pattern.StartsWith("/", StringComparison.Ordinal);
        if (anchored)
        {
            pattern = pattern.Substring(1);
            if (!pattern.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                pattern = string.Concat("Assets/", pattern);
        }

        pattern = pattern.Trim('/');
        if (pattern.Length == 0)
            return false;

        bool hasSlash = pattern.IndexOf('/') >= 0;
        bool segmentOnly = !anchored && !hasSlash;
        string expression = GlobToRegex(pattern, segmentOnly, directory);
        return Regex.IsMatch(assetPath, expression, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static string GlobToRegex(string pattern, bool segmentOnly, bool directory)
    {
        var builder = new StringBuilder();
        if (segmentOnly)
            builder.Append("(^|/)");
        else
            builder.Append('^');

        for (int i = 0; i < pattern.Length; i++)
        {
            char current = pattern[i];
            if (current == '*')
            {
                bool doubleStar = i + 1 < pattern.Length && pattern[i + 1] == '*';
                if (doubleStar)
                {
                    i++;
                    if (i + 1 < pattern.Length && pattern[i + 1] == '/')
                    {
                        i++;
                        builder.Append("(?:.*/)?");
                    }
                    else
                    {
                        builder.Append(".*");
                    }
                }
                else
                {
                    builder.Append("[^/]*");
                }
            }
            else if (current == '?')
            {
                builder.Append("[^/]");
            }
            else
            {
                builder.Append(Regex.Escape(current.ToString()));
            }
        }

        if (directory)
            builder.Append("(?:/.*)?");

        builder.Append('$');
        return builder.ToString();
    }

    private static bool TryParsePattern(
        string rawPattern,
        out string pattern,
        out bool negated,
        out bool directory)
    {
        pattern = string.Empty;
        negated = false;
        directory = false;

        string value = rawPattern?.Trim();
        if (string.IsNullOrEmpty(value) || value.StartsWith("#", StringComparison.Ordinal))
            return false;

        if (value[0] == '!')
        {
            negated = true;
            value = value.Substring(1).TrimStart();
        }

        bool directoryRule = value.EndsWith("/", StringComparison.Ordinal);
        bool anchoredRule = value.StartsWith("/", StringComparison.Ordinal);
        value = NormalizePath(value);
        if (anchoredRule)
            value = "/" + value;
        if (directoryRule)
        {
            directory = true;
            value = value.TrimEnd('/');
        }

        if (value.Length == 0)
            return false;

        // Patterns are authored relative to the project root. Keep an explicit Assets/
        // prefix and anchor every pattern that contains a path separator there.
        if (!value.StartsWith("/", StringComparison.Ordinal)
            && !value.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)
            && value.IndexOf('/') >= 0)
        {
            value = string.Concat("Assets/", value);
        }

        pattern = value;
        return true;
    }

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrEmpty(path))
            return string.Empty;

        string normalized = path.Replace('\\', '/').Trim();
        while (normalized.StartsWith("./", StringComparison.Ordinal))
            normalized = normalized.Substring(2);
        return normalized.Trim('/');
    }
}
