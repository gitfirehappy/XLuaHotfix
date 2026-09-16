using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
internal sealed class BinarySerializableAttribute : Attribute
{
    public uint Magic { get; set; }
    public int SchemaVersion { get; set; }
}
[AttributeUsage(AttributeTargets.Field)]
internal sealed class BinaryFieldAttribute : Attribute
{
    public BinaryFieldAttribute(int order) { }
}

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

public static class FileHelper
{
    public static byte[] ReadAllBytes(string path) => File.ReadAllBytes(path);
}
public static class BinaryHeader
{
    public static bool HasValidMagic(byte[] data) => false;
}
public static class SerializationUtility
{
    public static T Deserialize<T>(byte[] data) => throw new NotSupportedException();
    public static T DeserializeJson<T>(string json) => throw new NotSupportedException();
    public static string SerializeToJson<T>(T value, bool prettyPrint) => throw new NotSupportedException();
}

public readonly struct VersionNumber : IEquatable<VersionNumber>, IComparable<VersionNumber>
{
    public readonly int Major;
    public readonly int Minor;
    public readonly int Patch;
    public VersionNumber(int major, int minor, int patch) { Major = major; Minor = minor; Patch = patch; }
    public static bool operator ==(VersionNumber a, VersionNumber b) => a.Equals(b);
    public static bool operator !=(VersionNumber a, VersionNumber b) => !a.Equals(b);
    public bool Equals(VersionNumber other) => Major == other.Major && Minor == other.Minor && Patch == other.Patch;
    public override bool Equals(object obj) => obj is VersionNumber other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Major, Minor, Patch);
    public int CompareTo(VersionNumber other) => (Major, Minor, Patch).CompareTo((other.Major, other.Minor, other.Patch));
    public static bool JsonHasObjectField(string json, string name) => true;
}

internal sealed class FacadeAsset : UnityEngine.Object { }

public sealed class AAPackageManager
{
    public static readonly AAPackageManager Instance = new();
    public static string LastAddress;
    public Task<(T asset, RuntimeMessage error)> LoadAssetAsync<T>(string address) where T : UnityEngine.Object
    { LastAddress = address; return Task.FromResult<(T, RuntimeMessage)>((null, null)); }
    public (T asset, RuntimeMessage error) LoadAssetSync<T>(string address) where T : UnityEngine.Object
    { LastAddress = address; return (null, null); }
    public void UnloadAsset<T>(string address) where T : UnityEngine.Object => LastAddress = address;
}

public sealed class ABPackageManager
{
    public static readonly ABPackageManager Instance = new();
    public static string LastAddress;
    public Task<AssetHandle<T>> LoadByAddress<T>(string address) where T : UnityEngine.Object
    { LastAddress = address; return Task.FromResult(default(AssetHandle<T>)); }
    public AssetHandle<T> LoadByAddressSync<T>(string address) where T : UnityEngine.Object
    { LastAddress = address; return default; }
}

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
        ABManifest manifest = new ABManifest
        {
            AssetEntries = new List<ManifestAssetEntry>
            {
                Entry("Dialogue", "config", AssetContentType.SerializedObject),
                Entry("RawTable", "raw", AssetContentType.RawFile),
                Entry("Battle", "scene", AssetContentType.Scene)
            },
            ContentEntries = new List<ManifestContentEntry>
            {
                new ManifestContentEntry { FileName = "config.bundle", ContentType = AssetContentType.SerializedObject },
                new ManifestContentEntry { FileName = "raw.dat", ContentType = AssetContentType.RawFile },
                new ManifestContentEntry { FileName = "scene.bundle", ContentType = AssetContentType.Scene }
            }
        };
        manifest.Initialize();
        var index = new ABAssetIndex(manifest);

        ResolveResult exact = AssetResolver.ResolveByAddress(index, "Dialogue");
        RepoAssert.True(exact.IsSuccess && exact.Entry.Address == "Dialogue", "unique public address resolves to its entry");
        ResolveResult insensitive = AssetResolver.ResolveByAddress(index, "dialogue");
        RepoAssert.True(insensitive.IsSuccess && insensitive.Entry.Address == "Dialogue", "address lookup is case insensitive");
        RepoAssert.Equal(RuntimeErrorCodes.NotFound, AssetResolver.ResolveByAddress(index, "Missing").Error?.Code, "unknown address fails with NotFound");
        RepoAssert.Equal(RuntimeErrorCodes.InvalidPayloadKind, AssetResolver.ResolveByAddress(index, "RawTable").Error?.Code, "asset API rejects RawFile");
        RepoAssert.Equal(RuntimeErrorCodes.InvalidPayloadKind, AssetResolver.ResolveByAddress(index, "Battle").Error?.Code, "asset API rejects Scene");
        RepoAssert.True(AssetResolver.ResolveRawByAddress(index, "RawTable").IsSuccess, "RawFile API resolves RawFile");
        RepoAssert.Equal(RuntimeErrorCodes.InvalidPayloadKind, AssetResolver.ResolveRawByAddress(index, "Dialogue").Error?.Code, "RawFile API rejects asset");
        RepoAssert.True(AssetResolver.ResolveSceneByAddress(index, "Battle").IsSuccess, "Scene API resolves Scene");
        RepoAssert.Equal(RuntimeErrorCodes.InvalidPayloadKind, AssetResolver.ResolveSceneByAddress(index, "Dialogue").Error?.Code, "Scene API rejects asset");
        List<ResolveResult> many = AssetResolver.ResolveMany(index, new[] { "Dialogue", "Missing" });
        RepoAssert.True(many.Count == 2 && many[0].IsSuccess && !many[1].IsSuccess, "batch resolution reports each result");
    }

    private static ManifestAssetEntry Entry(string address, string path, AssetContentType contentType)
        => new ManifestAssetEntry { Address = address, AssetPath = path, AssetType = "test:Object", ContentIndex = contentType == AssetContentType.SerializedObject ? 0 : contentType == AssetContentType.RawFile ? 1 : 2 };

    private static void RunCase(string name, Action test, ref int failures)
    {
        try { test(); Console.WriteLine($"PASS {name}"); }
        catch (Exception ex) { failures++; Console.WriteLine($"FAIL {name}: {ex.Message}"); }
    }
}
