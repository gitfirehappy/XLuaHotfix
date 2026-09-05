using System;
using System.Threading.Tasks;
using UnityEngine;

internal static class AAHandleTicketLifetimeTests
{
    public static async Task SequentialLoadsRetainTwoTickets()
    {
        FakeAddressables.Reset();
        await EnsureManagerInitialized();

        const string address = "aa-ticket-sequential";
        var expected = new TicketAssetA { name = "sequential" };
        FakeAddressables.Register(address, expected);

        var first = await AAPackageManager.Instance.LoadAssetAsync<TicketAssetA>(address);
        var second = await AAPackageManager.Instance.LoadAssetAsync<TicketAssetA>(address);

        ScenarioAssert.True(first.error == null, "first sequential AA load should succeed");
        ScenarioAssert.True(second.error == null, "second sequential AA load should succeed");
        ScenarioAssert.Same(expected, first.asset, "first sequential load should return the registered asset");
        ScenarioAssert.Same(expected, second.asset, "second sequential load should return the registered asset");
        ScenarioAssert.Equal(2, FakeAddressables.LoadCalls<TicketAssetA>(address),
            "every successful manager load must acquire its own Addressables ticket");
        ScenarioAssert.Equal(2, FakeAddressables.Outstanding<TicketAssetA>(address),
            "two successful loads must leave two Addressables references outstanding");

        AAPackageManager.Instance.UnloadAsset<TicketAssetA>(address);
        ScenarioAssert.Equal(1, FakeAddressables.ReleaseCalls<TicketAssetA>(address),
            "first unload should release exactly one ticket");
        ScenarioAssert.Equal(1, FakeAddressables.Outstanding<TicketAssetA>(address),
            "first unload must preserve the second load ticket");

        AAPackageManager.Instance.UnloadAsset<TicketAssetA>(address);
        ScenarioAssert.Equal(2, FakeAddressables.ReleaseCalls<TicketAssetA>(address),
            "second unload should release the remaining ticket");
        ScenarioAssert.Equal(0, FakeAddressables.Outstanding<TicketAssetA>(address),
            "balanced unloads must release all Addressables references");

        AAPackageManager.Instance.UnloadAsset<TicketAssetA>(address);
        ScenarioAssert.Equal(2, FakeAddressables.ReleaseCalls<TicketAssetA>(address),
            "an unmatched unload must not release another ticket");

        SourceUsesAddressablesHandleTickets();
    }

    public static async Task ConcurrentLoadsDoNotOverwriteTickets()
    {
        await ConcurrentAsyncLoadsRetainBothTickets();
        await SyncAndAsyncLoadsRetainBothTickets();
        await SameAddressDifferentTypesRemainIndependent();
    }

    public static async Task FailedTicketsAreReleasedAndNotRetained()
    {
        await FailedAsyncTicketIsReleased();
        FailedSyncTicketIsReleased();
        await ConcurrentFailedTicketsAreEachReleased();
        await SuccessfulNullResultIsReleased();
    }

    private static async Task ConcurrentAsyncLoadsRetainBothTickets()
    {
        FakeAddressables.Reset();
        await EnsureManagerInitialized();

        const string address = "aa-ticket-concurrent";
        var expected = new TicketAssetA { name = "concurrent" };
        FakeAddressables.Register(address, expected, autoComplete: false);

        Task<(TicketAssetA asset, RuntimeMessage error)> firstTask =
            AAPackageManager.Instance.LoadAssetAsync<TicketAssetA>(address);
        Task<(TicketAssetA asset, RuntimeMessage error)> secondTask =
            AAPackageManager.Instance.LoadAssetAsync<TicketAssetA>(address);

        ScenarioAssert.Equal(2, FakeAddressables.LoadCalls<TicketAssetA>(address),
            "overlapping async loads must each acquire an Addressables handle");
        ScenarioAssert.Equal(2, FakeAddressables.Outstanding<TicketAssetA>(address),
            "both async acquisitions must exist before completion");

        FakeAddressables.Complete<TicketAssetA>(address);
        var first = await ScenarioTask.WithTimeout(firstTask);
        var second = await ScenarioTask.WithTimeout(secondTask);

        ScenarioAssert.True(first.error == null && second.error == null,
            "both overlapping async loads should succeed");
        ScenarioAssert.Same(expected, first.asset, "first async result should match the registered asset");
        ScenarioAssert.Same(expected, second.asset, "second async result should match the registered asset");

        AAPackageManager.Instance.UnloadAsset<TicketAssetA>(address);
        ScenarioAssert.Equal(1, FakeAddressables.Outstanding<TicketAssetA>(address),
            "one concurrent ticket must remain after the first unload");
        AAPackageManager.Instance.UnloadAsset<TicketAssetA>(address);
        ScenarioAssert.Equal(0, FakeAddressables.Outstanding<TicketAssetA>(address),
            "both concurrent tickets must be releasable");
        ScenarioAssert.Equal(2, FakeAddressables.ReleaseCalls<TicketAssetA>(address),
            "concurrent completion must not overwrite an unload ticket");
    }

