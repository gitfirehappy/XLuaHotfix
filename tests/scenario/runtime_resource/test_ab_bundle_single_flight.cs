using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

internal static class ABBundleSingleFlightTests
{
    public static Task AsyncLeaderSyncFollowerUsesOnePhysicalRequest()
    {
        return RunOnUnityThread(async () =>
        {
            AssertSyncJoinDoesNotBlockOnTask();

            FakeAssetBundleIO.Reset();
            FakeAssetBundleIO.Register("shared", autoComplete: false);

            var loader = new ABBundleLoader(new ABManifest().Add("shared"));
            Task<(AssetBundle bundle, RuntimeMessage error)> leader = loader.LoadBundleAsync("shared");

            ScenarioAssert.Equal(1, FakeAssetBundleIO.AsyncOpenCount("shared"),
                "async leader must start one local physical request");
            ScenarioAssert.False(leader.IsCompleted,
                "manual local request must remain pending before the sync follower joins");

            var follower = loader.LoadBundle("shared");
            var leaderResult = await ScenarioTask.WithTimeout(leader);

            ScenarioAssert.True(follower.error == null && follower.bundle != null,
                "sync follower must complete the existing local request successfully");
            ScenarioAssert.True(leaderResult.error == null && leaderResult.bundle != null,
                "async leader must receive the shared successful result");
            ScenarioAssert.Same(follower.bundle, leaderResult.bundle,
                "sync follower and async leader must observe the same AssetBundle instance");
            ScenarioAssert.Equal(1, FakeAssetBundleIO.AsyncOpenCount("shared"),
                "sync follower must not start another async physical request");
            ScenarioAssert.Equal(0, FakeAssetBundleIO.SyncOpenCount("shared"),
                "sync follower must not call AssetBundle.LoadFromFile while async is inflight");
            ScenarioAssert.Equal(0, FakeAssetBundleIO.DuplicateOpenCount("shared"),
                "joining the request must not trigger Unity's duplicate-open condition");

            loader.UnloadBundle("shared");
            ScenarioAssert.Equal(0, FakeAssetBundleIO.UnloadCount("shared"),
                "the first of two successful acquisitions must keep the Bundle resident");
            loader.UnloadBundle("shared");
            ScenarioAssert.Equal(1, FakeAssetBundleIO.UnloadCount("shared"),
                "the shared Bundle must unload exactly once after both acquisitions release");
        });
    }

    public static Task DifferentRootsShareOneDependencyPhysicalOpen()
    {
        return RunOnUnityThread(async () =>
        {
            await SameTargetAsyncFollowersShareOnePhysicalOpen();

            FakeAssetBundleIO.Reset();
            FakeAssetBundleIO.Register("dependency", autoComplete: false);
            FakeAssetBundleIO.Register("root-a");
            FakeAssetBundleIO.Register("root-b");

            var manifest = new ABManifest()
                .Add("dependency")
                .Add("root-a", "dependency")
                .Add("root-b", "dependency");
            var loader = new ABBundleLoader(manifest);

            Task<(AssetBundle bundle, RuntimeMessage error)> first = loader.LoadBundleAsync("root-a");
            Task<(AssetBundle bundle, RuntimeMessage error)> second = loader.LoadBundleAsync("root-b");

            ScenarioAssert.Equal(1, FakeAssetBundleIO.AsyncOpenCount("dependency"),
                "different roots must join one inflight dependency request");
            ScenarioAssert.Equal(0, FakeAssetBundleIO.DuplicateOpenCount("dependency"),
                "the shared dependency must not be opened twice");

            FakeAssetBundleIO.CompleteAll("dependency");
            var results = await ScenarioTask.WithTimeout(Task.WhenAll(first, second));

            ScenarioAssert.True(results[0].error == null && results[0].bundle != null,
                "first root must load after the dependency completes");
            ScenarioAssert.True(results[1].error == null && results[1].bundle != null,
                "second root must load after the dependency completes");
            ScenarioAssert.Equal(1, FakeAssetBundleIO.AsyncOpenCount("root-a"),
                "first root must physically open once");
            ScenarioAssert.Equal(1, FakeAssetBundleIO.AsyncOpenCount("root-b"),
                "second root must physically open once");

            loader.UnloadBundle("root-a");
            ScenarioAssert.Equal(0, FakeAssetBundleIO.UnloadCount("dependency"),
                "one remaining root structural reference must keep the dependency resident");
            loader.UnloadBundle("root-b");
            ScenarioAssert.Equal(1, FakeAssetBundleIO.UnloadCount("dependency"),
                "the dependency must unload once after both root structural references release");
        });
    }

