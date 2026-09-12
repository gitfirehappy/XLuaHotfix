using System;
using UnityEditor;

/// <summary>
/// 维护 FYAsset 的序列化生成清单和 Editor 注册入口。
/// </summary>
public static class FYAssetSerializationEditor
{
    public const string GeneratedDirectory = "Assets/FYAsset/Scripts/Compat/Serialization/Generated";

    [InitializeOnLoadMethod]
    private static void Initialize() => BinarySerializerInitializer.Initialize();

    public static Type[] GetSerializableTypes() => new[]
    {
        typeof(ABManifest), typeof(AAManifest), typeof(VersionNumber),
        typeof(ManifestAssetEntry), typeof(ManifestContentEntry), typeof(BundleInfo),
        typeof(PackageEntry), typeof(TypeToKeys), typeof(LabelToKeys)
    };

    [MenuItem("FYAsset/Tools/Serialization/Generate Binary Serializers", false, 30)]
    public static void GenerateAll()
    {
        BinarySerializerGenerator.GenerateAll(GetSerializableTypes(), GeneratedDirectory);
        AssetDatabase.Refresh();
    }
}
