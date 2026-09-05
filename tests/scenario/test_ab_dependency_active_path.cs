using System;

internal static class ABDependencyActivePathTests
{
    public static void Run()
    {
        string source = RepoSource.Read("Assets/FYAsset/Scripts/AB/Runtime/Backends/ABBundleLoader.cs");

        RepoAssert.AtLeast(2, RepoSource.Count(source, "if (!visited.Add(dep.BundleName))"),
            "sync and async paths must retain active-cycle detection");
        RepoAssert.AtLeast(2, RepoSource.Count(source, "visited.Remove(dep.BundleName)"),
            "sync and async paths must remove completed dependencies from the active path");
        RepoAssert.AtLeast(2, RepoSource.Count(source, "finally"),
            "active-path removal must survive dependency load failure");
    }
}
