using System;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 最小手工验证入口（round-trip + 生成器运行）。
/// </summary>
public static class BinarySerializationSmokeTestMenu
{
    [MenuItem("Tools/Serialization/Run Smoke Test", false, 31)]
    public static void Run()
    {
        BinarySerializerGenerator.GenerateAll();

        var codec = new BinaryCodec();
        codec.Register<SmokeModel>(
            0x54455354,
            (writer, obj) =>
            {
                BinaryHeader.WriteHeader(writer, 0x54455354, 1);
                BinaryReflectionSerializer.WriteObject(writer, typeof(SmokeModel), obj);
            },
            reader =>
            {
                var header = BinaryHeader.ReadHeader(reader);
                if (header.Magic != 0x54455354)
                {
                    throw new InvalidDataException("Smoke test magic mismatch");
                }

                return (SmokeModel)BinaryReflectionSerializer.ReadObject(reader, typeof(SmokeModel));
            });

        var source = new SmokeModel
        {
            Id = 42,
            Name = "smoke",
            Tags = new System.Collections.Generic.List<string> { "a", "b" }
        };

        byte[] data = codec.Serialize(source);
        var target = codec.Deserialize<SmokeModel>(data);

        if (target == null || target.Id != source.Id || target.Name != source.Name || target.Tags == null || target.Tags.Count != 2)
        {
            throw new InvalidOperationException("Smoke test failed: round-trip mismatch");
        }

        Debug.Log("[BinarySerializationSmokeTest] PASS");
        AssetDatabase.Refresh();
    }
}
