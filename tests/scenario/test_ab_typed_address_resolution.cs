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

/// <summary>
/// ABPackageManager 替身：只保留 Compat facade 现在依赖的句柄分配入口。
/// </summary>
public sealed class ABPackageManager
{
    public static readonly ABPackageManager Instance = new();
    public static string LastAddress;

    public System.Threading.Tasks.Task<AssetHandle<T>> LoadByAddress<T>(string address)
        where T : UnityEngine.Object
    {
        LastAddress = address;
        return System.Threading.Tasks.Task.FromResult(default(AssetHandle<T>));
    }

    public AssetHandle<T> LoadByAddressSync<T>(string address) where T : UnityEngine.Object
    {
        LastAddress = address;
        return default;
    }
}

/// <summary>
/// ABAssetIndex 替身：AssetResolver 只依赖索引不可用原因与 Address 唯一命中。
/// </summary>
public abstract class ABAssetIndex
{
    public abstract RuntimeMessage BuildError { get; }
    public abstract RuntimeAssetEntry GetEntryByAddress(string address);
}

internal sealed class LuaScriptContainer : UnityEngine.Object { }
internal sealed class UIFormConfigSO : UnityEngine.ScriptableObject { }
internal sealed class UniqueConfigSO : UnityEngine.ScriptableObject { }
internal sealed class FacadeAsset : UnityEngine.Object { }

/// <summary>
/// AB Address 解析契约（计划 T5 后的语义）。
/// 公共 Address 在单包内大小写不敏感且唯一，因此解析不再做 Type 消歧、也没有 Object 回退分支；
/// 地址命中的条目还必须与请求的内容类型一致（SerializedObject / RawFile / Scene）。
/// </summary>
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
        var index = new FakeAssetIndex(
            Entry("config", "Dialogue", nameof(LuaScriptContainer), AssetContentType.SerializedObject),
            Entry("raw", "RawTable", nameof(UnityEngine.Object), AssetContentType.RawFile),
            Entry("scene", "Battle", nameof(UnityEngine.Object), AssetContentType.Scene));

        ResolveResult exact = AssetResolver.ResolveByAddress(index, "Dialogue");
        RepoAssert.True(exact.IsSuccess && exact.Entry.EntryId == "config",
            "a unique public address must resolve to its entry");

        ResolveResult caseInsensitive = AssetResolver.ResolveByAddress(index, "dialogue");
        RepoAssert.True(caseInsensitive.IsSuccess && caseInsensitive.Entry.EntryId == "config",
            "address lookup must be case insensitive");

        ResolveResult missing = AssetResolver.ResolveByAddress(index, "Missing");
        RepoAssert.Equal(RuntimeErrorCodes.NotFound, missing.Error?.Code,
            "an unknown address must fail with NotFound");

        ResolveResult rawThroughAssetApi = AssetResolver.ResolveByAddress(index, "RawTable");
        RepoAssert.Equal(RuntimeErrorCodes.InvalidPayloadKind, rawThroughAssetApi.Error?.Code,
            "the asset API must reject an address whose content type is RawFile");

        ResolveResult sceneThroughAssetApi = AssetResolver.ResolveByAddress(index, "Battle");
        RepoAssert.Equal(RuntimeErrorCodes.InvalidPayloadKind, sceneThroughAssetApi.Error?.Code,
            "the asset API must reject an address whose content type is Scene");

        ResolveResult rawHit = AssetResolver.ResolveRawByAddress(index, "RawTable");
        RepoAssert.True(rawHit.IsSuccess && rawHit.Entry.EntryId == "raw",
            "the RawFile API must resolve a RawFile address");

        ResolveResult rawMismatch = AssetResolver.ResolveRawByAddress(index, "Dialogue");
        RepoAssert.Equal(RuntimeErrorCodes.InvalidPayloadKind, rawMismatch.Error?.Code,
            "the RawFile API must reject a SerializedObject address");

        ResolveResult sceneHit = AssetResolver.ResolveSceneByAddress(index, "Battle");
        RepoAssert.True(sceneHit.IsSuccess && sceneHit.Entry.EntryId == "scene",
            "the Scene API must resolve a Scene address");

        ResolveResult sceneMismatch = AssetResolver.ResolveSceneByAddress(index, "Dialogue");
        RepoAssert.Equal(RuntimeErrorCodes.InvalidPayloadKind, sceneMismatch.Error?.Code,
            "the Scene API must reject a SerializedObject address");

        var broken = new FakeAssetIndex(RuntimeMessage.DuplicateAddress("Dialogue", 2));
        ResolveResult brokenResult = AssetResolver.ResolveByAddress(broken, "Dialogue");
        RepoAssert.Equal(RuntimeErrorCodes.NotFound, brokenResult.Error?.Code,
            "an index rejected for duplicate addresses must not resolve anything");

        List<ResolveResult> many = AssetResolver.ResolveMany(index, new[] { "Dialogue", "Missing" });
        RepoAssert.True(many.Count == 2 && many[0].IsSuccess && !many[1].IsSuccess,
            "batch resolution must report success and failure independently");
    }

    private static RuntimeAssetEntry Entry(string id, string address, string type, AssetContentType contentType)
    {
        return new RuntimeAssetEntry
        {
            EntryId = id,
            Address = address,
            PrimaryType = type,
            IsPublic = true,
            ContentType = contentType
        };
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

        public FakeAssetIndex(RuntimeMessage buildError)
        {
            _entries = Array.Empty<RuntimeAssetEntry>();
            BuildError = buildError;
        }

        public override RuntimeMessage BuildError { get; }

        public override RuntimeAssetEntry GetEntryByAddress(string address)
        {
            for (int i = 0; i < _entries.Length; i++)
            {
                if (string.Equals(_entries[i].Address, address, StringComparison.OrdinalIgnoreCase))
                    return _entries[i];
            }

            return null;
        }
    }
}
