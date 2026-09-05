using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.ResourceManagement.AsyncOperations;

internal sealed class FakeAddressablesOperation
{
    private readonly TaskCompletionSource<UnityEngine.Object> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public string Address;
    public Type AssetType;
    public UnityEngine.Object Asset;
    public bool ShouldFail;
    public bool AutoComplete;
    public int ReferenceCount;
    public AsyncOperationStatus Status = AsyncOperationStatus.None;

    public void AddReference()
    {
        ReferenceCount++;
        if (AutoComplete) Complete();
    }

    public void Release()
    {
        if (ReferenceCount > 0) ReferenceCount--;
    }

    public void Complete()
    {
        if (Status != AsyncOperationStatus.None) return;
        Status = ShouldFail ? AsyncOperationStatus.Failed : AsyncOperationStatus.Succeeded;
        _completion.TrySetResult(ShouldFail ? null : Asset);
    }

    public async Task<T> GetTask<T>()
    {
        UnityEngine.Object result = await _completion.Task;
        return result is T typed ? typed : default;
    }

    public T WaitForCompletion<T>()
    {
        Complete();
        return !ShouldFail && Asset is T typed ? typed : default;
    }
}

internal static class FakeAddressables
{
    private sealed class Config
    {
        public UnityEngine.Object Asset;
        public bool AutoComplete;
        public bool Fail;
        public int LoadCalls;
        public int ReleaseCalls;
        public FakeAddressablesOperation Current;
    }

    private static readonly Dictionary<(string address, Type type), Config> Configs = new();

    public static void Reset()
    {
        Configs.Clear();
    }

    public static void Register<T>(string address, T asset, bool autoComplete = true, bool fail = false)
        where T : UnityEngine.Object
    {
        Configs[(address, typeof(T))] = new Config
        {
            Asset = asset,
            AutoComplete = autoComplete,
            Fail = fail
        };
    }

    public static AsyncOperationHandle<T> Load<T>(string address) where T : UnityEngine.Object
    {
        Config config = Get(address, typeof(T));
        if (config.Current == null || config.Current.ReferenceCount <= 0)
        {
            config.Current = new FakeAddressablesOperation
            {
                Address = address,
                AssetType = typeof(T),
                Asset = config.Asset,
                AutoComplete = config.AutoComplete,
                ShouldFail = config.Fail
            };
        }

        config.LoadCalls++;
        config.Current.AddReference();
        return new AsyncOperationHandle<T>(config.Current);
    }

    public static void Complete<T>(string address) where T : UnityEngine.Object
    {
        Get(address, typeof(T)).Current?.Complete();
    }

    public static void Release(FakeAddressablesOperation operation)
    {
        if (operation == null) return;
        Config config = Get(operation.Address, operation.AssetType);
        config.ReleaseCalls++;
        operation.Release();
    }

    public static int LoadCalls<T>(string address) where T : UnityEngine.Object
        => Get(address, typeof(T)).LoadCalls;

    public static int ReleaseCalls<T>(string address) where T : UnityEngine.Object
        => Get(address, typeof(T)).ReleaseCalls;

    public static int Outstanding<T>(string address) where T : UnityEngine.Object
        => Get(address, typeof(T)).Current?.ReferenceCount ?? 0;

    private static Config Get(string address, Type type)
    {
        if (!Configs.TryGetValue((address, type), out Config config))
            throw new InvalidOperationException($"Addressables asset is not registered: {address}, {type.Name}");
        return config;
    }
}

namespace UnityEngine.ResourceManagement.AsyncOperations
{
    public enum AsyncOperationStatus
    {
        None,
        Succeeded,
        Failed
    }

    public readonly struct AsyncOperationHandle
    {
        internal AsyncOperationHandle(FakeAddressablesOperation operation)
        {
            Operation = operation;
        }

        internal FakeAddressablesOperation Operation { get; }
        public object Result => Operation?.Asset;
        public AsyncOperationStatus Status => Operation?.Status ?? AsyncOperationStatus.None;
        public bool IsValid() => Operation != null && Operation.ReferenceCount > 0;
    }

    public readonly struct AsyncOperationHandle<T>
    {
        internal AsyncOperationHandle(FakeAddressablesOperation operation)
        {
            Operation = operation;
        }

        internal FakeAddressablesOperation Operation { get; }
        public T Result => Operation != null && Operation.Asset is T typed ? typed : default;
        public AsyncOperationStatus Status => Operation?.Status ?? AsyncOperationStatus.None;
        public Task<T> Task => Operation.GetTask<T>();
        public bool IsValid() => Operation != null && Operation.ReferenceCount > 0;
        public T WaitForCompletion() => Operation.WaitForCompletion<T>();

        public static implicit operator AsyncOperationHandle(AsyncOperationHandle<T> handle)
        {
            return new AsyncOperationHandle(handle.Operation);
        }
    }
}

namespace UnityEngine.AddressableAssets
{
    public static class Addressables
    {
        public static AsyncOperationHandle<T> LoadAssetAsync<T>(string address)
            where T : UnityEngine.Object
        {
            return FakeAddressables.Load<T>(address);
        }

        public static void Release(AsyncOperationHandle handle)
        {
            FakeAddressables.Release(handle.Operation);
        }

        public static void Release<T>(AsyncOperationHandle<T> handle)
        {
            FakeAddressables.Release(handle.Operation);
        }
    }
}
