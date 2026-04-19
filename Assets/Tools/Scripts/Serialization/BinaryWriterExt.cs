using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

/// <summary>
/// BinaryWriter 扩展方法，提供可空类型和集合的写入支持。
/// </summary>
public static class BinaryWriterExt
{
    public static void WriteNullableString(this BinaryWriter writer, string value)
    {
        if (writer == null)
        {
            throw new ArgumentNullException(nameof(writer));
        }

        if (value == null)
        {
            writer.Write((byte)0);
            return;
        }

        writer.Write((byte)1);
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        writer.Write(bytes.Length);
        writer.Write(bytes);
    }

    public static void WriteNullableList<T>(this BinaryWriter writer, List<T> list, Action<BinaryWriter, T> writeElement)
    {
        if (writer == null)
        {
            throw new ArgumentNullException(nameof(writer));
        }

        if (writeElement == null)
        {
            throw new ArgumentNullException(nameof(writeElement));
        }

        if (list == null)
        {
            writer.Write(-1);
            return;
        }

        writer.Write(list.Count);
        for (int i = 0; i < list.Count; i++)
        {
            writeElement(writer, list[i]);
        }
    }

    public static void WriteNullableArray<T>(this BinaryWriter writer, T[] array, Action<BinaryWriter, T> writeElement)
    {
        if (writer == null)
        {
            throw new ArgumentNullException(nameof(writer));
        }

        if (writeElement == null)
        {
            throw new ArgumentNullException(nameof(writeElement));
        }

        if (array == null)
        {
            writer.Write(-1);
            return;
        }

        writer.Write(array.Length);
        for (int i = 0; i < array.Length; i++)
        {
            writeElement(writer, array[i]);
        }
    }

    public static void WriteNullableObject<T>(this BinaryWriter writer, T obj, Action<BinaryWriter, T> writeObject)
        where T : class
    {
        if (writer == null)
        {
            throw new ArgumentNullException(nameof(writer));
        }

        if (writeObject == null)
        {
            throw new ArgumentNullException(nameof(writeObject));
        }

        if (obj == null)
        {
            writer.Write((byte)0);
            return;
        }

        writer.Write((byte)1);
        writeObject(writer, obj);
    }
}
