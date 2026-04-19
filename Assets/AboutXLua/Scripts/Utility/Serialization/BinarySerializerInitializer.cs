using System.IO;

/// <summary>
/// 二进制序列化器初始化：注册 Magic 值到 BinaryCodec。
/// </summary>
public static class BinarySerializerInitializer
{
    public const uint ABManifestMagic = 0x41424D46;

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

        IsInitialized = true;
    }
}
