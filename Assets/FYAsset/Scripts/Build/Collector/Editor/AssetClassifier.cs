using System;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 资产分类器 —— 根据采集器配置和资源路径推导资产角色与载荷类型。
/// </summary>
public static class AssetClassifier
{
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

    #endregion
}
