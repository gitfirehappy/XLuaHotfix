using System;
using System.Collections.Generic;

/// <summary>
/// Collector 路径工具方法 — 集中管理 NormalizePath / PathDepth / IsPathContained / MatchesIgnorePattern，
/// 消除各文件中的私有副本。
/// </summary>
public static class CollectorPathUtility
{
    public static string NormalizePath(string path)
    {
        if (string.IsNullOrEmpty(path))
            return string.Empty;
        return path.Replace('\\', '/').TrimEnd('/');
    }

    public static int PathDepth(string path)
    {
        string normalized = NormalizePath(path);
        if (normalized.Length == 0)
            return 0;

        int depth = 0;
        for (int i = 0; i < normalized.Length; i++)
        {
            if (normalized[i] == '/')
                depth++;
        }
        return depth;
    }

    public static bool IsPathContained(string parent, string child)
    {
        if (string.Equals(parent, child, StringComparison.OrdinalIgnoreCase))
            return true;

        if (child.Length > parent.Length &&
            child[parent.Length] == '/' &&
            child.StartsWith(parent, StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    public static bool MatchesIgnorePattern(string assetPath, string collectPath, List<string> patterns)
    {
        if (patterns == null || patterns.Count == 0)
            return false;

        string normalizedAsset = NormalizePath(assetPath);
        string normalizedCollect = NormalizePath(collectPath);
        string relativePath;

        if (normalizedAsset.Length > normalizedCollect.Length + 1 &&
            normalizedAsset.StartsWith(normalizedCollect, StringComparison.OrdinalIgnoreCase) &&
            normalizedAsset[normalizedCollect.Length] == '/')
        {
            relativePath = normalizedAsset.Substring(normalizedCollect.Length + 1);
        }
        else if (string.Equals(normalizedAsset, normalizedCollect, StringComparison.OrdinalIgnoreCase))
        {
            int slash = normalizedAsset.LastIndexOf('/');
            relativePath = slash >= 0 ? normalizedAsset.Substring(slash + 1) : normalizedAsset;
        }
        else
        {
            return false;
        }

        for (int i = 0; i < patterns.Count; i++)
        {
            string pattern = patterns[i];
            if (string.IsNullOrEmpty(pattern))
                continue;

            string normalizedPattern = NormalizePath(pattern);
            if (IsFullPathPattern(normalizedPattern) && MatchesFullPathPattern(normalizedAsset, normalizedPattern))
                return true;

            if (pattern.EndsWith("/"))
            {
                string dirName = pattern.Substring(0, pattern.Length - 1);
                if (ContainsPathSegment(relativePath, dirName))
                    return true;
            }
            else if (GlobMatcher.IsMatch(relativePath, pattern))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsFullPathPattern(string pattern)
    {
        return pattern.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(pattern, "Assets", StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesFullPathPattern(string assetPath, string pattern)
    {
        if (string.Equals(assetPath, pattern, StringComparison.OrdinalIgnoreCase))
            return true;

        const string subtreeSuffix = "/**";
        if (pattern.EndsWith(subtreeSuffix, StringComparison.Ordinal))
        {
            string root = pattern.Substring(0, pattern.Length - subtreeSuffix.Length);
            return IsPathContained(root, assetPath);
        }

        return GlobMatcher.IsMatch(assetPath, pattern);
    }

    private static bool ContainsPathSegment(string path, string segment)
    {
        int start = 0;
        int len = path.Length;
        int segLen = segment.Length;

        while (start <= len)
        {
            int slash = path.IndexOf('/', start);
            int end = slash < 0 ? len : slash;
            int currentLen = end - start;

            if (currentLen == segLen &&
                string.Compare(path, start, segment, 0, segLen, StringComparison.OrdinalIgnoreCase) == 0)
            {
                return true;
            }

            start = end + 1;
            if (slash < 0)
                break;
        }

        return false;
    }
}
