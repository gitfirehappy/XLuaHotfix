using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class DialogueData
{
}

public static class DialogueCsvReader
{
    private static readonly Queue<List<DialogueData>> Results = new();
    private static Exception _nextException;

    public static int ParseCalls { get; private set; }
    public static TextAsset LastAsset { get; private set; }

    public static void Reset()
    {
        Results.Clear();
        _nextException = null;
        ParseCalls = 0;
        LastAsset = null;
    }

    public static void EnqueueResult(List<DialogueData> result)
    {
        Results.Enqueue(result);
    }

    public static void ThrowNext(Exception exception)
    {
        _nextException = exception;
    }

    public static List<DialogueData> ParseCsv(TextAsset asset)
    {
        ParseCalls++;
        LastAsset = asset;
        if (_nextException != null)
        {
            Exception exception = _nextException;
            _nextException = null;
            throw exception;
        }
        return Results.Count > 0 ? Results.Dequeue() : null;
    }
}

public sealed class AssetPackageManager
{
    private sealed class Config
    {
        public UnityEngine.Object Asset;
        public RuntimeMessage Error;
        public int LoadCalls;
        public int UnloadCalls;
    }

    private static readonly Dictionary<(string address, Type type), Config> Configs = new();
    private static readonly AssetPackageManager SharedInstance = new();

    public static AssetPackageManager Instance => SharedInstance;

    public static string LastLoadAddress { get; private set; }
    public static Type LastLoadType { get; private set; }
    public static string LastUnloadAddress { get; private set; }
    public static Type LastUnloadType { get; private set; }

    public static void Reset()
    {
        Configs.Clear();
        LastLoadAddress = null;
        LastLoadType = null;
        LastUnloadAddress = null;
        LastUnloadType = null;
    }

    public static void Register<T>(string address, T asset, RuntimeMessage error = null)
        where T : UnityEngine.Object
    {
        Configs[(address, typeof(T))] = new Config
        {
            Asset = asset,
            Error = error
        };
    }

    public static int LoadCalls<T>(string address) where T : UnityEngine.Object
    {
        return Get(address, typeof(T)).LoadCalls;
    }

    public static int UnloadCalls<T>(string address) where T : UnityEngine.Object
    {
        return Get(address, typeof(T)).UnloadCalls;
    }

    public (T asset, RuntimeMessage error) LoadAssetSync<T>(string address)
        where T : UnityEngine.Object
    {
        LastLoadAddress = address;
        LastLoadType = typeof(T);

        if (!Configs.TryGetValue((address, typeof(T)), out Config config))
            return (null, RuntimeMessage.LoadFailed(address, "Fake facade asset is not registered"));

        config.LoadCalls++;
        return (config.Asset as T, config.Error);
    }

    public void UnloadAsset<T>(string address) where T : UnityEngine.Object
    {
        LastUnloadAddress = address;
        LastUnloadType = typeof(T);
        Get(address, typeof(T)).UnloadCalls++;
    }

    private static Config Get(string address, Type type)
    {
        if (!Configs.TryGetValue((address, type), out Config config))
            throw new InvalidOperationException($"Facade asset is not registered: {address}, {type.Name}");
        return config;
    }
}
