using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 二进制序列化器代码生成器。
/// 扫描 [BinarySerializable] 类型并生成 {TypeName}_BinarySerializer.cs。
/// </summary>
public static class BinarySerializerGenerator
{
    public const string GeneratedDir = FYAssetSettings.BINARY_SERIALIZER_GENERATE_PATH;
    public const string HashPrefix = "// Hash:";
    private const string SerializationTestPathSegment = "/Serialization/Test/";

    [MenuItem("Tools/Serialization/Generate Binary Serializers", false, 30)]
    public static void GenerateMenu()
    {
        GenerateAll();
        AssetDatabase.Refresh();
        Debug.Log("[BinarySerializerGenerator] 生成完成。\n");
    }

    public static void GenerateAll()
    {
        var fieldIssues = GetFieldIssues();
        if (fieldIssues.Count > 0)
        {
            throw new InvalidOperationException(BuildFieldIssueMessage(fieldIssues));
        }

        if (!Directory.Exists(GeneratedDir))
        {
            Directory.CreateDirectory(GeneratedDir);
        }

        CleanupGeneratedFiles();

        var types = GetSerializableTypes();
        for (int i = 0; i < types.Count; i++)
        {
            GenerateForType(types[i]);
        }
    }

    public static bool IsStale(Type type)
    {
        string path = GetGeneratedFilePath(type);
        if (!File.Exists(path))
        {
            return true;
        }

        string[] lines = File.ReadAllLines(path);
        string hashLine = lines.FirstOrDefault(l => l.StartsWith(HashPrefix, StringComparison.Ordinal));
        if (string.IsNullOrEmpty(hashLine))
        {
            return true;
        }

        string currentHash = SerializationHashUtility.ComputeTypeHash(type);
        string fileHash = hashLine.Substring(HashPrefix.Length).Trim();
        return !string.Equals(currentHash, fileHash, StringComparison.OrdinalIgnoreCase);
    }

    public static List<Type> GetStaleTypes()
    {
        var types = GetSerializableTypes();
        var stale = new List<Type>();
        for (int i = 0; i < types.Count; i++)
        {
            if (IsStale(types[i]))
            {
                stale.Add(types[i]);
            }
        }

        return stale;
    }

    public static List<BinarySerializableFieldIssue> GetFieldIssues()
    {
        var types = GetSerializableTypes();
        var issues = new List<BinarySerializableFieldIssue>();
        for (int i = 0; i < types.Count; i++)
        {
            var fields = types[i].GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            for (int j = 0; j < fields.Length; j++)
            {
                var field = fields[j];
                if (!IsUnityJsonSerializedField(field))
                {
                    continue;
                }

                if (field.GetCustomAttribute<BinaryFieldAttribute>() == null)
                {
                    issues.Add(new BinarySerializableFieldIssue(types[i], field));
                }
            }
        }

        return issues;
    }

    public static string BuildFieldIssueMessage(List<BinarySerializableFieldIssue> fieldIssues)
    {
        var sb = new StringBuilder();
        sb.AppendLine("以下 [BinarySerializable] 类型存在 JSON 字段但缺少 [BinaryField]：");
        for (int i = 0; i < fieldIssues.Count; i++)
        {
            sb.Append("- ")
                .Append(fieldIssues[i].Type.FullName)
                .Append('.')
                .AppendLine(fieldIssues[i].Field.Name);
        }
        sb.AppendLine("请为这些字段添加 [BinaryField]，或添加 [NonSerialized]，或移除类型上的 [BinarySerializable]。");
        return sb.ToString();
    }

    private static List<Type> GetSerializableTypes()
    {
        var result = new List<Type>();
        var assemblies = AppDomain.CurrentDomain.GetAssemblies();
        for (int i = 0; i < assemblies.Length; i++)
        {
            string assemblyName = assemblies[i].GetName().Name ?? string.Empty;
            if (assemblyName.Contains("Editor", StringComparison.OrdinalIgnoreCase) ||
                assemblyName.StartsWith("UnityEditor", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            Type[] types;
            try
            {
                types = assemblies[i].GetTypes();
            }
            catch (ReflectionTypeLoadException e)
            {
                types = e.Types.Where(t => t != null).ToArray();
            }

            for (int j = 0; j < types.Length; j++)
            {
                var t = types[j];
                if (t.GetCustomAttribute<BinarySerializableAttribute>() != null && !IsSerializationTestType(t))
                {
                    result.Add(t);
                }
            }
        }

        return result.OrderBy(t => t.FullName).ToList();
    }

    private static bool IsSerializationTestType(Type type)
    {
        if (type == null)
        {
            return false;
        }

        var guids = AssetDatabase.FindAssets($"{type.Name} t:MonoScript");
        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]).Replace('\\', '/');
            if (path.IndexOf(SerializationTestPathSegment, StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }

            var monoScript = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
            if (monoScript != null && monoScript.GetClass() == type)
            {
                return true;
            }
        }

        return false;
    }

