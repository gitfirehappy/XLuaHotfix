using System;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 版本号存储，仅编辑器和构建时使用
/// </summary>
public class VersionDataBase : ScriptableObject
{
    [Header("当前版本号")]
    public VersionNumber CurrentVersion = new() { Major = 1, Minor = 0, Patch = 0 };
    
    [Header("上次构建时间")]
    public string LastBuildTime;
    
    [Header("当日构建次数")]
    public int DailyBuildCount;

    public void IncrementVersion(bool isMajor = false, bool isMinor = false, string channel = "")
    {
        ApplyVersion(BuildNextVersion(isMajor, isMinor, channel));
    }

    public VersionNumber BuildNextVersion(bool isMajor = false, bool isMinor = false, string channel = "")
    {
        // 日期处理
        string today = DateTime.Now.ToString("yyyy-MM-dd");
        int nextDailyBuildCount;
        if (!string.IsNullOrEmpty(LastBuildTime) && LastBuildTime.StartsWith(today))
        {
            nextDailyBuildCount = DailyBuildCount + 1;
        }
        else
        {
            nextDailyBuildCount = 1;
        }

        // 版本号处理
        var next = new VersionNumber
        {
            Major = CurrentVersion != null ? CurrentVersion.Major : 1,
            Minor = CurrentVersion != null ? CurrentVersion.Minor : 0,
            Patch = CurrentVersion != null ? CurrentVersion.Patch : 0,
            Channel = CurrentVersion != null ? CurrentVersion.Channel : string.Empty
        };
        if (isMajor)
        {
            next.Major++;
            next.Minor = 0;
            next.Patch = 0;
        }
        else if (isMinor)
        {
            next.Minor++;
            next.Patch = 0;
        }
        else
        {
            next.Patch++;
        }

        next.Build = nextDailyBuildCount;
        if (!string.IsNullOrEmpty(channel) &&
            channel != "alpha" && channel != "beta" && channel != "rc")
        {
            Debug.LogError($"[VersionDataBase] Invalid channel '{channel}'. Fallback to \"\".");
            channel = "";
        }
        next.Channel = channel ?? "";
        return next;
    }

    public void ApplyVersion(VersionNumber version)
    {
        if (version == null)
            throw new ArgumentNullException(nameof(version));

        LastBuildTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        DailyBuildCount = version.Build;
        CurrentVersion = version;

#if UNITY_EDITOR
        EditorUtility.SetDirty(this);
#endif
        Debug.Log($"[VersionDataBase] 版本更新至: {CurrentVersion.GetFullVersionString()}");
    }
}
