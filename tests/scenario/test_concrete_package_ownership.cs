using System;
using System.Collections.Generic;
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
        RepoAssert.Contains(ab, "LoadRawBytesAsync(string address)", "AB manager retains RawFile API");
        RepoAssert.Contains(ab, "Task<AssetHandle<T>> LoadByAddress<T>", "AB manager returns owned asset handles");
        RepoAssert.Contains(ab, "HandleRegistry.Alloc", "AB manager owns handle lifetime allocation");
        RepoAssert.Contains(ab, "Task<SceneHandle> LoadSceneAsync(", "AB manager owns scene loading");
        RepoAssert.Contains(ab, "_assetLoader.UnloadByAddress", "asset release delegates to ABAssetLoader");
        RepoAssert.Contains(ab, "new ABAssetLoader(bundleLoader)", "initialization creates a fresh asset loader");
        RepoAssert.Contains(ab, "_isInitialized = false", "shutdown clears initialization latch");
        RepoAssert.NotContains(ab, "LoadByTypeKey", "type-key load entry point is removed");
        RepoAssert.NotContains(ab, "LoadAssetAsync<T>", "handle-less asset load is removed");
        RepoAssert.NotContains(ab, "LoadAssetSync<T>", "handle-less sync load is removed");
        RepoAssert.NotContains(ab, "void UnloadAsset<T>", "forced address unload is removed");
        RepoAssert.NotContains(ab, "_typeToKeys", "duplicate type cache is removed from manager");
        RepoAssert.NotContains(ab, "_labelToKeys", "duplicate label cache is removed from manager");
        RepoAssert.Contains(index, "StringComparer.OrdinalIgnoreCase", "address lookup is case insensitive");
        RepoAssert.Contains(index, "RuntimeMessage.DuplicateAddress", "duplicate addresses produce structured error");
        RepoAssert.Contains(index, "GetEntryByAddress", "index exposes address lookup");
        RepoAssert.NotContains(index, "GetEntriesByAddressAndType", "address/type disambiguation is removed");
        RepoAssert.Contains(bundleLoader, "RuntimePathManager.ActivePackageRoot", "Bundle loading reads one active root");
        RepoAssert.NotContains(bundleLoader, "CurrentGUIDRoot", "Bundle loading does not derive a separate root");
        RepoAssert.False(RepoSource.Exists("Assets/FYAsset/Scripts/Shared/Runtime/PackageManagerBase.cs"), "shared package manager base is deleted");
        RepoAssert.True(RepoSource.Exists("Assets/FYAsset/Scripts/AB/Runtime/AssetResolver.cs"), "resolver remains AB-owned");
        RepoAssert.NotContains(runtimeMessage, "RuntimeAssetEntry", "shared diagnostics do not depend on AB models");
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
        for (int index = 0; (index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0; index += value.Length) count++;
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
    public static void True(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    public static void False(bool value, string message) => True(!value, message);
    public static void AtLeast(int minimum, int actual, string message) => True(actual >= minimum, message);
    public static void Equal<T>(T expected, T actual, string message) => True(EqualityComparer<T>.Default.Equals(expected, actual), message);
    public static void Contains(string source, string value, string message) => True(source.Contains(value, StringComparison.Ordinal), message);
    public static void NotContains(string source, string value, string message) => False(source.Contains(value, StringComparison.Ordinal), message);
}
