#if UNITY_EDITOR
using System;
using System.IO;

/// <summary>
/// 以事务方式替换单个后端包及其根目录 PackageIndex。
/// </summary>
internal sealed class PackagePublishTransaction : IDisposable
{
    private readonly RepositoryCommit _commit;
    private readonly string _sourcePackage;
    private readonly string _backendRoot;
    private readonly string _targetPackage;
    private readonly string _packageIndexPath;
    private readonly string _workRoot;
    private readonly string _stagedPackage;
    private readonly string _backupPackage;
    private readonly string _backupPackageIndex;
    private readonly bool _sourceAlreadyAtTarget;

    private bool _targetPackageBackedUp;
    private bool _targetPackageReplaced;
    private bool _packageIndexExisted;
    private bool _packageIndexWritten;
    private bool _completed;

    public string BackendRoot => _backendRoot;

    public PackagePublishTransaction(RepositoryCommit commit, string sourcePackage, string backendRoot)
    {
        _commit = commit ?? throw new ArgumentNullException(nameof(commit));
        _sourcePackage = sourcePackage ?? throw new ArgumentNullException(nameof(sourcePackage));
        _backendRoot = backendRoot ?? throw new ArgumentNullException(nameof(backendRoot));
        _targetPackage = FYAssetPathUtility.JoinFilePath(
            _backendRoot,
            FYAssetSettings.Instance.BuildPackagesFolderName,
            _commit.PackageName);
        _packageIndexPath = FYAssetPathUtility.JoinFilePath(_backendRoot, FYAssetSettings.PACKAGE_INDEX_FILE_NAME);
        _workRoot = FYAssetPathUtility.JoinFilePath(
            _backendRoot,
            ".fyasset_push",
            _commit.PackageName + "_" + Guid.NewGuid().ToString("N").Substring(0, 8));
        _stagedPackage = FYAssetPathUtility.JoinFilePath(_workRoot, "staged", _commit.PackageName);
        _backupPackage = FYAssetPathUtility.JoinFilePath(_workRoot, "backup", _commit.PackageName);
        _backupPackageIndex = FYAssetPathUtility.JoinFilePath(_workRoot, "backup", FYAssetSettings.PACKAGE_INDEX_FILE_NAME);
        _sourceAlreadyAtTarget = FYAssetPathUtility.AreSamePath(_sourcePackage, _targetPackage);
    }

    public void Apply()
    {
        if (_completed)
            throw new InvalidOperationException("Publish transaction is already completed.");
        if (!FileHelper.DirectoryExists(_sourcePackage))
            throw new DirectoryNotFoundException($"PackageRootDir missing: {_sourcePackage}");

        try
        {
            ValidatePackage(_sourcePackage, _commit);
            FileHelper.EnsureDirectory(_backendRoot);

            _packageIndexExisted = FileHelper.Exists(_packageIndexPath);
            if (_packageIndexExisted)
                FileHelper.CopyFile(_packageIndexPath, _backupPackageIndex, true);

            if (!_sourceAlreadyAtTarget)
            {
                CopyDirectory(_sourcePackage, _stagedPackage);
                ValidatePackage(_stagedPackage, _commit);

                if (FileHelper.DirectoryExists(_targetPackage))
                {
                    MoveDirectory(_targetPackage, _backupPackage);
                    _targetPackageBackedUp = true;
                }

                MoveDirectory(_stagedPackage, _targetPackage);
                _targetPackageReplaced = true;
            }

            WritePackageIndex();
            _packageIndexWritten = true;
        }
        catch
        {
            Rollback();
            throw;
        }
    }

    public void Commit()
    {
        if (_completed)
            return;

        _completed = true;
        CleanupWorkRoot();
    }

    public void Rollback()
    {
        if (_completed)
            return;

        try
        {
            if (_targetPackageReplaced)
                FileHelper.TryDeleteDirectory(_targetPackage, true);
            if (_targetPackageBackedUp && FileHelper.DirectoryExists(_backupPackage))
                MoveDirectory(_backupPackage, _targetPackage);

            if (_packageIndexExisted && FileHelper.Exists(_backupPackageIndex))
                FileHelper.CopyFile(_backupPackageIndex, _packageIndexPath, true);
            else if (_packageIndexWritten)
                FileHelper.TryDelete(_packageIndexPath);
        }
        finally
        {
            _completed = true;
            CleanupWorkRoot();
        }
    }

    public void Dispose()
    {
        if (!_completed)
            Rollback();
    }

    internal static void ValidatePackage(string packageDir, RepositoryCommit commit)
    {
        if (!FileHelper.DirectoryExists(packageDir))
            throw new DirectoryNotFoundException($"Package directory missing: {packageDir}");

        bool isAB = commit != null && string.Equals(commit.BackendMode, BackendModeNames.AB, StringComparison.OrdinalIgnoreCase);
        string jsonName = isAB ? FYAssetSettings.MANIFEST_FILE_NAME : FYAssetSettings.AA_MANIFEST_FILE_NAME;
        string binName = isAB ? FYAssetSettings.MANIFEST_FILE_NAME_BIN : FYAssetSettings.AA_MANIFEST_FILE_NAME_BIN;
        string jsonPath = FYAssetPathUtility.JoinFilePath(packageDir, jsonName);
        string binPath = FYAssetPathUtility.JoinFilePath(packageDir, binName);
        if (!FileHelper.Exists(jsonPath) && !FileHelper.Exists(binPath))
            throw new FileNotFoundException($"Package manifest missing: {jsonName} or {binName}", jsonPath);
    }

    private void WritePackageIndex()
    {
        var packageIndex = new PackageIndex
        {
            LatestPackage = _commit.PackageName,
            LatestVersion = _commit.Version,
            BackendMode = _commit.BackendMode
        };
        FileHelper.WriteAllTextAtomic(_packageIndexPath, SerializationUtility.SerializeToJson(packageIndex, true));
    }

    private static void CopyDirectory(string sourceDir, string targetDir)
    {
        FileHelper.EnsureDirectory(targetDir);
        string[] files = FileHelper.GetFiles(sourceDir, "*", SearchOption.AllDirectories);
        for (int i = 0; i < files.Length; i++)
        {
            string relativePath = FYAssetPathUtility.GetRelativeFilePath(sourceDir, files[i]);
            FileHelper.CopyFile(files[i], FYAssetPathUtility.JoinFilePath(targetDir, relativePath), true);
        }
    }

    private static void MoveDirectory(string sourceDir, string targetDir)
    {
        if (!FileHelper.DirectoryExists(sourceDir))
            return;

        FileHelper.EnsureDirectory(Path.GetDirectoryName(targetDir));
        if (FileHelper.DirectoryExists(targetDir))
            FileHelper.TryDeleteDirectory(targetDir, true);
        Directory.Move(sourceDir, targetDir);
    }

    private void CleanupWorkRoot()
    {
        FileHelper.TryDeleteDirectory(_workRoot, true);
        string parent = Path.GetDirectoryName(_workRoot);
        if (FileHelper.DirectoryExists(parent)
            && FileHelper.GetDirectories(parent).Length == 0
            && FileHelper.GetFiles(parent).Length == 0)
        {
            FileHelper.TryDeleteDirectory(parent, false);
        }
    }
}
#endif