    private static async Task SyncAndAsyncLoadsRetainBothTickets()
    {
        FakeAddressables.Reset();
        await EnsureManagerInitialized();

        const string address = "aa-ticket-sync-async";
        var expected = new TicketAssetA { name = "sync-async" };
        FakeAddressables.Register(address, expected, autoComplete: false);

        Task<(TicketAssetA asset, RuntimeMessage error)> asyncTask =
            AAPackageManager.Instance.LoadAssetAsync<TicketAssetA>(address);
        var syncResult = AAPackageManager.Instance.LoadAssetSync<TicketAssetA>(address);
        var asyncResult = await ScenarioTask.WithTimeout(asyncTask);

        ScenarioAssert.True(syncResult.error == null, "overlapping sync load should succeed");
        ScenarioAssert.True(asyncResult.error == null, "overlapping async load should succeed");
        ScenarioAssert.Same(expected, syncResult.asset, "sync result should match the registered asset");
        ScenarioAssert.Same(expected, asyncResult.asset, "async result should match the registered asset");
        ScenarioAssert.Equal(2, FakeAddressables.LoadCalls<TicketAssetA>(address),
            "sync and async callers must each acquire a ticket");
        ScenarioAssert.Equal(2, FakeAddressables.Outstanding<TicketAssetA>(address),
            "sync/async overlap must retain both Addressables references");

        AAPackageManager.Instance.UnloadAsset<TicketAssetA>(address);
        ScenarioAssert.Equal(1, FakeAddressables.Outstanding<TicketAssetA>(address),
            "first sync/async unload should leave one ticket");
        AAPackageManager.Instance.UnloadAsset<TicketAssetA>(address);
        ScenarioAssert.Equal(0, FakeAddressables.Outstanding<TicketAssetA>(address),
            "second sync/async unload should release the final ticket");
        ScenarioAssert.Equal(2, FakeAddressables.ReleaseCalls<TicketAssetA>(address),
            "sync/async tickets must both be released exactly once");
    }

    private static async Task SameAddressDifferentTypesRemainIndependent()
    {
        FakeAddressables.Reset();
        await EnsureManagerInitialized();

        const string address = "aa-ticket-shared-address";
        var assetA = new TicketAssetA { name = "type-a" };
        var assetB = new TicketAssetB { name = "type-b" };
        FakeAddressables.Register(address, assetA);
        FakeAddressables.Register(address, assetB);

        var resultA = AAPackageManager.Instance.LoadAssetSync<TicketAssetA>(address);
        var resultB = await AAPackageManager.Instance.LoadAssetAsync<TicketAssetB>(address);

        ScenarioAssert.True(resultA.error == null && resultB.error == null,
            "the same address should load independently for two requested types");
        ScenarioAssert.Same(assetA, resultA.asset, "type A result should remain typed");
        ScenarioAssert.Same(assetB, resultB.asset, "type B result should remain typed");

        AAPackageManager.Instance.UnloadAsset<TicketAssetA>(address);
        ScenarioAssert.Equal(0, FakeAddressables.Outstanding<TicketAssetA>(address),
            "unloading type A should release only the type A ticket");
        ScenarioAssert.Equal(1, FakeAddressables.Outstanding<TicketAssetB>(address),
            "unloading type A must not consume the type B ticket");

        AAPackageManager.Instance.UnloadAsset<TicketAssetB>(address);
        ScenarioAssert.Equal(0, FakeAddressables.Outstanding<TicketAssetB>(address),
            "type B should release through its own typed ticket stack");
    }

    private static async Task FailedAsyncTicketIsReleased()
    {
        FakeAddressables.Reset();
        await EnsureManagerInitialized();

        const string address = "aa-ticket-failed-async";
        FakeAddressables.Register<TicketAssetA>(address, null, fail: true);

        var result = await AAPackageManager.Instance.LoadAssetAsync<TicketAssetA>(address);

        ScenarioAssert.True(result.asset == null && result.error != null,
            "failed async loads should return a structured failure");
        ScenarioAssert.Equal(1, FakeAddressables.ReleaseCalls<TicketAssetA>(address),
            "a failed async handle must be released immediately");
        ScenarioAssert.Equal(0, FakeAddressables.Outstanding<TicketAssetA>(address),
            "failed async handles must not remain outstanding");

        AAPackageManager.Instance.UnloadAsset<TicketAssetA>(address);
        ScenarioAssert.Equal(1, FakeAddressables.ReleaseCalls<TicketAssetA>(address),
            "failed async handles must not be retained as unload tickets");
    }

