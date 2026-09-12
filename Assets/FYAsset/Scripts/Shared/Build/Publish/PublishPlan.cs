using System;
using System.Collections.Generic;

/// <summary>发布事务在读取和组装阶段产生的事实。</summary>
public sealed class PublishPlan
{
    public PackageBuildIdentity Identity;
    public string ServerPackageIndexPath;
    public string ServerPackageDir;
    public bool ServerIndexReadable;
    public string ServerLatestPackage;
    public bool ServerManifestReadable;
    public string DegradeReason = string.Empty;

    public List<FileHelper.FileDigest> LocalFiles = new();
    public List<FileHelper.FileDigest> TargetFiles = new();
    public Dictionary<string, string> FileSources = new(StringComparer.Ordinal);
    public bool AssembledFromBaselineFull;
    public string BaselineFullPackageDir = string.Empty;
    public int LocalSourcedCount;
    public int ServerSourcedCount;
    public int BaselineSourcedCount;

    public List<FileHelper.FileDigest> ServerFiles = new();
    public List<FileHelper.FileDigest> AddedFiles = new();
    public List<FileHelper.FileDigest> ModifiedFiles = new();
    public List<FileHelper.FileDigest> UnchangedFiles = new();
    public List<string> RemovedFiles = new();
    public List<FileHelper.FileDigest> ReusedByHash = new();
    public List<string> Messages = new();

    public List<FileHelper.FileDigest> ResolveTargetFiles() =>
        TargetFiles != null && TargetFiles.Count > 0 ? TargetFiles : LocalFiles;

    public void SetTargetFiles(List<FileHelper.FileDigest> targetFiles, Dictionary<string, string> fileSources)
    {
        if (targetFiles != null && targetFiles.Count > 0)
            TargetFiles = targetFiles;
        if (fileSources != null)
            FileSources = fileSources;
    }

    public bool IsFullUpload => !string.IsNullOrEmpty(DegradeReason);

    public int UploadCount
    {
        get
        {
            int count = AddedFiles.Count + ModifiedFiles.Count;
            for (int i = 0; i < ReusedByHash.Count; i++)
            {
                string name = ReusedByHash[i].Name;
                if (ContainsName(AddedFiles, name) || ContainsName(ModifiedFiles, name))
                    count--;
            }
            return count;
        }
    }

    public int ReuseCount => UnchangedFiles.Count + ReusedByHash.Count;

    public void AddMessage(string message)
    {
        if (!string.IsNullOrEmpty(message))
            Messages.Add(message);
    }

    private static bool ContainsName(List<FileHelper.FileDigest> files, string name)
    {
        for (int i = 0; i < files.Count; i++)
        {
            if (string.Equals(files[i].Name, name, StringComparison.Ordinal))
                return true;
        }
        return false;
    }
}
