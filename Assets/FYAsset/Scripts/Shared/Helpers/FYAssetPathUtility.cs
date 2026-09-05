using System;
using System.IO;

/// <summary>
/// FYAsset 共享 path 和 URL 规范化工具。
/// </summary>
public static class FYAssetPathUtility
{
    private static StringComparison FilePathComparison =>
        Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public static string JoinUrl(string root, params string[] segments)
    {
        string result = NormalizeUrlPart(root, trimBoth: false).TrimEnd('/');

        if (segments == null)
            return result;

        for (int i = 0; i < segments.Length; i++)
        {
            string segment = NormalizeUrlPart(segments[i], trimBoth: true);
            if (string.IsNullOrEmpty(segment))
                continue;

            result = string.IsNullOrEmpty(result)
                ? segment
                : result + "/" + segment;
        }

        return result;
    }

    public static string JoinFilePath(params string[] segments)
    {
        if (segments == null || segments.Length == 0)
            return string.Empty;

        if (IsFileUriLikePath(segments[0]))
            return JoinUriPath(segments);

        string result = string.Empty;
        for (int i = 0; i < segments.Length; i++)
        {
            string segment = NormalizeFileSeparators(segments[i]);
            if (string.IsNullOrEmpty(segment))
                continue;

            if (!string.IsNullOrEmpty(result))
                segment = segment.TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.IsNullOrEmpty(segment))
                continue;

            result = string.IsNullOrEmpty(result) ? segment : Path.Combine(result, segment);
        }

