using System.Threading.Tasks;
using UnityEngine.SceneManagement;

/// <summary>
/// SceneHandle 所有权契约：卸载失败保留可重试 token，只有最后一个所有者能触发物理卸载。
/// </summary>
internal static class SceneOwnershipTests
{
    /// <summary>卸载失败时不得消费 token；失败后必须可以重试。</summary>
    public static Task FailedUnloadKeepsRetryableToken()
    {
        return ScenarioTask.RunOnUnityThread(async () =>
        {
            try
            {
                var sink = new FakeSceneUnloadSink { FailNext = true };
                SceneHandle handle = CreateHandle(sink, out _);

                SceneUnloadResult first = await handle.UnloadAsync();
                ScenarioAssert.False(first.Success, "第一次卸载失败必须返回失败结果");
                ScenarioAssert.True(first.Error != null, "失败结果必须携带原因");
                ScenarioAssert.True(handle.IsValid, "卸载失败后 token 必须保留，调用方可以重试");
                ScenarioAssert.Equal(1, sink.UnloadCalls, "失败尝试必须调用过物理卸载");

                sink.FailNext = false;
                SceneUnloadResult retry = await handle.UnloadAsync();
                ScenarioAssert.True(retry.Success, "重试必须能够完成卸载");
                ScenarioAssert.False(handle.IsValid, "卸载成功后 token 必须结算");
            }
            finally
            {
                HandleRegistry.Reset();
            }
        });
    }

    /// <summary>任一所有者不得在其它所有者存活时卸载共享场景。</summary>
    public static Task OtherOwnersKeepSceneAlive()
    {
        return ScenarioTask.RunOnUnityThread(async () =>
        {
            try
            {
                var sink = new FakeSceneUnloadSink();
                SceneHandle original = CreateHandle(sink, out _);
                SceneHandle retained = original.Retain();

                SceneUnloadResult rejected = await retained.UnloadAsync();
                ScenarioAssert.False(rejected.Success, "仍有其它所有者时不得物理卸载");
                ScenarioAssert.Equal(2, rejected.ActiveOwners, "拒绝结果必须给出活跃所有者数量");
                ScenarioAssert.Equal(0, sink.UnloadCalls, "不得调用物理卸载");
                ScenarioAssert.True(original.IsValid, "一个所有者不得使其它 token 失效");
                ScenarioAssert.True(retained.IsValid, "被拒绝的卸载不得消费当前 token");

                original.Release();
                SceneUnloadResult lastOwnerUnloads = await retained.UnloadAsync();
                ScenarioAssert.True(lastOwnerUnloads.Success, "最后一个所有者必须能够完成卸载");
                ScenarioAssert.Equal(1, sink.UnloadCalls, "最后一个所有者只触发一次物理卸载");
            }
            finally
            {
                HandleRegistry.Reset();
            }
        });
    }

    private static SceneHandle CreateHandle(IABSceneUnloadSink sink, out Scene scene)
    {
        (int token, int generation) = HandleRegistry.Alloc(
            "scene.entry", HandleKind.Scene, "battle.content", null, null);
        scene = new Scene { path = "Assets/Scenes/Battle.unity", isLoaded = true };
        return new SceneHandle(token, generation, "Battle", scene.path, scene, sink);
    }
}

/// <summary>可控的 Scene 卸载出口替身：只记录调用并返回预设结果。</summary>
internal sealed class FakeSceneUnloadSink : IABSceneUnloadSink
{
    public bool FailNext;
    public int UnloadCalls;

    public Task<RuntimeMessage> UnloadSceneAsync(Scene scene, string scenePath)
    {
        UnloadCalls++;
        if (FailNext)
            return Task.FromResult(RuntimeMessage.Error(
                RuntimeErrorCodes.LoadFailed, "simulated scene unload failure"));

        return Task.FromResult<RuntimeMessage>(null);
    }
}
