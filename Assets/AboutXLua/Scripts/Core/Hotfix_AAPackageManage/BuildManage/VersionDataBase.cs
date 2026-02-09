using System;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 版本号存储，仅编辑器和构建时使用
/// </summary>
[CreateAssetMenu(fileName = "VersionDataBase", menuName = "Build/VersionDataBase", order = 1)]
public class VersionDataBase : ScriptableObject
{
    [Header("当前版本号")]
    public VersionNumber CurrentVersion = new() { Major = 1, Minor = 0, Patch = 0 };
    
    [Header("上次构建时间")]
    public string LastBuildTime;
    
    [Header("当日构建次数")]
    public int DailyBuildCount;
    
    public void IncrementVersion(bool isMajor = false, bool isMinor = false)
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
#if UNITY_EDITOR
        EditorUtility.SetDirty(this);
#endif        
        Debug.Log($"[VersionDataBase] 版本更新至: {CurrentVersion.GetVersionString()} (构建 #{DailyBuildCount})");
    }
}

/// <summary>
/// 版本号数据类型，整个项目统一使用
/// </summary>
[Serializable]
public class VersionNumber
{
    public int Major;
    public int Minor;
    public int Patch;
    
    public string GetVersionString() => $"{Major}.{Minor}.{Patch}";
    
    public override bool Equals(object obj)
    {
        if (obj is VersionNumber other)
        {
            return Major == other.Major && Minor == other.Minor && Patch == other.Patch;
        }
        return false;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Major, Minor, Patch);
    }

    public static bool operator ==(VersionNumber a, VersionNumber b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (ReferenceEquals(a, null) || ReferenceEquals(b, null)) return false;
        return a.Equals(b);
    }

    public static bool operator !=(VersionNumber a, VersionNumber b) => !(a == b);
}