    public static Task FailedInflightFansOutAndCanRetry()
    {
        return RunOnUnityThread(async () =>
        {
            FakeAssetBundleIO.Reset();
            FakeAssetBundleIO.Register("dependency");
            FakeAssetBundleIO.Register("root", autoComplete: false, fail: true);

            var manifest = new ABManifest()
                .Add("dependency")
                .Add("root", "dependency");
            var loader = new ABBundleLoader(manifest);

            Task<(AssetBundle bundle, RuntimeMessage error)> first = loader.LoadBundleAsync("root");
            Task<(AssetBundle bundle, RuntimeMessage error)> second = loader.LoadBundleAsync("root");

            ScenarioAssert.Equal(1, FakeAssetBundleIO.AsyncOpenCount("root"),
                "followers must share the leader's failing physical request");
            ScenarioAssert.Equal(0, FakeAssetBundleIO.DuplicateOpenCount("root"),
                "a failing inflight request must still be single-flight");

            FakeAssetBundleIO.CompleteAll("root");
            var failures = await ScenarioTask.WithTimeout(Task.WhenAll(first, second));

            ScenarioAssert.True(failures[0].bundle == null && failures[0].error != null,
                "the leader must expose the physical load failure");
            ScenarioAssert.True(failures[1].bundle == null && failures[1].error != null,
                "the follower must receive the same physical load failure");
            ScenarioAssert.Equal(RuntimeErrorCodes.BundleLoadFailed, failures[0].error.Code,
                "leader failure must retain the BundleLoadFailed contract");
            ScenarioAssert.Equal(RuntimeErrorCodes.BundleLoadFailed, failures[1].error.Code,
                "follower failure must retain the BundleLoadFailed contract");
            ScenarioAssert.Same(failures[0].error, failures[1].error,
                "all inflight waiters must fan out one finalized RuntimeMessage");
            ScenarioAssert.Equal(1, FakeAssetBundleIO.UnloadCount("dependency"),
                "leader dependency ownership must roll back exactly once after shared failure");

            FakeAssetBundleIO.SetBehavior("root", autoComplete: true, fail: false);
            var retry = await ScenarioTask.WithTimeout(loader.LoadBundleAsync("root"));

            ScenarioAssert.True(retry.error == null && retry.bundle != null,
                "failed inflight state must be removed so a later request can retry");
            ScenarioAssert.Equal(2, FakeAssetBundleIO.AsyncOpenCount("root"),
                "retry must create exactly one new physical request");
            ScenarioAssert.Equal(2, FakeAssetBundleIO.AsyncOpenCount("dependency"),
                "retry must reacquire the dependency after the failed leader rolled it back");

            loader.UnloadBundle("root");
            ScenarioAssert.Equal(1, FakeAssetBundleIO.UnloadCount("root"),
                "the successful retry Bundle must unload once");
            ScenarioAssert.Equal(2, FakeAssetBundleIO.UnloadCount("dependency"),
                "the retry dependency reference must release independently of the failed attempt");

            await UwrAsyncFollowersShareOnePhysicalRequest();
            await UwrSyncFollowerIsUnsupportedAndRollsBackDependency();
        });
    }

