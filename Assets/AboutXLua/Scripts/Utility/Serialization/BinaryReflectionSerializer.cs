using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

/// <summary>
/// 二进制反射序列化器：按 [BinaryField] 顺序递归读写。
/// S2 用作生成器输出的底层通用实现，避免手写每种类型。
/// </summary>
public static class BinaryReflectionSerializer
{
    public static void WriteObject(BinaryWriter writer, Type type, object value)
    {
        if (writer == null) throw new ArgumentNullException(nameof(writer));
        if (type == null) throw new ArgumentNullException(nameof(type));

        if (!type.IsValueType)
        {
            writer.WriteNullableObject(value, (w, obj) => WriteObjectBody(w, type, obj));
            return;
        }

        WriteObjectBody(writer, type, value);
    }

    public static object ReadObject(BinaryReader reader, Type type)
    {
        if (reader == null) throw new ArgumentNullException(nameof(reader));
        if (type == null) throw new ArgumentNullException(nameof(type));

        if (!type.IsValueType)
        {
            byte hasValue = reader.ReadByte();
            if (hasValue == 0)
            {
                return null;
            }

            return ReadObjectBody(reader, type);
        }

        return ReadObjectBody(reader, type);
    }

    private static void WriteObjectBody(BinaryWriter writer, Type type, object value)
    {
        if (value == null)
        {
            value = Activator.CreateInstance(type);
        }

        var fields = GetOrderedBinaryFields(type);
        for (int i = 0; i < fields.Length; i++)
        {
            var field = fields[i];
            WriteValue(writer, field.FieldType, field.GetValue(value));
        }
    }

    private static object ReadObjectBody(BinaryReader reader, Type type)
    {
        object obj = Activator.CreateInstance(type);
        var fields = GetOrderedBinaryFields(type);
        for (int i = 0; i < fields.Length; i++)
        {
            var field = fields[i];
            object fieldValue = ReadValue(reader, field.FieldType);
            field.SetValue(obj, fieldValue);
        }

        return obj;
    }

    private static void WriteValue(BinaryWriter writer, Type fieldType, object value)
    {
        if (fieldType == typeof(bool)) writer.Write(value != null && (bool)value);
        else if (fieldType == typeof(byte)) writer.Write(value == null ? (byte)0 : (byte)value);
        else if (fieldType == typeof(short)) writer.Write(value == null ? (short)0 : (short)value);
        else if (fieldType == typeof(ushort)) writer.Write(value == null ? (ushort)0 : (ushort)value);
        else if (fieldType == typeof(int)) writer.Write(value == null ? 0 : (int)value);
        else if (fieldType == typeof(uint)) writer.Write(value == null ? 0u : (uint)value);
        else if (fieldType == typeof(long)) writer.Write(value == null ? 0L : (long)value);
        else if (fieldType == typeof(ulong)) writer.Write(value == null ? 0UL : (ulong)value);
        else if (fieldType == typeof(float)) writer.Write(value == null ? 0f : (float)value);
        else if (fieldType == typeof(double)) writer.Write(value == null ? 0d : (double)value);
        else if (fieldType == typeof(string)) writer.WriteNullableString((string)value);
        else if (fieldType.IsEnum) writer.Write(Convert.ToInt32(value ?? Activator.CreateInstance(fieldType)));
        else if (fieldType.IsArray)
        {
            var elementType = fieldType.GetElementType();
            var array = value as Array;
            if (array == null)
            {
                writer.Write(-1);
                return;
            }

            writer.Write(array.Length);
            for (int i = 0; i < array.Length; i++)
            {
                WriteValue(writer, elementType, array.GetValue(i));
            }
        }
        else if (IsListType(fieldType))
        {
            var elementType = fieldType.GetGenericArguments()[0];
            if (value is not IList list)
            {
                writer.Write(-1);
                return;
            }

            writer.Write(list.Count);
            for (int i = 0; i < list.Count; i++)
            {
                WriteValue(writer, elementType, list[i]);
            }
        }
        else if (IsBinarySerializableType(fieldType))
        {
            WriteObject(writer, fieldType, value);
        }
        else
        {
            throw new NotSupportedException($"不支持的二进制字段类型: {fieldType.FullName}");
        }
    }

    private static object ReadValue(BinaryReader reader, Type fieldType)
    {
        if (fieldType == typeof(bool)) return reader.ReadBoolean();
        if (fieldType == typeof(byte)) return reader.ReadByte();
        if (fieldType == typeof(short)) return reader.ReadInt16();
        if (fieldType == typeof(ushort)) return reader.ReadUInt16();
        if (fieldType == typeof(int)) return reader.ReadInt32();
        if (fieldType == typeof(uint)) return reader.ReadUInt32();
        if (fieldType == typeof(long)) return reader.ReadInt64();
        if (fieldType == typeof(ulong)) return reader.ReadUInt64();
        if (fieldType == typeof(float)) return reader.ReadSingle();
        if (fieldType == typeof(double)) return reader.ReadDouble();
        if (fieldType == typeof(string)) return reader.ReadNullableString();
        if (fieldType.IsEnum) return Enum.ToObject(fieldType, reader.ReadInt32());
        if (fieldType.IsArray)
        {
            var elementType = fieldType.GetElementType();
            int count = reader.ReadInt32();
            if (count < 0) return null;
            var arr = Array.CreateInstance(elementType, count);
            for (int i = 0; i < count; i++)
            {
                arr.SetValue(ReadValue(reader, elementType), i);
            }
            return arr;
        }

        if (IsListType(fieldType))
        {
            var elementType = fieldType.GetGenericArguments()[0];
            int count = reader.ReadInt32();
            if (count < 0) return null;
            var list = (IList)Activator.CreateInstance(fieldType);
            for (int i = 0; i < count; i++)
            {
                list.Add(ReadValue(reader, elementType));
            }
            return list;
        }

        if (IsBinarySerializableType(fieldType))
        {
            return ReadObject(reader, fieldType);
        }

        throw new NotSupportedException($"不支持的二进制字段类型: {fieldType.FullName}");
    }

    public static FieldInfo[] GetOrderedBinaryFields(Type type)
    {
        return type
            .GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Select(f => new
            {
                Field = f,
                Attr = f.GetCustomAttribute<BinaryFieldAttribute>()
            })
            .Where(x => x.Attr != null)
            .OrderBy(x => x.Attr.Order)
            .Select(x => x.Field)
            .ToArray();
    }

    public static bool IsBinarySerializableType(Type type)
    {
        return type.GetCustomAttribute<BinarySerializableAttribute>() != null;
    }

    private static bool IsListType(Type type)
    {
        return type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>);
    }
}
