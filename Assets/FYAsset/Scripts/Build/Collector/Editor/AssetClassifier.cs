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
                if (IsScene(assetPath))
                    return EPayloadKind.Scene;
                return IsSerializedAsset(assetPath) ? EPayloadKind.Serialized : EPayloadKind.RawFile;
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

    private static bool IsSerializedAsset(string assetPath)
    {
        string extension = Path.GetExtension(assetPath);
        if (string.IsNullOrEmpty(extension))
            return false;

        switch (extension.ToLowerInvariant())
        {
            case ".prefab":
            case ".asset":
            case ".controller":
            case ".anim":
            case ".mat":
            case ".shader":
            case ".compute":
            case ".png":
            case ".jpg":
            case ".jpeg":
            case ".tga":
            case ".psd":
            case ".fbx":
            case ".mp3":
            case ".wav":
            case ".ogg":
            case ".mp4":
            case ".rendertexture":
            case ".cubemap":
            case ".spriteatlas":
                return true;
            default:
                return false;
        }
    }

    #endregion
}
