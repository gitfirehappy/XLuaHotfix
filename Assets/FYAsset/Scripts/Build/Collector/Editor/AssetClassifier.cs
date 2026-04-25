using System;
using System.IO;

/// <summary>
/// 资产分类器 —— 根据采集器配置和资源路径推导资产角色与载荷类型。
/// </summary>
public static class AssetClassifier
{
    #region Public Methods

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

    #region Private Methods

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
                return IsScene(assetPath) ? EPayloadKind.Scene : EPayloadKind.Serialized;
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

    #endregion
}
