using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// S2-T9: 最小手工验证入口（round-trip + 生成器运行）。
/// </summary>
public static class BinarySerializationSmokeTestMenu
{
    [BinarySerializable(Magic = 0x54455354, SchemaVersion = 1)]
    [Serializable]
    private class S2SmokeModel
    {
        [BinaryField(0)] public int Id;
        [BinaryField(1)] public string Name;
        [BinaryField(2)] public List<string> Tags = new();
    }

    [MenuItem("XLua/Serialization/Run S2 Smoke Test", false, 31)]
    public static void Run()
    {
        BinarySerializerGenerator.GenerateAll();

        var codec = new BinaryCodec();
        codec.Register<S2SmokeModel>(
            0x54455354,
            (writer, obj) =>
            {
                BinaryHeader.WriteHeader(writer, 0x54455354, 1);
                BinaryReflectionSerializer.WriteObject(writer, typeof(S2SmokeModel), obj);
            },
            reader =>
            {
                var header = BinaryHeader.ReadHeader(reader);
                if (header.Magic != 0x54455354)
                {
                    throw new InvalidDataException("S2 smoke test magic mismatch");
                }

                return (S2SmokeModel)BinaryReflectionSerializer.ReadObject(reader, typeof(S2SmokeModel));
            });

        var source = new S2SmokeModel
        {
            Id = 42,
            Name = "s2-smoke",
            Tags = new List<string> { "a", "b" }
        };

        byte[] data = codec.Serialize(source);
        var target = codec.Deserialize<S2SmokeModel>(data);

        if (target == null || target.Id != source.Id || target.Name != source.Name || target.Tags == null || target.Tags.Count != 2)
        {
            throw new InvalidOperationException("S2 smoke test failed: round-trip mismatch");
        }

        Debug.Log("[BinarySerializationSmokeTest] PASS");
        AssetDatabase.Refresh();
    }
}
