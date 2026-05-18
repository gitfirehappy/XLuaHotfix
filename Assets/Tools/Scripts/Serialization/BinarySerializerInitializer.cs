using System.IO;

/// <summary>
/// 二进制序列化器初始化：注册顶层类型的 Magic 值到 BinaryCodec。
///
/// 注意：ManifestAssetEntry / ManifestBundleEntry / VersionNumber 是 ABManifest 的嵌套类型，
/// 由 ABManifest_BinarySerializer 通过 BinaryReflectionSerializer 递归处理，
/// 无需在此处独立注册。其对应的 *_BinarySerializer 文件仅供递归调用使用。
/// </summary>
public static class BinarySerializerInitializer
{
    /// <summary>ABManifest 的二进制魔数（ASCII: 'ABMF' = 0x41424D46）</summary>
    public const uint ABManifestMagic = 0x41424D46;

    /// <summary>AAManifest 的二进制魔数（ASCII: 'AAMF' = 0x41414D46）</summary>
    public const uint AAManifestMagic = 0x41414D46;

    public static bool IsInitialized { get; private set; }

    public static void Initialize()
    {
        if (IsInitialized) return;

        var codec = SerializationUtility.GetBinaryCodec();
        codec.Register<ABManifest>(
            ABManifestMagic,
            1,
            (writer, obj) => ABManifest_BinarySerializer.WriteWithHeader(writer, obj),
            reader => (ABManifest)ABManifest_BinarySerializer.ReadWithHeader(reader));

        codec.Register<AAManifest>(
            AAManifestMagic,
            1,
            (writer, obj) => AAManifest_BinarySerializer.WriteWithHeader(writer, obj),
            reader => (AAManifest)AAManifest_BinarySerializer.ReadWithHeader(reader));

        IsInitialized = true;
    }
}
