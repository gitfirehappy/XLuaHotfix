/// <summary>
/// AB 当前发布目标解析器。
/// </summary>
/// <remarks>
/// AB 的客户端地址从 FYAssetSettings.CurrentABTargetId 解析。
/// 缺失目标、重复身份和非法公开地址均直接失败，不使用列表首项或旧配置回退。
/// </remarks>
public static class ABHotfixTargetResolver
{
    public static bool TryResolve(out PublishTargetConfig target, out string error)
    {
        FYAssetSettings settings = FYAssetSettings.Instance;
        if (settings == null)
        {
            target = null;
            error = "FYAssetSettings 不可用。";
            return false;
        }

        return settings.TryResolvePublishTarget(
            settings.CurrentABTargetId,
            out target,
            out error);
    }

    public static bool TryResolveUrl(out string url, out string error)
    {
        url = string.Empty;
        if (!TryResolve(out PublishTargetConfig target, out error))
            return false;

        return target.TryGetHotfixUrl("AB", out url, out error);
    }
}
