#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

/// <summary>
/// baseline 滚动存储：{Platform}[-{Channel}]/{AA|AB}/baseline.json，VCS 跟踪，原子写入。
/// 只在构建+发布全部成功后写入（Latest=本次交付，Full 交付同时更新 LatestFull）。
/// </summary>
public static class BuildBaselineStore
{
    private const string RootDirName = "BuildData/Baselines";
    private const string FileName = "baseline.json";

    /// <summary>channel key 与原 Repository 布局一致：同时隔离 BuildTarget、业务 channel 和 backend。</summary>
    public static string GetChannelKey(VersionNumber version, string backendKey)
    {
        string buildTarget = EditorUserBuildSettings.activeBuildTarget.ToString();
        string channelRoot = string.IsNullOrEmpty(version?.Channel)
            ? buildTarget
            : $"{buildTarget}-{version.Channel}";
        return $"{channelRoot}/{backendKey}";
    }

    public static string GetChannelKey(string channel, string backendKey)
    {
        return GetChannelKey(new VersionNumber { Channel = channel }, backendKey);
    }

    public static bool Exists(string channelKey)
    {
        return FileHelper.Exists(GetPath(channelKey));
    }

    public static BuildBaseline LoadLatest(string channelKey) => Load(channelKey).Latest;

    public static BuildBaseline LoadLatestFull(string channelKey) => Load(channelKey).LatestFull;

    /// <summary>加载双槽状态。文件缺失=无历史（无迁移：旧机制数据已在 aa-ab-decoupling R6 删除）；损坏=致命 BuildBaselineException。</summary>
    public static BuildBaselineState Load(string channelKey)
    {
        string path = GetPath(channelKey);
        if (!FileHelper.Exists(path))
            return new BuildBaselineState();

        try
        {
            string json = FileHelper.ReadAllText(path);
            return JsonUtility.FromJson<BuildBaselineState>(json) ?? new BuildBaselineState();
        }
        catch (Exception ex)
        {
            throw new BuildBaselineException($"baseline 文件损坏: {path} — {ex.Message}");
        }
    }

    /// <summary>写入新交付 baseline（仅在构建+发布全部成功后调用）。</summary>
    public static void Save(string channelKey, BuildBaseline delivered)
    {
        if (delivered == null)
            throw new ArgumentNullException(nameof(delivered));

        BuildBaselineState state;
        try
        {
            state = Load(channelKey);
        }
        catch (BuildBaselineException)
        {
            // 旧文件损坏不等于放弃交付：以全新状态重写，并在日志中显式说明。
            Debug.LogWarning($"[BuildBaselineStore] 旧 baseline 损坏，将以本次交付重建。Channel={channelKey}");
            state = new BuildBaselineState();
        }

        // Hotfix 交付的 parent 锁定为交付时刻的 LatestFull；Full 交付无 parent。
        // BuildType 枚举在 Editor 程序集，这里只比较已序列化的字符串。
        const string fullBuildType = "Full";
        if (!string.Equals(delivered.BuildType, fullBuildType, StringComparison.Ordinal)
            && string.IsNullOrEmpty(delivered.ParentVersion))
        {
            delivered.ParentVersion = state.LatestFull?.Version != null
                ? state.LatestFull.Version.GetReleaseVersionString()
                : string.Empty;
        }

        state.Latest = delivered;
        if (string.Equals(delivered.BuildType, fullBuildType, StringComparison.Ordinal))
            state.LatestFull = delivered;

        string path = GetPath(channelKey);
        string json = JsonUtility.ToJson(state, true);
        FileHelper.WriteAllTextAtomic(path, json);
        Debug.Log($"[BuildBaselineStore] baseline 已更新: Channel={channelKey}, Version={delivered.Version?.GetReleaseVersionString()}, Type={delivered.BuildType}");
    }

    /// <summary>测试专用：删除 channel 的 baseline 文件。</summary>
    public static void ClearForTest(string channelKey)
    {
        FileHelper.TryDelete(GetPath(channelKey));
    }

    private static string GetPath(string channelKey)
    {
        return FYAssetPathUtility.JoinFilePath(
            BuildPathManager.ProjectRoot, RootDirName, channelKey, FileName);
    }
}
#endif