    public static Task DiamondSucceedsAndTrueCycleFails()
    {
        return RunOnUnityThread(async () =>
        {
            FakeAssetBundleIO.Reset();
            FakeAssetBundleIO.Register("a");
            FakeAssetBundleIO.Register("b");
            FakeAssetBundleIO.Register("c");
            FakeAssetBundleIO.Register("d");

            var diamondManifest = new ABManifest()
                .Add("d")
                .Add("b", "d")
                .Add("c", "d")
                .Add("a", "b", "c");
            var diamondLoader = new ABBundleLoader(diamondManifest);

            var diamond = await ScenarioTask.WithTimeout(diamondLoader.LoadBundleAsync("a"));

            ScenarioAssert.True(diamond.error == null && diamond.bundle != null,
                "a legal dependency diamond must load successfully");
            ScenarioAssert.Equal(1, FakeAssetBundleIO.AsyncOpenCount("d"),
                "the diamond leaf must physically open once");
            ScenarioAssert.Equal(0, FakeAssetBundleIO.DuplicateOpenCount("d"),
                "the second diamond branch must reuse the loaded leaf");

            diamondLoader.UnloadBundle("a");
            ScenarioAssert.Equal(1, FakeAssetBundleIO.UnloadCount("d"),
                "both diamond structural references must balance when the root unloads");

            FakeAssetBundleIO.Reset();
            FakeAssetBundleIO.Register("cycle-a");
            FakeAssetBundleIO.Register("cycle-b");
            var cycleManifest = new ABManifest()
                .Add("cycle-a", "cycle-b")
                .Add("cycle-b", "cycle-a");

            var syncCycle = new ABBundleLoader(cycleManifest).LoadBundle("cycle-a");
            ScenarioAssert.True(syncCycle.bundle == null && syncCycle.error != null,
                "sync traversal must reject a true active-path cycle");
            ScenarioAssert.Equal(RuntimeErrorCodes.DependencyFailed, syncCycle.error.Code,
                "sync cycle must retain the DependencyFailed contract");

            var asyncCycle = await ScenarioTask.WithTimeout(
                new ABBundleLoader(cycleManifest).LoadBundleAsync("cycle-a"));
            ScenarioAssert.True(asyncCycle.bundle == null && asyncCycle.error != null,
                "async traversal must reject a true active-path cycle");
            ScenarioAssert.Equal(RuntimeErrorCodes.DependencyFailed, asyncCycle.error.Code,
                "async cycle must retain the DependencyFailed contract");
            ScenarioAssert.Equal(0, FakeAssetBundleIO.AsyncOpenCount("cycle-a"),
                "cycle detection must fail before opening the root Bundle");
            ScenarioAssert.Equal(0, FakeAssetBundleIO.AsyncOpenCount("cycle-b"),
                "cycle detection must fail before opening the dependency Bundle");
        });
    }

    private static async Task SameTargetAsyncFollowersShareOnePhysicalOpen()
    {
        FakeAssetBundleIO.Reset();
        FakeAssetBundleIO.Register("same-target", autoComplete: false);

        var loader = new ABBundleLoader(new ABManifest().Add("same-target"));
        Task<(AssetBundle bundle, RuntimeMessage error)> first = loader.LoadBundleAsync("same-target");
        Task<(AssetBundle bundle, RuntimeMessage error)> second = loader.LoadBundleAsync("same-target");

        ScenarioAssert.Equal(1, FakeAssetBundleIO.AsyncOpenCount("same-target"),
            "async followers for one target must share one physical request");
        ScenarioAssert.Equal(0, FakeAssetBundleIO.DuplicateOpenCount("same-target"),
            "same-target async followers must not trigger duplicate physical opens");

        FakeAssetBundleIO.CompleteAll("same-target");
        var results = await ScenarioTask.WithTimeout(Task.WhenAll(first, second));

        ScenarioAssert.True(results[0].error == null && results[1].error == null,
            "both same-target async callers must succeed");
        ScenarioAssert.Same(results[0].bundle, results[1].bundle,
            "both same-target async callers must receive the same Bundle instance");

        loader.UnloadBundle("same-target");
        ScenarioAssert.Equal(0, FakeAssetBundleIO.UnloadCount("same-target"),
            "one async acquisition must remain after the first release");
        loader.UnloadBundle("same-target");
        ScenarioAssert.Equal(1, FakeAssetBundleIO.UnloadCount("same-target"),
            "same-target Bundle must unload after both async acquisitions release");
    }

    private static async Task UwrAsyncFollowersShareOnePhysicalRequest()
    {
        FakeAssetBundleIO.Reset();
        FakeAssetBundleIO.Register("remote", autoComplete: false, useUwr: true);

        var loader = new ABBundleLoader(new ABManifest().Add("remote"));
        Task<(AssetBundle bundle, RuntimeMessage error)> first = loader.LoadBundleAsync("remote");
        Task<(AssetBundle bundle, RuntimeMessage error)> second = loader.LoadBundleAsync("remote");

        ScenarioAssert.Equal(1, FakeAssetBundleIO.UwrOpenCount("remote"),
            "async UWR followers must share one physical request");
        ScenarioAssert.Equal(0, FakeAssetBundleIO.DuplicateOpenCount("remote"),
            "UWR single-flight must avoid duplicate physical opens");

        FakeAssetBundleIO.CompleteAll("remote");
        var results = await ScenarioTask.WithTimeout(Task.WhenAll(first, second));

        ScenarioAssert.True(results[0].error == null && results[1].error == null,
            "both async UWR followers must succeed");
        ScenarioAssert.Same(results[0].bundle, results[1].bundle,
            "both async UWR followers must receive the same Bundle instance");

        loader.UnloadBundle("remote");
        ScenarioAssert.Equal(0, FakeAssetBundleIO.UnloadCount("remote"),
            "one UWR acquisition must remain after the first release");
        loader.UnloadBundle("remote");
        ScenarioAssert.Equal(1, FakeAssetBundleIO.UnloadCount("remote"),
            "the UWR Bundle must unload after both acquisitions release");
    }

