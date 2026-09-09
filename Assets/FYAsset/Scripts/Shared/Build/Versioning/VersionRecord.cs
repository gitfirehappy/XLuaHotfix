using System;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 版本号存储，仅编辑器和构建时使用
/// </summary>
public class VersionRecord : ScriptableObject
{
    [Header("当前版本号")]
    public VersionNumber CurrentVersion = new() { Major = 1, Minor = 0, Patch = 0 };
    
    [Header("上次构建时间")]
    public string LastBuildTime;
    
    [Header("当日构建次数")]
    public int DailyBuildCount;

    public VersionNumber BuildNextVersion(bool isMajor = false, bool isMinor = false, string channel = "")
    {
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

        var next = new VersionNumber
        {
            Major = CurrentVersion.Major,
            Minor = CurrentVersion.Minor,
            Patch = CurrentVersion.Patch,
            Channel = CurrentVersion.Channel
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
            Debug.LogError($"[VersionRecord] Channel '{channel}' 无效，回退为 \"\"。");
            channel = "";
        }
        next.Channel = channel ?? "";
        return next;
    }

    public void ApplyVersion(VersionNumber version)
    {
        LastBuildTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        DailyBuildCount = version.Build;
        CurrentVersion = version;

#if UNITY_EDITOR
        EditorUtility.SetDirty(this);
#endif
        Debug.Log($"[VersionRecord] 版本更新至: {CurrentVersion.GetReleaseVersionString()} (Build={DailyBuildCount})");
    }
}
