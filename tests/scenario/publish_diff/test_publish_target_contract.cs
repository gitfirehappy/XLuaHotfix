using System;

internal static class PublishTargetContractTests
{
    public static void Run()
    {
        string localId = "11111111-1111-1111-1111-111111111111";
        string cloudId = "22222222-2222-2222-2222-222222222222";
        FYAssetSettings.Instance.PushTargets.Clear();
        FYAssetSettings.Instance.PushTargets.Add(new PushTargetConfig
        {
            TargetId = localId,
            Name = "local",
            Type = PushTargetType.LocalDirectory,
            PublicBaseUrl = "https://example.com/root///"
        });
        FYAssetSettings.Instance.PushTargets.Add(new PushTargetConfig
        {
            TargetId = cloudId,
            Name = "cloudflare",
            Type = PushTargetType.CloudflarePages,
            PublicBaseUrl = "https://cdn.example.com/"
        });
        FYAssetSettings.Instance.CurrentABTargetId = cloudId;

        Check.True(ABHotfixTargetResolver.TryResolveUrl(out string url, out string error), error);
        Check.Equal("https://cdn.example.com/AB/", url, "AB URL must use the selected target");
        Check.True(FYAssetSettings.Instance.PushTargets[0].TryNormalizePublicBaseUrl(
            out string normalized, out error), error);
        Check.Equal("https://example.com/root/", normalized, "base URL trailing slashes must normalize");

        FYAssetSettings.Instance.CurrentABTargetId = "missing";
        Check.True(!ABHotfixTargetResolver.TryResolveUrl(out _, out _),
            "missing current target must block without fallback");

        FYAssetSettings.Instance.CurrentABTargetId = localId;
        FYAssetSettings.Instance.PushTargets[0].PublicBaseUrl = "C:/hotfix";
        Check.True(!ABHotfixTargetResolver.TryResolveUrl(out _, out _),
            "local path must not be accepted as public URL");
        FYAssetSettings.Instance.PushTargets[0].PublicBaseUrl = "https://example.com/root?token=1";
        Check.True(!ABHotfixTargetResolver.TryResolveUrl(out _, out _),
            "query strings must not be accepted in public URL");
        FYAssetSettings.Instance.PushTargets[0].PublicBaseUrl = "https://example.com/root#fragment";
        Check.True(!ABHotfixTargetResolver.TryResolveUrl(out _, out _),
            "URL fragments must not be accepted in public URL");

        FYAssetSettings.Instance.PushTargets[0].PublicBaseUrl = "https://example.com/root";
        FYAssetSettings.Instance.PushTargets[1].Name = "LOCAL";
        Check.True(!ABHotfixTargetResolver.TryResolve(out _, out _),
            "target names must be unique case insensitively");
        FYAssetSettings.Instance.PushTargets[1].Name = "cloudflare";
        FYAssetSettings.Instance.PushTargets[1].TargetId = localId;
        Check.True(!ABHotfixTargetResolver.TryResolve(out _, out _),
            "target IDs must be unique");
    }
}
