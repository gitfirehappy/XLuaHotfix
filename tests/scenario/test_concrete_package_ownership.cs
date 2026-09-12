using System;
using System.IO;

internal static class ConcretePackageOwnershipTests
{
    public static void Run()
    {
        string aa = RepoSource.Read("Assets/FYAsset/Scripts/AA/Runtime/AAPackageManager.cs");
        string ab = RepoSource.Read("Assets/FYAsset/Scripts/AB/Runtime/ABPackageManager.cs");
        string index = RepoSource.Read("Assets/FYAsset/Scripts/AB/Runtime/ABAssetIndex.cs");
        string bundleLoader = RepoSource.Read("Assets/FYAsset/Scripts/AB/Runtime/ABBundleLoader.cs");
        string runtimeMessage = RepoSource.Read("Assets/FYAsset/Scripts/Shared/Runtime/RuntimeMessage.cs");

        RepoAssert.NotContains(aa, ": PackageManagerBase", "AA manager must not inherit shared implementation");
        RepoAssert.Contains(aa, "Addressables.LoadAssetAsync<T>", "AA manager owns Addressables loading");
        RepoAssert.Contains(aa, "AAManifestLoader.LoadAsync", "AA manager owns manifest initialization");

        RepoAssert.NotContains(ab, ": PackageManagerBase", "AB manager must not inherit shared implementation");
        RepoAssert.Contains(ab, "LoadRawBytesAsync", "AB manager retains RawFile API");
        RepoAssert.Contains(ab, "Task<AssetHandle<T>> LoadByAddress<T>",
            "AB manager loads assets through handle-returning address entry points");
        RepoAssert.Contains(ab, "HandleRegistry.Alloc", "AB manager retains handle lifetime ownership");
        RepoAssert.Contains(ab, "HandleKind.Asset", "AB manager allocates asset-kind handle tokens");
        RepoAssert.Contains(ab, "RuntimeMessage Shutdown()",
            "AB manager exposes a guarded shutdown for runtime hotfix apply");
        RepoAssert.Contains(ab, "Task<SceneHandle> LoadSceneAsync(",
            "AB manager owns scene loading through SceneHandle");
        RepoAssert.Contains(ab, "AssetResolver.ResolveByAddress(",
            "AB manager resolves addresses through the shared resolver");
        RepoAssert.Contains(ab, "_backend.UnloadByEntryId",
            "handle release delegates content unload to the backend by EntryId");
        RepoAssert.Contains(ab, "new ABPackageBackend(manifest, bundleLoader)",
            "each initialization must build a fresh backend so no stale asset cache survives Shutdown");
        RepoAssert.Contains(ab, "_isInitialized = false",
            "Shutdown must clear the initialization latch so a later Initialize really reloads");

        RepoAssert.NotContains(ab, "LoadByTypeKey", "TypeKey disambiguation must be removed");
        RepoAssert.NotContains(ab, "LoadAssetAsync<T>", "handle-less asset load entry point must be removed");
        RepoAssert.NotContains(ab, "LoadAssetSync<T>", "handle-less sync asset load entry point must be removed");
        RepoAssert.NotContains(ab, "void UnloadAsset<T>", "address-forced unload entry point must be removed");
        RepoAssert.Contains(ab, "Task<byte[]> LoadRawBytesAsync(string address)",
            "RawFile byte loading must take only the address");
        RepoAssert.Contains(ab, "Task<string> LoadRawTextAsync(string address, Encoding encoding = null)",
            "RawFile text loading must take only the address and encoding");
        RepoAssert.NotContains(ab, "LoadRawBytesSync",
            "RawFile loading must not keep a label-carrying sync entry point");
        RepoAssert.NotContains(ab, "LoadRawTextSync",
            "RawFile text loading must not keep a label-carrying sync entry point");
        RepoAssert.NotContains(ab, "_typeToKeys", "duplicate type query cache must move into ABAssetIndex");
        RepoAssert.NotContains(ab, "_labelToKeys", "duplicate label query cache must move into ABAssetIndex");
        RepoAssert.NotContains(ab, "_addressSet", "duplicate address set must move into ABAssetIndex");
        RepoAssert.NotContains(ab, "BuildQueryCaches", "AB manager must not rebuild index query caches");

        RepoAssert.Contains(index, "entry.IsPublic",
            "only public entries may enter the address/type/label indexes");
        RepoAssert.Contains(index, "StringComparer.OrdinalIgnoreCase",
            "public address lookup must be case insensitive");
        RepoAssert.Contains(index, "RuntimeMessage.DuplicateAddress",
            "duplicate public addresses must fail index construction with a structured error");
        RepoAssert.NotContains(index, "GetEntriesByAddressAndType",
            "address+type disambiguation index must be removed");
        RepoAssert.Contains(index, "GetEntryById", "EntryId lookup must remain available for load and dependencies");

        RepoAssert.Contains(bundleLoader, "RuntimePathManager.ActivePackageRoot",
            "Bundle loading must read the single active package root");
        RepoAssert.NotContains(bundleLoader, "CurrentGUIDRoot",
            "Bundle loading must not derive its own package root");

        RepoAssert.False(RepoSource.Exists("Assets/FYAsset/Scripts/Shared/Runtime/PackageManagerBase.cs"),
            "shared package manager base must be deleted");
        RepoAssert.False(RepoSource.Exists("Assets/FYAsset/Scripts/Shared/Runtime/Contracts/IAssetIndex.cs"),
            "shared asset index interface must be deleted");
        RepoAssert.False(RepoSource.Exists("Assets/FYAsset/Scripts/Shared/Runtime/Contracts/IPackageBackend.cs"),
            "shared package backend interface must be deleted");
        RepoAssert.True(RepoSource.Exists("Assets/FYAsset/Scripts/AB/Runtime/AssetResolver.cs"),
            "resolver must live under AB runtime ownership");
        RepoAssert.NotContains(runtimeMessage, "RuntimeAssetEntry", "shared diagnostics must not depend on AB models");
    }
}

internal static class RepoSource
{
    public static readonly string Root = FindRoot();

    public static string Read(string relativePath) => File.ReadAllText(Path.Combine(Root, Normalize(relativePath)));

    public static bool Exists(string relativePath) => File.Exists(Path.Combine(Root, Normalize(relativePath)));

    public static int Count(string source, string value)
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

    private static string FindRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "Assets"))) return current.FullName;
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("Repository root with Assets directory was not found.");
    }

    private static string Normalize(string path) => path.Replace('/', Path.DirectorySeparatorChar);
}

internal static class RepoAssert
{
    public static void True(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    public static void False(bool value, string message) => True(!value, message);

    public static void Contains(string source, string value, string message) =>
        True(source.Contains(value, StringComparison.Ordinal), message);

    public static void NotContains(string source, string value, string message) =>
        True(!source.Contains(value, StringComparison.Ordinal), message);

    public static void AtLeast(int expected, int actual, string message) =>
        True(actual >= expected, $"{message}: expected >= {expected}, actual {actual}");

    public static void Equal(string expected, string actual, string message) =>
        True(string.Equals(expected, actual, StringComparison.Ordinal),
            $"{message}: expected {expected}, actual {actual ?? "<null>"}");
}