        return NormalizeFileSeparators(result);
    }

    public static string ResolveFilePath(string root, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return NormalizePath(root);

        string normalizedPath = NormalizeFileSeparators(path);
        if (Path.IsPathRooted(normalizedPath))
            return NormalizePath(normalizedPath);

        return NormalizePath(Path.Combine(root, normalizedPath));
    }

    public static string GetRelativeFilePath(string root, string path)
    {
        string normalizedRoot = NormalizePath(root);
        string normalizedPath = NormalizePath(path);
        if (string.IsNullOrEmpty(normalizedRoot) || string.IsNullOrEmpty(normalizedPath))
            return string.Empty;

        if (string.Equals(normalizedRoot, normalizedPath, FilePathComparison))
            return string.Empty;

        string rootWithSeparator = EnsureTrailingFileSeparator(normalizedRoot);
        if (!normalizedPath.StartsWith(rootWithSeparator, FilePathComparison))
            throw new InvalidOperationException($"Path is outside root. Root: {root}, Path: {path}");

        return NormalizeFileSeparators(normalizedPath.Substring(rootWithSeparator.Length));
    }

    public static string NormalizeAssetPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;

        string normalized = path.Replace('\\', '/').Trim();
        if (normalized == "/")
            return normalized;

        return normalized.TrimEnd('/');
    }

    public static string JoinAssetPath(params string[] segments)
    {
        if (segments == null || segments.Length == 0)
            return string.Empty;

        string result = string.Empty;
        for (int i = 0; i < segments.Length; i++)
        {
            string segment = NormalizeAssetPath(segments[i]).Trim('/');
            if (string.IsNullOrEmpty(segment))
                continue;

            result = string.IsNullOrEmpty(result) ? segment : result + "/" + segment;
        }

        return result;
    }

    public static bool TryMakeAssetPath(string absolutePath, string assetRootPath, out string assetPath)
    {
        assetPath = string.Empty;
        string normalizedAbsolute = NormalizeAbsolutePathForUnity(absolutePath);
        string normalizedRoot = NormalizeAbsolutePathForUnity(assetRootPath);

        if (string.IsNullOrEmpty(normalizedAbsolute) || string.IsNullOrEmpty(normalizedRoot))
            return false;

        if (string.Equals(normalizedAbsolute, normalizedRoot, StringComparison.OrdinalIgnoreCase))
        {
            assetPath = "Assets";
            return true;
        }

        string rootWithSlash = normalizedRoot.TrimEnd('/') + "/";
        if (!normalizedAbsolute.StartsWith(rootWithSlash, StringComparison.OrdinalIgnoreCase))
            return false;

        string relative = normalizedAbsolute.Substring(rootWithSlash.Length);
        assetPath = JoinAssetPath("Assets", relative);
        return true;
    }

    public static bool TryMakeProjectRelativePath(string absolutePath, string projectRoot, out string relativePath)
    {
        relativePath = string.Empty;
        string normalizedAbsolute = NormalizeAbsolutePathForUnity(absolutePath);
        string normalizedRoot = NormalizeAbsolutePathForUnity(projectRoot);

        if (string.IsNullOrEmpty(normalizedAbsolute) || string.IsNullOrEmpty(normalizedRoot))
            return false;

        if (string.Equals(normalizedAbsolute, normalizedRoot, StringComparison.OrdinalIgnoreCase))
            return true;

        string rootWithSlash = normalizedRoot.TrimEnd('/') + "/";
        if (!normalizedAbsolute.StartsWith(rootWithSlash, StringComparison.OrdinalIgnoreCase))
            return false;

        relativePath = NormalizeAssetPath(normalizedAbsolute.Substring(rootWithSlash.Length));
        return true;
    }

    public static bool IsHttpUrl(string value)
    {
        return !string.IsNullOrEmpty(value)
               && (value.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                   || value.StartsWith("https://", StringComparison.OrdinalIgnoreCase));
    }

    public static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;

        return TrimTrailingFileSeparators(Path.GetFullPath(NormalizeFileSeparators(path)));
    }

    public static bool AreSamePath(string left, string right)
    {
        string normalizedLeft = NormalizePath(left);
        string normalizedRight = NormalizePath(right);
        if (string.IsNullOrEmpty(normalizedLeft) || string.IsNullOrEmpty(normalizedRight))
            return false;

        return string.Equals(normalizedLeft, normalizedRight, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeUrlPart(string value, bool trimBoth)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        string normalized = value.Replace('\\', '/');
        return trimBoth ? normalized.Trim('/') : normalized.Trim();
    }

    private static string NormalizeFileSeparators(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;

        char separator = Path.DirectorySeparatorChar;
        return path.Trim().Replace('\\', separator).Replace('/', separator);
    }

    private static bool IsFileUriLikePath(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        string trimmed = value.Trim();
        return trimmed.StartsWith("file://", StringComparison.OrdinalIgnoreCase)
               || trimmed.StartsWith("jar:", StringComparison.OrdinalIgnoreCase)
               || trimmed.StartsWith("content://", StringComparison.OrdinalIgnoreCase);
    }

    private static string JoinUriPath(string[] segments)
    {
        string result = NormalizeUrlPart(segments[0], trimBoth: false).TrimEnd('/');
        for (int i = 1; i < segments.Length; i++)
        {
            string segment = NormalizeUrlPart(segments[i], trimBoth: true);
            if (string.IsNullOrEmpty(segment))
                continue;

            result = string.IsNullOrEmpty(result) ? segment : result + "/" + segment;
        }

        return result;
    }

    private static string EnsureTrailingFileSeparator(string path)
    {
        if (string.IsNullOrEmpty(path))
            return string.Empty;

        char separator = Path.DirectorySeparatorChar;
        return path.EndsWith(separator.ToString(), StringComparison.Ordinal)
            ? path
            : path + separator;
    }

    private static string TrimTrailingFileSeparators(string path)
    {
        if (string.IsNullOrEmpty(path))
            return string.Empty;

        string root = Path.GetPathRoot(path);
        if (!string.IsNullOrEmpty(root) && string.Equals(path, root, FilePathComparison))
            return path;

        string trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.IsNullOrEmpty(trimmed))
            return path;
        if (!string.IsNullOrEmpty(root) && trimmed.Length < root.Length)
            return root;

        return trimmed;
    }

    private static string NormalizeAbsolutePathForUnity(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;

        return NormalizePath(path).Replace('\\', '/').TrimEnd('/');
    }
}
