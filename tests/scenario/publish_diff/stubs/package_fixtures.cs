using System;
using System.Collections.Generic;
using System.IO;

/// <summary>
/// 测试用后端清单读取器：清单文件名与内容目录名由测试指定，内容条目来自清单 JSON。
/// </summary>
/// <remarks>
/// 生产代码里清单解析由 AA/AB 各自实现；场景只需要一份“能被发布会话读取的清单”，
/// 因此这里用与包目录布局一致的测试格式：包根 TestManifest.json 声明 bundles/ 下的文件摘要。
/// </remarks>
internal sealed class TestManifestReader : IPackageManifestReader
{
    public const string ManifestFileName = "TestManifest.json";

    private readonly Func<string, FileHelper.FileDigest, FileHelper.FileDigest> _digestOverride;

    /// <param name="digestOverride">可选：篡改清单声明摘要，用于验证“校验未过不得写 PackageIndex”。</param>
    public TestManifestReader(Func<string, FileHelper.FileDigest, FileHelper.FileDigest> digestOverride = null)
    {
        _digestOverride = digestOverride;
    }

    public IReadOnlyList<string> RequiredPackageFileNames => new[] { ManifestFileName };

    public string ContentDirectoryName => "bundles";

    public bool TryReadContentDigests(string packageDir, out IReadOnlyList<FileHelper.FileDigest> contents, out string error)
    {
        contents = new List<FileHelper.FileDigest>();
        error = string.Empty;

        string manifestPath = Path.Combine(packageDir, ManifestFileName);
        if (!File.Exists(manifestPath))
        {
            error = $"清单不存在: {manifestPath}";
            return false;
        }

        ManifestDocument document;
        try
        {
            document = SerializationUtility.ReadFromFile<ManifestDocument>(manifestPath);
        }
        catch (Exception ex)
        {
            error = $"清单解析失败: {manifestPath} — {ex.Message}";
            return false;
        }

        if (document?.Files == null)
        {
            error = $"清单缺少 files: {manifestPath}";
            return false;
        }

        var result = new List<FileHelper.FileDigest>();
        for (int i = 0; i < document.Files.Count; i++)
        {
            ManifestEntry entry = document.Files[i];
            if (entry == null || string.IsNullOrEmpty(entry.Name))
                continue;

            var digest = new FileHelper.FileDigest(entry.Name, entry.Hash, entry.Crc, entry.Size);
            if (_digestOverride != null)
                digest = _digestOverride(entry.Name, digest);
            result.Add(digest);
        }

        contents = result;
        return true;
    }

    /// <summary>按目录内实际文件写出一份清单（模拟构建产出的完整清单）。</summary>
    public static void WriteManifest(string packageDir, IReadOnlyList<FileHelper.FileDigest> contents, Func<FileHelper.FileDigest, FileHelper.FileDigest> tamper = null)
    {
        var document = new ManifestDocument { Files = new List<ManifestEntry>() };
        for (int i = 0; i < contents.Count; i++)
        {
            FileHelper.FileDigest digest = tamper != null ? tamper(contents[i]) : contents[i];
            document.Files.Add(new ManifestEntry
            {
                Name = digest.Name,
                Hash = digest.Hash,
                Crc = digest.CRC,
                Size = digest.Size
            });
        }

        File.WriteAllText(
            Path.Combine(packageDir, ManifestFileName),
            SerializationUtility.SerializeToJson(document, true));
    }

    [Serializable]
    public sealed class ManifestDocument
    {
        public List<ManifestEntry> Files = new();
    }

    [Serializable]
    public sealed class ManifestEntry
    {
        public string Name;
        public string Hash;
        public uint Crc;
        public long Size;
    }
}

/// <summary>
/// 测试用目录型发布目标：服务器内容用本地临时目录表达。
/// </summary>
/// <remarks>目录型目标由 BuildPublisher 直接执行发布事务，Push 只在完整上传路径被调用。</remarks>
internal sealed class TestDirectoryTarget : IDirectoryPushTarget
{
    private readonly string _serverRoot;

    public TestDirectoryTarget(string serverRoot)
    {
        _serverRoot = serverRoot;
    }

    public string Id => "local-test";

    /// <summary>完整上传路径调用次数，用于验证目录型目标不会走上传分支。</summary>
    public int PushCalls { get; private set; }

    public string ResolveBackendRoot(string backendKey) =>
        Path.Combine(_serverRoot, backendKey.ToUpperInvariant());

    public PushReceipt Push(PushPayload payload)
    {
        PushCalls++;
        throw new NotSupportedException("目录型目标由发布事务直接落地，不应走完整上传分支。");
    }
}

/// <summary>测试用发布包：在源目录内构造 Manifest 与 bundles 内容，并生成构建摘要（包身份事实）。</summary>
internal sealed class TestPackage
{
    public TestPackage(string sourceDir, string packageName, string version, string backendKey)
    {
        SourceDir = sourceDir;
        PackageName = packageName;
        Version = version;
        BackendKey = backendKey;
        Directory.CreateDirectory(sourceDir);
        Directory.CreateDirectory(Path.Combine(sourceDir, "bundles"));
    }

    public string SourceDir { get; }
    public string PackageName { get; }
    public string Version { get; }
    public string BackendKey { get; }

    private readonly List<FileHelper.FileDigest> _contents = new List<FileHelper.FileDigest>();

    /// <summary>写入一个内容文件；同名重复写入即覆盖内容。</summary>
    public void WriteContent(string fileName, string content)
    {
        string relative = "bundles/" + fileName;
        FileFixtures.Write(SourceDir, relative, content);
        FileHelper.FileDigest digest = FileFixtures.Digest(SourceDir, relative);

        _contents.RemoveAll(item => string.Equals(item.Name, relative, StringComparison.Ordinal));
        _contents.Add(digest);
    }

    /// <summary>删除一个内容文件（模拟内容被移除）。</summary>
    public void DeleteContent(string fileName)
    {
        string relative = "bundles/" + fileName;
        string path = Path.Combine(SourceDir, relative.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(path))
            File.Delete(path);
        _contents.RemoveAll(item => string.Equals(item.Name, relative, StringComparison.Ordinal));
    }

    /// <summary>
    /// 只在清单里声明一个内容文件，不随包携带其字节。
    /// 用于构造稀疏 Hotfix 包：目标清单声明完整内容集合，而未变化内容不在包目录内。
    /// </summary>
    public void DeclareContent(string fileName, FileHelper.FileDigest digest)
    {
        string relative = "bundles/" + fileName;
        _contents.RemoveAll(item => string.Equals(item.Name, relative, StringComparison.Ordinal));
        _contents.Add(new FileHelper.FileDigest(relative, digest.Hash, digest.CRC, digest.Size));
    }

    /// <summary>当前清单声明的内容集合（名称为包根相对路径）。</summary>
    public List<FileHelper.FileDigest> Contents => new List<FileHelper.FileDigest>(_contents);

    /// <summary>写出清单（包目录只含发布内容，不含构建摘要）。</summary>
    public void Seal(Func<FileHelper.FileDigest, FileHelper.FileDigest> manifestTamper = null)
    {
        TestManifestReader.WriteManifest(SourceDir, _contents, manifestTamper);
    }

}
