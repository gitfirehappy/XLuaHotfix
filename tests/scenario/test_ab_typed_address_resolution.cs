using System;
using System.Collections.Generic;

namespace UnityEngine
{
    public class Object { }
    public class ScriptableObject : Object { }
    public static class Debug
    {
        public static void Log(object message) { }
        public static void LogError(object message) { }
        public static void LogWarning(object message) { }
    }
}

public class Singleton<T> where T : new()
{
    private static T _instance;
    public static T Instance => _instance ??= new T();
}

public sealed class AAPackageManager
{
    public static readonly AAPackageManager Instance = new();
    public static string LastAddress;

    public System.Threading.Tasks.Task<(T asset, RuntimeMessage error)> LoadAssetAsync<T>(string address)
        where T : UnityEngine.Object
    {
        LastAddress = address;
        return System.Threading.Tasks.Task.FromResult<(T, RuntimeMessage)>((null, null));
    }

    public (T asset, RuntimeMessage error) LoadAssetSync<T>(string address) where T : UnityEngine.Object
    {
        LastAddress = address;
        return (null, null);
    }

    public void UnloadAsset<T>(string address) where T : UnityEngine.Object => LastAddress = address;
}

public sealed class ABPackageManager
{
    public static readonly ABPackageManager Instance = new();
    public static string LastAddress;

    public System.Threading.Tasks.Task<(T asset, RuntimeMessage error)> LoadAssetAsync<T>(string address)
        where T : UnityEngine.Object
    {
        LastAddress = address;
        return System.Threading.Tasks.Task.FromResult<(T, RuntimeMessage)>((null, null));
    }

    public (T asset, RuntimeMessage error) LoadAssetSync<T>(string address) where T : UnityEngine.Object
    {
        LastAddress = address;
        return (null, null);
    }

    public void UnloadAsset<T>(string address) where T : UnityEngine.Object => LastAddress = address;
}

public abstract class ABAssetIndex
{
    public abstract RuntimeAssetEntry GetEntryById(string entryId);
    public abstract IReadOnlyList<RuntimeAssetEntry> GetEntriesByAddress(string address);
    public abstract IReadOnlyList<RuntimeAssetEntry> GetEntriesByAddressAndType(string address, string primaryType);
    public abstract IReadOnlyList<RuntimeAssetEntry> GetAllEntries();
}

internal sealed class LuaScriptContainer : UnityEngine.Object { }
internal sealed class UIFormConfigSO : UnityEngine.ScriptableObject { }
internal sealed class UniqueConfigSO : UnityEngine.ScriptableObject { }
internal sealed class FacadeAsset : UnityEngine.Object { }

internal static class ABTypedAddressResolutionTests
{
    private static int Main()
    {
        int failures = 0;
        RunCase(nameof(ABDependencyActivePathTests), ABDependencyActivePathTests.Run, ref failures);
        RunCase(nameof(ABTypedAddressResolutionTests), Run, ref failures);
        RunCase(nameof(ConcretePackageOwnershipTests), ConcretePackageOwnershipTests.Run, ref failures);
        RunCase(nameof(AssetFacadeBindingTests), AssetFacadeBindingTests.Run, ref failures);
        RunCase(nameof(StartupLoadErrorTests), StartupLoadErrorTests.Run, ref failures);
        Console.WriteLine(failures == 0 ? "PASS - S2 runtime boundary checks." : $"FAIL - {failures} S2 checks.");
        return failures == 0 ? 0 : 1;
    }

    public static void Run()
    {
        var duplicate = new FakeAssetIndex(
            Entry("ui", "Dialogue", nameof(UIFormConfigSO)),
            Entry("lua", "Dialogue", nameof(LuaScriptContainer)));
        ResolveResult exact = AssetResolver.ResolveByAddress<LuaScriptContainer>(duplicate, "Dialogue");
        RepoAssert.True(exact.IsSuccess && exact.Entry.EntryId == "lua", "exact requested type must win");

        ResolveResult ambiguousBase = AssetResolver.ResolveByAddress<UnityEngine.Object>(duplicate, "Dialogue");
        RepoAssert.Equal(RuntimeErrorCodes.AmbiguousMatch, ambiguousBase.Error?.Code,
            "Object fallback must reject multiple address candidates");

        var duplicateExact = new FakeAssetIndex(
            Entry("lua-a", "Player", nameof(LuaScriptContainer)),
            Entry("lua-b", "Player", nameof(LuaScriptContainer)));
        ResolveResult ambiguousExact = AssetResolver.ResolveByAddress<LuaScriptContainer>(duplicateExact, "Player");
        RepoAssert.Equal(RuntimeErrorCodes.AmbiguousMatch, ambiguousExact.Error?.Code,
            "multiple exact type candidates must remain ambiguous");

        var unique = new FakeAssetIndex(Entry("config", "Config", nameof(UniqueConfigSO)));
        ResolveResult uniqueBase = AssetResolver.ResolveByAddress<UnityEngine.ScriptableObject>(unique, "Config");
        RepoAssert.True(uniqueBase.IsSuccess && uniqueBase.Entry.EntryId == "config",
            "ScriptableObject may fall back to one unique address candidate");

        ResolveResult mismatch = AssetResolver.ResolveByAddress<LuaScriptContainer>(unique, "Config");
        RepoAssert.Equal(RuntimeErrorCodes.TypeMismatch, mismatch.Error?.Code,
            "non-base request without an exact type must fail");
    }

    private static RuntimeAssetEntry Entry(string id, string address, string type)
    {
        return new RuntimeAssetEntry { EntryId = id, Address = address, PrimaryType = type };
    }

    private static void RunCase(string name, Action test, ref int failures)
    {
        try
        {
            test();
            Console.WriteLine($"PASS {name}");
        }
        catch (Exception ex)
        {
            failures++;
            Console.WriteLine($"FAIL {name}: {ex.Message}");
        }
    }

    private sealed class FakeAssetIndex : ABAssetIndex
    {
        private readonly RuntimeAssetEntry[] _entries;

        public FakeAssetIndex(params RuntimeAssetEntry[] entries) => _entries = entries;

        public override RuntimeAssetEntry GetEntryById(string entryId)
        {
            for (int i = 0; i < _entries.Length; i++)
                if (_entries[i].EntryId == entryId) return _entries[i];
            return null;
        }

        public override IReadOnlyList<RuntimeAssetEntry> GetEntriesByAddress(string address)
        {
            var result = new List<RuntimeAssetEntry>();
            for (int i = 0; i < _entries.Length; i++)
                if (_entries[i].Address == address) result.Add(_entries[i]);
            return result;
        }

        public override IReadOnlyList<RuntimeAssetEntry> GetEntriesByAddressAndType(string address, string primaryType)
        {
            var result = new List<RuntimeAssetEntry>();
            for (int i = 0; i < _entries.Length; i++)
                if (_entries[i].Address == address &&
                    string.Equals(_entries[i].PrimaryType, primaryType, StringComparison.OrdinalIgnoreCase))
                    result.Add(_entries[i]);
            return result;
        }

        public override IReadOnlyList<RuntimeAssetEntry> GetAllEntries() => _entries;
    }
}
