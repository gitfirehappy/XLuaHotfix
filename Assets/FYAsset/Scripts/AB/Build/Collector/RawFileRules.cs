using System;
using System.Collections.Generic;
using System.IO;

/// <summary>
/// 项目级 RawFile 白名单规则。命中任一维度的文件按原始文件构建与加载，即使 Unity 能识别为序列化资产。
/// </summary>
/// <remarks>
/// 三个维度分别匹配文件后缀（如 ".txt"）、文件名（如 "config.json"）和文件夹路径前缀（如 "Assets/Config"）。
/// 匹配对大小写不敏感；空项由校验器拒绝。文件夹维度按前缀匹配，不接受通配符。
/// </remarks>
[Serializable]
public class RawFileRules
{
    /// <summary>按后缀匹配，项需包含前导点，例如 ".json"。</summary>
    public List<string> Extensions = new();

    /// <summary>按文件名匹配，例如 "server.txt"。</summary>
    public List<string> FileNames = new();

    /// <summary>按项目相对文件夹路径匹配，目录内全部文件命中。</summary>
    public List<string> Folders = new();

    /// <summary>
    /// 判断资产路径是否命中白名单。规则为 null 或三个列表均为空时一律返回 false。
    /// </summary>
    public bool Matches(string assetPath)
    {
        if (string.IsNullOrEmpty(assetPath))
            return false;

        return MatchesExtension(assetPath) || MatchesFileName(assetPath) || MatchesFolder(assetPath);
    }

    /// <summary>后缀匹配：把规则统一成带前导点的形式后与路径结尾比较，文件不存在时也能判定。</summary>
    private bool MatchesExtension(string assetPath)
    {
        if (Extensions == null)
            return false;

        for (int i = 0; i < Extensions.Count; i++)
        {
            string extension = NormalizeExtension(Extensions[i]);
            if (extension.Length > 0 && assetPath.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private bool MatchesFileName(string assetPath)
    {
        if (FileNames == null || FileNames.Count == 0)
            return false;

        string fileName = Path.GetFileName(assetPath);
        if (string.IsNullOrEmpty(fileName))
            return false;

        for (int i = 0; i < FileNames.Count; i++)
        {
            string rule = FileNames[i]?.Trim();
            if (!string.IsNullOrEmpty(rule) && string.Equals(fileName, rule, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>文件夹匹配：路径以该目录为前缀即命中，因此目录内的深层文件同样命中。</summary>
    private bool MatchesFolder(string assetPath)
    {
        if (Folders == null || Folders.Count == 0)
            return false;

        for (int i = 0; i < Folders.Count; i++)
        {
            string folder = NormalizeFolder(Folders[i]);
            if (folder.Length == 0)
                continue;

            if (assetPath.Length > folder.Length &&
                assetPath[folder.Length] == '/' &&
                assetPath.StartsWith(folder, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static string NormalizeExtension(string extension)
    {
        string normalized = extension?.Trim();
        if (string.IsNullOrEmpty(normalized))
            return string.Empty;

        return normalized[0] == '.' ? normalized : string.Concat(".", normalized);
    }

    private static string NormalizeFolder(string folder)
    {
        string normalized = folder?.Trim().Replace('\\', '/');
        return string.IsNullOrEmpty(normalized) ? string.Empty : normalized.TrimEnd('/');
    }
}
