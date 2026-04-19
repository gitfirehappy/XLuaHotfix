using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

public static class LaunchSignal
{
    private static TaskCompletionSource<bool> _tcs = new TaskCompletionSource<bool>();

    public static bool IsLaunched => _tcs.Task.IsCompleted;

    public static Task WaitForLaunch()
    {
        return _tcs.Task;
    }

    public static void NotifyLaunched()
    {
        if (!_tcs.Task.IsCompleted)
            _tcs.SetResult(true);
    }
}
