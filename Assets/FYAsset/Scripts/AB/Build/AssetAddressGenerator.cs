using System;
using System.IO;

/// <summary>
/// 资产 Address 生成器。未保存显式 Address 的资源使用完整 Assets 路径；
/// 用户可在 Details 中将生成结果固化为短名或长路径。
/// </summary>
public static class AssetAddressGenerator
{
    public static string GenerateAddress(string assetPath, string primaryType, AssetAddressStyle style)
    {
        if (string.IsNullOrEmpty(assetPath))
            throw new ArgumentException("Asset path 不能为 null 或空。", nameof(assetPath));

        switch (style)
        {
            case AssetAddressStyle.ShortName:
                return Path.GetFileNameWithoutExtension(assetPath);
            case AssetAddressStyle.LongAssetPath:
            default:
                return GenerateLongAssetPath(assetPath);
        }
    }

    private static string GenerateLongAssetPath(string assetPath)
    {
        string normalized = FYAssetPathUtility.NormalizeAssetPath(assetPath);
        if (string.IsNullOrEmpty(normalized))
            throw new ArgumentException("Asset path 不能为 null 或空。", nameof(assetPath));

        return normalized;
    }
}
