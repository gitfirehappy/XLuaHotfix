using System;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

/// <summary>
/// 计算 [BinarySerializable] 类型 schema 哈希。
/// Hash 输入：Type + SchemaVersion + (FieldName + FieldType + Order)
/// </summary>
public static class SerializationHashUtility
{
    public static string ComputeTypeHash(Type type)
    {
        if (type == null) throw new ArgumentNullException(nameof(type));

        var attr = type.GetCustomAttribute<BinarySerializableAttribute>();
        if (attr == null)
        {
            throw new InvalidOperationException($"类型未标记 [BinarySerializable]: {type.FullName}");
        }

        var fields = type
            .GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Select(f => new
            {
                Field = f,
                Attr = f.GetCustomAttribute<BinaryFieldAttribute>()
            })
            .Where(x => x.Attr != null)
            .OrderBy(x => x.Attr.Order)
            .ToArray();

        var sb = new StringBuilder();
        sb.Append(type.FullName).Append('|');
        sb.Append(attr.SchemaVersion).Append('|');

        for (int i = 0; i < fields.Length; i++)
        {
            var x = fields[i];
            sb.Append(x.Attr.Order)
                .Append(':')
                .Append(x.Field.Name)
                .Append(':')
                .Append(GetFieldTypeSignature(x.Field.FieldType))
                .Append('|');
        }

        using var md5 = MD5.Create();
        byte[] bytes = Encoding.UTF8.GetBytes(sb.ToString());
        byte[] hash = md5.ComputeHash(bytes);
        return BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
    }

    private static string GetFieldTypeSignature(Type type)
    {
        if (type.IsArray)
        {
            return $"{GetFieldTypeSignature(type.GetElementType())}[]";
        }

        if (type.IsGenericType)
        {
            string genericName = type.GetGenericTypeDefinition().FullName;
            string args = string.Join(",", type.GetGenericArguments().Select(GetFieldTypeSignature));
            return $"{genericName}<{args}>";
        }

        return type.FullName;
    }
}
