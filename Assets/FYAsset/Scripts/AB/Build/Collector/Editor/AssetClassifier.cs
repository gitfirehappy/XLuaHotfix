using System;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 资产分类器 —— 按分类顺序推断内容类型（序列化资产 / 场景 / 原始文件）。
/// </summary>
/// <remarks>
/// 分类不依赖 Importer 名称，只判断 Unity 能否有效识别并进入 SerializedObject 构建路线；
/// 项目级 RawFile 白名单优先于该判断，命中白名单的文件即使 Unity 可识别也按 RawFile 构建与加载。
/// </remarks>
public static class AssetClassifier
{
    private static readonly string[] UnsupportedBundleEntryExtensions =
    {
        ".cginc",
        ".hlsl",
        ".hlslinc"
    };

    /// <summary>
    /// 扫描阶段的默认排除扩展名：脚本、程序集定义、元数据与版本控制文件都不是可打包内容。
    /// 这套排除原先由 CollectAll 过滤规则承担，规则反射体系移除后必须由扫描层保留同等行为。
    /// </summary>
    private static readonly string[] DefaultExcludedExtensions =
    {
        ".meta",
        ".cs",
        ".dll",
        ".asmdef",
        ".asmref",
        ".gitignore"
    };

    /// <summary>
    /// 扫描阶段是否按默认规则排除该资产：扩展名命中默认排除列表，或路径位于任意 Editor 目录下。
    /// </summary>
    public static bool IsExcludedByDefault(string assetPath)
    {
        if (string.IsNullOrEmpty(assetPath))
            return true;

        string extension = Path.GetExtension(assetPath);
        for (int i = 0; i < DefaultExcludedExtensions.Length; i++)
        {
            if (string.Equals(extension, DefaultExcludedExtensions[i], StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return ContainsEditorDirectory(assetPath);
    }

    /// <summary>路径中任意一段为 Editor 即视为编辑器专用内容，不参与运行时打包。</summary>
    private static bool ContainsEditorDirectory(string assetPath)
    {
        string normalizedPath = "/" + assetPath.Replace('\\', '/').Trim('/') + "/";
        return normalizedPath.IndexOf("/Editor/", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    /// <summary>
    /// 按分类顺序推断资产内容类型：RawFile 白名单 → Scene（.unity）→ Unity 可有效识别为序列化资产 → 其余 RawFile。
    /// </summary>
    /// <param name="assetPath">项目相对资产路径</param>
    /// <param name="rawFileRules">项目级 RawFile 白名单；为 null 时跳过白名单维度</param>
    public static AssetContentType ClassifyContentType(string assetPath, RawFileRules rawFileRules)
    {
        // 白名单优先：命中即按物理文件构建与加载，即使 Unity 能把它识别为序列化资产。
        if (rawFileRules != null && rawFileRules.Matches(assetPath))
            return AssetContentType.RawFile;

        if (IsScene(assetPath))
            return AssetContentType.Scene;

        if (CanUseAsSerializedBundleEntry(assetPath, out _))
            return AssetContentType.SerializedObject;

        // 兜底：Unity 无法有效识别的文件一律按原始文件处理，不再作为 Bundle 入口。
        return AssetContentType.RawFile;
    }

    /// <summary>
    /// Unity 将 shader include 文件导入为 Editor-only ShaderInclude 对象。
    /// 这些对象绝不能传给 AssetBundleBuild.assetNames。
    /// </summary>
    public static bool IsUnsupportedAssetBundleEntry(string assetPath, out string reason)
    {
        reason = string.Empty;
        if (string.IsNullOrEmpty(assetPath))
            return false;

        string extension = Path.GetExtension(assetPath);
        if (IsUnsupportedBundleEntryExtension(extension))
        {
            reason = string.Concat("extension '", extension, "' is a shader include file.");
            return true;
        }

        Type mainType = AssetDatabase.GetMainAssetTypeAtPath(assetPath);
        if (IsShaderIncludeType(mainType))
        {
            reason = string.Concat("Unity imported it as ", mainType.Name, ".");
            return true;
        }

        UnityEngine.Object mainAsset = AssetDatabase.LoadMainAssetAtPath(assetPath);
        Type loadedType = mainAsset != null ? mainAsset.GetType() : null;
        if (IsShaderIncludeType(loadedType))
        {
            reason = string.Concat("Unity loaded it as ", loadedType.Name, ".");
            return true;
        }

        return false;
    }

    /// <summary>判断 Unity 能否把该资产作为序列化主资产加载；不能则必须走 RawFile 路线。</summary>
    public static bool CanUseAsSerializedBundleEntry(string assetPath, out string reason)
    {
        if (IsUnsupportedAssetBundleEntry(assetPath, out reason))
            return false;

        Type mainType = AssetDatabase.GetMainAssetTypeAtPath(assetPath);
        if (mainType == null || mainType == typeof(DefaultAsset))
        {
            reason = "it has no serializable Unity main asset type.";
            return false;
        }

        UnityEngine.Object mainAsset = AssetDatabase.LoadMainAssetAtPath(assetPath);
        if (mainAsset == null || mainAsset is DefaultAsset)
        {
            reason = "Unity cannot load it as a serializable main asset.";
            return false;
        }

        return true;
    }

    private static bool IsScene(string assetPath)
    {
        if (string.IsNullOrEmpty(assetPath))
            return false;

        return string.Equals(Path.GetExtension(assetPath), ".unity", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsUnsupportedBundleEntryExtension(string extension)
    {
        if (string.IsNullOrEmpty(extension))
            return false;

        for (int i = 0; i < UnsupportedBundleEntryExtensions.Length; i++)
        {
            if (string.Equals(extension, UnsupportedBundleEntryExtensions[i], StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static bool IsShaderIncludeType(Type type)
    {
        return type != null && string.Equals(type.Name, "ShaderInclude", StringComparison.Ordinal);
    }
}