    private static void GenerateForType(Type type)
    {
        string hash = SerializationHashUtility.ComputeTypeHash(type);
        string code = BuildCode(type, hash);
        File.WriteAllText(GetGeneratedFilePath(type), code, Encoding.UTF8);
    }

    private static string BuildCode(Type type, string hash)
    {
        string typeName = GetCodeTypeName(type);
        string aqn = type.AssemblyQualifiedName?.Replace("\\", "\\\\").Replace("\"", "\\\"") ?? string.Empty;
        string serializerName = $"{type.Name}_BinarySerializer";

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated>");
        sb.AppendLine("// Generated by BinarySerializerGenerator. Do not edit manually.");
        sb.AppendLine($"{HashPrefix} {hash}");
        sb.AppendLine($"// Source: {typeName}");
        sb.AppendLine("// </auto-generated>");
        sb.AppendLine("using System;");
        sb.AppendLine("using System.IO;");
        sb.AppendLine();
        sb.AppendLine($"public static class {serializerName}");
        sb.AppendLine("{");
        sb.AppendLine($"    private const string TargetTypeAqn = \"{aqn}\";");
        sb.AppendLine();
        sb.AppendLine("    private static Type GetTargetType()");
        sb.AppendLine("    {");
        sb.AppendLine("        var t = Type.GetType(TargetTypeAqn);");
        sb.AppendLine("        if (t == null) throw new InvalidOperationException(\"Target type not found for generated serializer\");");
        sb.AppendLine("        return t;");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    public static void Write(BinaryWriter writer, object obj)");
        sb.AppendLine("    {");
        sb.AppendLine("        BinaryReflectionSerializer.WriteObject(writer, GetTargetType(), obj);");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    public static object Read(BinaryReader reader)");
        sb.AppendLine("    {");
        sb.AppendLine("        return BinaryReflectionSerializer.ReadObject(reader, GetTargetType());");
        sb.AppendLine("    }");

        var attr = type.GetCustomAttribute<BinarySerializableAttribute>();
        if (attr != null && attr.Magic != 0)
        {
            sb.AppendLine();
            sb.AppendLine("    public static void WriteWithHeader(BinaryWriter writer, object obj)");
            sb.AppendLine("    {");
            sb.AppendLine($"        BinaryHeader.WriteHeader(writer, {attr.Magic}u, {attr.SchemaVersion}, 0);");
            sb.AppendLine("        Write(writer, obj);");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    public static object ReadWithHeader(BinaryReader reader)");
            sb.AppendLine("    {");
            sb.AppendLine("        var header = BinaryHeader.ReadHeader(reader);");
            sb.AppendLine($"        if (header.Magic != {attr.Magic}u) throw new InvalidDataException(\"Magic mismatch\");");
            sb.AppendLine($"        if (header.SchemaVersion != {attr.SchemaVersion}) throw new InvalidDataException(\"SchemaVersion mismatch\");");
            sb.AppendLine("        return Read(reader);");
            sb.AppendLine("    }");
        }

        sb.AppendLine("}");
        return sb.ToString();
    }

    private static string GetGeneratedFilePath(Type type)
    {
        return Path.Combine(GeneratedDir, $"{type.Name}_BinarySerializer.cs");
    }

    private static void CleanupGeneratedFiles()
    {
        if (!Directory.Exists(GeneratedDir))
        {
            return;
        }

        string[] files = Directory.GetFiles(GeneratedDir, "*_BinarySerializer.cs", SearchOption.TopDirectoryOnly);
        for (int i = 0; i < files.Length; i++)
        {
            File.Delete(files[i]);
            string meta = files[i] + ".meta";
            if (File.Exists(meta))
            {
                File.Delete(meta);
            }
        }
    }

    private static string GetCodeTypeName(Type type)
    {
        if (type == null)
        {
            throw new ArgumentNullException(nameof(type));
        }

        if (type.IsGenericType)
        {
            string genericTypeName = type.GetGenericTypeDefinition().FullName;
            int tick = genericTypeName.IndexOf('`');
            if (tick >= 0)
            {
                genericTypeName = genericTypeName.Substring(0, tick);
            }

            genericTypeName = genericTypeName.Replace('+', '.');
            string args = string.Join(", ", type.GetGenericArguments().Select(GetCodeTypeName));
            return $"{genericTypeName}<{args}>";
        }

        return (type.FullName ?? type.Name).Replace('+', '.');
    }

    private static bool IsUnityJsonSerializedField(FieldInfo field)
    {
        if (field == null)
        {
            return false;
        }

        if (field.IsStatic || field.IsLiteral || field.IsInitOnly)
        {
            return false;
        }

        if (field.GetCustomAttribute<NonSerializedAttribute>() != null)
        {
            return false;
        }

        return field.IsPublic || field.GetCustomAttribute<SerializeField>() != null;
    }
}

public sealed class BinarySerializableFieldIssue
{
    public readonly Type Type;
    public readonly FieldInfo Field;

    public BinarySerializableFieldIssue(Type type, FieldInfo field)
    {
        Type = type;
        Field = field;
    }
}
