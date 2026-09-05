using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

internal static class FakeAssetBundleIO
{
    private sealed class BundleConfig
    {
        public bool AutoComplete;
        public bool Fail;
        public bool UseUwr;
        public int SyncOpenCount;
        public int AsyncOpenCount;
        public int UwrOpenCount;
        public int DuplicateOpenCount;
        public int UnloadCount;
        public readonly List<AssetBundleCreateRequest> PendingLocal = new();
        public readonly List<UnityEngine.Networking.UnityWebRequest> PendingUwr = new();
    }

    private static readonly Dictionary<string, BundleConfig> Configs =
        new(StringComparer.OrdinalIgnoreCase);

    public static void Reset()
    {
        Configs.Clear();
        RuntimePathManager.CurrentGUIDRoot = "hotfix";
        Application.streamingAssetsPath = "streaming";
    }

    public static void Register(string bundleName, bool autoComplete = true, bool fail = false, bool useUwr = false)
    {
        Configs[bundleName] = new BundleConfig
        {
            AutoComplete = autoComplete,
            Fail = fail,
            UseUwr = useUwr
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
        return Configs.TryGetValue(name, out BundleConfig config) && !config.UseUwr;
    }

    public static AssetBundle LoadFromFile(string path)
    {
        BundleConfig config = Get(BundleName(path));
        config.SyncOpenCount++;
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
        if (HasPendingPhysical(config)) config.DuplicateOpenCount++;

        var request = new AssetBundleCreateRequest { Path = path };
        config.PendingLocal.Add(request);
        if (config.AutoComplete) CompleteRequest(config, request);
        return request;
    }

    public static UnityEngine.Networking.UnityWebRequest LoadFromUwr(string path)
    {
        BundleConfig config = Get(BundleName(path));
        config.UwrOpenCount++;
        if (HasPendingPhysical(config)) config.DuplicateOpenCount++;

        var request = new UnityEngine.Networking.UnityWebRequest(path);
        config.PendingUwr.Add(request);
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

        UnityEngine.Networking.UnityWebRequest[] uwrs = config.PendingUwr.ToArray();
        for (int i = 0; i < uwrs.Length; i++) CompleteRequest(config, uwrs[i]);
    }

    public static int SyncOpenCount(string bundleName) => Get(bundleName).SyncOpenCount;
    public static int AsyncOpenCount(string bundleName) => Get(bundleName).AsyncOpenCount;
    public static int UwrOpenCount(string bundleName) => Get(bundleName).UwrOpenCount;
    public static int DuplicateOpenCount(string bundleName) => Get(bundleName).DuplicateOpenCount;
    public static int UnloadCount(string bundleName) => Get(bundleName).UnloadCount;

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

    private static void CompleteRequest(BundleConfig config, UnityEngine.Networking.UnityWebRequest request)
    {
        if (request.IsDone) return;
        config.PendingUwr.Remove(request);
        request.Complete(config.Fail ? null : NewBundle(request.Path), config.Fail);
    }

    private static bool HasPendingPhysical(BundleConfig config)
    {
        return config.PendingLocal.Count > 0 || config.PendingUwr.Count > 0;
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

namespace UnityEngine.Networking
{
    public sealed class UnityWebRequest : IDisposable
    {
        private readonly TaskCompletionSource<bool> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal UnityWebRequest(string path)
        {
            Path = path;
        }

        internal string Path { get; }
        internal AssetBundle Bundle { get; private set; }
        internal bool IsDone { get; private set; }

        public Result result { get; private set; } = Result.InProgress;
        public string error { get; private set; }

        public Task SendWebRequest() => _completion.Task;

        internal void Complete(AssetBundle bundle, bool failed)
        {
            if (IsDone) return;
            IsDone = true;
            Bundle = bundle;
            result = failed ? Result.ConnectionError : Result.Success;
            error = failed ? "fake UWR failure" : null;
            _completion.TrySetResult(true);
        }

        public void Dispose() { }

        public enum Result
        {
            InProgress,
            Success,
            ConnectionError
        }
    }

    public static class UnityWebRequestAssetBundle
    {
        public static UnityWebRequest GetAssetBundle(string path)
        {
            return FakeAssetBundleIO.LoadFromUwr(path);
        }
    }

    public static class DownloadHandlerAssetBundle
    {
        public static AssetBundle GetContent(UnityWebRequest request)
        {
            return request.Bundle;
        }
    }
}
