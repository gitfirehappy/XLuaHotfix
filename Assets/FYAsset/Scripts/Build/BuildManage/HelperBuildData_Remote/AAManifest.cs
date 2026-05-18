using System;
using System.Collections.Generic;

[Serializable]
[BinarySerializable(Magic = 0x41414D46, SchemaVersion = 1)]
public class AAManifest
{
    [BinaryField(0)]
    public VersionNumber Version;

    [BinaryField(1)]
    public string FileHash;

    [BinaryField(2)]
    public long TotalSize;

    [BinaryField(3)]
    public List<BundleInfo> Bundles = new();

    [BinaryField(4)]
    public List<PackageEntry> AssetEntries = new();

    [BinaryField(5)]
    public List<TypeToKeys> KeysByType = new();

    [BinaryField(6)]
    public List<LabelToKeys> KeysByLabel = new();
}

[Serializable]
[BinarySerializable]
public class BundleInfo
{
    [BinaryField(0)]
    public string BundleName;

    [BinaryField(1)]
    public string FileHash;

    [BinaryField(2)]
    public uint FileCRC;

    [BinaryField(3)]
    public long FileSize;
}

[Serializable]
[BinarySerializable]
public class TypeToKeys
{
    [BinaryField(0)]
    public string Type;

    [BinaryField(1)]
    public List<string> Keys = new();
}

[Serializable]
[BinarySerializable]
public class LabelToKeys
{
    [BinaryField(0)]
    public string Label;

    [BinaryField(1)]
    public List<string> Keys = new();
}
