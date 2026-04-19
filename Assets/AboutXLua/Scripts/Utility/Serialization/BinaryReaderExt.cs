using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

public static class BinaryReaderExt
{
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
        if (count < 0)
        {
            return null;
        }

        var result = new List<T>(count);
        for (int i = 0; i < count; i++)
        {
            result.Add(readElement(reader));
        }

        return result;
    }
}
