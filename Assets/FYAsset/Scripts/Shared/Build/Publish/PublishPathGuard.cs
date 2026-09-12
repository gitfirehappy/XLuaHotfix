using System;
using System.IO;

/// <summary>
/// 发布路径边界：所有创建、复制、移动、替换、删除之前必须通过本类校验。
/// </summary>
/// <remarks>
/// 只做字符串与文件系统属性判断，不修改任何文件；调用方必须在实际 mutation 边界使用已验证的路径。
/// 拒绝空值、点段、根路径、分隔符、非法字符、Windows 设备名与符号链接/重解析点逃逸。
/// </remarks>
public static class PublishPathGuard
{
    /// <summary>单个目录段是否安全：非空、不是点段、无分隔符与非法字符、不是 Windows 设备名。</summary>
    public static bool IsSafeSegment(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value == "." || value == "..")
            return false;

        if (value.Equals("CON", StringComparison.OrdinalIgnoreCase)
            || value.Equals("PRN", StringComparison.OrdinalIgnoreCase)
            || value.Equals("AUX", StringComparison.OrdinalIgnoreCase)
            || value.Equals("NUL", StringComparison.OrdinalIgnoreCase)
            || value.Equals("COM1", StringComparison.OrdinalIgnoreCase)
            || value.Equals("LPT1", StringComparison.OrdinalIgnoreCase))
            return false;

        if (value.IndexOfAny(new[] { '/', '\\', ':', '*', '?', '"', '<', '>', '|' }) >= 0)
            return false;

        if (value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return false;

        // 尾部空格与句点在 Windows 上会被静默裁剪，导致路径与实际不符。
        char last = value[value.Length - 1];
        return last != ' ' && last != '.';
    }

    /// <summary>candidate 规范化后是否位于 ownerRoot 之内；allowEqual 控制是否允许等于根本身。</summary>
    public static bool IsContainedIn(string ownerRoot, string candidate, bool allowEqual = false)
    {
        if (string.IsNullOrWhiteSpace(ownerRoot) || string.IsNullOrWhiteSpace(candidate))
            return false;

        string root = TrimSeparator(Path.GetFullPath(ownerRoot));
        string path = TrimSeparator(Path.GetFullPath(candidate));
        StringComparison comparison = Path.DirectorySeparatorChar == '\\'
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        if (string.Equals(root, path, comparison))
            return allowEqual;
        return path.StartsWith(root + Path.DirectorySeparatorChar, comparison);
    }

    /// <summary>路径或任一已存在的父目录不得是符号链接/重解析点。</summary>
    public static bool HasNoReparsePoint(string path, out string error)
    {
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(path))
            return true;

        string current = Path.GetFullPath(path);
        while (!string.IsNullOrEmpty(current))
        {
            bool exists = Directory.Exists(current) || File.Exists(current);
            if (exists)
            {
                try
                {
                    FileAttributes attributes = File.GetAttributes(current);
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        error = $"路径包含符号链接/重解析点: {current}";
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    error = $"无法读取路径属性: {current} — {ex.Message}";
                    return false;
                }
            }

            string parent = Path.GetDirectoryName(current);
            if (string.IsNullOrEmpty(parent) || string.Equals(parent, current, StringComparison.Ordinal))
                break;
            current = parent;
        }

        return true;
    }

    private static string TrimSeparator(string path) =>
        path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}
