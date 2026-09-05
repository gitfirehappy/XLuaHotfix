using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

/// <summary>
/// AA/AB 独立导出边界门禁（R 系列验收）。
/// 规则：Shared 不得引用任何后端声明类型；AA 不得引用 AB 声明类型；AB 不得引用 AA 声明类型。
/// 类型集合从源码声明自维护收集，新增后端类型自动纳管。
/// </summary>
internal static class XLuaFrameworkBoundaryTests
{
    private const string FrameworkRoot = "Assets/XLuaFramework/Scripts";
    private const string FYAssetRoot = "Assets/FYAsset/Scripts";

    // 只纳管跨文件可见的类型（public/internal）；私有嵌套类型不可跨树引用，收集会造成同名误判。
    private static readonly Regex TypeDecl = new(
        @"\b(?:public|internal)\s+(?:sealed\s+|static\s+|abstract\s+|partial\s+|readonly\s+|new\s+)*?(?:class|struct|interface|enum)\s+(\w+)",
        RegexOptions.Compiled);

    // 本文件自声明排除用更宽的模式（私有声明同名也不是引用）。
    private static readonly Regex AnyTypeDecl = new(
        @"\b(?:class|struct|interface|enum)\s+(\w+)",
        RegexOptions.Compiled);

    public static void Run()
    {
        var fyTypes = CollectDeclaredTypeNames(FYAssetRoot);
        Console.WriteLine($"xlua framework boundary: FYAsset types={fyTypes.Count}");

        foreach (string file in EnumerateSources(FrameworkRoot))
        {
            string source = Sanitize(File.ReadAllText(file));
            var localDecls = new HashSet<string>(StringComparer.Ordinal);
            foreach (Match m in AnyTypeDecl.Matches(File.ReadAllText(file)))
                localDecls.Add(m.Groups[1].Value);
            foreach (string type in fyTypes)
                if (!localDecls.Contains(type))
                    RepoAssert.False(ContainsToken(source, type),
                        $"XLuaFramework must not reference FYAsset type {type}: {file}");
        }
    }

    private static HashSet<string> CollectDeclaredTypeNames(string root)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (string file in EnumerateSources(root))
        {
            foreach (Match match in TypeDecl.Matches(File.ReadAllText(file)))
            {
                string name = match.Groups[1].Value;
                if (name.Length > 1)
                    result.Add(name);
            }
        }
        return result;
    }

    /// <summary>剥离注释/字符串字面量与 BackendMode 词表访问，减少误报。</summary>
    private static string Sanitize(string source)
    {
        source = Regex.Replace(source, @"//[^\n]*", " ");
        source = Regex.Replace(source, @"#(?:region|endregion)[^\n]*", " ");
        source = Regex.Replace(source, @"/\*.*?\*/", " ", RegexOptions.Singleline);
        source = Regex.Replace(source, "\\\"(?:[^\\\"\\\\]|\\\\.)*?\\\"", "\"\"");
        // Unity Addressables 嵌套枚举 BundledAssetGroupSchema.BundlePackingMode 与 AB 顶层枚举同名：限定访问不是引用 AB 类型。
        source = source.Replace("BundledAssetGroupSchema.BundlePackingMode", " ");
        source = source.Replace("BackendMode.ABManifest", " ").Replace("BackendMode.AA", " ");
        source = source.Replace("BackendModeNames.AB", " ").Replace("BackendModeNames.AA", " ");
        return source;
    }

    private static bool ContainsToken(string source, string token)
    {
        return Regex.IsMatch(source, @"\b" + Regex.Escape(token) + @"\b");
    }

    private static IEnumerable<string> EnumerateSources(string root)
    {
        string abs = Path.GetFullPath(root);
        if (!Directory.Exists(abs))
            yield break;
        foreach (string file in Directory.EnumerateFiles(abs, "*.cs", SearchOption.AllDirectories))
            yield return file.Replace((char)92, '/');
    }
}
