using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

internal static class RuntimeResourceScenarioRunner
{
    private static int _failures;

    private static async Task<int> Main()
    {
        await RunAsync(nameof(ABBundleSingleFlightTests.AsyncLeaderSyncFollowerUsesOnePhysicalRequest),
            ABBundleSingleFlightTests.AsyncLeaderSyncFollowerUsesOnePhysicalRequest);
        await RunAsync(nameof(ABBundleSingleFlightTests.DifferentRootsShareOneDependencyPhysicalOpen),
            ABBundleSingleFlightTests.DifferentRootsShareOneDependencyPhysicalOpen);
        await RunAsync(nameof(ABBundleSingleFlightTests.FailedInflightFansOutAndCanRetry),
            ABBundleSingleFlightTests.FailedInflightFansOutAndCanRetry);
        await RunAsync(nameof(ABBundleSingleFlightTests.DiamondSucceedsAndTrueCycleFails),
            ABBundleSingleFlightTests.DiamondSucceedsAndTrueCycleFails);
        await RunAsync(nameof(AAHandleTicketLifetimeTests.SequentialLoadsRetainTwoTickets),
            () => ScenarioTask.RunOnUnityThread(
                AAHandleTicketLifetimeTests.SequentialLoadsRetainTwoTickets));
        await RunAsync(nameof(AAHandleTicketLifetimeTests.ConcurrentLoadsDoNotOverwriteTickets),
            () => ScenarioTask.RunOnUnityThread(
                AAHandleTicketLifetimeTests.ConcurrentLoadsDoNotOverwriteTickets));
        await RunAsync(nameof(AAHandleTicketLifetimeTests.FailedTicketsAreReleasedAndNotRetained),
            () => ScenarioTask.RunOnUnityThread(
                AAHandleTicketLifetimeTests.FailedTicketsAreReleasedAndNotRetained));
        await RunAsync(nameof(DialogueFacadeLoadingTests.LoadsCachesAndUnloadsThroughFacade),
            DialogueFacadeLoadingTests.LoadsCachesAndUnloadsThroughFacade);
        await RunAsync(nameof(DialogueFacadeLoadingTests.ParseFailureReleasesFacadeAsset),
            DialogueFacadeLoadingTests.ParseFailureReleasesFacadeAsset);
        await RunAsync(nameof(DialogueFacadeLoadingTests.SourceBoundaryUsesOnlyFacade),
            DialogueFacadeLoadingTests.SourceBoundaryUsesOnlyFacade);

        Console.WriteLine(_failures == 0
            ? "PASS - runtime resource scenarios."
            : $"FAIL - {_failures} runtime resource scenarios.");
        return _failures == 0 ? 0 : 1;
    }

    private static async Task RunAsync(string name, Func<Task> test)
    {
        try
        {
            await test();
            Console.WriteLine($"PASS - {name}");
        }
        catch (Exception ex)
        {
            _failures++;
            Console.WriteLine($"FAIL - {name}: {ex.Message}");
        }
    }
}

internal static class ScenarioAssert
{
    public static void True(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    public static void False(bool value, string message) => True(!value, message);

    public static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{message}: expected={expected}, actual={actual}");
    }

    public static void Same(object expected, object actual, string message)
    {
        if (!ReferenceEquals(expected, actual))
            throw new InvalidOperationException(message);
    }

    public static void Contains(string source, string value, string message)
    {
        True(source != null && source.Contains(value, StringComparison.Ordinal), message);
    }

    public static void NotContains(string source, string value, string message)
    {
        True(source == null || !source.Contains(value, StringComparison.Ordinal), message);
    }
}

internal static class ScenarioSource
{
    public static readonly string Root = FindRoot();

    public static string Read(string relativePath)
    {
        return File.ReadAllText(Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
    }

    private static string FindRoot()
    {
        DirectoryInfo current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "Assets"))) return current.FullName;
            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}

internal static class ScenarioTask
{
    public static Task RunOnUnityThread(Func<Task> scenario)
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

    public static async Task<T> WithTimeout<T>(Task<T> task, int milliseconds = 2000)
    {
        Task winner = await Task.WhenAny(task, Task.Delay(milliseconds));
        if (!ReferenceEquals(winner, task))
            throw new TimeoutException($"Task did not complete within {milliseconds} ms.");
        return await task;
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
