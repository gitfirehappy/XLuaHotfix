using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// AssetBundle 物理 I/O 替身。Bundle 只按“当前激活包根下的文件名”注册，
/// 因此物理路径既可以按名字查，也可以校验它是否来自预期包根。
/// </summary>
internal static class FakeAssetBundleIO
{
    private sealed class BundleConfig
    {
        public bool AutoComplete;
        public bool Fail;
        public bool Missing;
        public int SyncOpenCount;
        public int AsyncOpenCount;
        public int DuplicateOpenCount;
        public int UnloadCount;
        public string LastOpenPath;
        public readonly List<AssetBundleCreateRequest> PendingLocal = new();
    }

    private static readonly Dictionary<string, BundleConfig> Configs =
        new(StringComparer.OrdinalIgnoreCase);

    public static void Reset()
    {
        Configs.Clear();
        RuntimePathManager.CurrentGUIDRoot = "hotfix";
        RuntimePathManager.ActivePackageRoot = "hotfix";
    }

    /// <param name="missing">true 表示该内容在激活包根下不存在（Exists 返回 false）</param>
    public static void Register(
        string bundleName,
        bool autoComplete = true,
        bool fail = false,
        bool missing = false)
    {
        Configs[bundleName] = new BundleConfig
        {
            AutoComplete = autoComplete,
            Fail = fail,
            Missing = missing
        };
    }

    public static void SetBehavior(string bundleName, bool autoComplete, bool fail)
    {
        BundleConfig config = Get(bundleName);
        config.AutoComplete = autoComplete;
        config.Fail = fail;
    }

    public static bool Exists(string path)
    {
        string name = BundleName(path);
        return Configs.TryGetValue(name, out BundleConfig config) && !config.Missing;
    }

    public static AssetBundle LoadFromFile(string path)
    {
        BundleConfig config = Get(BundleName(path));
        config.SyncOpenCount++;
        config.LastOpenPath = path;
        if (HasPendingPhysical(config))
        {
            config.DuplicateOpenCount++;
            return null;
        }

        return config.Fail ? null : NewBundle(path);
    }

    public static AssetBundleCreateRequest LoadFromFileAsync(string path)
    {
        BundleConfig config = Get(BundleName(path));
        config.AsyncOpenCount++;
        config.LastOpenPath = path;
        if (HasPendingPhysical(config)) config.DuplicateOpenCount++;

        var request = new AssetBundleCreateRequest { Path = path };
        config.PendingLocal.Add(request);
        if (config.AutoComplete) CompleteRequest(config, request);
        return request;
    }

    public static void ForceComplete(AssetBundleCreateRequest request)
    {
        BundleConfig config = Get(BundleName(request.Path));
        CompleteRequest(config, request);
    }

    public static void CompleteAll(string bundleName)
    {
        BundleConfig config = Get(bundleName);
        AssetBundleCreateRequest[] locals = config.PendingLocal.ToArray();
        for (int i = 0; i < locals.Length; i++) CompleteRequest(config, locals[i]);
    }

    public static int SyncOpenCount(string bundleName) => Get(bundleName).SyncOpenCount;
    public static int AsyncOpenCount(string bundleName) => Get(bundleName).AsyncOpenCount;
    public static int DuplicateOpenCount(string bundleName) => Get(bundleName).DuplicateOpenCount;
    public static int UnloadCount(string bundleName) => Get(bundleName).UnloadCount;
    public static string LastOpenPath(string bundleName) => Get(bundleName).LastOpenPath;

    public static void RecordUnload(string path)
    {
        Get(BundleName(path)).UnloadCount++;
    }

    private static void CompleteRequest(BundleConfig config, AssetBundleCreateRequest request)
    {
        if (request.isDone) return;
        config.PendingLocal.Remove(request);
        request.Complete(config.Fail ? null : NewBundle(request.Path));
    }

    private static bool HasPendingPhysical(BundleConfig config)
    {
        return config.PendingLocal.Count > 0;
    }

    private static AssetBundle NewBundle(string path)
    {
        return new AssetBundle { Path = path, name = BundleName(path) };
    }

    private static BundleConfig Get(string bundleName)
    {
        if (!Configs.TryGetValue(bundleName, out BundleConfig config))
            throw new InvalidOperationException($"Bundle is not registered: {bundleName}");
        return config;
    }

    private static string BundleName(string path)
    {
        if (string.IsNullOrEmpty(path)) return string.Empty;
        string normalized = path.Replace('\\', '/').TrimEnd('/');
        int slash = normalized.LastIndexOf('/');
        return slash >= 0 ? normalized.Substring(slash + 1) : normalized;
    }
}
