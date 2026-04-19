using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

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
}
