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
        // 日期处理
        string today = DateTime.Now.ToString("yyyy-MM-dd");
        if (!string.IsNullOrEmpty(LastBuildTime) && LastBuildTime.StartsWith(today))
        {
            DailyBuildCount++;
        }
        else
        {
            DailyBuildCount = 1;
        }
        LastBuildTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

        // 版本号处理
        if (isMajor)
        {
            CurrentVersion.Major++;
            CurrentVersion.Minor = 0;
            CurrentVersion.Patch = 0;
        }
        else if (isMinor)
        {
            CurrentVersion.Minor++;
            CurrentVersion.Patch = 0;
        }
        else
        {
            CurrentVersion.Patch++;
        }

        CurrentVersion.Build = DailyBuildCount;
        if (!string.IsNullOrEmpty(channel) &&
            channel != "alpha" && channel != "beta" && channel != "rc")
        {
            Debug.LogError($"[VersionDataBase] Invalid channel '{channel}'. Fallback to \"\".");
            channel = "";
        }
        CurrentVersion.Channel = channel ?? "";

#if UNITY_EDITOR
        EditorUtility.SetDirty(this);
#endif
        Debug.Log($"[VersionDataBase] 版本更新至: {CurrentVersion.GetFullVersionString()}");
    }
}
