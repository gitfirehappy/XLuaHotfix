#if UNITY_EDITOR
using System;

/// <summary>版本候选计算结果：失败时携带可展示原因，不抛异常。</summary>
public readonly struct BuildVersionPlan
{
    public bool Success { get; }

    /// <summary>候选版本；失败时为 default。</summary>
    public VersionNumber Version { get; }

    public string Error { get; }

    private BuildVersionPlan(bool success, VersionNumber version, string error)
    {
        Success = success;
        Version = version;
        Error = error;
    }

    public static BuildVersionPlan Ok(VersionNumber version) => new(true, version, string.Empty);

    public static BuildVersionPlan Fail(string error) => new(false, default, error);
}

/// <summary>
/// 版本候选计算（纯服务）：项目版本全局唯一，Full/Standalone 推进 Major，Hotfix 推进 Patch；
/// Channel 默认继承当前成功版本，只能在构建确认时按显式选择切换，且禁止通道降级。
/// </summary>
/// <remarks>
/// 空基准表示项目尚无成功构建：Full/Standalone 从 1.0.0 开始，Hotfix 因缺少同作用域基准而拒绝。
/// 版本提交由 Summary 事务所有者完成，本服务不读写任何存储。
/// </remarks>
public static class BuildVersionPlanner
{
    /// <summary>构建确认界面可显式选择的通道；release 表示无后缀正式版。</summary>
    public static readonly string[] SelectableChannels = { "alpha", "beta", "rc", "release" };

    /// <summary>把界面通道选择映射为 VersionNumber.Channel："release" 映射为无后缀正式版。</summary>
    public static string NormalizeChannel(string selection)
    {
        if (string.IsNullOrEmpty(selection))
            return string.Empty;
        return string.Equals(selection, "release", StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : selection.ToLowerInvariant();
    }

    /// <summary>界面通道选择是否为允许值。</summary>
    public static bool IsSelectableChannel(string selection)
    {
        if (string.IsNullOrEmpty(selection))
            return false;

        for (int i = 0; i < SelectableChannels.Length; i++)
        {
            if (string.Equals(SelectableChannels[i], selection, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>
    /// 计算下一个候选版本。
    /// </summary>
    /// <param name="currentVersionText">项目当前全局成功版本文本；空表示尚无成功构建。</param>
    /// <param name="buildType">本次构建类型。</param>
    /// <param name="requestedChannel">界面显式通道选择；null 表示继承，空字符串表示显式切到正式版。</param>
    public static BuildVersionPlan Plan(
        string currentVersionText,
        BuildType buildType,
        string requestedChannel)
    {
        bool hasBaseline = VersionNumber.TryParse(currentVersionText, out VersionNumber baseline);
        if (buildType == BuildType.Hotfix && !hasBaseline)
            return BuildVersionPlan.Fail("缺少同作用域的成功 Full 基准，无法生成 Hotfix 版本。");

        string currentChannel = hasBaseline ? baseline.Channel ?? string.Empty : string.Empty;
        string targetChannel;
        if (requestedChannel == null)
        {
            // 未提供选择时继承当前通道，不得被默认参数静默清空。
            targetChannel = currentChannel;
        }
        else if (requestedChannel.Length == 0)
        {
            targetChannel = string.Empty;
        }
        else
        {
            if (!IsSelectableChannel(requestedChannel))
                return BuildVersionPlan.Fail(
                    $"Channel 只能显式选择 alpha/beta/rc/release，实际='{requestedChannel}'。");
            targetChannel = NormalizeChannel(requestedChannel);
        }

        if (hasBaseline && ChannelRank(targetChannel) < ChannelRank(currentChannel))
            return BuildVersionPlan.Fail(
                $"不允许通道降级：当前={DisplayChannel(currentChannel)}, 目标={DisplayChannel(targetChannel)}。");

        VersionNumber next;
        if (!hasBaseline)
        {
            // 项目尚无成功构建：Full/Standalone 从 1.0.0 开始（Hotfix 已在上方拒绝）。
            next = new VersionNumber { Major = 1, Minor = 0, Patch = 0 };
        }
        else if (buildType == BuildType.Hotfix)
        {
            next = new VersionNumber { Major = baseline.Major, Minor = baseline.Minor, Patch = baseline.Patch + 1 };
        }
        else
        {
            next = new VersionNumber { Major = baseline.Major + 1, Minor = 0, Patch = 0 };
        }

        next.Channel = targetChannel;

        if (hasBaseline && next.CompareTo(baseline) <= 0)
            return BuildVersionPlan.Fail("目标版本必须严格高于当前全局成功版本。");

        return BuildVersionPlan.Ok(next);
    }

    /// <summary>界面展示用的通道名；无后缀正式版展示为 release。</summary>
    public static string DisplayChannel(string channel) =>
        string.IsNullOrEmpty(channel) ? "release" : channel;

    private static int ChannelRank(string channel)
    {
        if (string.IsNullOrEmpty(channel))
            return 3;
        return channel.ToLowerInvariant() switch
        {
            "alpha" => 0,
            "beta" => 1,
            "rc" => 2,
            _ => 3
        };
    }
}
#endif
