using System;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 资产分类器 —— 根据采集器配置和资源路径推导资产角色与载荷类型。
/// </summary>
public static class AssetClassifier
{
    private static readonly string[] UnsupportedBundleEntryExtensions =
    {
        ".cginc",
        ".hlsl",
        ".hlslinc"
    };

    #region 公共方法

    /// <summary>
    /// 根据路径和采集器配置生成分类结果。
    /// </summary>
    public static AssetClassification Classify(string assetPath, ECollectorType collectorType, EForcePayloadKind forcePayloadKind)
    {
        return new AssetClassification
        {
            Role = MapRole(collectorType),
            PayloadKind = ResolvePayloadKind(assetPath, forcePayloadKind)
        };
    }

    /// <summary>
    /// Unity imports shader include files as editor-only ShaderInclude objects.
    /// They must never be passed to AssetBundleBuild.assetNames.
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

    #endregion

    #region 私有方法

    private static EAssetRole MapRole(ECollectorType collectorType)
    {
        switch (collectorType)
        {
            case ECollectorType.Main:
                return EAssetRole.Main;
            case ECollectorType.Static:
                return EAssetRole.Static;
            case ECollectorType.Depend:
                return EAssetRole.Depend;
            default:
                throw new ArgumentOutOfRangeException(nameof(collectorType), collectorType, "不支持的采集器类型。");
        }
    }

    private static EPayloadKind ResolvePayloadKind(string assetPath, EForcePayloadKind forcePayloadKind)
    {
        switch (forcePayloadKind)
        {
            case EForcePayloadKind.Serialized:
                return EPayloadKind.Serialized;
            case EForcePayloadKind.RawFile:
                return EPayloadKind.RawFile;
            case EForcePayloadKind.Scene:
                return EPayloadKind.Scene;
            case EForcePayloadKind.Auto:
                if (IsScene(assetPath))
                    return EPayloadKind.Scene;
                return HasUsableImportedAsset(assetPath) ? EPayloadKind.Serialized : EPayloadKind.RawFile;
            default:
                throw new ArgumentOutOfRangeException(nameof(forcePayloadKind), forcePayloadKind, "不支持的载荷类型覆盖值。");
        }
    }

    private static bool IsScene(string assetPath)
    {
        if (string.IsNullOrEmpty(assetPath))
            return false;

        return string.Equals(Path.GetExtension(assetPath), ".unity", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasUsableImportedAsset(string assetPath)
    {
        if (string.IsNullOrEmpty(assetPath))
            return false;

        Type mainType = AssetDatabase.GetMainAssetTypeAtPath(assetPath);
        if (mainType == null || mainType == typeof(DefaultAsset))
            return false;

        UnityEngine.Object mainAsset = AssetDatabase.LoadMainAssetAtPath(assetPath);
        if (mainAsset == null || mainAsset is DefaultAsset)
            return false;

        return true;
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

    #endregion
}
