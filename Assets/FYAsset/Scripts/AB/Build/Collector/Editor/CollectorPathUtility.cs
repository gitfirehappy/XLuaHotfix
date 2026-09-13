using System;

/// <summary>
/// Collector 路径工具：路径规范化、深度计算与包含判断。
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

        return child.Length > parent.Length &&
               child[parent.Length] == '/' &&
               child.StartsWith(parent, StringComparison.OrdinalIgnoreCase);
    }
}
