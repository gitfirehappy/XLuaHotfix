using System;
using System.Collections.Generic;
using System.IO;

/// <summary>
/// 热更入口数据的纯校验函数。
/// </summary>
public static class HotfixPackageValidator
{
    public static bool IsSafePathSegment(string value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value == "."
            || value == ".."
            || Path.IsPathRooted(value)
            || value.IndexOfAny(new[] { '/', '\\' }) >= 0
            || value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || value.EndsWith(".", StringComparison.Ordinal)
            || value.EndsWith(" ", StringComparison.Ordinal)
            || !string.Equals(Path.GetFileName(value), value, StringComparison.Ordinal))
        {
            return false;
        }

        int extensionIndex = value.IndexOf('.');
        string baseName = extensionIndex >= 0 ? value.Substring(0, extensionIndex) : value;
        return !IsWindowsDeviceName(baseName);
    }

    public static bool IsVersionValid(VersionNumber version)
    {
        return version != null
               && version.Major >= 0
               && version.Minor >= 0
               && version.Patch >= 0
               && VersionNumber.TryParse(version.GetReleaseVersionString(), out _);
    }

    public static bool IsPackageName(string value)
    {
        return IsSafePathSegment(value)
               && value.StartsWith("Build_", StringComparison.Ordinal);
    }

    public static bool IsBundleMetadataValid(string bundleName, long fileSize, uint fileCrc)
    {
        return IsSafePathSegment(bundleName) && fileSize >= 0 && fileCrc != 0;
    }

    public static bool TryValidateBundleFiles(
        string bundleRoot,
        IReadOnlyList<BundleDownloadItem> bundles,
        out string error)
    {
        error = string.Empty;
        if (string.IsNullOrEmpty(bundleRoot) || !Directory.Exists(bundleRoot))
        {
            error = "Bundle directory is missing.";
            return false;
        }
        if (bundles == null)
        {
            error = "Bundle list is missing.";
            return false;
        }

        var expected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < bundles.Count; i++)
        {
            BundleDownloadItem bundle = bundles[i];
            if (!IsBundleMetadataValid(bundle.BundleName, bundle.FileSize, bundle.FileCRC))
            {
                error = $"Bundle metadata is invalid at index {i}.";
                return false;
            }
            if (!expected.Add(bundle.BundleName))
            {
                error = $"Duplicate Bundle name: {bundle.BundleName}";
                return false;
            }

            string path = Path.Combine(bundleRoot, bundle.BundleName);
            if (!File.Exists(path))
            {
                error = $"Bundle is missing: {bundle.BundleName}";
                return false;
            }
            if (new FileInfo(path).Length != bundle.FileSize)
            {
                error = $"Bundle size mismatch: {bundle.BundleName}";
                return false;
            }
            if (HashGenerator.GenerateFileCRC(path) != bundle.FileCRC)
            {
                error = $"Bundle CRC mismatch: {bundle.BundleName}";
                return false;
            }
        }

        string[] files = Directory.GetFiles(bundleRoot, "*", SearchOption.TopDirectoryOnly);
        if (files.Length != expected.Count)
        {
            error = "Bundle file set does not match the manifest.";
            return false;
        }
        for (int i = 0; i < files.Length; i++)
        {
            if (!expected.Contains(Path.GetFileName(files[i])))
            {
                error = $"Unexpected Bundle file: {Path.GetFileName(files[i])}";
                return false;
            }
        }

        return true;
    }

    private static bool IsWindowsDeviceName(string value)
    {
        if (string.Equals(value, "CON", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "PRN", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "AUX", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "NUL", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (value == null || value.Length != 4)
            return false;
        string prefix = value.Substring(0, 3);
        return value[3] >= '1'
               && value[3] <= '9'
               && (string.Equals(prefix, "COM", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(prefix, "LPT", StringComparison.OrdinalIgnoreCase));
    }
}
