using System;
using System.IO;

internal static class ConcretePackageOwnershipTests
{
    public static void Run()
    {
        string aa = RepoSource.Read("Assets/FYAsset/Scripts/AA/Runtime/AAPackageManager.cs");
        string ab = RepoSource.Read("Assets/FYAsset/Scripts/AB/Runtime/ABPackageManager.cs");
        string runtimeMessage = RepoSource.Read("Assets/FYAsset/Scripts/Shared/Runtime/RuntimeMessage.cs");

        RepoAssert.NotContains(aa, ": PackageManagerBase", "AA manager must not inherit shared implementation");
        RepoAssert.Contains(aa, "Addressables.LoadAssetAsync<T>", "AA manager owns Addressables loading");
        RepoAssert.Contains(aa, "AAManifestLoader.LoadAsync", "AA manager owns manifest initialization");

        RepoAssert.NotContains(ab, ": PackageManagerBase", "AB manager must not inherit shared implementation");
        RepoAssert.Contains(ab, "LoadRawBytesAsync", "AB manager retains RawFile API");
        RepoAssert.Contains(ab, "LoadByTypeKey", "AB manager retains TypeKey API");
        RepoAssert.Contains(ab, "HandleRegistry.Alloc", "AB manager retains handle lifetime ownership");
        RepoAssert.Contains(ab, "void UnloadAsset<T>", "AB common unload remains typed");
        RepoAssert.Contains(ab, "AssetResolver.ResolveByAddress<T>",
            "AB typed unload reuses the same resolver as load");
        RepoAssert.Contains(ab, "UnloadByEntryId", "AB typed unload releases only the resolved entry");

        RepoAssert.False(RepoSource.Exists("Assets/FYAsset/Scripts/Shared/Runtime/PackageManagerBase.cs"),
            "shared package manager base must be deleted");
        RepoAssert.False(RepoSource.Exists("Assets/FYAsset/Scripts/Shared/Runtime/Contracts/IAssetIndex.cs"),
            "shared asset index interface must be deleted");
        RepoAssert.False(RepoSource.Exists("Assets/FYAsset/Scripts/Shared/Runtime/Contracts/IPackageBackend.cs"),
            "shared package backend interface must be deleted");
        RepoAssert.True(RepoSource.Exists("Assets/FYAsset/Scripts/AB/Runtime/Backends/AssetResolver.cs"),
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
