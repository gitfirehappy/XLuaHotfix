using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Entry 级加载并发契约：异步 inflight 时同步 follower 不得重复获取 Bundle，全部 handle 释放后引用计数必须归零。
/// </summary>
internal static class EntrySingleFlightTests
{
    public static Task SyncFollowerDuringAsyncInflightDoesNotDoubleAcquire()
    {
        return ScenarioTask.RunOnUnityThread(async () =>
        {
            FakeAssetBundleIO.Reset();
            FakeAssetBundleIO.Register("content.x", autoComplete: false);

            var manifest = new ABManifest()
                .AddContent("content.x", AssetContentType.SerializedObject)
                .AddAsset(new ManifestAssetEntry
                {
                    Address = "address.x",
                    AssetType = "UnityEngine.Object",
                    AssetPath = "Assets/Test/Prefab.prefab",
                    ContentIndex = 0
                });

            var backend = new ABAssetLoader(new ABBundleLoader(manifest));

            (int leaderToken, int leaderGeneration) = HandleRegistry.Alloc(
                "address.x", HandleKind.Asset, null, backend.UnloadByAddress);
            (int followerToken, int followerGeneration) = HandleRegistry.Alloc(
                "address.x", HandleKind.Asset, null, backend.UnloadByAddress);

            Task<(Object asset, string content, RuntimeMessage error)> leader =
                backend.LoadAssetTupleAsync<Object>(manifest.AssetEntries[0], manifest.GetContentForAsset(manifest.AssetEntries[0]));
            var follower = backend.LoadAssetTupleSync<Object>(manifest.AssetEntries[0], manifest.GetContentForAsset(manifest.AssetEntries[0]));

            ScenarioAssert.True(
                follower.error != null && follower.error.Code == RuntimeErrorCodes.LoadInProgress,
                "sync follower must fail fast with LoadInProgress instead of blocking or double-acquiring");
            ScenarioAssert.Equal(
                0, FakeAssetBundleIO.SyncOpenCount("content.x"),
                "同步 follower 不得重新发起物理打开");

            FakeAssetBundleIO.CompleteAll("content.x");
            var leaderResult = await ScenarioTask.WithTimeout(leader);

            ScenarioAssert.True(
                leaderResult.error == null && leaderResult.asset != null,
                "async leader must load the asset");
            ScenarioAssert.Equal(
                1, FakeAssetBundleIO.AsyncOpenCount("content.x"),
                "同一 Entry 的物理加载只允许发生一次");

            HandleRegistry.Release(leaderToken, leaderGeneration);
            ScenarioAssert.Equal(
                0, FakeAssetBundleIO.UnloadCount("content.x"),
                "仍有 handle 存活时不得卸载 Bundle");

            HandleRegistry.Release(followerToken, followerGeneration);
            ScenarioAssert.Equal(
                1, FakeAssetBundleIO.UnloadCount("content.x"),
                "全部 handle 释放后 Bundle 必须正好卸载一次");
        });
    }
}
