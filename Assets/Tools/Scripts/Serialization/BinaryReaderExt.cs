using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

/// <summary>
/// BinaryReader 扩展方法，提供可空类型和集合的读取支持。
/// </summary>
public static class BinaryReaderExt
{
    /// <summary>
    /// 从 BinaryReader 读取可空字符串。
    /// </summary>
    /// <param name="reader">要读取字符串的 BinaryReader。</param>
    /// <returns>可空字符串。</returns>
    public static string ReadNullableString(this BinaryReader reader)
    {
        if (reader == null)
        {
            throw new ArgumentNullException(nameof(reader));
        }

        byte hasValue = reader.ReadByte();
        if (hasValue == 0)
        {
            return null;
        }

        int len = reader.ReadInt32();
        if (len == 0)
        {
            return string.Empty;
        }

        byte[] bytes = reader.ReadBytes(len);
        return Encoding.UTF8.GetString(bytes);
    }

    /// <summary>
    /// 从 BinaryReader 读取可空列表。
    /// </summary>
    /// <typeparam name="T">列表元素类型。</typeparam>
    /// <param name="reader">要读取列表的 BinaryReader。</param>
    /// <param name="readElement">读取列表元素的委托。</param>
    /// <returns>可空列表。</returns>
    public static List<T> ReadNullableList<T>(this BinaryReader reader, Func<BinaryReader, T> readElement)
    {
        if (reader == null)
        {
            throw new ArgumentNullException(nameof(reader));
        }

        if (readElement == null)
        {
            throw new ArgumentNullException(nameof(readElement));
        }

        int count = reader.ReadInt32();
        if (count < 0) return null;

        var result = new List<T>(count);
        for (int i = 0; i < count; i++)
        {
            result.Add(readElement(reader));
        }

        return result;
    }

    /// <summary>
    /// 从 BinaryReader 读取可空数组。
    /// </summary>
    /// <typeparam name="T">数组元素类型。</typeparam>
    /// <param name="reader">要读取数组的 BinaryReader。</param>
    /// <param name="readElement">读取数组元素的委托。</param>
    /// <returns>可空数组。</returns>
    public static T[] ReadNullableArray<T>(this BinaryReader reader, Func<BinaryReader, T> readElement)
    {
        if (reader == null)
        {
            throw new ArgumentNullException(nameof(reader));
        }

        if (readElement == null)
        {
            throw new ArgumentNullException(nameof(readElement));
        }

        int count = reader.ReadInt32();
        if (count < 0) return null;

        var arr = new T[count];
        for (int i = 0; i < count; i++)
        {
            arr[i] = readElement(reader);
        }

        return arr;
    }

    /// <summary>
    /// 从 BinaryReader 读取可空对象。
    /// </summary>
    /// <typeparam name="T">对象类型。</typeparam>
    /// <param name="reader">要读取对象的 BinaryReader。</param>
    /// <param name="readObject">读取对象的委托。</param>
    /// <returns>可空对象。</returns>
    public static T ReadNullableObject<T>(this BinaryReader reader, Func<BinaryReader, T> readObject) where T : class
    {
        if (reader == null)
        {
            throw new ArgumentNullException(nameof(reader));
        }

        if (readObject == null)
        {
            throw new ArgumentNullException(nameof(readObject));
        }

        byte hasValue = reader.ReadByte();
        if (hasValue == 0) return null;

        return readObject(reader);
    }
}
