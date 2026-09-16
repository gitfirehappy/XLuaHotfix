using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

[BinarySerializable]
public class SerializationReferenceFixture
{
    [BinaryField(0)] public VersionNumber Version;
    [BinaryField(1)] public SerializationValueFixture Value;
    [BinaryField(2)] public SerializationReferenceFixture Child;
    [BinaryField(3)] public List<VersionNumber> Versions;
}

[BinarySerializable]
public struct SerializationValueFixture
{
    [BinaryField(0)] public VersionNumber Version;
    [BinaryField(1)] public SerializationReferenceFixture Reference;
}

internal static class SerializationScenarios
{
    private static int Main()
    {
        try
        {
            var original = VersionNumber.Parse("1.2.3-beta");
            var copy = original;
            copy.Patch = 9;
            Check(original.Patch == 3, "assignment copies the value");
            Console.WriteLine("PASS: VersionNumber assignment copies its value");
            copy = original;
            copy.Patch = 7;
            Check(copy != original && copy.CompareTo(original) > 0, "version fields participate in equality and ordering");
            Check(VersionNumber.Parse("1.2.3-alpha") < original && original < VersionNumber.Parse("1.2.3-rc") && VersionNumber.Parse("1.2.3-rc") < VersionNumber.Parse("1.2.3"), "channel ordering");
            Check(!VersionNumber.TryParse("1.2.3+B", out var invalid) && invalid.Equals(default(VersionNumber)), "failed parse returns default");
            Check(!VersionNumber.TryParse("1.2.3-BETA", out _) && VersionNumber.TryParse(" 1.2.3- ", out _), "existing parse syntax");
            Check(default(VersionNumber).ToString() == "0.0.0", "default version text");
            Check(default(VersionNumber).CompareTo(VersionNumber.Parse("0.0.0")) == 0 && default(VersionNumber) != VersionNumber.Parse("0.0.0"), "preserve null versus empty Channel equality");
            Check(!VersionNumber.JsonHasObjectField("{\"LatestPackage\":\"Build_x\",\"BackendMode\":\"AB\"}", "LatestVersion"), "missing LatestVersion is rejected");
            Check(VersionNumber.JsonHasObjectField("{\"LatestVersion\":{\"Major\":0,\"Minor\":0,\"Patch\":0}}", "LatestVersion"), "explicit zero LatestVersion is present");
            Check(VersionNumber.JsonHasObjectField("{\"Version\":{\"Major\":1,\"Minor\":2,\"Patch\":3}}", "Version"), "root Version object is present");
            Check(!VersionNumber.JsonHasObjectField("{\"Version\":null}", "Version"), "null Version is not an object");
            Check(!VersionNumber.JsonHasObjectField("{\"Version\":[]}", "Version"), "array Version is not an object");
            Check(!VersionNumber.JsonHasObjectField("{\"Note\":\"\\\"Version\\\":{}\"}", "Version"), "Version text inside a string is ignored");
            Check(!VersionNumber.JsonHasObjectField("{\"Payload\":{\"Version\":{}}}", "Version"), "nested Version does not satisfy a root field");
            Check(!VersionNumber.JsonHasObjectField("{\"version\":{}}", "Version"), "Version field matching is case-sensitive");
            Check(VersionNumber.JsonHasObjectField("{  \n  \"Version\"  :  { \"Major\": 0 } }", "Version"), "whitespace around Version is accepted");
            Check(VersionNumber.JsonHasObjectField(Encoding.UTF8.GetBytes("\uFEFF{\"Version\":{\"Major\":0}}"), "Version"), "UTF-8 BOM is accepted");
            Check(VersionNumber.JsonHasNestedObjectField("{\"Latest\":{\"Version\":{\"Major\":0,\"Minor\":0,\"Patch\":0}}}", "Latest", "Version"), "nested Version object is present");
            Check(!VersionNumber.JsonHasNestedObjectField("{\"Latest\":{\"PackageName\":\"Build_x\"}}", "Latest", "Version"), "nested Version missing is rejected");
            Check(VersionNumber.TryParse("0.0.0", out var zero) && zero.Major == 0 && zero.Minor == 0 && zero.Patch == 0, "explicit 0.0.0 remains valid");
            Console.WriteLine("PASS: parsing, default, ordering, equality and JSON field presence");
            var reference = new SerializationReferenceFixture {
                Version = original,
                Value = new SerializationValueFixture { Version = copy, Reference = new SerializationReferenceFixture { Version = original } },
                Versions = new List<VersionNumber> { default, original }
            };
            var round = RoundTrip(reference);
            Check(round.Version == original && round.Value.Version.Patch == 7 && round.Value.Reference.Version == original, "class/struct nested fields");
            Check(round.Child == null && round.Versions.Count == 2 && round.Versions[0].Equals(default(VersionNumber)), "null reference and struct list");
            Check(RoundTrip<SerializationReferenceFixture>(null) == null, "null class root");
            var empty = RoundTrip(default(SerializationValueFixture));
            Check(empty.Reference == null && empty.Version.Equals(default(VersionNumber)), "default struct root");
            round.Version.Patch = 30;
            Check(reference.Version.Patch == 3, "deserialized value independence");
            Console.WriteLine("PASS: nested class/struct, null class, default struct, lists and copied fields");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FAIL: " + ex.Message);
            return 1;
        }
    }

    private static T RoundTrip<T>(T value)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        BinaryReflectionSerializer.WriteObject(writer, typeof(T), value);
        writer.Flush();
        stream.Position = 0;
        using var reader = new BinaryReader(stream);
        return (T)BinaryReflectionSerializer.ReadObject(reader, typeof(T));
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