    private static async Task UwrSyncFollowerIsUnsupportedAndRollsBackDependency()
    {
        FakeAssetBundleIO.Reset();
        FakeAssetBundleIO.Register("remote-dependency");
        FakeAssetBundleIO.Register("remote-root", autoComplete: false, useUwr: true);

        var manifest = new ABManifest()
            .Add("remote-dependency")
            .Add("remote-root", "remote-dependency");
        var loader = new ABBundleLoader(manifest);

        Task<(AssetBundle bundle, RuntimeMessage error)> leader =
            loader.LoadBundleAsync("remote-root");
        ScenarioAssert.Equal(1, FakeAssetBundleIO.UwrOpenCount("remote-root"),
            "the async leader must start one pending UWR request");

        var syncFollower = loader.LoadBundle("remote-root");

        ScenarioAssert.True(syncFollower.bundle == null && syncFollower.error != null,
            "a sync caller must fail explicitly instead of blocking on an inflight UWR request");
        ScenarioAssert.Equal(RuntimeErrorCodes.UnsupportedOperation, syncFollower.error.Code,
            "sync joining an inflight UWR request must report UnsupportedOperation");
        ScenarioAssert.Equal(1, FakeAssetBundleIO.UwrOpenCount("remote-root"),
            "the unsupported sync follower must not start a second UWR request");
        ScenarioAssert.Equal(0, FakeAssetBundleIO.DuplicateOpenCount("remote-root"),
            "the unsupported sync follower must not trigger a duplicate physical open");

        FakeAssetBundleIO.CompleteAll("remote-root");
        var leaderResult = await ScenarioTask.WithTimeout(leader);

        ScenarioAssert.True(leaderResult.error == null && leaderResult.bundle != null,
            "the original async UWR leader must remain valid after the sync follower is rejected");

        loader.UnloadBundle("remote-root");
        ScenarioAssert.Equal(1, FakeAssetBundleIO.UnloadCount("remote-root"),
            "the completed UWR leader must release its Bundle normally");
        ScenarioAssert.Equal(1, FakeAssetBundleIO.UnloadCount("remote-dependency"),
            "sync follower dependency acquisition must roll back so the leader release reaches zero");
    }

    private static void AssertSyncJoinDoesNotBlockOnTask()
    {
        string source = ScenarioSource.Read(
            "Assets/FYAsset/Scripts/AB/Runtime/Backends/ABBundleLoader.cs");
        ScenarioAssert.Contains(source, "AssetBundle.LoadFromFileAsync",
            "the async local Bundle path must preserve LoadFromFileAsync");
        ScenarioAssert.NotContains(source, ".Wait(",
            "sync single-flight joining must not block a Task with Wait");
        ScenarioAssert.NotContains(source, ".Task.Result",
            "sync single-flight joining must not block the inflight completion Task with Result");
        ScenarioAssert.NotContains(source, "GetAwaiter().GetResult",
            "sync single-flight joining must not block a Task through GetResult");
    }

    private static Task RunOnUnityThread(Func<Task> scenario)
    {
        SynchronizationContext previous = SynchronizationContext.Current;
        using var context = new PumpSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(context);

        try
        {
            Task task = scenario();
            context.RunUntilCompleted(task);
            task.GetAwaiter().GetResult();
            return Task.CompletedTask;
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    private sealed class PumpSynchronizationContext : SynchronizationContext, IDisposable
    {
        private readonly Queue<(SendOrPostCallback callback, object state)> _queue = new();
        private readonly AutoResetEvent _workAvailable = new(false);

        public override void Post(SendOrPostCallback callback, object state)
        {
            lock (_queue)
            {
                _queue.Enqueue((callback, state));
            }
            _workAvailable.Set();
        }

        public void RunUntilCompleted(Task task)
        {
            while (!task.IsCompleted)
            {
                if (TryDequeue(out var work))
                {
                    work.callback(work.state);
                    continue;
                }

                _workAvailable.WaitOne(20);
            }

            while (TryDequeue(out var remaining))
                remaining.callback(remaining.state);
        }

        public void Dispose()
        {
            _workAvailable.Dispose();
        }

        private bool TryDequeue(out (SendOrPostCallback callback, object state) work)
        {
            lock (_queue)
            {
                if (_queue.Count > 0)
                {
                    work = _queue.Dequeue();
                    return true;
                }
            }

            work = default;
            return false;
        }
    }
}
