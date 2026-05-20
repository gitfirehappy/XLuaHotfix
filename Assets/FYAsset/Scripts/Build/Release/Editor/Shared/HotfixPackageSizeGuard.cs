#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 热更包大小校验器，检验其是否超过阈值
/// </summary>
public static class HotfixPackageSizeGuard
{
    public static bool ValidateOrAbort(long totalSizeBytes, string source)
    {
        long maxSizeBytes = FYAssetSettings.Instance.MaxHotfixSizeBytes;
        if (maxSizeBytes <= 0 || totalSizeBytes < maxSizeBytes)
            return true;

        string owner = string.IsNullOrEmpty(source) ? "HotfixPackageSizeGuard" : source;
        Debug.LogWarning($"[{owner}] 热更包大小过大，需缩减大小: {totalSizeBytes} >= {maxSizeBytes}");

        if (Application.isBatchMode)
        {
            Debug.LogWarning($"[{owner}] BatchMode 下已阻断构建：热更包大小超过阈值。请缩减资源后重试。");
            throw new Exception("热更包大小超过阈值");
        }

        EditorUtility.DisplayDialog(
            "热更包过大",
            $"热更包大小 ({ToMB(totalSizeBytes)} MB) 已超过阈值 ({ToMB(maxSizeBytes)} MB)。请缩减资源大小。",
            "OK");
        return false;
    }

    private static long ToMB(long bytes)
    {
        return bytes / (1024 * 1024);
    }
}
#endif
