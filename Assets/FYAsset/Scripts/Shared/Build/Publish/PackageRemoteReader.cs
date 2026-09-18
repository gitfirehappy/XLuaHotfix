#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;

internal enum RemotePackageState
{
    Empty,
    Usable,
    Invalid,
    Inaccessible
}

internal sealed class RemotePackageFacts
{
    public RemotePackageState State;
    public string PackageDir = string.Empty;
    public string Error = string.Empty;
    public List<FileHelper.FileDigest> Files = new List<FileHelper.FileDigest>();
    public PackageIndex Index;

    public bool IsUsable => State == RemotePackageState.Usable;
}

/// <summary>读取服务器 PackageIndex、包清单和实际文件，形成可复用的远端事实。</summary>
internal static class PackageRemoteReader
{
    public static RemotePackageFacts Read(PublishRequest request, string backendRoot)
    {
        var facts = new RemotePackageFacts();
        if (request == null || request.ManifestReader == null)
            return Fail(facts, RemotePackageState.Invalid, "缺少发布清单读取器。");
        if (string.IsNullOrWhiteSpace(backendRoot))
            return Fail(facts, RemotePackageState.Inaccessible, "服务器后端根目录为空。");

        string root = FYAssetPathUtility.NormalizePath(Path.GetFullPath(backendRoot));
        string packagesRoot = FYAssetPathUtility.JoinFilePath(root, request.PackagesFolderName);
        string indexPath = FYAssetPathUtility.JoinFilePath(root, FYAssetSettings.PACKAGE_INDEX_FILE_NAME);
        string reparseError = string.Empty;
        if (!PublishPathGuard.IsContainedIn(root, packagesRoot)
            || !PublishPathGuard.HasNoReparsePoint(root, out reparseError))
            return Fail(facts, RemotePackageState.Inaccessible,
                string.IsNullOrEmpty(reparseError) ? $"发布路径越出服务器根: {root}" : reparseError);

        if (!FileHelper.Exists(indexPath))
            return Fail(facts, RemotePackageState.Empty, $"文件不存在: {indexPath}");

        PackageIndex index;
        try
        {
            string json = FileHelper.ReadAllText(indexPath);
            index = SerializationUtility.DeserializeJson<PackageIndex>(json);
            if (index == null || !VersionNumber.JsonHasObjectField(json, nameof(PackageIndex.LatestVersion)))
                return Fail(facts, RemotePackageState.Invalid, $"PackageIndex 无效: {indexPath}");
        }
        catch (Exception ex)
        {
            return Fail(facts, RemotePackageState.Invalid, $"{indexPath} — {ex.Message}");
        }

        if (!PublishPathGuard.IsSafeSegment(index.LatestPackage))
            return Fail(facts, RemotePackageState.Invalid, $"服务器 PackageIndex 的包名不是合法目录名: '{index.LatestPackage}'");

        string packageDir = FYAssetPathUtility.JoinFilePath(packagesRoot, index.LatestPackage);
        if (!FileHelper.DirectoryExists(packageDir))
            return Fail(facts, RemotePackageState.Invalid, $"服务器包目录不存在: {packageDir}");
        if (!request.ManifestReader.TryReadContentDigests(packageDir,
                out IReadOnlyList<FileHelper.FileDigest> contents, out string manifestError))
            return Fail(facts, RemotePackageState.Invalid, $"服务器 Manifest 损坏或缺失: {manifestError}");

        var files = new List<FileHelper.FileDigest>();
        IReadOnlyList<string> required = request.ManifestReader.RequiredPackageFileNames;
        if (required == null || required.Count == 0)
            return Fail(facts, RemotePackageState.Invalid, "后端未声明包清单文件名。");
        for (int i = 0; i < required.Count; i++)
        {
            string name = required[i];
            if (string.IsNullOrEmpty(name))
                continue;
            string path = FYAssetPathUtility.JoinFilePath(packageDir, name);
            if (!FileHelper.TryCreateDigest(path, name.Replace('\\', '/'), out FileHelper.FileDigest digest))
                return Fail(facts, RemotePackageState.Invalid, $"服务器包缺少清单文件或不可读: {name}");
            files.Add(digest);
        }
        if (contents != null)
        {
            for (int i = 0; i < contents.Count; i++)
            {
                if (contents[i].IsComplete)
                    files.Add(contents[i]);
            }
        }
        if (!VerifyManifestFiles(request, packageDir, contents, out string driftError))
            return Fail(facts, RemotePackageState.Invalid, $"服务器包内容与其 Manifest 不一致: {driftError}");

        facts.State = RemotePackageState.Usable;
        facts.Index = index;
        facts.PackageDir = packageDir;
        facts.Files = files;
        return facts;
    }

    private static RemotePackageFacts Fail(RemotePackageFacts facts, RemotePackageState state, string error)
    {
        facts.State = state;
        facts.Error = error ?? string.Empty;
        facts.Files.Clear();
        facts.PackageDir = string.Empty;
        facts.Index = null;
        return facts;
    }

    private static bool VerifyManifestFiles(
        PublishRequest request,
        string packageDir,
        IReadOnlyList<FileHelper.FileDigest> declared,
        out string error)
    {
        error = string.Empty;
        string contentDirectoryName = request.ManifestReader.ContentDirectoryName;
        if (string.IsNullOrEmpty(contentDirectoryName))
            return true;
        string contentDirectory = FYAssetPathUtility.JoinFilePath(packageDir, contentDirectoryName);
        if (!FileHelper.DirectoryExists(contentDirectory))
            return true;
        if (!PackageFileScanner.TryScanContentDirectory(packageDir, contentDirectory,
                out List<FileHelper.FileDigest> physical, out string scanError))
        {
            error = scanError;
            return false;
        }

        var declaredByName = FileHelper.IndexByName(declared);
        for (int i = 0; i < physical.Count; i++)
        {
            FileHelper.FileDigest file = physical[i];
            if (!declaredByName.TryGetValue(file.Name, out FileHelper.FileDigest expected))
            {
                error = $"存在清单未声明的文件: {file.Name}";
                return false;
            }
            if (!file.Matches(expected))
            {
                error = $"文件与清单摘要不一致: {file.Name}";
                return false;
            }
        }
        return true;
    }
}
#endif