    private static void FailedSyncTicketIsReleased()
    {
        const string address = "aa-ticket-failed-sync";
        FakeAddressables.Register<TicketAssetA>(address, null, fail: true);

        var result = AAPackageManager.Instance.LoadAssetSync<TicketAssetA>(address);

        ScenarioAssert.True(result.asset == null && result.error != null,
            "failed sync loads should return a structured failure");
        ScenarioAssert.Equal(1, FakeAddressables.ReleaseCalls<TicketAssetA>(address),
            "a failed sync handle must be released immediately");
        ScenarioAssert.Equal(0, FakeAddressables.Outstanding<TicketAssetA>(address),
            "failed sync handles must not remain outstanding");

        AAPackageManager.Instance.UnloadAsset<TicketAssetA>(address);
        ScenarioAssert.Equal(1, FakeAddressables.ReleaseCalls<TicketAssetA>(address),
            "failed sync handles must not be retained as unload tickets");
    }

    private static async Task ConcurrentFailedTicketsAreEachReleased()
    {
        const string address = "aa-ticket-failed-concurrent";
        FakeAddressables.Register<TicketAssetA>(address, null, autoComplete: false, fail: true);

        Task<(TicketAssetA asset, RuntimeMessage error)> firstTask =
            AAPackageManager.Instance.LoadAssetAsync<TicketAssetA>(address);
        Task<(TicketAssetA asset, RuntimeMessage error)> secondTask =
            AAPackageManager.Instance.LoadAssetAsync<TicketAssetA>(address);

        FakeAddressables.Complete<TicketAssetA>(address);
        var first = await ScenarioTask.WithTimeout(firstTask);
        var second = await ScenarioTask.WithTimeout(secondTask);

        ScenarioAssert.True(first.error != null && second.error != null,
            "all failed concurrent callers should receive a structured failure");
        ScenarioAssert.Equal(2, FakeAddressables.ReleaseCalls<TicketAssetA>(address),
            "each failed concurrent acquisition must release its own handle");
        ScenarioAssert.Equal(0, FakeAddressables.Outstanding<TicketAssetA>(address),
            "failed concurrent acquisitions must leave no outstanding references");

        AAPackageManager.Instance.UnloadAsset<TicketAssetA>(address);
        ScenarioAssert.Equal(2, FakeAddressables.ReleaseCalls<TicketAssetA>(address),
            "failed concurrent handles must not enter the ticket stack");
    }

    private static async Task SuccessfulNullResultIsReleased()
    {
        const string address = "aa-ticket-null-result";
        FakeAddressables.Register<TicketAssetA>(address, null);

        var result = await AAPackageManager.Instance.LoadAssetAsync<TicketAssetA>(address);

        ScenarioAssert.True(result.asset == null && result.error != null,
            "a succeeded operation with a null result should be treated as a load failure");
        ScenarioAssert.Equal(1, FakeAddressables.ReleaseCalls<TicketAssetA>(address),
            "a null-result handle must be released immediately");
        ScenarioAssert.Equal(0, FakeAddressables.Outstanding<TicketAssetA>(address),
            "a null-result handle must not remain outstanding");
    }

    private static async Task EnsureManagerInitialized()
    {
        bool initialized = await AAPackageManager.Instance.InitializePackageAsync();
        ScenarioAssert.True(initialized, "AAPackageManager test manifest should initialize");
    }

    private static void SourceUsesAddressablesHandleTickets()
    {
        string source = ScenarioSource.Read("Assets/FYAsset/Scripts/AA/Runtime/AAPackageManager.cs");

        ScenarioAssert.NotContains(source, "class ResourceEntry",
            "AA lifetime must not keep the old resource-entry counter wrapper");
        ScenarioAssert.NotContains(source, "ReferenceCount",
            "AA lifetime must not maintain a parallel integer reference count");
        ScenarioAssert.NotContains(source, "_resourceCache",
            "AA lifetime should store load tickets rather than a cached result with a synthetic count");
        ScenarioAssert.Contains(source, "Stack<AsyncOperationHandle>",
            "AA lifetime should retain one Addressables handle ticket per successful load");
        ScenarioAssert.Contains(source, ".Push(handle)",
            "successful loads should append their exact handle to the ticket stack");
        ScenarioAssert.Contains(source, ".Pop()",
            "typed unload should remove exactly one retained ticket");
        ScenarioAssert.True(CountOccurrences(source, "Addressables.LoadAssetAsync<T>(address)") >= 2,
            "both async and sync load paths must acquire a real Addressables handle");
    }

    private static int CountOccurrences(string source, string value)
    {
        int count = 0;
        int index = 0;
        while ((index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }
        return count;
    }
}

internal sealed class TicketAssetA : UnityEngine.Object
{
}

internal sealed class TicketAssetB : UnityEngine.Object
{
}
