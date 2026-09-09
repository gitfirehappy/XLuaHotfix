using UnityEngine;

/// <summary>
/// 注册 FYAsset 根 Manifest。嵌套字段由 BinaryReflectionSerializer 直接读写。
/// </summary>
public static class BinarySerializerInitializer
{
    public const uint ABManifestMagic = 0x41424D46;
    public const ushort ABManifestSchemaVersion = 5;
    public const uint AAManifestMagic = 0x41414D46;
    public const ushort AAManifestSchemaVersion = 2;

    public static bool IsInitialized { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    public static void Initialize()
    {
        if (IsInitialized) return;

        var codec = SerializationUtility.GetBinaryCodec();
        codec.Register<ABManifest>(
            ABManifestMagic,
            ABManifestSchemaVersion,
            (writer, obj) => ABManifest_BinarySerializer.WriteWithHeader(writer, obj),
            reader => (ABManifest)ABManifest_BinarySerializer.ReadWithHeader(reader));

        codec.Register<AAManifest>(
            AAManifestMagic,
            AAManifestSchemaVersion,
            (writer, obj) => AAManifest_BinarySerializer.WriteWithHeader(writer, obj),
            reader => (AAManifest)AAManifest_BinarySerializer.ReadWithHeader(reader));

        IsInitialized = true;
    }
}
